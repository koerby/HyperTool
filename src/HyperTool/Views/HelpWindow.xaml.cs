using HyperTool.Helpers;
using MahApps.Metro.Controls;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace HyperTool.Views;

public partial class HelpWindow : MetroWindow
{
    private readonly string _configPath;
    private readonly string _repoUrl;

    public HelpWindow(string configPath, string repoUrl)
    {
        InitializeComponent();
        SourceInitialized += (_, _) => DwmWindowHelper.ApplyRoundedCorners(this);
        _configPath = configPath;
        _repoUrl = repoUrl;
    }

    private void OpenLogsButton_Click(object sender, RoutedEventArgs e)
    {
        var logsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HyperTool",
            "logs");

        Directory.CreateDirectory(logsPath);
        Process.Start(new ProcessStartInfo
        {
            FileName = logsPath,
            UseShellExecute = true
        });
    }

    private void OpenConfigButton_Click(object sender, RoutedEventArgs e)
    {
        var directoryPath = Path.GetDirectoryName(_configPath);
        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        if (File.Exists(_configPath))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{_configPath}\"",
                UseShellExecute = true
            });

            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = directoryPath ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            UseShellExecute = true
        });
    }

    private void OpenRepoButton_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = _repoUrl,
            UseShellExecute = true
        });
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
