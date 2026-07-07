using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;

internal sealed class DesktopBannerService : IDisposable
{
	private readonly Action _onReset;
	private readonly ManualResetEventSlim _ready = new();
	private readonly CancellationTokenSource _shutdown = new();
	private readonly Thread _uiThread;

	private BannerWindow? _bannerWindow;
	private Exception? _startupException;
	private IClassicDesktopStyleApplicationLifetime? _lifetime;
	private bool _disposed;

	private DesktopBannerService(Action onReset)
	{
		_onReset = onReset;
		_uiThread = new Thread(RunUiLoop)
		{
			IsBackground = true,
			Name = "ActivityWatchdogBanner"
		};

		if (OperatingSystem.IsWindows())
		{
			_uiThread.SetApartmentState(ApartmentState.STA);
		}

		_uiThread.Start();

		if (!_ready.Wait(TimeSpan.FromSeconds(5)))
		{
			throw new InvalidOperationException("Timed out while starting the desktop banner UI thread.");
		}

		if (_startupException is not null)
		{
			throw new InvalidOperationException("Failed to initialize the desktop banner UI.", _startupException);
		}
	}

	public static DesktopBannerService? TryCreate(Action onReset)
	{
		if (!Environment.UserInteractive)
		{
			return null;
		}

		try
		{
			return new DesktopBannerService(onReset);
		}
		catch
		{
			return null;
		}
	}

	public bool ShowBanner(string title, string message, string buttonText)
	{
		if (_disposed)
		{
			return false;
		}

		Dispatcher.UIThread.Post(() => ShowBannerCore(title, message, buttonText));
		return true;
	}

	public void DismissActiveBanner()
	{
		if (_disposed)
		{
			return;
		}

		Dispatcher.UIThread.Post(() => _bannerWindow?.Hide());
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		Dispatcher.UIThread.Post(() =>
		{
			_bannerWindow?.Close();
			_bannerWindow = null;
			_lifetime?.TryShutdown();
		});
		_shutdown.Cancel();

		if (!_uiThread.Join(TimeSpan.FromSeconds(2)))
		{
			try
			{
				_uiThread.Interrupt();
			}
			catch
			{
			}
		}
	}

	private void RunUiLoop()
	{
		try
		{
			BuildAvaloniaApp().SetupWithClassicDesktopLifetime([], lifetime =>
			{
				lifetime.ShutdownMode = ShutdownMode.OnExplicitShutdown;
				_lifetime = lifetime;
			});
		}
		catch (Exception exception)
		{
			_startupException = exception;
			_ready.Set();
			return;
		}

		_ready.Set();

		try
		{
			Dispatcher.UIThread.MainLoop(_shutdown.Token);
		}
		catch (OperationCanceledException)
		{
		}
	}

	private void ShowBannerCore(string title, string message, string buttonText)
	{
		_bannerWindow ??= new BannerWindow(HandleResetClicked, HandleDismissClicked);
		_bannerWindow.UpdateContent(title, message, buttonText);
		_bannerWindow.Position = GetBottomRightPosition(_bannerWindow);

		if (_bannerWindow.IsVisible)
		{
			_bannerWindow.BringIntoView();
			return;
		}

		_bannerWindow.Show();
	}

	private void HandleResetClicked()
	{
		_bannerWindow?.Hide();
		Task.Run(_onReset);
	}

	private void HandleDismissClicked()
	{
		_bannerWindow?.Hide();
	}

	private static PixelPoint GetBottomRightPosition(Window window)
	{
		var workingArea = window.Screens.Primary?.WorkingArea ?? new PixelRect(0, 0, 1280, 720);
		var x = workingArea.X + Math.Max(0, workingArea.Width - (int)window.Width - 16);
		var y = workingArea.Y + Math.Max(0, workingArea.Height - (int)window.Height - 16);
		return new PixelPoint(x, y);
	}

	private static AppBuilder BuildAvaloniaApp()
	{
		return AppBuilder.Configure<BannerApplication>()
			.UsePlatformDetect();
	}
}

internal sealed class BannerApplication : Application
{
	public override void Initialize()
	{
		Styles.Add(new FluentTheme());
	}
}

internal sealed class BannerWindow : Window
{
	private readonly Button _dismissButton;
	private readonly Label _messageLabel;
	private readonly Button _resetButton;
	private readonly TextBlock _titleBlock;

	public BannerWindow(Action onReset, Action onDismiss)
	{
		Background = new SolidColorBrush(Color.Parse("#212529"));
		CanResize = false;
		Height = 136;
		ShowActivated = false;
		ShowInTaskbar = false;
		WindowDecorations = WindowDecorations.None;
		Topmost = true;
		Width = 360;

		_titleBlock = new TextBlock
		{
			FontSize = 18,
			FontWeight = FontWeight.Bold,
			Foreground = Brushes.White,
			TextWrapping = TextWrapping.NoWrap
		};

		_messageLabel = new Label
		{
			Foreground = Brushes.Gainsboro,
			Padding = new Thickness(0),
			VerticalContentAlignment = VerticalAlignment.Top
		};

		_resetButton = CreateButton("Reset timer", Color.Parse("#FFC107"), Brushes.Black, onReset);
		_dismissButton = CreateButton("Dismiss", Color.Parse("#495057"), Brushes.White, onDismiss);

		Content = new Border
		{
			Padding = new Thickness(16),
			Child = new StackPanel
			{
				Spacing = 12,
				Children =
				{
					_titleBlock,
					_messageLabel,
					new StackPanel
					{
						Orientation = Orientation.Horizontal,
						Spacing = 8,
						Children = { _resetButton, _dismissButton }
					}
				}
			}
		};
	}

	public void UpdateContent(string title, string message, string buttonText)
	{
		Title = title;
		_titleBlock.Text = title;
		_messageLabel.Content = message;
		_resetButton.Content = buttonText;
	}

	private static Button CreateButton(string text, Color background, IBrush foreground, Action onClick)
	{
		var button = new Button
		{
			Background = new SolidColorBrush(background),
			Content = text,
			Foreground = foreground,
			MinWidth = 96,
			Padding = new Thickness(14, 8)
		};

		button.Click += (_, _) => onClick();
		return button;
	}
}