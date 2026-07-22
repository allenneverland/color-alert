using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Windows;

namespace ColorAlert;

[SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "WPF owns the application lifetime; OnExit releases all wait handles.")]
public partial class App : System.Windows.Application
{
    private const string MutexName = @"Local\ColorAlert.SingleInstance";
    private const string ActivationEventName = @"Local\ColorAlert.Activate";

    private Mutex? _singleInstanceMutex;
    private EventWaitHandle? _activationEvent;
    private RegisteredWaitHandle? _activationRegistration;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _activationEvent = new EventWaitHandle(
            initialState: false,
            EventResetMode.AutoReset,
            ActivationEventName);
        _singleInstanceMutex = new Mutex(initiallyOwned: true, MutexName, out var isFirstInstance);

        if (!isFirstInstance)
        {
            _activationEvent.Set();
            _activationEvent.Dispose();
            _activationEvent = null;
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
            Shutdown();
            return;
        }

        _activationRegistration = ThreadPool.RegisterWaitForSingleObject(
            _activationEvent,
            static (state, timedOut) =>
            {
                if (!timedOut && state is App app)
                {
                    app.Dispatcher.BeginInvoke(app.RestoreMainWindow);
                }
            },
            this,
            Timeout.Infinite,
            executeOnlyOnce: false);

        var mainWindow = new MainWindow();
        MainWindow = mainWindow;
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _activationRegistration?.Unregister(null);
        _activationEvent?.Dispose();

        if (_singleInstanceMutex is not null)
        {
            try
            {
                _singleInstanceMutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // The mutex may already have been released during abnormal shutdown.
            }

            _singleInstanceMutex.Dispose();
        }

        base.OnExit(e);
    }

    private void RestoreMainWindow()
    {
        if (MainWindow is MainWindow mainWindow)
        {
            mainWindow.RestoreFromTray();
        }
    }
}
