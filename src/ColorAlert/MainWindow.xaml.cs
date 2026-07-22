using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using ColorAlert.Core;
using ColorAlert.Interop;
using ColorAlert.Services;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace ColorAlert;

[SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "WPF owns the window lifetime; ShutdownAsync releases all native and managed resources.")]
public partial class MainWindow : Window
{
    private readonly SettingsStore _settingsStore = new();
    private readonly SystemAlertPlayer _alertPlayer = new();
    private readonly DispatcherTimer _saveTimer;
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Forms.ContextMenuStrip _trayMenu;
    private readonly Forms.ToolStripMenuItem _trayStartItem;
    private readonly Forms.ToolStripMenuItem _trayPauseItem;

    private MonitorController? _monitorController;
    private AppSettings _settings = new();
    private HwndSource? _windowSource;
    private bool _sessionNotificationRegistered;
    private bool _resumeAfterSessionUnlock;
    private bool _initialized;
    private bool _uiReady;
    private bool _updatingControls;
    private bool _regionInvalid;
    private bool _shutdownStarted;
    private bool _shutdownComplete;

    public MainWindow()
    {
        InitializeComponent();

        _saveTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(450),
        };
        _saveTimer.Tick += SaveTimer_Tick;

        _trayStartItem = new Forms.ToolStripMenuItem("開始監看");
        _trayPauseItem = new Forms.ToolStripMenuItem("暫停") { Enabled = false };
        var selectItem = new Forms.ToolStripMenuItem("重新選取區域");
        var showItem = new Forms.ToolStripMenuItem("顯示主畫面");
        var exitItem = new Forms.ToolStripMenuItem("退出");

        _trayStartItem.Click += (_, _) => Dispatch(StartMonitoringAsync);
        _trayPauseItem.Click += (_, _) => Dispatch(PauseMonitoringAsync);
        selectItem.Click += (_, _) => Dispatch(SelectRegionAsync);
        showItem.Click += (_, _) => RestoreFromTray();
        exitItem.Click += (_, _) => Dispatch(() =>
        {
            Close();
            return Task.CompletedTask;
        });

        _trayMenu = new Forms.ContextMenuStrip();
        _trayMenu.Items.AddRange([
            _trayStartItem,
            _trayPauseItem,
            selectItem,
            new Forms.ToolStripSeparator(),
            showItem,
            exitItem,
        ]);

        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = Drawing.SystemIcons.Information,
            Text = "Color Alert — 尚未開始",
            ContextMenuStrip = _trayMenu,
            Visible = true,
        };
        _notifyIcon.DoubleClick += (_, _) => RestoreFromTray();

        _uiReady = true;
    }

    public void RestoreFromTray()
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(RestoreFromTray);
            return;
        }

        if (_shutdownStarted)
        {
            return;
        }

        ShowInTaskbar = true;
        Show();
        WindowState = WindowState.Normal;
        Activate();

        var handle = new WindowInteropHelper(this).Handle;
        if (handle != nint.Zero)
        {
            _ = NativeMethods.SetForegroundWindow(handle);
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var handle = new WindowInteropHelper(this).Handle;
        _windowSource = HwndSource.FromHwnd(handle);
        _windowSource?.AddHook(WindowMessageHook);
        _sessionNotificationRegistered = NativeMethods.WTSRegisterSessionNotification(
            handle,
            NativeMethods.NotifyForThisSession);
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;

        try
        {
            _monitorController = new MonitorController(
                new GdiScreenSampler(),
                _alertPlayer);
            _monitorController.StatusChanged += MonitorController_StatusChanged;
            _monitorController.Sampled += MonitorController_Sampled;

            _settings = await _settingsStore.LoadAsync();
            ApplySettingsToControls();
            ValidateSavedRegion();
            ApplyStatus(MonitorStatus.Idle);
        }
        catch (Exception exception)
        {
            ApplyStatus(MonitorStatus.Error, exception.Message);
            StartButton.IsEnabled = false;
            PauseButton.IsEnabled = false;
            System.Windows.MessageBox.Show(
                this,
                $"Color Alert 無法初始化：\n\n{exception.Message}",
                "啟動失敗",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState != WindowState.Minimized)
        {
            return;
        }

        ShowInTaskbar = false;
        Hide();
        _notifyIcon.ShowBalloonTip(
            timeout: 2_000,
            tipTitle: "Color Alert 仍在執行",
            tipText: "可從系統匣暫停、重新選區或退出。",
            tipIcon: Forms.ToolTipIcon.Info);
    }

    private async void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_shutdownComplete)
        {
            return;
        }

        e.Cancel = true;
        if (_shutdownStarted)
        {
            return;
        }

        _shutdownStarted = true;
        await ShutdownAsync();
        _shutdownComplete = true;
        Close();
    }

    private async void StartButton_Click(object sender, RoutedEventArgs e) =>
        await StartMonitoringAsync();

    private async void PauseButton_Click(object sender, RoutedEventArgs e) =>
        await PauseMonitoringAsync();

    private async void SelectRegionButton_Click(object sender, RoutedEventArgs e) =>
        await SelectRegionAsync();

    private void TestSoundButton_Click(object sender, RoutedEventArgs e) =>
        _alertPlayer.Play();

    private void DetectionSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_uiReady || _updatingControls)
        {
            return;
        }

        var detection = _settings.Detection with
        {
            BlackTolerance = (int)Math.Round(ToleranceSlider.Value),
            TriggerRatio = RatioSlider.Value / 100d,
        };

        _settings = _settings with { Detection = detection.Normalize() };
        _monitorController?.UpdateSettings(_settings.Detection);
        UpdateDetectionLabels();
        ScheduleSave();
    }

    private async Task StartMonitoringAsync()
    {
        if (_monitorController is null)
        {
            return;
        }

        if (_settings.Region is not { IsValid: true } region)
        {
            RestoreFromTray();
            System.Windows.MessageBox.Show(
                this,
                "請先選取要監看的螢幕區域。",
                "尚未選取區域",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (!DisplayService.IsAvailable(region))
        {
            _regionInvalid = true;
            UpdateRegionText();
            RestoreFromTray();
            System.Windows.MessageBox.Show(
                this,
                "原本的監看區域已不在目前顯示器範圍內，請重新選取。",
                "顯示器配置已變更",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        try
        {
            _regionInvalid = false;
            await _monitorController.StartAsync(region, _settings.Detection);
            await PersistSettingsAsync(showError: false);
        }
        catch (Exception exception)
        {
            ApplyStatus(MonitorStatus.Error, exception.Message);
        }
    }

    private async Task PauseMonitoringAsync()
    {
        if (_monitorController is null || !_monitorController.IsRunning)
        {
            return;
        }

        try
        {
            await _monitorController.PauseAsync();
        }
        catch (ObjectDisposedException)
        {
            // The application is already shutting down.
        }
    }

    private async Task SelectRegionAsync()
    {
        if (_monitorController is null)
        {
            return;
        }

        var shouldResumeOnCancel = _monitorController.IsRunning;
        if (shouldResumeOnCancel)
        {
            await _monitorController.PauseAsync();
        }

        Hide();
        ShowInTaskbar = false;
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

        ScreenRegion? selectedRegion = null;
        try
        {
            var overlay = new SelectionOverlayWindow();
            if (overlay.ShowDialog() == true)
            {
                selectedRegion = overlay.SelectedRegion;
            }
        }
        catch (Exception exception)
        {
            System.Windows.MessageBox.Show(
                $"無法開啟區域選取介面：\n\n{exception.Message}",
                "選取失敗",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            RestoreFromTray();
        }

        if (selectedRegion is { IsValid: true } region)
        {
            _settings = _settings with { Region = region };
            _regionInvalid = false;
            _monitorController.ResetDetection();
            UpdateRegionText();
            ApplyStatus(MonitorStatus.Paused);
            SampleText.Text = "目前非黑比例：—";
            await PersistSettingsAsync(showError: true);
            return;
        }

        if (shouldResumeOnCancel && _settings.Region is { IsValid: true } previousRegion)
        {
            await _monitorController.StartAsync(previousRegion, _settings.Detection);
        }
    }

    private void MonitorController_StatusChanged(
        object? sender,
        MonitorStatusChangedEventArgs e)
    {
        _ = Dispatcher.BeginInvoke(
            () => ApplyStatus(e.Status, e.ErrorMessage),
            DispatcherPriority.Background);
    }

    private void MonitorController_Sampled(object? sender, SampleStatistics statistics)
    {
        _ = Dispatcher.BeginInvoke(
            () => SampleText.Text = $"目前非黑比例：{statistics.NonBlackRatio:P2}",
            DispatcherPriority.Background);
    }

    private void ApplySettingsToControls()
    {
        _updatingControls = true;
        try
        {
            ToleranceSlider.Value = _settings.Detection.BlackTolerance;
            RatioSlider.Value = _settings.Detection.TriggerRatio * 100d;
        }
        finally
        {
            _updatingControls = false;
        }

        UpdateDetectionLabels();
        UpdateRegionText();
    }

    private void UpdateDetectionLabels()
    {
        ToleranceValueText.Text = $"{_settings.Detection.BlackTolerance} / 255";
        RatioValueText.Text = $"{_settings.Detection.TriggerRatio:P1}";
    }

    private void UpdateRegionText()
    {
        if (_settings.Region is not { IsValid: true } region)
        {
            RegionText.Text = "尚未選取";
            return;
        }

        var invalidSuffix = _regionInvalid ? "（已超出目前顯示器，請重新選取）" : string.Empty;
        RegionText.Text = $"X {region.X:N0}, Y {region.Y:N0} · {region.Width:N0} × {region.Height:N0} px{invalidSuffix}";
    }

    private void ApplyStatus(MonitorStatus status, string? errorMessage = null)
    {
        var (text, color) = status switch
        {
            MonitorStatus.Monitoring => ("監看中 — 等待非黑畫面", System.Windows.Media.Color.FromRgb(18, 183, 106)),
            MonitorStatus.Alerted => ("已提示 — 等待恢復黑色", System.Windows.Media.Color.FromRgb(247, 144, 9)),
            MonitorStatus.Paused => ("已暫停", System.Windows.Media.Color.FromRgb(102, 112, 133)),
            MonitorStatus.Error => ($"擷取錯誤 — {errorMessage ?? "稍後重試"}", System.Windows.Media.Color.FromRgb(240, 68, 56)),
            _ => ("尚未開始", System.Windows.Media.Color.FromRgb(152, 162, 179)),
        };

        StatusText.Text = text;
        StatusIndicator.Background = new SolidColorBrush(color);

        var isRunning = _monitorController?.IsRunning == true;
        StartButton.IsEnabled = !isRunning && _monitorController is not null;
        PauseButton.IsEnabled = isRunning;
        _trayStartItem.Enabled = !isRunning && _monitorController is not null;
        _trayPauseItem.Enabled = isRunning;
        _notifyIcon.Text = status switch
        {
            MonitorStatus.Monitoring => "Color Alert — 監看中",
            MonitorStatus.Alerted => "Color Alert — 已提示",
            MonitorStatus.Paused => "Color Alert — 已暫停",
            MonitorStatus.Error => "Color Alert — 擷取錯誤",
            _ => "Color Alert — 尚未開始",
        };
    }

    private void ValidateSavedRegion()
    {
        _regionInvalid = _settings.Region is { IsValid: true } region &&
            !DisplayService.IsAvailable(region);
        UpdateRegionText();
    }

    private async Task HandleDisplayChangeAsync()
    {
        ValidateSavedRegion();
        if (!_regionInvalid)
        {
            return;
        }

        if (_monitorController?.IsRunning == true)
        {
            await _monitorController.PauseAsync();
        }

        _notifyIcon.ShowBalloonTip(
            3_000,
            "監看已暫停",
            "顯示器配置已改變，請重新選取監看區域。",
            Forms.ToolTipIcon.Warning);
    }

    private async Task HandleSessionLockAsync(bool isLocked)
    {
        if (_monitorController is null)
        {
            return;
        }

        if (isLocked)
        {
            _resumeAfterSessionUnlock = _monitorController.IsRunning;
            if (_resumeAfterSessionUnlock)
            {
                await _monitorController.PauseAsync();
            }

            return;
        }

        if (!_resumeAfterSessionUnlock)
        {
            return;
        }

        _resumeAfterSessionUnlock = false;
        await Task.Delay(500);

        if (_settings.Region is { IsValid: true } region && DisplayService.IsAvailable(region))
        {
            await _monitorController.StartAsync(region, _settings.Detection);
        }
    }

    private nint WindowMessageHook(
        nint windowHandle,
        int message,
        nint wordParameter,
        nint longParameter,
        ref bool handled)
    {
        if (message == NativeMethods.WmDisplayChange)
        {
            Dispatch(HandleDisplayChangeAsync);
        }
        else if (message == NativeMethods.WmWtsSessionChange)
        {
            var sessionEvent = wordParameter.ToInt32();
            if (sessionEvent == NativeMethods.WtsSessionLock)
            {
                Dispatch(() => HandleSessionLockAsync(isLocked: true));
            }
            else if (sessionEvent == NativeMethods.WtsSessionUnlock)
            {
                Dispatch(() => HandleSessionLockAsync(isLocked: false));
            }
        }

        return nint.Zero;
    }

    private void ScheduleSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private async void SaveTimer_Tick(object? sender, EventArgs e)
    {
        _saveTimer.Stop();
        await PersistSettingsAsync(showError: false);
    }

    private async Task PersistSettingsAsync(bool showError)
    {
        try
        {
            await _settingsStore.SaveAsync(_settings);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            if (showError)
            {
                System.Windows.MessageBox.Show(
                    this,
                    $"無法保存設定：\n\n{exception.Message}",
                    "設定未保存",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
    }

    private async Task ShutdownAsync()
    {
        _saveTimer.Stop();

        try
        {
            await PersistSettingsAsync(showError: false);
            if (_monitorController is not null)
            {
                _monitorController.StatusChanged -= MonitorController_StatusChanged;
                _monitorController.Sampled -= MonitorController_Sampled;
                await _monitorController.DisposeAsync();
            }
        }
        finally
        {
            var handle = new WindowInteropHelper(this).Handle;
            if (_sessionNotificationRegistered && handle != nint.Zero)
            {
                _ = NativeMethods.WTSUnRegisterSessionNotification(handle);
            }

            _windowSource?.RemoveHook(WindowMessageHook);
            _settingsStore.Dispose();
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _trayMenu.Dispose();
        }
    }

    private void Dispatch(Func<Task> operation)
    {
        _ = Dispatcher.BeginInvoke(
            () => _ = RunDispatchedOperationAsync(operation),
            DispatcherPriority.Normal);
    }

    private static async Task RunDispatchedOperationAsync(Func<Task> operation)
    {
        try
        {
            await operation();
        }
        catch (ObjectDisposedException)
        {
            // The application is already shutting down.
        }
    }
}
