using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using ColorAlert.Core;
using ColorAlert.Interop;
using ColorAlert.Services;
using Brush = System.Windows.Media.Brush;
using Button = System.Windows.Controls.Button;
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
    private readonly RegionOutlineOverlay _regionOutlineOverlay = new();
    private readonly DispatcherTimer _saveTimer;
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Forms.ContextMenuStrip _trayMenu;
    private readonly Forms.ToolStripMenuItem _trayStartItem;
    private readonly Forms.ToolStripMenuItem _trayPauseItem;
    private readonly Dictionary<Guid, RegionDisplayState> _regionStates = [];
    private readonly Dictionary<Guid, RegionRowControls> _regionRowControls = [];

    private MonitorController? _monitorController;
    private AppSettings _settings = new();
    private HwndSource? _windowSource;
    private bool _sessionNotificationRegistered;
    private bool _resumeAfterSessionUnlock;
    private bool _initialized;
    private bool _uiReady;
    private bool _updatingControls;
    private bool _exitRequested;
    private bool _trayHintShown;
    private bool _shutdownStarted;
    private bool _shutdownComplete;
    private bool _regionOperationInProgress;
    private bool _suppressRegionOutline;
    private MonitorStatus _lastDisplayedStatus = MonitorStatus.Idle;
    private string? _lastStatusError;
    private CancellationTokenSource? _locatorCancellation;

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
        var addRegionItem = new Forms.ToolStripMenuItem("新增監看區域");
        var showItem = new Forms.ToolStripMenuItem("顯示主畫面");
        var exitItem = new Forms.ToolStripMenuItem("退出");

        _trayStartItem.Click += (_, _) => Dispatch(StartMonitoringAsync);
        _trayPauseItem.Click += (_, _) => Dispatch(PauseMonitoringAsync);
        addRegionItem.Click += (_, _) => Dispatch(AddRegionAsync);
        showItem.Click += (_, _) => RestoreFromTray();
        exitItem.Click += (_, _) => Dispatch(RequestExitAsync);

        _trayMenu = new Forms.ContextMenuStrip();
        _trayMenu.Items.AddRange([
            _trayStartItem,
            _trayPauseItem,
            addRegionItem,
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

    internal void AllowSystemExit() => _exitRequested = true;

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
            _monitorController.RegionStatusChanged += MonitorController_RegionStatusChanged;

            _settings = (await _settingsStore.LoadAsync()).Normalize();
            _monitorController.SynchronizeRegions(_settings.Regions);
            InitializeRegionStates();
            ApplySettingsToControls();
            ValidateRegions();
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
        if (WindowState == WindowState.Minimized)
        {
            HideToTray();
        }
    }

    private async void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_shutdownComplete)
        {
            return;
        }

        e.Cancel = true;
        if (!_exitRequested)
        {
            HideToTray();
            return;
        }

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

    private async void AddRegionButton_Click(object sender, RoutedEventArgs e) =>
        await AddRegionAsync();

    private async void LocateRegionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: Guid regionId })
        {
            await LocateRegionAsync(regionId);
        }
    }

    private async void ReselectRegionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: Guid regionId })
        {
            await ReselectRegionAsync(regionId);
        }
    }

    private async void DeleteRegionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: Guid regionId })
        {
            await DeleteRegionAsync(regionId);
        }
    }

    private async void TestSoundButton_Click(object sender, RoutedEventArgs e)
    {
        TestSoundButton.IsEnabled = false;
        try
        {
            await _alertPlayer.PlayAsync(_settings.AlertMode);
        }
        finally
        {
            TestSoundButton.IsEnabled = true;
        }
    }

    private void ColorSensitivitySlider_ValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_uiReady || _updatingControls)
        {
            return;
        }

        var detection = _settings.Detection with
        {
            ColorSensitivity = (int)Math.Round(ColorSensitivitySlider.Value),
        };

        _settings = _settings with { Detection = detection.Normalize() };
        _monitorController?.UpdateSettings(_settings.Detection);
        UpdateDetectionDisplay();
        ScheduleSave();
    }

    private void AreaSensitivitySlider_ValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_uiReady || _updatingControls)
        {
            return;
        }

        var detection = _settings.Detection with
        {
            AreaSensitivity = (int)Math.Round(AreaSensitivitySlider.Value),
        };

        _settings = _settings with { Detection = detection.Normalize() };
        _monitorController?.UpdateSettings(_settings.Detection);
        UpdateDetectionDisplay();
        ScheduleSave();
    }

    private void ShowRegionOverlayCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (!_uiReady || _updatingControls)
        {
            return;
        }

        _settings = _settings with
        {
            ShowRegionOverlay = ShowRegionOverlayCheckBox.IsChecked == true,
        };
        UpdateRegionOutline();
        ScheduleSave();
    }

    private void AlertModeRadioButton_Checked(object sender, RoutedEventArgs e)
    {
        if (!_uiReady || _updatingControls)
        {
            return;
        }

        var alertMode = ThreeSoundsRadioButton.IsChecked == true
            ? AlertRepeatMode.ThreeTimes
            : AlertRepeatMode.Once;
        _settings = _settings with { AlertMode = alertMode };
        _monitorController?.UpdateAlertMode(alertMode);
        ScheduleSave();
    }

    private async Task StartMonitoringAsync()
    {
        if (_monitorController is null || _regionOperationInProgress)
        {
            return;
        }

        if (_settings.Regions.Length == 0)
        {
            RestoreFromTray();
            System.Windows.MessageBox.Show(
                this,
                "請先新增至少一個監看區域。",
                "尚未選取區域",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        ValidateRegions();
        var invalidRegions = _settings.Regions
            .Where(definition => !DisplayService.IsAvailable(definition.Bounds))
            .ToArray();
        if (invalidRegions.Length > 0)
        {
            RestoreFromTray();
            System.Windows.MessageBox.Show(
                this,
                "部分監看區域已超出目前顯示器範圍，請重新選取或刪除標示的項目。",
                "監看區域無效",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        try
        {
            SetRegionOperationInProgress(true);
            _monitorController.SynchronizeRegions(_settings.Regions);
            await _monitorController.StartAsync(
                _settings.Regions,
                _settings.Detection,
                _settings.AlertMode);
            await PersistSettingsAsync(showError: false);
        }
        catch (Exception exception)
        {
            ApplyStatus(MonitorStatus.Error, exception.Message);
            RestoreFromTray();
            System.Windows.MessageBox.Show(
                this,
                $"無法開始監看：\n\n{exception.Message}",
                "開始失敗",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            SetRegionOperationInProgress(false);
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

    private async Task LocateRegionAsync(Guid regionId)
    {
        if (_monitorController is null || _regionOperationInProgress)
        {
            return;
        }

        var regionIndex = Array.FindIndex(
            _settings.Regions,
            region => region.Id == regionId);
        if (regionIndex < 0)
        {
            return;
        }

        var definition = _settings.Regions[regionIndex];
        if (!DisplayService.IsAvailable(definition.Bounds))
        {
            ValidateRegions();
            return;
        }

        var shouldResume = _monitorController.IsRunning;
        var wasVisible = IsVisible;
        var locatorCancellation = new CancellationTokenSource();
        _locatorCancellation = locatorCancellation;
        Exception? failure = null;

        SetRegionOperationInProgress(true);
        try
        {
            if (shouldResume)
            {
                await _monitorController.PauseAsync(locatorCancellation.Token);
            }

            _suppressRegionOutline = true;
            _regionOutlineOverlay.Hide();
            Hide();
            ShowInTaskbar = false;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            locatorCancellation.Token.ThrowIfCancellationRequested();

            _regionOutlineOverlay.ShowLocator(
                definition.Bounds,
                $"區域 {regionIndex + 1}");
            await Task.Delay(TimeSpan.FromSeconds(2), locatorCancellation.Token);
        }
        catch (OperationCanceledException) when (locatorCancellation.IsCancellationRequested)
        {
            // Expected when the application exits during the temporary locator display.
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        finally
        {
            _regionOutlineOverlay.Hide();
            _suppressRegionOutline = false;

            if (ReferenceEquals(_locatorCancellation, locatorCancellation))
            {
                _locatorCancellation = null;
            }

            locatorCancellation.Dispose();

            if (!_shutdownStarted)
            {
                if (wasVisible)
                {
                    RestoreFromTray();
                }

                UpdateRegionOutline();
                SetRegionOperationInProgress(false);
                if (shouldResume)
                {
                    await StartMonitoringAsync();
                }
            }
        }

        if (failure is not null && !_shutdownStarted)
        {
            RestoreFromTray();
            System.Windows.MessageBox.Show(
                this,
                $"無法定位監看區域：\n\n{failure.Message}",
                "定位失敗",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async Task AddRegionAsync()
    {
        if (_monitorController is null || _regionOperationInProgress)
        {
            return;
        }

        if (_settings.Regions.Length >= AppSettings.MaximumRegionCount)
        {
            RestoreFromTray();
            System.Windows.MessageBox.Show(
                this,
                $"最多只能同時監看 {AppSettings.MaximumRegionCount} 個區域。",
                "已達上限",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var shouldResume = _monitorController.IsRunning;
        SetRegionOperationInProgress(true);
        try
        {
            if (shouldResume)
            {
                await _monitorController.PauseAsync();
            }

            var selectedRegion = await SelectRegionAsync();
            if (selectedRegion is not { } region)
            {
                return;
            }

            var definition = new MonitoredRegionDefinition { Bounds = region };
            _settings = _settings with
            {
                Regions = [.. _settings.Regions, definition],
            };
            _monitorController.SynchronizeRegions(_settings.Regions);
            _regionStates[definition.Id] = new RegionDisplayState
            {
                Status = RegionMonitorStatus.Monitoring,
            };
            RenderRegionList();
            UpdateRegionOutline();
            await PersistSettingsAsync(showError: true);
        }
        catch (Exception exception)
        {
            System.Windows.MessageBox.Show(
                this,
                $"無法新增監看區域：\n\n{exception.Message}",
                "新增失敗",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            SetRegionOperationInProgress(false);
            if (shouldResume && _settings.Regions.Length > 0)
            {
                await StartMonitoringAsync();
            }
        }
    }

    private async Task ReselectRegionAsync(Guid regionId)
    {
        if (_monitorController is null || _regionOperationInProgress)
        {
            return;
        }

        var existing = FindRegion(regionId);
        if (existing is null)
        {
            return;
        }

        var shouldResume = _monitorController.IsRunning;
        SetRegionOperationInProgress(true);
        try
        {
            if (shouldResume)
            {
                await _monitorController.PauseAsync();
            }

            var selectedRegion = await SelectRegionAsync();
            if (selectedRegion is not { } region)
            {
                return;
            }

            var replacement = existing with { Bounds = region };
            _settings = _settings with
            {
                Regions = _settings.Regions
                    .Select(region => region.Id == regionId ? replacement : region)
                    .ToArray(),
            };
            _monitorController.SynchronizeRegions(_settings.Regions);
            _regionStates[regionId] = new RegionDisplayState
            {
                Status = RegionMonitorStatus.Monitoring,
            };
            RenderRegionList();
            UpdateRegionOutline();
            await PersistSettingsAsync(showError: true);
        }
        catch (Exception exception)
        {
            System.Windows.MessageBox.Show(
                this,
                $"無法重新選取區域：\n\n{exception.Message}",
                "選取失敗",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            SetRegionOperationInProgress(false);
            if (shouldResume)
            {
                await StartMonitoringAsync();
            }
        }
    }

    private async Task DeleteRegionAsync(Guid regionId)
    {
        if (_monitorController is null || _regionOperationInProgress)
        {
            return;
        }

        var definition = FindRegion(regionId);
        if (definition is null)
        {
            return;
        }

        var result = System.Windows.MessageBox.Show(
            this,
            "確定要刪除這個監看區域嗎？",
            "刪除監看區域",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        var shouldResume = _monitorController.IsRunning;
        SetRegionOperationInProgress(true);
        try
        {
            if (shouldResume)
            {
                await _monitorController.PauseAsync();
            }

            _settings = _settings with
            {
                Regions = _settings.Regions.Where(region => region.Id != regionId).ToArray(),
            };
            _monitorController.SynchronizeRegions(_settings.Regions);
            _regionStates.Remove(regionId);
            RenderRegionList();
            UpdateRegionOutline();
            await PersistSettingsAsync(showError: true);
            if (_settings.Regions.Length == 0)
            {
                ApplyStatus(MonitorStatus.Paused);
            }
        }
        finally
        {
            SetRegionOperationInProgress(false);
            if (shouldResume && _settings.Regions.Length > 0)
            {
                await StartMonitoringAsync();
            }
        }
    }

    private async Task<ScreenRegion?> SelectRegionAsync()
    {
        _suppressRegionOutline = true;
        _regionOutlineOverlay.Hide();
        Hide();
        ShowInTaskbar = false;
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

        try
        {
            var overlay = new SelectionOverlayWindow();
            return overlay.ShowDialog() == true &&
                overlay.SelectedRegion is { IsValid: true } region
                ? region
                : null;
        }
        finally
        {
            _suppressRegionOutline = false;
            RestoreFromTray();
            UpdateRegionOutline();
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

    private void MonitorController_RegionStatusChanged(
        object? sender,
        RegionStatusChangedEventArgs e)
    {
        _ = Dispatcher.BeginInvoke(
            () => ApplyRegionStatus(e),
            DispatcherPriority.Background);
    }

    private void ApplyRegionStatus(RegionStatusChangedEventArgs e)
    {
        var definition = FindRegion(e.RegionId);
        if (definition is null)
        {
            return;
        }

        var status = DisplayService.IsAvailable(definition.Bounds)
            ? e.Status
            : RegionMonitorStatus.Invalid;
        _regionStates.TryGetValue(e.RegionId, out var previous);
        var statusChanged = previous?.Status != status;

        _regionStates[e.RegionId] = new RegionDisplayState
        {
            Status = status,
            YellowRatio = e.YellowRatio,
            BlueRatio = e.BlueRatio,
            YellowPresent = e.YellowPresent,
            BluePresent = e.BluePresent,
            ErrorMessage = e.ErrorMessage,
        };
        UpdateRegionRow(e.RegionId);
        UpdateSampleSummary();

        if (statusChanged)
        {
            UpdateRegionOutline();
        }
    }

    private void ApplySettingsToControls()
    {
        _updatingControls = true;
        try
        {
            ColorSensitivitySlider.Value = _settings.Detection.ColorSensitivity;
            AreaSensitivitySlider.Value = _settings.Detection.AreaSensitivity;
            ShowRegionOverlayCheckBox.IsChecked = _settings.ShowRegionOverlay;
            OneSoundRadioButton.IsChecked = _settings.AlertMode == AlertRepeatMode.Once;
            ThreeSoundsRadioButton.IsChecked = _settings.AlertMode == AlertRepeatMode.ThreeTimes;
        }
        finally
        {
            _updatingControls = false;
        }

        _monitorController?.UpdateSettings(_settings.Detection);
        _monitorController?.UpdateAlertMode(_settings.AlertMode);
        UpdateDetectionDisplay();
        RenderRegionList();
    }

    private void UpdateDetectionDisplay()
    {
        ColorSensitivityValueText.Text = FormatSensitivity(
            _settings.Detection.ColorSensitivity);
        AreaSensitivityValueText.Text = FormatSensitivity(
            _settings.Detection.AreaSensitivity);
    }

    private static string FormatSensitivity(int sensitivity) => sensitivity switch
    {
        <= 33 => "低",
        <= 66 => "標準",
        _ => "高",
    };

    private void InitializeRegionStates()
    {
        _regionStates.Clear();
        foreach (var definition in _settings.Regions)
        {
            _regionStates[definition.Id] = new RegionDisplayState
            {
                Status = DisplayService.IsAvailable(definition.Bounds)
                    ? RegionMonitorStatus.Monitoring
                    : RegionMonitorStatus.Invalid,
            };
        }
    }

    private void RenderRegionList()
    {
        RegionListPanel.Children.Clear();
        _regionRowControls.Clear();
        RegionCountText.Text = $"{_settings.Regions.Length} / {AppSettings.MaximumRegionCount}";
        AddRegionButton.IsEnabled = !_regionOperationInProgress &&
            _settings.Regions.Length < AppSettings.MaximumRegionCount;

        if (_settings.Regions.Length == 0)
        {
            RegionListPanel.Children.Add(new TextBlock
            {
                Text = "尚未加入區域。按「新增區域」開始框選。",
                Foreground = FindBrush("MutedTextBrush"),
                FontSize = 14,
                Padding = new Thickness(0, 8, 0, 8),
            });
            UpdateSampleSummary();
            return;
        }

        for (var index = 0; index < _settings.Regions.Length; index++)
        {
            var definition = _settings.Regions[index];
            RegionListPanel.Children.Add(CreateRegionRow(definition, index + 1));
        }

        UpdateSampleSummary();
    }

    private Border CreateRegionRow(MonitoredRegionDefinition definition, int index)
    {
        var root = new Border
        {
            Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(249, 250, 251)),
            BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(234, 236, 240)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Margin = new Thickness(0, 0, 0, 8),
            Padding = new Thickness(12),
        };

        var panel = new StackPanel();
        root.Child = panel;

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var indicator = new Border
        {
            Width = 9,
            Height = 9,
            Margin = new Thickness(0, 3, 8, 0),
            VerticalAlignment = VerticalAlignment.Top,
            CornerRadius = new CornerRadius(5),
        };
        Grid.SetColumn(indicator, 0);
        header.Children.Add(indicator);

        var title = new TextBlock
        {
            Text = $"區域 {index}",
            FontWeight = FontWeights.SemiBold,
            Foreground = FindBrush("TextBrush"),
            FontSize = 14,
        };
        Grid.SetColumn(title, 1);
        header.Children.Add(title);

        var statusText = new TextBlock
        {
            Foreground = FindBrush("MutedTextBrush"),
            FontSize = 12,
        };
        Grid.SetColumn(statusText, 2);
        header.Children.Add(statusText);
        panel.Children.Add(header);

        panel.Children.Add(new TextBlock
        {
            Text = FormatRegion(definition.Bounds),
            Margin = new Thickness(17, 5, 0, 0),
            Foreground = FindBrush("MutedTextBrush"),
            FontSize = 12,
        });

        var footer = new Grid { Margin = new Thickness(17, 9, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition());
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var colorRatioText = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = FindBrush("MutedTextBrush"),
            FontSize = 12,
        };
        footer.Children.Add(colorRatioText);

        var actions = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
        };
        actions.Children.Add(CreateRegionActionButton(
            "定位",
            definition.Id,
            LocateRegionButton_Click,
            DisplayService.IsAvailable(definition.Bounds)));
        actions.Children.Add(CreateRegionActionButton("重新選取", definition.Id, ReselectRegionButton_Click));
        actions.Children.Add(CreateRegionActionButton("刪除", definition.Id, DeleteRegionButton_Click));
        Grid.SetColumn(actions, 1);
        footer.Children.Add(actions);
        panel.Children.Add(footer);

        _regionRowControls[definition.Id] = new RegionRowControls(
            indicator,
            statusText,
            colorRatioText);
        UpdateRegionRow(definition.Id);
        return root;
    }

    private Button CreateRegionActionButton(
        string text,
        Guid regionId,
        RoutedEventHandler handler,
        bool isAvailable = true)
    {
        var button = new Button
        {
            Content = text,
            Tag = regionId,
            Margin = new Thickness(6, 0, 0, 0),
            IsEnabled = !_regionOperationInProgress && isAvailable,
            Style = (Style)FindResource("SmallButtonStyle"),
        };
        button.Click += handler;
        return button;
    }

    private void UpdateRegionRow(Guid regionId)
    {
        if (!_regionRowControls.TryGetValue(regionId, out var controls) ||
            !_regionStates.TryGetValue(regionId, out var state))
        {
            return;
        }

        var (statusText, color) = state.Status switch
        {
            RegionMonitorStatus.Monitoring => ("待命", System.Windows.Media.Color.FromRgb(18, 183, 106)),
            RegionMonitorStatus.Alerted => ("已偵測", System.Windows.Media.Color.FromRgb(247, 144, 9)),
            RegionMonitorStatus.Error => ("擷取錯誤", System.Windows.Media.Color.FromRgb(240, 68, 56)),
            RegionMonitorStatus.Invalid => ("需要處理", System.Windows.Media.Color.FromRgb(240, 68, 56)),
            _ => ("待命", System.Windows.Media.Color.FromRgb(18, 183, 106)),
        };

        controls.Indicator.Background = new SolidColorBrush(color);
        controls.StatusText.Text = statusText;
        controls.ColorRatioText.Text = state.Status switch
        {
            RegionMonitorStatus.Invalid => "已超出目前顯示器，請重新選取或刪除",
            RegionMonitorStatus.Error => state.ErrorMessage ?? "擷取失敗，稍後會自動重試",
            _ => FormatColorRatios(state),
        };
    }

    private void UpdateSampleSummary()
    {
        if (_settings.Regions.Length == 0)
        {
            SampleText.Text = "尚未加入監看區域";
            return;
        }

        var states = _settings.Regions
            .Select(region => _regionStates.GetValueOrDefault(region.Id))
            .Where(state => state is not null)
            .ToArray();
        var maximumYellow = states.Length == 0
            ? 0d
            : states.Max(state => state!.YellowRatio);
        var maximumBlue = states.Length == 0
            ? 0d
            : states.Max(state => state!.BlueRatio);
        var alertedCount = states.Count(state => state!.Status == RegionMonitorStatus.Alerted);
        SampleText.Text = alertedCount > 0
            ? $"監看 {_settings.Regions.Length} 個區域 · {alertedCount} 個偵測到目標色 · 黃 {maximumYellow:P2} · 藍 {maximumBlue:P2}"
            : $"監看 {_settings.Regions.Length} 個區域 · 黃 {maximumYellow:P2} · 藍 {maximumBlue:P2}";
    }

    private void ApplyStatus(MonitorStatus status, string? errorMessage = null)
    {
        _lastDisplayedStatus = status;
        _lastStatusError = errorMessage;
        var (text, color) = status switch
        {
            MonitorStatus.Monitoring => ("監看中 — 等待黃色或藍色", System.Windows.Media.Color.FromRgb(18, 183, 106)),
            MonitorStatus.Alerted => ("已偵測到目標色 — 持續監看新增面積", System.Windows.Media.Color.FromRgb(247, 144, 9)),
            MonitorStatus.Paused => ("已暫停", System.Windows.Media.Color.FromRgb(102, 112, 133)),
            MonitorStatus.Error => ($"部分區域擷取錯誤 — {errorMessage ?? "稍後重試"}", System.Windows.Media.Color.FromRgb(240, 68, 56)),
            _ => ("尚未開始", System.Windows.Media.Color.FromRgb(152, 162, 179)),
        };

        StatusText.Text = text;
        StatusIndicator.Background = new SolidColorBrush(color);

        var isRunning = _monitorController?.IsRunning == true;
        var regionsCanStart = _settings.Regions.Length > 0 &&
            _settings.Regions.All(region => DisplayService.IsAvailable(region.Bounds));
        StartButton.IsEnabled = !isRunning &&
            !_regionOperationInProgress &&
            _monitorController is not null &&
            regionsCanStart;
        PauseButton.IsEnabled = isRunning && !_regionOperationInProgress;
        _trayStartItem.Enabled = !isRunning &&
            !_regionOperationInProgress &&
            _monitorController is not null &&
            regionsCanStart;
        _trayPauseItem.Enabled = isRunning && !_regionOperationInProgress;
        _notifyIcon.Text = status switch
        {
            MonitorStatus.Monitoring => "Color Alert — 監看中",
            MonitorStatus.Alerted => "Color Alert — 已提示",
            MonitorStatus.Paused => "Color Alert — 已暫停",
            MonitorStatus.Error => "Color Alert — 部分區域擷取錯誤",
            _ => "Color Alert — 尚未開始",
        };
    }

    private void ValidateRegions()
    {
        foreach (var definition in _settings.Regions)
        {
            if (!_regionStates.TryGetValue(definition.Id, out var state))
            {
                state = new RegionDisplayState();
                _regionStates[definition.Id] = state;
            }

            if (!DisplayService.IsAvailable(definition.Bounds))
            {
                state.Status = RegionMonitorStatus.Invalid;
            }
            else if (state.Status == RegionMonitorStatus.Invalid)
            {
                state.Status = RegionMonitorStatus.Monitoring;
            }
        }

        RenderRegionList();
        UpdateRegionOutline();
    }

    private async Task HandleDisplayChangeAsync()
    {
        if (_monitorController is null)
        {
            return;
        }

        if (_monitorController.IsRunning)
        {
            await _monitorController.PauseAsync();
        }

        _monitorController.ResetDetections();
        foreach (var definition in _settings.Regions)
        {
            _regionStates[definition.Id] = new RegionDisplayState
            {
                Status = DisplayService.IsAvailable(definition.Bounds)
                    ? RegionMonitorStatus.Monitoring
                    : RegionMonitorStatus.Invalid,
            };
        }

        ValidateRegions();
        _notifyIcon.ShowBalloonTip(
            3_000,
            "監看已暫停",
            "顯示器配置已改變；偵測狀態已重設，無效區域需要重新選取或刪除。",
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
        await StartMonitoringAsync();
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

    private void UpdateRegionOutline()
    {
        if (_suppressRegionOutline || !_settings.ShowRegionOverlay)
        {
            _regionOutlineOverlay.Hide();
            return;
        }

        var items = _settings.Regions.Select(definition => new RegionOutlineItem(
            definition.Bounds,
            _regionStates.TryGetValue(definition.Id, out var state)
                ? state.Status
                : RegionMonitorStatus.Monitoring));
        _regionOutlineOverlay.Show(items);
    }

    private void SetRegionOperationInProgress(bool isInProgress)
    {
        _regionOperationInProgress = isInProgress;
        AddRegionButton.IsEnabled = !isInProgress &&
            _settings.Regions.Length < AppSettings.MaximumRegionCount;
        ApplyStatus(_lastDisplayedStatus, _lastStatusError);
        RenderRegionList();
    }

    private MonitoredRegionDefinition? FindRegion(Guid regionId) =>
        _settings.Regions.FirstOrDefault(region => region.Id == regionId);

    private Brush FindBrush(string resourceKey) => (Brush)FindResource(resourceKey);

    private static string FormatRegion(ScreenRegion region) =>
        $"X {region.X:N0}, Y {region.Y:N0} · {region.Width:N0} × {region.Height:N0} px";

    private static string FormatColorRatios(RegionDisplayState state)
    {
        var detected = (state.YellowPresent, state.BluePresent) switch
        {
            (true, true) => " · 已偵測：黃色、藍色",
            (true, false) => " · 已偵測：黃色",
            (false, true) => " · 已偵測：藍色",
            _ => string.Empty,
        };

        return $"黃色 {state.YellowRatio:P2} · 藍色 {state.BlueRatio:P2}{detected}";
    }

    private void HideToTray()
    {
        ShowInTaskbar = false;
        Hide();

        if (_trayHintShown)
        {
            return;
        }

        _trayHintShown = true;
        _notifyIcon.ShowBalloonTip(
            timeout: 2_000,
            tipTitle: "Color Alert 仍在執行",
            tipText: "可從系統匣開始、暫停、新增區域或退出。",
            tipIcon: Forms.ToolTipIcon.Info);
    }

    private Task RequestExitAsync()
    {
        _exitRequested = true;
        Close();
        return Task.CompletedTask;
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
        if (_locatorCancellation is not null)
        {
            await _locatorCancellation.CancelAsync();
        }

        try
        {
            await PersistSettingsAsync(showError: false);
            if (_monitorController is not null)
            {
                _monitorController.StatusChanged -= MonitorController_StatusChanged;
                _monitorController.RegionStatusChanged -= MonitorController_RegionStatusChanged;
                await _monitorController.DisposeAsync();
            }
        }
        finally
        {
            _regionOutlineOverlay.Dispose();
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

    private sealed class RegionDisplayState
    {
        internal RegionMonitorStatus Status { get; set; } = RegionMonitorStatus.Monitoring;

        internal double YellowRatio { get; set; }

        internal double BlueRatio { get; set; }

        internal bool YellowPresent { get; set; }

        internal bool BluePresent { get; set; }

        internal string? ErrorMessage { get; set; }
    }

    private sealed record RegionRowControls(
        Border Indicator,
        TextBlock StatusText,
        TextBlock ColorRatioText);
}
