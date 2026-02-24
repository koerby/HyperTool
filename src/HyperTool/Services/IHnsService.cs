namespace HyperTool.Services;

public interface IHnsService
{
    Task<(bool Success, string Message)> RestartHnsElevatedAsync(CancellationToken cancellationToken);
}