using System.Threading;
using System.Windows;
using SoftMochiPet.Services;

namespace SoftMochiPet;

public partial class App : System.Windows.Application
{
    private const string SingleInstanceMutexName = @"Local\SoftMochiPet.DesktopPet.SingleInstance.v2";
    private const string ActivationEventName = @"Local\SoftMochiPet.DesktopPet.Activate.v2";
    private const string BehaviorControlMarkerName = @"Local\SoftMochiPet.DesktopPet.BehaviorControl.v1";

    private Mutex? _singleInstanceMutex;
    private EventWaitHandle? _activationEvent;
    private EventWaitHandle? _behaviorControlMarker;
    private RegisteredWaitHandle? _activationWait;
    private bool _ownsMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Contains("--feibi-feature-target", StringComparer.Ordinal))
        {
            base.OnStartup(e);
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            if (FeatureTestTargetWindow.TryCreate(e.Args, out var target))
            {
                MainWindow = target;
                target!.Show();
            }
            else
            {
                Shutdown(1);
            }
            return;
        }

        var featureTest = e.Args.Contains("--feibi-feature-test", StringComparer.Ordinal);
        var behaviorControl = e.Args.Contains("--behavior-control", StringComparer.Ordinal);
        if (featureTest && behaviorControl)
        {
            Shutdown(1);
            return;
        }
        DispatcherUnhandledException += (_, args) =>
            DiagnosticsLog.Write("Unhandled dispatcher exception.", args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            DiagnosticsLog.Write("Unhandled application-domain exception.", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
            DiagnosticsLog.Write("Unobserved task exception.", args.Exception);
        DiagnosticsLog.WriteEvent(
            "ProcessStarted",
            ("Version", typeof(App).Assembly.GetName().Version),
            ("OS", Environment.OSVersion.VersionString),
            ("Architecture", System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture),
            ("Mode", featureTest ? "feibi-feature-test" : behaviorControl ? "behavior-control" : "native-delete-reaction"));
        _activationEvent = new EventWaitHandle(
            false,
            EventResetMode.AutoReset,
            ActivationEventName);
        _singleInstanceMutex = new Mutex(true, SingleInstanceMutexName, out _ownsMutex);
        if (!_ownsMutex)
        {
            var behaviorIsRunning = EventWaitHandle.TryOpenExisting(BehaviorControlMarkerName, out var existingBehaviorMarker);
            existingBehaviorMarker?.Dispose();
            if (behaviorControl || behaviorIsRunning)
            {
                System.Windows.MessageBox.Show(
                    "桌宠或行为控制版已经在运行。\n\n请先从托盘退出当前桌宠，再打开需要的版本。",
                    "啾糯桌宠 · 行为控制版",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                DiagnosticsLog.WriteEvent("BehaviorControlStartBlockedByExistingInstance");
                Environment.Exit(0);
            }
            if (featureTest)
            {
                System.Windows.MessageBox.Show(
                    "桌宠或专属功能测试已经在运行。\n\n请先从托盘退出当前桌宠，再打开“菲比啾比专属功能测试”。",
                    "啾糯桌宠 · 菲比啾比专属功能测试",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                DiagnosticsLog.WriteEvent("FeatureTestStartBlockedByExistingInstance");
                Environment.Exit(0);
            }
            _activationEvent.Set();
            DiagnosticsLog.WriteEvent("ExistingInstanceActivationRequested");
            Environment.Exit(0);
        }

        if (behaviorControl)
            _behaviorControlMarker = new EventWaitHandle(false, EventResetMode.ManualReset, BehaviorControlMarkerName);

        base.OnStartup(e);
        _activationWait = ThreadPool.RegisterWaitForSingleObject(
            _activationEvent,
            (_, _) =>
            {
                try
                {
                    Dispatcher.BeginInvoke(() =>
                    {
                        if (MainWindow is SoftMochiPet.MainWindow window && !window.IsFeatureTestMode && !window.IsBehaviorControlMode)
                        {
                            window.BringHomeFromExternalRequest();
                        }
                    });
                }
                catch (InvalidOperationException)
                {
                    // The dispatcher is already shutting down.
                }
            },
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);
        var window = featureTest ? new MainWindow(featureTest: true)
            : behaviorControl ? new MainWindow(behaviorControl: true) : new MainWindow();
        MainWindow = window;
        window.Show();
        window.StartCompanionIfEnabled();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _activationWait?.Unregister(null);
        _activationWait = null;
        _activationEvent?.Dispose();
        _activationEvent = null;
        _behaviorControlMarker?.Dispose();
        _behaviorControlMarker = null;
        if (_ownsMutex)
        {
            _singleInstanceMutex?.ReleaseMutex();
        }
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }

}
