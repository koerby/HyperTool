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

	protected override void OnStartup(StartupEventArgs e)
	{
		base.OnStartup(e);

		InitializeLogging();

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

	protected override void OnExit(ExitEventArgs e)
	{
		_trayService?.Dispose();
		Log.Information("HyperTool exited.");
		Log.CloseAndFlush();
		base.OnExit(e);
	}

	private static void InitializeLogging()
	{
		var logsDirectory = Path.Combine(AppContext.BaseDirectory, "logs");
		Directory.CreateDirectory(logsDirectory);

		Log.Logger = new LoggerConfiguration()
			.MinimumLevel.Information()
			.WriteTo.File(
				Path.Combine(logsDirectory, "hypertool-.log"),
				rollingInterval: RollingInterval.Day,
				retainedFileCountLimit: 14)
			.CreateLogger();
	}
}

