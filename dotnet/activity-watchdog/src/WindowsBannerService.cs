using System.Drawing;
using System.Windows.Forms;

internal sealed class WindowsBannerService : IDisposable
{
	private readonly Action _onReset;
	private readonly ManualResetEventSlim _ready = new();
	private readonly Thread _uiThread;

	private ApplicationContext? _applicationContext;
	private ResetBannerForm? _bannerForm;
	private WindowsFormsSynchronizationContext? _synchronizationContext;
	private bool _disposed;

	private WindowsBannerService(Action onReset)
	{
		_onReset = onReset;
		_uiThread = new Thread(RunMessageLoop)
		{
			IsBackground = true,
			Name = "ActivityWatchdogBanner"
		};
		_uiThread.SetApartmentState(ApartmentState.STA);
		_uiThread.Start();

		if (!_ready.Wait(TimeSpan.FromSeconds(5)))
		{
			throw new InvalidOperationException("Timed out while starting the banner UI thread.");
		}
	}

	public static WindowsBannerService? TryCreate(Action onReset)
	{
		if (!OperatingSystem.IsWindows() || !Environment.UserInteractive)
		{
			return null;
		}

		try
		{
			return new WindowsBannerService(onReset);
		}
		catch
		{
			return null;
		}
	}

	public bool ShowBanner(string title, string message, string buttonText)
	{
		if (_disposed || _synchronizationContext is null)
		{
			return false;
		}

		Post(() => ShowBannerCore(title, message, buttonText));
		return true;
	}

	public void DismissActiveBanner()
	{
		if (_disposed)
		{
			return;
		}

		Post(() => _bannerForm?.Hide());
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		Post(() =>
		{
			if (_bannerForm is not null)
			{
				_bannerForm.Close();
				_bannerForm.Dispose();
				_bannerForm = null;
			}

			_applicationContext?.ExitThread();
		});

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

	private void RunMessageLoop()
	{
		Application.SetHighDpiMode(HighDpiMode.SystemAware);
		_synchronizationContext = new WindowsFormsSynchronizationContext();
		SynchronizationContext.SetSynchronizationContext(_synchronizationContext);
		_applicationContext = new ApplicationContext();
		_ready.Set();

		Application.Run(_applicationContext);
	}

	private void Post(Action action)
	{
		var synchronizationContext = _synchronizationContext;

		if (synchronizationContext is null)
		{
			return;
		}

		synchronizationContext.Post(_ => action(), null);
	}

	private void ShowBannerCore(string title, string message, string buttonText)
	{
		_bannerForm ??= new ResetBannerForm(HandleResetClicked, HandleDismissClicked);
		_bannerForm.UpdateContent(title, message, buttonText);
		_bannerForm.Location = GetBottomRightLocation(_bannerForm.Size);

		if (_bannerForm.Visible)
		{
			_bannerForm.BringToFront();
			return;
		}

		_bannerForm.Show();
	}

	private void HandleResetClicked()
	{
		_bannerForm?.Hide();
		Task.Run(_onReset);
	}

	private void HandleDismissClicked()
	{
		_bannerForm?.Hide();
	}

	private static Point GetBottomRightLocation(Size size)
	{
		var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
		var x = Math.Max(area.Left, area.Right - size.Width - 16);
		var y = Math.Max(area.Top, area.Bottom - size.Height - 16);
		return new Point(x, y);
	}
}

internal sealed class ResetBannerForm : Form
{
	private readonly Action _onDismiss;
	private readonly Action _onReset;
	private readonly Button _dismissButton;
	private readonly Button _resetButton;
	private readonly Label _messageLabel;
	private readonly Label _titleLabel;

	public ResetBannerForm(Action onReset, Action onDismiss)
	{
		_onDismiss = onDismiss;
		_onReset = onReset;

		AutoScaleMode = AutoScaleMode.Font;
		BackColor = Color.FromArgb(33, 37, 41);
		ClientSize = new Size(360, 136);
		ControlBox = false;
		FormBorderStyle = FormBorderStyle.FixedSingle;
		MaximizeBox = false;
		MinimizeBox = false;
		ShowInTaskbar = false;
		StartPosition = FormStartPosition.Manual;
		TopMost = true;

		_titleLabel = new Label
		{
			AutoEllipsis = true,
			ForeColor = Color.White,
			Font = new Font("Segoe UI", 11, FontStyle.Bold),
			Location = new Point(16, 14),
			Size = new Size(328, 24)
		};

		_messageLabel = new Label
		{
			ForeColor = Color.Gainsboro,
			Font = new Font("Segoe UI", 9),
			Location = new Point(16, 44),
			Size = new Size(328, 42)
		};

		_resetButton = new Button
		{
			AutoSize = false,
			BackColor = Color.FromArgb(255, 193, 7),
			FlatStyle = FlatStyle.Flat,
			Font = new Font("Segoe UI", 9, FontStyle.Bold),
			ForeColor = Color.Black,
			Location = new Point(16, 94),
			Size = new Size(120, 30),
			Text = "Reset timer",
			UseVisualStyleBackColor = false
		};

		_resetButton.FlatAppearance.BorderSize = 0;
		_resetButton.Click += (_, _) => _onReset();

		_dismissButton = new Button
		{
			AutoSize = false,
			BackColor = Color.FromArgb(73, 80, 87),
			FlatStyle = FlatStyle.Flat,
			Font = new Font("Segoe UI", 9, FontStyle.Regular),
			ForeColor = Color.White,
			Location = new Point(144, 94),
			Size = new Size(96, 30),
			Text = "Dismiss",
			UseVisualStyleBackColor = false
		};

		_dismissButton.FlatAppearance.BorderSize = 0;
		_dismissButton.Click += (_, _) => _onDismiss();

		Controls.Add(_titleLabel);
		Controls.Add(_messageLabel);
		Controls.Add(_resetButton);
		Controls.Add(_dismissButton);
	}

	protected override bool ShowWithoutActivation => true;

	public void UpdateContent(string title, string message, string buttonText)
	{
		_titleLabel.Text = title;
		_messageLabel.Text = message;
		_resetButton.Text = buttonText;
	}
}