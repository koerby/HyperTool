using HyperTool.Services;
using HyperTool.ViewModels;
using HyperTool.Views;
using Serilog;
using System.IO;
using System.Windows;

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

		RegisterGlobalExceptionHandlers();

		try
		{
			var logPath = InitializeLogging();
			Log.Information("Logging initialized at {LogPath}", logPath);

			IConfigService configService = new ConfigService();
			IHyperVService hyperVService = new HyperVPowerShellService();
			var configPath = Path.Combine(AppContext.BaseDirectory, "HyperTool.config.json");
			var configResult = configService.LoadOrCreate(configPath);

			var mainViewModel = new MainViewModel(configResult, hyperVService);
			var mainWindow = new MainWindow
			{
				DataContext = mainViewModel
			};

			_trayService = new TrayService();
			_trayService.Initialize(
				showAction: () =>
				{
					mainWindow.Show();
					mainWindow.WindowState = WindowState.Normal;
					mainWindow.Activate();
				},
				hideAction: () => mainWindow.Hide(),
				startDefaultVmAction: () => mainViewModel.StartDefaultVmCommand.Execute(null),
				stopDefaultVmAction: () => mainViewModel.StopDefaultVmCommand.Execute(null),
				connectDefaultVmAction: () => mainViewModel.ConnectDefaultVmCommand.Execute(null),
				createCheckpointAction: () => mainViewModel.CreateCheckpointCommand.Execute(null),
				exitAction: () =>
				{
					_isExitRequested = true;
					Shutdown();
				});

			mainWindow.StateChanged += (_, _) =>
			{
				if (mainWindow.WindowState == WindowState.Minimized)
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
		}
		catch
		{
		}

		System.Windows.MessageBox.Show(
			$"{userMessage}{Environment.NewLine}{Environment.NewLine}{exception.Message}",
			"HyperTool - Fataler Fehler",
			System.Windows.MessageBoxButton.OK,
			System.Windows.MessageBoxImage.Error);

		Shutdown(-1);
	}
}

