using System.Diagnostics;
using System.Net;

namespace HyperTool.Guest;

internal sealed class GuestDriveMappingService
{
    public bool IsExpectedMapping(string mappedRemotePath, string? configuredSharePath, string? hostAddress)
    {
        var mapped = NormalizeUnc(mappedRemotePath);
        if (string.IsNullOrWhiteSpace(mapped))
        {
            return false;
        }

        var candidates = ResolveCandidateSharePaths(configuredSharePath, hostAddress);
        if (candidates.Any(candidate => string.Equals(mapped, NormalizeUnc(candidate), StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var configuredRaw = NormalizeUnc(configuredSharePath);
        if (!TryParseUnc(mapped, out var mappedHost, out var mappedShare)
            || !TryParseUnc(configuredRaw, out var rawHost, out var rawShare))
        {
            return false;
        }

        if (!string.Equals(mappedShare, rawShare, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var normalizedHostAddress = (hostAddress ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedHostAddress))
        {
            return false;
        }

        var mappedMatches = string.Equals(mappedHost, "HOST", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(mappedHost, normalizedHostAddress, StringComparison.OrdinalIgnoreCase);
        var configuredMatches = string.Equals(rawHost, "HOST", StringComparison.OrdinalIgnoreCase)
                                || string.Equals(rawHost, normalizedHostAddress, StringComparison.OrdinalIgnoreCase);

        return mappedMatches && configuredMatches;
    }

    public Task MountAsync(GuestSharedFolderMapping mapping, string? hostAddress, CancellationToken cancellationToken)
    {
        return MountAsync(mapping, hostAddress, credential: null, cancellationToken);
    }

    public async Task MountAsync(GuestSharedFolderMapping mapping, string? hostAddress, GuestCredential? credential, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mapping);

        var driveLetter = GuestConfigService.NormalizeDriveLetter(mapping.DriveLetter);
        var candidateSharePaths = ResolveCandidateSharePaths(mapping.SharePath, hostAddress);
        var firstSharePath = candidateSharePaths.FirstOrDefault() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(firstSharePath))
        {
            throw new InvalidOperationException("SharePath darf nicht leer sein.");
        }

        var existing = await QueryMappingAsync(driveLetter, cancellationToken);
        if (existing.Exists)
        {
            var existingRemoteNormalized = NormalizeUnc(existing.RemotePath);
            var matchesAnyCandidate = candidateSharePaths.Any(candidate =>
                string.Equals(existingRemoteNormalized, NormalizeUnc(candidate), StringComparison.OrdinalIgnoreCase));

            if (matchesAnyCandidate)
            {
                return;
            }

            var unmapResult = await RunProcessWithOutputAsync("net", $"use {driveLetter}: /delete /y", cancellationToken);
            if (unmapResult.ExitCode != 0)
            {
                throw new InvalidOperationException($"Bestehendes Mapping '{driveLetter}:' konnte nicht entfernt werden. Details: {unmapResult.Output}");
            }
        }

        var persistentText = mapping.Persistent ? "yes" : "no";
        var attemptErrors = new List<string>();

        foreach (var candidate in candidateSharePaths)
        {
            var credentialUsers = ResolveCredentialUsersForCandidate(credential?.Username, candidate, hostAddress);
            if (credentialUsers.Count == 0)
            {
                var mapArgs = $"use {driveLetter}: \"{candidate}\" /persistent:{persistentText}";
                var mapResult = await RunProcessWithOutputAsync("net", mapArgs, cancellationToken);
                if (mapResult.ExitCode == 0)
                {
                    return;
                }

                attemptErrors.Add($"{candidate} => {mapResult.Output}");
                continue;
            }

            var mapped = false;
            foreach (var credentialUser in credentialUsers)
            {
                var mapArgs = $"use {driveLetter}: \"{candidate}\" \"{credential?.Password ?? string.Empty}\" /user:\"{credentialUser}\" /persistent:{persistentText}";
                var mapResult = await RunProcessWithOutputAsync("net", mapArgs, cancellationToken);
                if (mapResult.ExitCode == 0)
                {
                    mapped = true;
                    break;
                }

                attemptErrors.Add($"{candidate} [{credentialUser}] => {mapResult.Output}");
            }

            if (mapped)
            {
                return;
            }
        }

        throw new InvalidOperationException(
            $"Mapping '{driveLetter}:' ist fehlgeschlagen. Versuchte Ziele: {string.Join(" | ", attemptErrors)}");
    }

    public async Task UnmountAsync(string driveLetter, CancellationToken cancellationToken)
    {
        var normalizedDriveLetter = GuestConfigService.NormalizeDriveLetter(driveLetter);
        var existing = await QueryMappingAsync(normalizedDriveLetter, cancellationToken);
        if (!existing.Exists)
        {
            return;
        }

        var unmapResult = await RunProcessWithOutputAsync("net", $"use {normalizedDriveLetter}: /delete /y", cancellationToken);
        if (unmapResult.ExitCode != 0)
        {
            throw new InvalidOperationException($"Laufwerk '{normalizedDriveLetter}:' konnte nicht getrennt werden. Details: {unmapResult.Output}");
        }
    }

    public async Task<GuestDriveMappingStatus> QueryMappingAsync(string driveLetter, CancellationToken cancellationToken)
    {
        var normalizedDriveLetter = GuestConfigService.NormalizeDriveLetter(driveLetter);
        var result = await RunProcessWithOutputAsync("net", $"use {normalizedDriveLetter}:", cancellationToken);
        if (result.ExitCode != 0)
        {
            return new GuestDriveMappingStatus(false, string.Empty);
        }

        var lines = result.Output
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .ToList();

        var remotePath = lines
            .Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .FirstOrDefault(parts => parts.Length >= 3 && parts[1].Equals($"{normalizedDriveLetter}:", StringComparison.OrdinalIgnoreCase))?
            .ElementAtOrDefault(2)
            ?? string.Empty;

        return new GuestDriveMappingStatus(!string.IsNullOrWhiteSpace(remotePath), remotePath);
    }

    public IReadOnlyList<string> ResolveCandidateSharePaths(string? sharePath, string? hostAddress)
    {
        var candidates = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddCandidate(string? value)
        {
            var normalized = (value ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return;
            }

            if (seen.Add(normalized))
            {
                candidates.Add(normalized);
            }
        }

        var normalizedSharePath = (sharePath ?? string.Empty).Trim();
        if (!normalizedSharePath.StartsWith("\\\\", StringComparison.Ordinal))
        {
            AddCandidate(normalizedSharePath);
            return candidates;
        }

        AddCandidate(normalizedSharePath);

        var withoutPrefix = normalizedSharePath[2..];
        var separatorIndex = withoutPrefix.IndexOf('\\');
        if (separatorIndex <= 0)
        {
            return candidates;
        }

        var uncHost = withoutPrefix[..separatorIndex];
        var suffix = withoutPrefix[separatorIndex..];
        var normalizedHostAddress = (hostAddress ?? string.Empty).Trim();

        if (!string.Equals(uncHost, "HOST", StringComparison.OrdinalIgnoreCase))
        {
            return candidates;
        }

        if (!string.IsNullOrWhiteSpace(normalizedHostAddress))
        {
            AddCandidate($"\\\\{normalizedHostAddress}{suffix}");

            if (IPAddress.TryParse(normalizedHostAddress, out _))
            {
                foreach (var derivedName in ResolveHostNamesFromAddress(normalizedHostAddress))
                {
                    AddCandidate($"\\\\{derivedName}{suffix}");
                }
            }
            else
            {
                AddCandidate($"\\\\{ExtractShortHostName(normalizedHostAddress)}{suffix}");
            }
        }

        return candidates;
    }

    public string ResolveEffectiveSharePath(string? sharePath, string? hostAddress)
    {
        return ResolveCandidateSharePaths(sharePath, hostAddress).FirstOrDefault() ?? string.Empty;
    }

    private static IReadOnlyList<string> ResolveCredentialUsersForCandidate(string? username, string? sharePath, string? hostAddress)
    {
        var normalizedUser = (username ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedUser))
        {
            return [];
        }

        if (normalizedUser.Contains('\\') || normalizedUser.Contains('@'))
        {
            return [normalizedUser];
        }

        var values = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string? candidateUser)
        {
            var normalized = (candidateUser ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return;
            }

            if (seen.Add(normalized))
            {
                values.Add(normalized);
            }
        }

        Add(normalizedUser);

        var hosts = new List<string>();
        if (TryParseUnc(sharePath, out var uncHost, out _))
        {
            hosts.Add(uncHost);
        }

        var normalizedHostAddress = (hostAddress ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(normalizedHostAddress))
        {
            hosts.Add(normalizedHostAddress);
        }

        foreach (var host in hosts)
        {
            var normalizedHost = (host ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalizedHost))
            {
                continue;
            }

            Add($"{normalizedHost}\\{normalizedUser}");
            Add($"{ExtractShortHostName(normalizedHost)}\\{normalizedUser}");
        }

        return values;
    }

    private static IReadOnlyList<string> ResolveHostNamesFromAddress(string hostAddress)
    {
        var values = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string? value)
        {
            var normalized = (value ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return;
            }

            if (seen.Add(normalized))
            {
                values.Add(normalized);
            }
        }

        try
        {
            var hostEntry = Dns.GetHostEntry(hostAddress);
            Add(hostEntry.HostName);
            Add(ExtractShortHostName(hostEntry.HostName));

            foreach (var alias in hostEntry.Aliases)
            {
                Add(alias);
                Add(ExtractShortHostName(alias));
            }
        }
        catch
        {
        }

        return values;
    }

    private static string ExtractShortHostName(string? hostName)
    {
        var normalized = (hostName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        var dotIndex = normalized.IndexOf('.');
        return dotIndex > 0 ? normalized[..dotIndex] : normalized;
    }

    private static string NormalizeUnc(string? value)
    {
        return (value ?? string.Empty).Trim().TrimEnd('\\').ToUpperInvariant();
    }

    private static bool TryParseUnc(string? uncPath, out string host, out string share)
    {
        host = string.Empty;
        share = string.Empty;

        var normalized = (uncPath ?? string.Empty).Trim();
        if (!normalized.StartsWith("\\\\", StringComparison.Ordinal))
        {
            return false;
        }

        var withoutPrefix = normalized[2..];
        var firstSeparator = withoutPrefix.IndexOf('\\');
        if (firstSeparator <= 0)
        {
            return false;
        }

        host = withoutPrefix[..firstSeparator].Trim();
        var shareAndRest = withoutPrefix[(firstSeparator + 1)..];
        var secondSeparator = shareAndRest.IndexOf('\\');
        share = (secondSeparator >= 0 ? shareAndRest[..secondSeparator] : shareAndRest).Trim();

        return !string.IsNullOrWhiteSpace(host) && !string.IsNullOrWhiteSpace(share);
    }

    private static async Task<GuestDriveProcessResult> RunProcessWithOutputAsync(string fileName, string arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);
        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        var output = string.IsNullOrWhiteSpace(stderr)
            ? stdout
            : string.Concat(stdout, Environment.NewLine, stderr);

        return new GuestDriveProcessResult(process.ExitCode, output.Trim());
    }
}

internal sealed record GuestDriveMappingStatus(bool Exists, string RemotePath);
internal sealed record GuestDriveProcessResult(int ExitCode, string Output);
