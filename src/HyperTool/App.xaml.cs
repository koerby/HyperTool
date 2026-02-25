using HyperTool.Services;
using HyperTool.ViewModels;
using HyperTool.Views;
using Serilog;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace HyperTool;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
	private ITrayService? _trayService;
	private bool _isExitRequested;
	private bool _isFatalErrorShown;

	protected override void OnStartup(StartupEventArgs e)
	{
		base.OnStartup(e);

		if (e.Args.Any(arg => string.Equals(arg, "--restart-hns", StringComparison.OrdinalIgnoreCase)))
		{
			RunRestartHnsHelperMode();
			return;
		}

		RegisterGlobalExceptionHandlers();

		try
		{
			var logPath = InitializeLogging();
			Log.Information("Logging initialized at {LogPath}", logPath);

			IConfigService configService = new ConfigService();
			IHyperVService hyperVService = new HyperVPowerShellService();
			IHnsService hnsService = new HnsService();
			IStartupService startupService = new StartupService();
			IUpdateService updateService = new GitHubUpdateService();
			var configPath = Path.Combine(AppContext.BaseDirectory, "HyperTool.config.json");
			var configResult = configService.LoadOrCreate(configPath);
			var uiConfig = configResult.Config.Ui;

			if (!startupService.SetStartWithWindows(uiConfig.StartWithWindows, "HyperTool", Environment.ProcessPath ?? string.Empty, out var startupError)
				&& !string.IsNullOrWhiteSpace(startupError))
			{
				Log.Warning("Could not apply startup setting: {StartupError}", startupError);
			}

			var mainViewModel = new MainViewModel(configResult, hyperVService, hnsService, configService, startupService, updateService);
			var mainWindow = new MainWindow
			{
				DataContext = mainViewModel
			};

			if (uiConfig.EnableTrayIcon)
			{
				TryInitializeTray(mainWindow, mainViewModel);
			}

			mainWindow.StateChanged += (_, _) =>
			{
				if (uiConfig.EnableTrayIcon && uiConfig.MinimizeToTray && mainWindow.WindowState == WindowState.Minimized)
				{
					mainWindow.Hide();
				}
			};

			mainWindow.Closing += (_, args) =>
			{
				if (_isExitRequested)
				{
					return;
				}

				if (!uiConfig.EnableTrayIcon || !uiConfig.MinimizeToTray)
				{
					return;
				}

				args.Cancel = true;
				mainWindow.Hide();
			};

			MainWindow = mainWindow;

			if (configResult.Config.Ui.StartMinimized)
			{
				mainWindow.WindowState = WindowState.Minimized;
				mainWindow.Hide();
			}
			else
			{
				mainWindow.Show();
			}

			Log.Information("Config loaded from {ConfigPath}", configResult.ConfigPath);
			Log.Information("HyperTool started.");
		}
		catch (Exception ex)
		{
			ShowFatalErrorAndExit(ex, "HyperTool konnte beim Start nicht initialisiert werden.");
		}
	}

	private void TryInitializeTray(MainWindow mainWindow, MainViewModel mainViewModel)
	{
		try
		{
			_trayService = new TrayService();
			_trayService.Initialize(
				showAction: () =>
				{
					mainWindow.Show();
					mainWindow.WindowState = WindowState.Normal;
					mainWindow.Activate();
				},
				hideAction: () => mainWindow.Hide(),
				getVms: () => mainViewModel.GetTrayVms(),
				getSwitches: () => mainViewModel.GetTraySwitches(),
				refreshTrayDataAction: () => mainViewModel.RefreshTrayDataAsync(),
				reloadConfigAction: () => mainViewModel.ReloadTrayDataAsync(),
				startVmAction: vmName => mainViewModel.StartVmFromTrayAsync(vmName),
				stopVmAction: vmName => mainViewModel.StopVmFromTrayAsync(vmName),
				openConsoleAction: vmName => mainViewModel.OpenConsoleFromTrayAsync(vmName),
				createSnapshotAction: vmName => mainViewModel.CreateSnapshotFromTrayAsync(vmName),
				connectVmToSwitchAction: (vmName, switchName) => mainViewModel.ConnectVmSwitchFromTrayAsync(vmName, switchName),
				exitAction: () =>
				{
					_isExitRequested = true;
					Shutdown();
				});
		}
		catch (Exception ex)
		{
			Log.Error(ex, "Tray initialization failed. App continues without tray icon.");
			System.Windows.MessageBox.Show(
				"Tray-Initialisierung fehlgeschlagen. HyperTool läuft ohne Tray-Icon.\n\n" + ex.Message,
				"HyperTool - Hinweis",
				System.Windows.MessageBoxButton.OK,
				System.Windows.MessageBoxImage.Warning);
		}
	}

	protected override void OnExit(ExitEventArgs e)
	{
		_trayService?.Dispose();
		Log.Information("HyperTool exited.");
		Log.CloseAndFlush();
		base.OnExit(e);
	}

	private static string InitializeLogging()
	{
		var logsDirectory = Path.Combine(AppContext.BaseDirectory, "logs");

		try
		{
			Directory.CreateDirectory(logsDirectory);
		}
		catch
		{
			logsDirectory = Path.Combine(
				Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
				"HyperTool",
				"logs");
			Directory.CreateDirectory(logsDirectory);
		}

		var logFilePath = Path.Combine(logsDirectory, "hypertool-.log");

		Log.Logger = new LoggerConfiguration()
			.MinimumLevel.Information()
			.WriteTo.File(
				logFilePath,
				rollingInterval: RollingInterval.Day,
				retainedFileCountLimit: 14)
			.CreateLogger();

		return logFilePath;
	}

	private void RegisterGlobalExceptionHandlers()
	{
		DispatcherUnhandledException += (_, args) =>
		{
			ShowFatalErrorAndExit(args.Exception, "Ein unerwarteter UI-Fehler ist aufgetreten.");
			args.Handled = true;
		};

		AppDomain.CurrentDomain.UnhandledException += (_, args) =>
		{
			var exception = args.ExceptionObject as Exception ?? new Exception("Unknown unhandled exception");
			ShowFatalErrorAndExit(exception, "Ein kritischer Fehler ist aufgetreten.");
		};
	}

	private void ShowFatalErrorAndExit(Exception exception, string userMessage)
	{
		if (_isFatalErrorShown)
		{
			return;
		}

		_isFatalErrorShown = true;

		try
		{
			Log.Fatal(exception, "Fatal startup/runtime error");
			Log.CloseAndFlush();
			WriteCrashDump(exception);
		}
		catch
		{
		}

		System.Windows.MessageBox.Show(
			$"{userMessage}{Environment.NewLine}{Environment.NewLine}{exception.Message}{Environment.NewLine}{Environment.NewLine}Details:{Environment.NewLine}{exception}",
			"HyperTool - Fataler Fehler",
			System.Windows.MessageBoxButton.OK,
			System.Windows.MessageBoxImage.Error);

		Shutdown(-1);
	}

	private static void WriteCrashDump(Exception exception)
	{
		try
		{
			var crashDir = Path.Combine(
				Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
				"HyperTool",
				"crash");

			Directory.CreateDirectory(crashDir);
			var crashFile = Path.Combine(crashDir, $"crash-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
			File.WriteAllText(crashFile, exception.ToString());
		}
		catch
		{
		}
	}

	private void RunRestartHnsHelperMode()
	{
		try
		{
			InitializeLogging();
		}
		catch
		{
		}

		var (success, message) = ExecuteHnsRestart();

		if (success)
		{
			ShowAutoCloseInfoWindow("HNS-Dienst wurde erfolgreich neu gestartet.\nDieses Fenster schließt in 3 Sekunden.");
			Shutdown(0);
			return;
		}

		ShowErrorWindowWaitForKey("Fehler beim Neustart des HNS-Dienstes", message);
		Shutdown(-1);
	}

	private static (bool Success, string Message) ExecuteHnsRestart()
	{
		try
		{
			var psi = new ProcessStartInfo
			{
				FileName = "powershell.exe",
				Arguments = "-NoProfile -ExecutionPolicy Bypass -Command \"Restart-Service hns -Force -ErrorAction Stop\"",
				UseShellExecute = false,
				CreateNoWindow = true,
				RedirectStandardError = true,
				RedirectStandardOutput = true
			};

			using var process = Process.Start(psi);
			if (process is null)
			{
				return (false, "PowerShell konnte nicht gestartet werden.");
			}

			process.WaitForExit();
			var stdErr = process.StandardError.ReadToEnd();
			var stdOut = process.StandardOutput.ReadToEnd();

			if (process.ExitCode == 0)
			{
				Log.Information("HNS restart succeeded. {Output}", stdOut);
				return (true, "OK");
			}

			var errorText = string.IsNullOrWhiteSpace(stdErr) ? stdOut : stdErr;
			return (false, string.IsNullOrWhiteSpace(errorText) ? $"ExitCode {process.ExitCode}" : errorText.Trim());
		}
		catch (Exception ex)
		{
			Log.Error(ex, "HNS restart helper failed");
			return (false, ex.Message);
		}
	}

	private void ShowAutoCloseInfoWindow(string text)
	{
		var textBlock = new TextBlock
		{
			Text = text,
			Margin = new Thickness(16),
			TextWrapping = TextWrapping.Wrap,
			FontSize = 14
		};

		var window = new Window
		{
			Title = "HyperTool Elevated Helper",
			Content = textBlock,
			Width = 520,
			Height = 180,
			WindowStartupLocation = WindowStartupLocation.CenterScreen,
			ResizeMode = ResizeMode.NoResize
		};

		var timer = new DispatcherTimer
		{
			Interval = TimeSpan.FromSeconds(3)
		};

		timer.Tick += (_, _) =>
		{
			timer.Stop();
			window.Close();
		};

		window.Loaded += (_, _) => timer.Start();
		window.ShowDialog();
	}

	private void ShowErrorWindowWaitForKey(string title, string details)
	{
		var stack = new StackPanel
		{
			Margin = new Thickness(16)
		};

		stack.Children.Add(new TextBlock
		{
			Text = title,
			FontWeight = FontWeights.Bold,
			FontSize = 15,
			Margin = new Thickness(0, 0, 0, 8)
		});

		stack.Children.Add(new TextBlock
		{
			Text = details,
			TextWrapping = TextWrapping.Wrap,
			Margin = new Thickness(0, 0, 0, 12)
		});

		stack.Children.Add(new TextBlock
		{
			Text = "Beliebige Taste drücken oder Fenster schließen...",
			FontStyle = FontStyles.Italic
		});

		var window = new Window
		{
			Title = "HyperTool Elevated Helper",
			Content = stack,
			Width = 680,
			Height = 300,
			WindowStartupLocation = WindowStartupLocation.CenterScreen
		};

		window.KeyDown += (_, _) => window.Close();
		window.PreviewKeyDown += (_, _) => window.Close();
		window.ShowDialog();
	}
}

