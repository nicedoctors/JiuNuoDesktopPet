using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SoftMochiPet.Core;
using SoftMochiPet.Interop;
using SoftMochiPet.Models;
using SoftMochiPet.Services;
using DrawingPoint = System.Drawing.Point;
using DrawingRectangle = System.Drawing.Rectangle;

namespace SoftMochiPet;

public partial class MainWindow : Window
{
    private double MouthCanvasX => _character.Geometry.MouthCanvasX;
    private double MouthCanvasY => _character.Geometry.MouthCanvasY;
    private double LickContactCanvasX => _character.Geometry.LickContactCanvasX;
    private double LickContactCanvasY => _character.Geometry.LickContactCanvasY;
    private double SlideContactCanvasX => _character.Geometry.SlideContactCanvasX;
    private double BodyCollisionLeftCanvasX => _character.Geometry.BodyCollisionLeftCanvasX;
    private double BodyCollisionTopCanvasY => _character.Geometry.BodyCollisionTopCanvasY;
    private double BodyCollisionRightCanvasX => _character.Geometry.BodyCollisionRightCanvasX;
    private double BodyCollisionBottomCanvasY => _character.Geometry.BodyCollisionBottomCanvasY;

    private readonly SettingsStore _settingsStore;
    private readonly PetSettings _settings;
    private PetCharacterProfile _character;
    private PetCharacterProfile? _pendingCharacter;
    private PetLifeStateStore _lifeStateStore;
    private PetLifeState _lifeState;
    private SpriteAnimator _animator;
    private VoicePlaybackService _voice;
    private Action<BitmapSource>? _frameChangedHandler;
    private Action<string>? _animationFinishedHandler;
    private CancellationTokenSource? _spriteWarmupCancellation;
    private MouseMonitor _mouseMonitor = new();
    private FileDeletionWatcher? _deletionWatcher;
    private readonly WindowSurfaceProvider _surfaceProvider = new();
    private readonly StartupRegistrationService _startupRegistration = new();
    private readonly TrayIconService _trayIcon;
    private readonly MainWindow? _primaryWindow;
    private MainWindow? _companionWindow;
    private bool EffectiveFastingMode => _settings.FastingMode || _settings.InfiniteMode;
    private readonly MealVisualQueue<DeletedItemInfo, GhostIconWindow?> _foodQueue = new();
    private readonly DeletionBurstTracker _deletionBurst = new();
    private readonly DragMotionTracker _dragMotion = new();
    private readonly CancellationTokenSource _deferredStartupCancellation = new();
    private readonly Dictionary<AutonomousAction, double> _actionCooldownUntil = [];
    private readonly Dictionary<IntPtr, WindowBodySnapshot> _previousWindowBodies = [];
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private double _preferredPetSize;

    private HwndSource? _windowSource;
    private IntPtr _windowHandle;
    private IntPtr _currentMonitor;
    private IntPtr _supportHandle;
    private bool _supportIsDesktopFloor;
    private PetState _state = PetState.Idle;
    private MovementPurpose _movementPurpose;
    private DeletedItemInfo? _currentFood;
    private GhostIconWindow? _currentGhostIconWindow;
    private double _lastTickSeconds;
    private double _stateElapsed;
    private double _surfaceRefreshRemaining;
    private double _lifeSaveRemaining = 60;
    private double _nextAutonomousDecision;
    private double _nextIdleVoiceAt;
    private double _sleepDuration;
    private double _curiousDuration;
    private double _velocityX;
    private double _velocityY;
    private double _movementPixelRemainderX;
    private double _movementPixelRemainderY;
    private double _fallPixelRemainder;
    private double _fallStartFootY;
    private string _fallReason = "None";
    private double _targetScreenX;
    private double _targetScreenY;
    private DateTimeOffset _buffetUntil;
    private bool _manualLickRequested;
    private bool _isClosing;
    private bool _isDragging;
    // WinForms tray-menu dismissal can leave a stale WPF mouse-down event.
    // Keep a short monotonic guard so menu interaction cannot start a drag.
    private double _suppressDragUntil;
    private IntPtr _menuForegroundHandle;
    private IntPtr _lastExternalForegroundHandle;
    private bool _isRenderingSubscribed;
    private bool _displayAdjustmentQueued;
    private bool _displayAdjustmentNeedsClamp;
    private bool _displayAdjustmentReturnsWindows;
    private DrawingPoint _dragCursorStart;
    private int _dragWindowLeft;
    private int _dragWindowTop;
    private DrawingPoint _previousDragCursor;
    private bool _usingGhostIcon;
    private bool _currentMealNutritionApplied;
    private long _nextMealQueueId;
    private IntPtr _supportMotionHandle;
    private double _lastSupportTop;
    private bool _supportMotionInitialized;
    private double _supportVelocityY;
    private double? _supportUnavailableSince;
    private PetState? _lastTracedState;
    private MovementPurpose? _lastTracedMovement;
    private IntPtr _lastTracedSupport;
    private double _nextRuntimeTrace;
    private IntPtr _temporarilyIgnoredLandingSupport;
    private double _ignoreLandingSupportUntil;
    private IntPtr _airbornePlatformSupportHandle;
    private int _lastAirbornePlatformTop;
    private bool _airbornePlatformInitialized;
    private WindowClimbPlan? _pendingClimb;
    private WindowClimbPlan? _activeClimb;
    private double _climbStartFootY;
    private double _climbDuration;
    private double _climbFailureProgress;
    private double _nextClimbAttemptAt;
    private double _nextPatrolBehaviorCheck;
    private double _nextHungerReactionAt;
    private double _nextHungerUiUpdate;
    private int _patrolDirection = 1;
    private bool _climbSucceeds;
    private bool _wakeForMeal;
    private bool _wakeForManualLick;
    private int _lastHungerBand = -1;
    private WindowSurface? _slideSurface;
    private int _slideDirection;
    private double _slideStartFootY;
    private double _slideEndFootY;
    private double _slideDuration;
    private double _nextTerrainActionAt;
    private long _processedWindowBodyRefreshVersion;
    private double _previousWindowBodySnapshotAt;
    private double _nextWindowSideImpactAt;
    private IntPtr _enclosureHandle;
    private WindowBodySnapshot _lastEnclosureBody;
    private bool _enclosureMotionInitialized;
    private bool _enclosureResting;

    public MainWindow(bool featureTest = false, bool behaviorControl = false,
        MainWindow? primaryWindow = null, PetCharacterProfile? companionCharacter = null)
    {
        InitializeComponent();

        _primaryWindow = primaryWindow;

        _featureSession = featureTest ? FeatureDemoSession.Create() : null;
        _behaviorSession = behaviorControl ? BehaviorControlSession.Create() : null;
        _settingsStore = primaryWindow?._settingsStore ?? _behaviorSession?.SettingsStore ?? _featureSession?.SettingsStore ?? new SettingsStore();
        _settings = primaryWindow?._settings ?? _behaviorSession?.Settings ?? _settingsStore.Load();
        _character = companionCharacter ?? PetCharacterProfile.Get(_settings.CharacterId);
        var runtimeDirectory = Path.Combine(AppContext.BaseDirectory, _character.RuntimeRelativeDirectory);
        if (!HasCharacterAssets(runtimeDirectory, _character) && _character != PetCharacterProfile.Nuonuo)
        {
            DiagnosticsLog.WriteEvent(
                "CharacterStartupFallback",
                ("RequestedCharacter", _character.Id),
                ("Reason", "IncompleteSprites"));
            _character = PetCharacterProfile.Nuonuo;
            runtimeDirectory = Path.Combine(AppContext.BaseDirectory, _character.RuntimeRelativeDirectory);
        }

        try
        {
            _animator = new SpriteAnimator(runtimeDirectory, _character);
        }
        catch (Exception exception) when (_character != PetCharacterProfile.Nuonuo)
        {
            DiagnosticsLog.Write($"Character startup artwork failed; Character={_character.Id}; using Nuonuo.", exception);
            _character = PetCharacterProfile.Nuonuo;
            _animator = new SpriteAnimator(
                Path.Combine(AppContext.BaseDirectory, _character.RuntimeRelativeDirectory), _character);
        }

        Title = IsBehaviorControlMode ? $"啾糯桌宠 · 行为控制版 · {_character.DisplayName}" : $"啾糯桌宠 · {_character.DisplayName}";
        _lifeStateStore = CreateLifeStateStore(_character);
        _lifeState = _lifeStateStore.Load();
        NormalizeCharacterNeeds();
        _preferredPetSize = PetSizePolicy.ClampPreferredSize(_settings.PetSize);
        Width = _preferredPetSize;
        Height = Width;
        UpdateSizeDependentUi();

        AttachAnimator(_animator);
        _voice = CreateVoiceService(_character);

        var startsWithWindows = false;
        try
        {
            startsWithWindows = _startupRegistration.IsEnabled();
        }
        catch (Exception exception)
        {
            DiagnosticsLog.Write("Startup registration could not be read.", exception);
        }

        _trayIcon = primaryWindow?._trayIcon ?? new TrayIconService(
            _settings.SoundEnabled,
            _preferredPetSize,
            startsWithWindows,
            _settings.FastingMode,
            _settings.QuietMode,
            _character, _settings.AllowMischief, behaviorControl: IsBehaviorControlMode,
            infiniteMode: _settings.InfiniteMode, coexistenceMode: _settings.CoexistenceMode,
            showSpeechBubbles: _settings.ShowSpeechBubbles);
        _trayIcon.MenuOpenChanged += TrayMenuOpenChanged;
        if (primaryWindow is null)
        {
            _trayIcon.CharacterChanged += id => Dispatcher.Invoke(() => RequestCharacterChange(id));
            _trayIcon.CoexistenceModeChanged += enabled => Dispatcher.Invoke(() => SetCoexistenceMode(enabled));
            _trayIcon.PairInteractionRequested += scene => Dispatcher.Invoke(() => StartPairSceneFromMenu(scene));
            _trayIcon.PairDialogueRequested += () => Dispatcher.Invoke(RequestPairDialogue);
            _trayIcon.SpeechBubblesChanged += enabled => Dispatcher.Invoke(() => ToggleSpeechBubbles(enabled));
            _trayIcon.BringHomeRequested += () => Dispatcher.Invoke(BringBothHome);
            _trayIcon.ExitRequested += () => Dispatcher.Invoke(ExitApplication);
            _trayIcon.SoundEnabledChanged += enabled => Dispatcher.Invoke(() => ApplyToBoth(pet => pet.ToggleVoice(enabled)));
            _trayIcon.PetSizeChanged += size => Dispatcher.Invoke(() => ApplyToBoth(pet => pet.ChangePetSize(size)));
            _trayIcon.StartWithWindowsChanged += enabled => Dispatcher.Invoke(() => ToggleStartWithWindows(enabled));
            _trayIcon.FastingModeChanged += enabled => Dispatcher.Invoke(() => ToggleFastingMode(enabled));
            _trayIcon.InfiniteModeChanged += enabled => Dispatcher.Invoke(() => ToggleInfiniteMode(enabled));
            _trayIcon.QuietModeChanged += enabled => Dispatcher.Invoke(() => ApplyToBoth(pet => pet.ToggleQuietMode(enabled)));
        }
        InitializeMischief();
        InitializeBehaviorControl();
        if (IsFeatureTestMode)
        {
            _trayIcon.ConfigureFeatureTest();
            _trayIcon.FeatureTestStopRequested += () => Dispatcher.Invoke(() => StopFeatureDemo("UserStopped"));
            _trayIcon.FeatureTestRetryRequested += () => Dispatcher.Invoke(RetryFeatureStep);
        }

        SourceInitialized += Window_SourceInitialized;
        Loaded += Window_Loaded;
        ContentRendered += Window_ContentRendered;
        Closing += Window_Closing;
    }

    private PetLifeStateStore CreateLifeStateStore(PetCharacterProfile character) =>
        new(_behaviorSession?.LifeStateDirectory(character) ?? _featureSession?.LifeStateDirectory(character) ??
            character.LifeStateDirectory(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)));

    private VoicePlaybackService CreateVoiceService(PetCharacterProfile character) =>
        new(
            Path.Combine(AppContext.BaseDirectory, character.VoiceRelativeDirectory),
            _settings.SoundEnabled && character.HasVoice,
            _settings.VoiceVolume,
            character);

    private void AttachAnimator(SpriteAnimator animator)
    {
        _frameChangedHandler = frame =>
        {
            if (!_isClosing && ReferenceEquals(_animator, animator))
            {
                if (_state != PetState.Pinching || !_cheekPinch.Active)
                    PetSprite.Source = frame;
                UpdatePrankForeground();
            }
        };
        _animationFinishedHandler = clip =>
        {
            if (!_isClosing && ReferenceEquals(_animator, animator))
            {
                AnimationFinished(clip);
            }
        };
        animator.FrameChanged += _frameChangedHandler;
        animator.AnimationFinished += _animationFinishedHandler;
        PetSprite.Source = animator.CurrentFrame;
    }

    private void DetachAnimator(SpriteAnimator animator)
    {
        animator.FrameChanged -= _frameChangedHandler;
        animator.AnimationFinished -= _animationFinishedHandler;
        _frameChangedHandler = null;
        _animationFinishedHandler = null;
    }

    private static bool CanApplyCharacterChange(PetState state, bool isDragging) =>
        !isDragging && state is not (PetState.Dragging or PetState.Pinching or PetState.Chomping or PetState.Satisfied);

    private void RequestCharacterChange(string characterId)
    {
        if (_companionWindow is not null) SetCoexistenceMode(false);
        _pendingBehavior = null;
        _activeBehavior = null;
        if (_isClosing)
        {
            return;
        }

        var requested = PetCharacterProfile.Get(characterId);
        if (requested == _character)
        {
            _pendingCharacter = null;
            return;
        }

        var runtimeDirectory = Path.Combine(AppContext.BaseDirectory, requested.RuntimeRelativeDirectory);
        if (!HasCharacterAssets(runtimeDirectory, requested))
        {
            DiagnosticsLog.WriteEvent(
                "CharacterChangeRejected",
                ("Character", requested.Id),
                ("Reason", "IncompleteSprites"));
            return;
        }

        _pendingCharacter = requested;
        CancelWindowPrank("CharacterRequested", returnToIdle: true, keepHatless: false);
        DiagnosticsLog.WriteEvent(
            "CharacterChangeRequested",
            ("Character", requested.Id),
            ("Deferred", !CanApplyCharacterChange(_state, _isDragging)));
        if (!_isRenderingSubscribed)
        {
            TryApplyPendingCharacterChange();
        }
    }

    private void TryApplyPendingCharacterChange()
    {
        if (_isClosing || _pendingCharacter is not { } requested ||
            !CanApplyCharacterChange(_state, _isDragging))
        {
            return;
        }

        _pendingCharacter = null;
        var runtimeDirectory = Path.Combine(AppContext.BaseDirectory, requested.RuntimeRelativeDirectory);
        if (!HasCharacterAssets(runtimeDirectory, requested))
        {
            DiagnosticsLog.WriteEvent(
                "CharacterChangeRejected",
                ("Character", requested.Id),
                ("Reason", "IncompleteSprites"));
            return;
        }

        SpriteAnimator nextAnimator;
        PetLifeStateStore nextStore;
        PetLifeState nextLife;
        VoicePlaybackService? nextVoice = null;
        try
        {
            nextAnimator = new SpriteAnimator(runtimeDirectory, requested);
            nextStore = CreateLifeStateStore(requested);
            nextLife = nextStore.Load();
            nextLife.Hunger = requested.SupportsFood
                ? PetModePolicy.ResolveHunger(nextLife.Hunger, EffectiveFastingMode) : 0;
            if (!requested.SupportsFood) nextLife.Fullness = 0;
            nextVoice = CreateVoiceService(requested);
            _lifeStateStore.Save(_lifeState);
        }
        catch (Exception exception)
        {
            nextVoice?.Dispose();
            DiagnosticsLog.Write($"Character change failed before activation; Character={requested.Id}.", exception);
            return;
        }

        var previousCharacter = _character;
        SaveMischiefState();
        CancelWindowPrank("CharacterChanged", returnToIdle: false, keepHatless: false);
        if (requested.SupportsFood) RequeueActiveMealForDrag(characterChange: true);
        else ClearFoodInteractions();
        _spriteWarmupCancellation?.Cancel();
        DetachAnimator(_animator);
        _voice.Dispose();
        _character = requested;
        _animator = nextAnimator;
        _lifeStateStore = nextStore;
        _lifeState = nextLife;
        _voice = nextVoice;
        AttachAnimator(_animator);
        CloseSpeechBubble();
        _speechSelector = null;

        _velocityX = 0;
        _velocityY = 0;
        _movementPixelRemainderX = 0;
        _movementPixelRemainderY = 0;
        _fallPixelRemainder = 0;
        _targetScreenX = 0;
        _targetScreenY = 0;
        _supportHandle = IntPtr.Zero;
        _supportIsDesktopFloor = false;
        _temporarilyIgnoredLandingSupport = IntPtr.Zero;
        _ignoreLandingSupportUntil = 0;
        ResetSupportMotionTracking();
        ResetAirbornePlatformSupport();
        ClearWindowEnclosure();
        _previousWindowBodies.Clear();
        _pendingClimb = null;
        _activeClimb = null;
        _climbSucceeds = false;
        _slideSurface = null;
        _slideDirection = 0;
        _wakeForMeal = false;
        _wakeForManualLick = false;
        _manualLickRequested = false;
        _actionCooldownUntil.Clear();
        _nextClimbAttemptAt = 0;
        _nextTerrainActionAt = 0;
        _nextHungerReactionAt = 0;
        _nextPatrolBehaviorCheck = 0;
        _nextWindowSideImpactAt = 0;
        _lastHungerBand = -1;
        _nextHungerUiUpdate = 0;
        _lifeSaveRemaining = 60;
        GroundShadow.Width = 202;
        _lastTracedState = null;
        Title = IsBehaviorControlMode ? $"啾糯桌宠 · 行为控制版 · {_character.DisplayName}" : $"啾糯桌宠 · {_character.DisplayName}";
        _settings.CharacterId = _character.Id;
        try
        {
            _settingsStore.Save(_settings);
        }
        catch (Exception exception)
        {
            DiagnosticsLog.Write($"Character preference could not be saved; Character={_character.Id}.", exception);
        }

        _trayIcon.SetCharacter(_character);
        ConfigureFoodMonitoring();
        LoadCharacterMischief();
        RefreshSurfaceMap(force: true);
        ReturnToIdle();
        if (_state == PetState.Idle && !TryResolveGroundSupport(allowSnap: false))
        {
            BeginFall(reason: "CharacterChanged");
        }
        ScheduleNextIdleVoice();
        UpdateHungerDisplay(force: true);
        PlayStartupVoice();
        var animator = _animator;
        var character = _character;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            _ = WarmCharacterSpritesAsync(animator, character);
        }), System.Windows.Threading.DispatcherPriority.ContextIdle);
        DiagnosticsLog.WriteEvent(
            "CharacterChanged",
            ("PreviousCharacter", previousCharacter.Id),
            ("Character", _character.Id),
            ("PendingMeals", _foodQueue.Count),
            ("VoiceEnabled", _voice.IsEnabled));
    }

    private async Task WarmCharacterSpritesAsync(SpriteAnimator animator, PetCharacterProfile character)
    {
        if (_isClosing || !ReferenceEquals(_animator, animator))
        {
            return;
        }

        _spriteWarmupCancellation?.Cancel();
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_deferredStartupCancellation.Token);
        _spriteWarmupCancellation = cancellation;
        try
        {
            await animator.PreloadRemainingAsync(cancellation.Token);
            if (!_isClosing && ReferenceEquals(_animator, animator))
            {
                DiagnosticsLog.WriteEvent(
                    "CharacterSpritesReady",
                    ("Character", character.Id),
                    ("LoadedSpriteFolders", animator.LoadedAssetFolderCount));
            }
        }
        catch (OperationCanceledException)
        {
            // A character switch or exit makes the previous warm-up unnecessary.
        }
        catch (Exception exception)
        {
            DiagnosticsLog.Write($"Character sprite warm-up failed; Character={character.Id}.", exception);
        }
        finally
        {
            if (ReferenceEquals(_spriteWarmupCancellation, cancellation))
            {
                _spriteWarmupCancellation = null;
            }
            cancellation.Dispose();
        }
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        _windowHandle = handle;
        var style = NativeMethods.GetWindowLongPtr(handle, NativeMethods.GwlExStyle).ToInt64();
        NativeMethods.SetWindowLongPtr(handle, NativeMethods.GwlExStyle, new IntPtr(style | NativeMethods.WsExToolWindow));

        _windowSource = HwndSource.FromHwnd(handle);
        _windowSource?.AddHook(WindowMessageHook);
        _currentMonitor = DisplayGeometry.FromWindow(handle)?.Handle ?? IntPtr.Zero;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        ConfigureFoodMonitoring();
        InitializeWindowPranks();
        RefreshSurfaceMap(force: true);
        BringHome();
        PlayStartupVoice();
        UpdateHungerDisplay(force: true);
        _lastTickSeconds = _clock.Elapsed.TotalSeconds;
        ScheduleNextAutonomousDecision(
            _character.MaximumIdleSeconds,
            _character.MaximumIdleSeconds);
        ScheduleNextIdleVoice();
        StartRendering();
        if (_settings.ShowSpeechBubbles && _primaryWindow is null)
        {
            TrySpeak(DialogueCue.Startup, bypassCooldown: true);
            _nextPairDialogueAt = _clock.Elapsed.TotalSeconds + 8;
            _nextAmbientSpeechAt = _clock.Elapsed.TotalSeconds + 22;
        }
        DiagnosticsLog.WriteEvent(
            "PetLoaded",
            ("Character", _character.Id),
            ("PetDip", $"{Width:0.#}x{Height:0.#}"),
            ("ReactToDeletes", _settings.ReactToDeletes),
            ("StartsWithWindows", TryReadStartupRegistration()),
            ("FastingMode", _settings.FastingMode),
            ("QuietMode", _settings.QuietMode),
            ("VoiceEnabled", _voice.IsEnabled),
            ("VoiceVolume", _settings.VoiceVolume),
            ("VoiceAssets", _character.HasVoice ? VoiceCueCatalog.AllFileNamesFor(_character).Count : 0),
            ("LogPath", DiagnosticsLog.FilePath),
            ("WindowDpi", DisplayGeometry.GetWindowDpi(_windowHandle)),
            ("Monitors", string.Join(';', DisplayGeometry.GetAllMonitors().Select(monitor =>
                $"{monitor.MonitorArea.Left},{monitor.MonitorArea.Top},{monitor.MonitorArea.Width}x{monitor.MonitorArea.Height}@{monitor.ScalePercent}%"))));
    }

    private async void Window_ContentRendered(object? sender, EventArgs e)
    {
        ContentRendered -= Window_ContentRendered;
        DiagnosticsLog.WriteEvent(
            "FirstFramePresented",
            ("ElapsedMs", _clock.Elapsed.TotalMilliseconds),
            ("LoadedSpriteFolders", _animator.LoadedAssetFolderCount),
            ("TotalSpriteFolders", _animator.TotalAssetFolderCount));

        _deletionWatcher?.BeginIconCacheWarmup();
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var spriteWarmup = WarmCharacterSpritesAsync(_animator, _character);
            var migration = IsFeatureTestMode || IsBehaviorControlMode || _primaryWindow is not null ? Task.FromResult(false) : Task.Run(
                ExplorerContextMenuService.UninstallOnce,
                _deferredStartupCancellation.Token);
            await Task.WhenAll(spriteWarmup, migration);
            if (_isClosing)
            {
                return;
            }

            DiagnosticsLog.WriteEvent(
                "DeferredStartupCompleted",
                ("ElapsedMs", stopwatch.Elapsed.TotalMilliseconds),
                ("LoadedSpriteFolders", _animator.LoadedAssetFolderCount),
                ("LegacyMenuMigrationRan", migration.Result));
            if (IsFeatureTestMode) _ = RunFeatureDemoAsync();
        }
        catch (OperationCanceledException)
        {
            // Exiting while background assets are warming should not delay shutdown.
        }
        catch (Exception exception)
        {
            DiagnosticsLog.Write("Deferred startup work failed.", exception);
        }
    }

    private IntPtr WindowMessageHook(IntPtr window, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == NativeMethods.WmWindowPosChanged)
        {
            var monitor = DisplayGeometry.MonitorHandleFromWindow(window);
            if (monitor != IntPtr.Zero && monitor != _currentMonitor)
            {
                _currentMonitor = DisplayGeometry.FromWindow(window)?.Handle ?? monitor;
                QueueDisplayAdjustment(clampToWorkArea: false);
            }

            return IntPtr.Zero;
        }

        if (message is NativeMethods.WmDpiChanged or NativeMethods.WmDisplayChange ||
            message == NativeMethods.WmSettingChange && wParam.ToInt32() == NativeMethods.SpiSetWorkArea)
        {
            if (message is NativeMethods.WmDisplayChange or NativeMethods.WmSettingChange)
            {
                DisplayGeometry.InvalidateScaleCache();
            }

            QueueDisplayAdjustment(clampToWorkArea: true,
                returnHeldWindows: message is NativeMethods.WmDisplayChange or NativeMethods.WmSettingChange);
            return IntPtr.Zero;
        }

        if (message != NativeMethods.WmNcHitTest || _isDragging)
        {
            return IntPtr.Zero;
        }

        var packed = lParam.ToInt64();
        var screenX = unchecked((short)(packed & 0xFFFF));
        var screenY = unchecked((short)((packed >> 16) & 0xFFFF));
        var local = PointFromScreen(new System.Windows.Point(screenX, screenY));
        var normalizedX = local.X / Math.Max(1, ActualWidth);
        var normalizedY = local.Y / Math.Max(1, ActualHeight);
        var ellipse = Math.Pow((normalizedX - 0.5) / 0.44, 2) + Math.Pow((normalizedY - 0.57) / 0.47, 2);

        if (ellipse > 1)
        {
            handled = true;
            return new IntPtr(NativeMethods.HtTransparent);
        }

        return IntPtr.Zero;
    }

    private void QueueDisplayAdjustment(bool clampToWorkArea, bool returnHeldWindows = false)
    {
        _displayAdjustmentNeedsClamp |= clampToWorkArea;
        _displayAdjustmentReturnsWindows |= returnHeldWindows;
        if (_displayAdjustmentQueued)
        {
            return;
        }

        _displayAdjustmentQueued = true;
        Dispatcher.BeginInvoke(() =>
        {
            var shouldClamp = _displayAdjustmentNeedsClamp;
            var shouldReturnWindows = _displayAdjustmentReturnsWindows;
            _displayAdjustmentQueued = false;
            _displayAdjustmentNeedsClamp = false;
            _displayAdjustmentReturnsWindows = false;
            HandleDisplayEnvironmentChanged(shouldClamp, shouldReturnWindows);
        }, System.Windows.Threading.DispatcherPriority.ContextIdle);
    }

    private void HandleDisplayEnvironmentChanged(bool clampToWorkArea, bool returnHeldWindows)
    {
        if (returnHeldWindows) CancelPairScene("DisplayChanged");
        if (_cheekPinch.Active) ReturnToIdle();
        CancelWindowPrank(returnHeldWindows ? "DisplayChanged" : "PetDisplayAdapted",
            returnToIdle: true, keepHatless: false);
        if (_isClosing || _windowHandle == IntPtr.Zero)
        {
            return;
        }

        var monitor = DisplayGeometry.FromWindow(_windowHandle);
        if (monitor is null)
        {
            return;
        }

        _currentMonitor = monitor.Handle;
        var sizeChanged = ApplyResponsivePetSize(monitor);
        if ((clampToWorkArea || sizeChanged) && !_isDragging)
        {
            DisplayGeometry.ClampWindowToNearestWorkArea(_windowHandle);
        }

        if (_state == PetState.Running && _movementPurpose == MovementPurpose.Meal)
        {
            var target = new DrawingPoint((int)Math.Round(_targetScreenX), (int)Math.Round(_targetScreenY));
            if (DisplayGeometry.FromPoint(target, nearest: false) is null)
            {
                target = DisplayGeometry.ClampPointToNearestWorkArea(target, 8);
                _targetScreenX = target.X;
                _targetScreenY = target.Y;
                if (_currentFood is not null)
                {
                    _currentFood = _currentFood with { ScreenX = target.X, ScreenY = target.Y };
                }

                if (_currentGhostIconWindow?.IsGhostVisible == true)
                {
                    _currentGhostIconWindow.SetVisual(target.X, target.Y, 1, 1, 0, 1);
                }
            }
        }

        if (_currentGhostIconWindow?.IsGhostVisible == true)
        {
            _currentGhostIconWindow.RefreshDisplayGeometry();
        }

        foreach (var pending in _foodQueue.Snapshot)
        {
            if (pending.Visual?.IsGhostVisible == true)
            {
                pending.Visual.RefreshDisplayGeometry();
            }
        }

        RefreshSurfaceMap(force: true);

        DiagnosticsLog.Write(
            $"Display geometry updated; Scale={monitor.ScalePercent}%; " +
            $"WindowDpi={DisplayGeometry.GetWindowDpi(_windowHandle)}; " +
            $"Monitor={monitor.MonitorArea.Left},{monitor.MonitorArea.Top}," +
            $"{monitor.MonitorArea.Width}x{monitor.MonitorArea.Height}; " +
            $"WorkArea={monitor.WorkArea.Left},{monitor.WorkArea.Top}," +
            $"{monitor.WorkArea.Width}x{monitor.WorkArea.Height}; PetDip={Width:0.#}.");
    }

    private bool ApplyResponsivePetSize(MonitorGeometry monitor)
    {
        var fittedSize = PetSizePolicy.FitToWorkArea(
            _preferredPetSize,
            monitor.WorkArea.Width,
            monitor.WorkArea.Height,
            monitor.Scale);
        if (Math.Abs(Width - fittedSize) < 0.5 && Math.Abs(Height - fittedSize) < 0.5)
        {
            return false;
        }

        Width = fittedSize;
        Height = fittedSize;
        UpdateSizeDependentUi();
        return true;
    }

    private void ChangePetSize(double requestedSize)
    {
        var nextSize = PetSizePolicy.ClampPreferredSize(requestedSize);
        if (Math.Abs(_preferredPetSize - nextSize) < 0.01)
        {
            return;
        }

        CancelPairScene("SizeChanged");
        if (_cheekPinch.Active) ReturnToIdle();
        CancelWindowPrank("SizeChanged", returnToIdle: true, keepHatless: false);

        DrawingPoint? anchoredFoot = null;
        var monitor = _windowHandle != IntPtr.Zero
            ? DisplayGeometry.FromWindow(_windowHandle)
            : null;
        if (monitor is not null &&
            NativeMethods.GetWindowRect(_windowHandle, out var currentRectangle))
        {
            var oldFootOffset = ProjectCanvasAnchorOffset(
                _character.Geometry.FootCanvasX,
                _character.Geometry.FootCanvasY,
                monitor);
            anchoredFoot = new DrawingPoint(
                currentRectangle.Left + oldFootOffset.X,
                currentRectangle.Top + oldFootOffset.Y);
        }

        _preferredPetSize = nextSize;
        _settings.PetSize = nextSize;
        _settingsStore.Save(_settings);
        if (monitor is null)
        {
            Width = nextSize;
            Height = nextSize;
            UpdateSizeDependentUi();
        }
        else
        {
            ApplyResponsivePetSize(monitor);
            if (anchoredFoot is DrawingPoint foot)
            {
                var newFootOffset = ProjectCanvasAnchorOffset(
                    _character.Geometry.FootCanvasX,
                    _character.Geometry.FootCanvasY,
                    monitor);
                DisplayGeometry.MoveWindowPhysical(
                    _windowHandle,
                    foot.X - newFootOffset.X,
                    foot.Y - newFootOffset.Y);
                DisplayGeometry.ClampWindowToNearestWorkArea(_windowHandle);
            }
        }

        RefreshSurfaceMap(force: true);
        DiagnosticsLog.WriteEvent(
            "PetSizeChanged",
            ("PreferredDip", _preferredPetSize),
            ("ActualDip", Width),
            ("Percent", PetSizePolicy.ToPercent(_preferredPetSize)));
    }

    private void UpdateSizeDependentUi()
    {
        HungerBadge.Width = Math.Min(126, Math.Max(112, Width - 6));
        MischiefBadge.Width = Math.Min(120, Math.Max(104, Width - 6));
    }

    private void DeletionWatcher_ItemDeleted(object? sender, DeletedItemInfo item)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (_isClosing || !_character.SupportsFood || !ReferenceEquals(sender, _deletionWatcher)) return;
            var hasActiveDeletionMeal = _currentFood is not null &&
                !_currentFood.OriginalPath.EndsWith(".snack", StringComparison.OrdinalIgnoreCase) &&
                _state is PetState.Running or PetState.Chomping or PetState.Satisfied or PetState.Interacting;
            if (!_settings.ReactToDeletes ||
                !PetModePolicy.AllowsDeletionMeal(_settings.QuietMode) ||
                !MealQueuePolicy.CanAcceptDeletion(_foodQueue.Count, hasActiveDeletionMeal))
            {
                return;
            }

            RegisterDeletionBurst(item.DetectedAt);

            var anchor = _mouseMonitor.TakeRecentDeletionAnchor(item.DisplayName);
            var pointer = anchor?.EffectivePoint ?? MouseMonitor.GetCursorPosition();
            var exactDesktopAnchor = anchor?.DesktopIcon is not null;
            if (!exactDesktopAnchor)
            {
                var occupied = GetOccupiedGhostAnchors();
                pointer = GhostAnchorLayout.ChooseDistinct(pointer, occupied);
                pointer = DisplayGeometry.ClampPointToNearestWorkArea(pointer, 8);
            }

            var labelBounds = anchor?.DesktopIcon?.LabelBounds;
            var queueId = Interlocked.Increment(ref _nextMealQueueId);
            var queuedItem = item with
            {
                ScreenX = pointer.X,
                ScreenY = pointer.Y,
                IconPixelWidth = anchor?.DesktopIcon?.PixelSize.Width,
                IconPixelHeight = anchor?.DesktopIcon?.PixelSize.Height,
                LabelPixelWidth = labelBounds?.Width,
                LabelPixelHeight = labelBounds?.Height,
                LabelOffsetX = labelBounds is null ? null : labelBounds.Value.Left + labelBounds.Value.Width / 2 - pointer.X,
                LabelOffsetY = labelBounds is null ? null : labelBounds.Value.Top + labelBounds.Value.Height / 2 - pointer.Y,
                QueueId = queueId,
                AnchorSource = anchor?.DesktopCaptureStatus ?? "CursorAtDeletion",
                ExactDesktopAnchor = exactDesktopAnchor,
            };
            _foodQueue.Enqueue(queuedItem, CreateWaitingGhost);
            DiagnosticsLog.WriteEvent(
                "DeletionQueued",
                ("QueueId", queueId),
                ("PendingCount", _foodQueue.Count),
                ("Path", item.OriginalPath),
                ("DisplayName", item.DisplayName),
                ("IconCaptured", item.IconCapturedBeforeDeletion),
                ("BitmapPixels", $"{item.CachedIcon?.PixelWidth}x{item.CachedIcon?.PixelHeight}"),
                ("Anchor", $"{pointer.X},{pointer.Y}"),
                ("AnchorSource", queuedItem.AnchorSource),
                ("ExactDesktopBounds", exactDesktopAnchor),
                ("DisplayPixels", $"{anchor?.DesktopIcon?.PixelSize.Width}x{anchor?.DesktopIcon?.PixelSize.Height}"),
                ("LabelPixels", $"{labelBounds?.Width}x{labelBounds?.Height}"));

            if (!IsVisible)
            {
                Show();
            }

            var pairOwner = _primaryWindow ?? this;
            if (pairOwner._pairScene is { } activePair && IsPairLifeScene(activePair))
            {
                pairOwner.CancelPairScene("DeletionPriority");
                return;
            }

            if (_state != PetState.Pinching && WindowEnclosureBehaviorPolicy.ShouldEscapeForMeal(
                    _enclosureHandle != IntPtr.Zero,
                    _foodQueue.Count,
                    _settings.ReactToDeletes))
            {
                StartNextMeal();
            }
            else if (_state == PetState.Sleeping)
            {
                BeginWake(forMeal: true);
            }
            else if (_state == PetState.Waking)
            {
                _wakeForMeal = true;
            }
            else if (_state is PetState.Idle or PetState.Curious or PetState.Licking ||
                _state == PetState.Running && _movementPurpose != MovementPurpose.Meal)
            {
                StartNextMeal();
            }
        });
    }

    private IReadOnlyCollection<DrawingPoint> GetOccupiedGhostAnchors()
    {
        var occupied = _foodQueue.Snapshot
            .Select(entry => entry.Meal)
            .Where(meal => meal.ScreenX is not null && meal.ScreenY is not null)
            .Select(meal => new DrawingPoint(meal.ScreenX!.Value, meal.ScreenY!.Value))
            .ToList();
        if (_currentFood?.ScreenX is int currentX && _currentFood.ScreenY is int currentY)
        {
            occupied.Add(new DrawingPoint(currentX, currentY));
        }

        return occupied;
    }

    private GhostIconWindow? CreateWaitingGhost(DeletedItemInfo item)
    {
        GhostIconWindow? ghost = null;
        try
        {
            var target = item.ScreenX is int screenX && item.ScreenY is int screenY
                ? new DrawingPoint(screenX, screenY)
                : MouseMonitor.GetCursorPosition();
            var icon = item.CachedIcon ?? ShellIconProvider.GetIcon(item.OriginalPath);
            ghost = new GhostIconWindow();
            ghost.ShowAt(
                icon,
                item.DisplayName,
                target.X,
                target.Y,
                item.IconPixelWidth,
                item.IconPixelHeight,
                item.LabelPixelWidth,
                item.LabelPixelHeight,
                item.LabelOffsetX,
                item.LabelOffsetY);
            DiagnosticsLog.WriteEvent(
                "GhostShown",
                ("QueueId", item.QueueId),
                ("Anchor", $"{target.X},{target.Y}"),
                ("AnchorSource", item.AnchorSource),
                ("Name", item.DisplayName));
            return ghost;
        }
        catch (Exception exception)
        {
            CloseGhostWindow(ghost);
            DiagnosticsLog.Write(
                $"Waiting deletion ghost could not be shown; inline fallback will be used. Path={item.OriginalPath}",
                exception);
            return null;
        }
    }

    private void CloseCurrentGhost()
    {
        CloseGhostWindow(_currentGhostIconWindow);
        _currentGhostIconWindow = null;
        _usingGhostIcon = false;
    }

    private static void CloseGhostWindow(GhostIconWindow? ghost)
    {
        if (ghost is null)
        {
            return;
        }

        try
        {
            ghost.Close();
        }
        catch (InvalidOperationException)
        {
            // Closing an already-closed WPF window is harmless for queue cleanup.
        }
    }

    private void RegisterDeletionBurst(DateTimeOffset detectedAt)
    {
        if (_deletionBurst.Register(detectedAt))
        {
            var wasBuffet = DateTimeOffset.UtcNow <= _buffetUntil;
            _buffetUntil = detectedAt.AddSeconds(12);
            if (!wasBuffet)
            {
                _voice.Play(VoiceCue.Affirmative, VoicePriority.Important, TimeSpan.FromSeconds(8));
            }
        }
    }

    private bool IsBuffetMode => DateTimeOffset.UtcNow <= _buffetUntil || _foodQueue.Count > 0;

    private void StartNextMeal()
    {
        if (!_character.SupportsFood || !PetModePolicy.AllowsDeletionMeal(_settings.QuietMode))
        {
            _foodQueue.Clear(CloseGhostWindow);
            ReturnToIdle();
            return;
        }

        if (_foodQueue.Count == 0 || (!_settings.ReactToDeletes && !IsBehaviorControlMode))
        {
            ReturnToIdle();
            return;
        }

        if (!_foodQueue.TryDequeue(out var pending) || pending is null)
        {
            ReturnToIdle();
            return;
        }

        _pendingClimb = null;
        _activeClimb = null;
        _wakeForMeal = false;

        _currentFood = pending.Meal;
        _currentGhostIconWindow = pending.Visual?.IsGhostVisible == true
            ? pending.Visual : CreateWaitingGhost(_currentFood);
        if (!ReferenceEquals(pending.Visual, _currentGhostIconWindow)) CloseGhostWindow(pending.Visual);

        var target = _currentFood.ScreenX is int screenX && _currentFood.ScreenY is int screenY
            ? new DrawingPoint(screenX, screenY)
            : MouseMonitor.GetCursorPosition();
        if (DisplayGeometry.FromPoint(target, nearest: false) is null)
        {
            target = DisplayGeometry.ClampPointToNearestWorkArea(target, 8);
            _currentFood = _currentFood with { ScreenX = target.X, ScreenY = target.Y };
            _currentGhostIconWindow?.SetVisual(target.X, target.Y, 1, 1, 0, 1);
        }

        ReleaseWindowEnclosureForMeal(target);

        _usingGhostIcon = _currentGhostIconWindow is not null;
        FoodVisual.Visibility = Visibility.Collapsed;

        if (_usingGhostIcon &&
            (_primaryWindow ?? this).TryStartPairIconMeal(this, _currentFood,
                _currentGhostIconWindow!, target)) return;

        // Device-pixel target. Running aligns the exact local ghost start point to
        // this center, so mixed-DPI monitors cannot shift the fake icon.
        _targetScreenX = target.X;
        _targetScreenY = target.Y;
        _velocityX = 0;
        _velocityY = 0;
        _movementPixelRemainderX = 0;
        _movementPixelRemainderY = 0;
        _movementPurpose = MovementPurpose.Meal;
        _currentMealNutritionApplied = false;
        _state = PetState.Running;
        _stateElapsed = 0;
        _animator.Play("run");
        DiagnosticsLog.WriteEvent(
            "MealDequeued",
            ("QueueId", _currentFood.QueueId),
            ("Target", $"{target.X},{target.Y}"),
            ("AnchorSource", _currentFood.AnchorSource),
            ("Remaining", _foodQueue.Count),
            ("Name", _currentFood.DisplayName));
    }

    private void CompositionTarget_Rendering(object? sender, EventArgs e)
    {
        var started = Stopwatch.GetTimestamp();
        var stateBefore = _state;
        var clipBefore = _animator.CurrentClip;
        try { UpdateRenderedFrame(); }
        finally
        {
            UpdateSpeechOverlay();
            TraceSlowUiFrame(started, stateBefore, clipBefore);
        }
    }

    private void UpdateRenderedFrame()
    {
        var now = _clock.Elapsed.TotalSeconds;
        var timing = FrameTiming.FromElapsed(now - _lastTickSeconds);
        var delta = timing.AnimationSeconds;
        _lastTickSeconds = now;
        if (!_trayIcon.IsMenuOpen)
        {
            var foreground = NativeMethods.GetForegroundWindow();
            if (foreground != IntPtr.Zero && foreground != _windowHandle &&
                NativeMethods.GetWindowThreadProcessId(foreground, out var foregroundProcessId) != 0 &&
                foregroundProcessId != (uint)Environment.ProcessId)
                _lastExternalForegroundHandle = foreground;
        }
        if (PauseFrameForMenu(now)) return;
        if (_primaryWindow is null && _pairIconWaiting) UpdatePairIconWait(timing.SimulationSeconds);
        if (_state == PetState.Pinching)
        {
            TickCheekPinch(delta);
            return;
        }
        if (_state == PetState.Interacting)
        {
            UpdateHeldPrank(delta);
            if (_primaryWindow is null) UpdatePairScene(delta);
            return;
        }
        TryStartAutonomousPair(now);
        if (_state == PetState.Interacting) return;
        ExecutePendingBehavior();
        _stateElapsed += delta;

        _lifeState.Advance(TimeSpan.FromSeconds(timing.SimulationSeconds), _state == PetState.Sleeping);
        NormalizeCharacterNeeds();
        UpdateHeldPrank(delta);
        UpdateMischief(timing.SimulationSeconds);
        UpdateHungerDisplay();
        _lifeSaveRemaining -= timing.SimulationSeconds;
        if (_lifeSaveRemaining <= 0)
        {
            SaveLifeState();
            _lifeSaveRemaining = 60;
        }

        TryApplyPendingCharacterChange();
        RefreshSurfaceMap(delta: delta);
        UpdateWindowSideCollisions(now);
        var enclosureHandled = _state != PetState.Pranking && UpdateWindowEnclosurePhysics(delta);
        if (!enclosureHandled && _state != PetState.Pranking)
        {
            UpdateSupportedPlatformPhysics(delta);
        }
        if (!_prankAwaitingSystem && !IsPrankTimelineControlled) _animator.Tick(delta);
        try { UpdateThrownHat(delta); }
        catch (InvalidOperationException exception)
        {
            DiagnosticsLog.Write("Hat prop update failed; stopping window prank safely.", exception);
            CancelWindowPrank("PlaybackFailed", true, false);
            return;
        }

        if (!enclosureHandled)
        {
            switch (_state)
            {
                case PetState.Pranking:
                    try { UpdateWindowPrank(delta); }
                    catch (Exception exception)
                    {
                        DiagnosticsLog.WriteEvent("PrankStoppedSafely", ("Reason", exception.GetType().Name));
                        CancelWindowPrank("PlaybackFailed", true, false);
                    }
                    break;
                case PetState.Running:
                    UpdateRunning(delta);
                    break;
                case PetState.Falling:
                    UpdateFalling(delta);
                    break;
                case PetState.Landing:
                    UpdateLanding();
                    break;
                case PetState.Curious:
                    UpdateCurious();
                    break;
                case PetState.Sleeping:
                    UpdateSleeping();
                    break;
                case PetState.Waking:
                    UpdateWaking();
                    break;
                case PetState.Climbing:
                    UpdateClimbing();
                    break;
                case PetState.Sliding:
                    UpdateSliding();
                    break;
                case PetState.Rolling:
                    UpdateRolling();
                    break;
                case PetState.Licking:
                    UpdateLicking();
                    break;
                case PetState.Chomping:
                    UpdateChomping();
                    break;
                case PetState.Satisfied:
                    UpdateSatisfied();
                    break;
                case PetState.Dragging:
                    break;
                default:
                    UpdateIdleMotion();
                    break;
            }
        }

        UpdatePrankImpact(delta);
        UpdateBehaviorControlCompletion();
        TraceRuntimeState(now);

    }

    private void StartRendering()
    {
        if (_isRenderingSubscribed || _isClosing)
        {
            return;
        }

        _lastTickSeconds = _clock.Elapsed.TotalSeconds;
        CompositionTarget.Rendering += CompositionTarget_Rendering;
        _isRenderingSubscribed = true;
    }

    private void StopRendering()
    {
        if (!_isRenderingSubscribed)
        {
            return;
        }

        CompositionTarget.Rendering -= CompositionTarget_Rendering;
        _isRenderingSubscribed = false;
    }

    private void TraceRuntimeState(double now)
    {
        var stateChanged = _lastTracedState != _state || _lastTracedMovement != _movementPurpose;
        var supportChanged = _lastTracedSupport != _supportHandle;
        if (!stateChanged && !supportChanged && now < _nextRuntimeTrace)
        {
            return;
        }

        _nextRuntimeTrace = now + 2;
        _lastTracedState = _state;
        _lastTracedMovement = _movementPurpose;
        _lastTracedSupport = _supportHandle;
        var foot = CanvasPointToScreen(_character.Geometry.FootCanvasX, _character.Geometry.FootCanvasY);
        var windowPosition = NativeMethods.GetWindowRect(_windowHandle, out var rectangle)
            ? $"{rectangle.Left},{rectangle.Top},{rectangle.Right - rectangle.Left}x{rectangle.Bottom - rectangle.Top}"
            : "Unavailable";
        var monitor = DisplayGeometry.FromPoint(
            new DrawingPoint((int)Math.Round(foot.X), (int)Math.Round(foot.Y)));
        DiagnosticsLog.WriteEvent(
            stateChanged ? "StateTransition" : supportChanged ? "SupportTransition" : "RuntimeSnapshot",
            ("Character", _character.Id),
            ("State", _state),
            ("Movement", _movementPurpose),
            ("Animation", _animator.CurrentClip),
            ("Frame", _animator.CurrentFrameIndex),
            ("StateElapsed", _stateElapsed),
            ("Window", windowPosition),
            ("Foot", $"{foot.X:0.0},{foot.Y:0.0}"),
            ("Velocity", $"{_velocityX:0.0},{_velocityY:0.0}"),
            ("BodyScale", $"{SquashTransform.ScaleX:0.000},{SquashTransform.ScaleY:0.000}"),
            ("Support", FormatHandle(_supportHandle)),
            ("SupportIsDesktop", _supportIsDesktopFloor),
            ("Enclosure", FormatHandle(_enclosureHandle)),
            ("SupportVelocityY", _supportVelocityY),
            ("Hunger", _lifeState.Hunger),
            ("MonitorScale", monitor?.ScalePercent),
            ("QueueCount", _foodQueue.Count),
            ("MealQueueId", _currentFood?.QueueId),
            ("MealTarget", _currentFood?.ScreenX is int x && _currentFood.ScreenY is int y ? $"{x},{y}" : null),
            ("ClimbTarget", FormatHandle(_activeClimb?.TargetHandle ?? _pendingClimb?.TargetHandle ?? IntPtr.Zero)),
            ("ClimbWillSucceed", _activeClimb is null ? null : _climbSucceeds));
    }

    private void UpdateRunning(double delta)
    {
        if (_movementPurpose is MovementPurpose.Wander or MovementPurpose.Explore or
            MovementPurpose.Patrol or MovementPurpose.ClimbApproach or
            MovementPurpose.EdgeSlideApproach or MovementPurpose.EdgeJumpApproach)
        {
            UpdateGroundRunning(delta);
            return;
        }

        if (_movementPurpose is not (MovementPurpose.Meal or MovementPurpose.IconLick))
        {
            ReturnToIdle();
            return;
        }

        var isMealRun = _movementPurpose == MovementPurpose.Meal;

        var currentAnchor = isMealRun
            ? CanvasPointToScreen(MouthCanvasX, MouthCanvasY)
            : CanvasPointToScreen(LickContactCanvasX, LickContactCanvasY);
        var deltaDeviceX = _targetScreenX - currentAnchor.X;
        var deltaDeviceY = _targetScreenY - currentAnchor.Y;
        var deviceDistance = Math.Sqrt(deltaDeviceX * deltaDeviceX + deltaDeviceY * deltaDeviceY);
        var transformFromDevice = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice
            ?? Matrix.Identity;
        var deltaDip = transformFromDevice.Transform(new System.Windows.Point(deltaDeviceX, deltaDeviceY));
        var distance = Math.Sqrt(deltaDip.X * deltaDip.X + deltaDip.Y * deltaDip.Y);

        if (deviceDistance < 18 || distance < 0.001 || _stateElapsed > 6.5)
        {
            var exactDeltaX = (int)Math.Round(deltaDeviceX);
            var exactDeltaY = (int)Math.Round(deltaDeviceY);
            if (!DisplayGeometry.OffsetWindowPhysical(_windowHandle, exactDeltaX, exactDeltaY))
            {
                Left += deltaDip.X;
                Top += deltaDip.Y;
            }
            if (isMealRun)
            {
                BeginChomp();
            }
            else
            {
                BeginLickAnimation();
            }
            return;
        }

        var speed = Math.Clamp(distance * 3.2, 250, isMealRun ? 720 : 640) *
            (isMealRun && IsBuffetMode ? 1.08 : 1) * _character.MovementSpeedMultiplier;
        var desiredX = deltaDip.X / distance * speed;
        var desiredY = deltaDip.Y / distance * speed;
        var response = Math.Min(1, delta * 7.5);
        _velocityX += (desiredX - _velocityX) * response;
        _velocityY += (desiredY - _velocityY) * response;
        var transformToDevice = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice
            ?? Matrix.Identity;
        var movementDevice = transformToDevice.Transform(new System.Windows.Point(
            _velocityX * delta,
            _velocityY * delta));
        var movementX = SubpixelMotion.TakeWholePixels(movementDevice.X, ref _movementPixelRemainderX);
        var movementY = SubpixelMotion.TakeWholePixels(movementDevice.Y, ref _movementPixelRemainderY);
        if (!DisplayGeometry.OffsetWindowPhysical(_windowHandle, movementX, movementY))
        {
            Left += _velocityX * delta;
            Top += _velocityY * delta;
        }

        FacingTransform.ScaleX = _velocityX < 0 ? -1 : 1;
        SquashTransform.ScaleX = 1;
        SquashTransform.ScaleY = 1;
        LeanTransform.Angle = Math.Clamp(_velocityX / 1050 * 3, -3, 3);
        GroundShadow.Width = 190;
    }

    private bool TryBeginIconLick(bool manualRequest)
    {
        if (!_character.SupportsFood) return false;
        if (!WindowEnclosureBehaviorPolicy.AllowsIconLick(_enclosureHandle != IntPtr.Zero))
        {
            DiagnosticsLog.WriteEvent(
                "IconLickSuppressedInEnclosure",
                ("Window", FormatHandle(_enclosureHandle)),
                ("Manual", manualRequest));
            return false;
        }

        var origin = CanvasPointToScreen(LickContactCanvasX, LickContactCanvasY);
        var candidates = DesktopIconLocator.GetAllIcons()
            .Where(icon => DisplayGeometry.FromPoint(icon.Center, nearest: false) is not null)
            .Where(icon =>
            {
                var listView = DesktopIconLocator.FindListViewAt(icon.Center);
                return listView != IntPtr.Zero && NativeMethods.IsWindowVisible(listView);
            })
            // She stands to the icon's right and touches its exposed right edge.
            .Select(icon => new DrawingPoint(
                icon.Bounds.Right,
                icon.Bounds.Top + icon.Bounds.Height / 2))
            .ToArray();
        var target = IconLickTargetSelector.Choose(
            candidates,
            new DrawingPoint((int)Math.Round(origin.X), (int)Math.Round(origin.Y)),
            Random.Shared.NextDouble());
        if (target is null)
        {
            if (manualRequest)
            {
                _voice.Play(VoiceCue.Sad, VoicePriority.Important, TimeSpan.FromSeconds(10));
            }

            return false;
        }

        _targetScreenX = target.Value.X;
        _targetScreenY = target.Value.Y;
        _velocityX = 0;
        _velocityY = 0;
        _movementPixelRemainderX = 0;
        _movementPixelRemainderY = 0;
        _movementPurpose = MovementPurpose.IconLick;
        _state = PetState.Running;
        _stateElapsed = 0;
        _animator.Play("run");
        _voice.Play(
            manualRequest ? VoiceCue.Aggrieved : VoiceCue.Question,
            manualRequest ? VoicePriority.Important : VoicePriority.Normal,
            TimeSpan.FromSeconds(manualRequest ? 8 : 20));
        DiagnosticsLog.Write(
            $"Desktop icon lick started; Manual={manualRequest}; Hunger={_lifeState.Hunger:0.0}; " +
            $"Target={target.Value.X},{target.Value.Y}.");
        return true;
    }

    private void BeginLickAnimation()
    {
        _state = PetState.Licking;
        _movementPurpose = MovementPurpose.None;
        _stateElapsed = 0;
        _velocityX = 0;
        _velocityY = 0;
        FacingTransform.ScaleX = 1;
        SquashTransform.ScaleX = 1;
        SquashTransform.ScaleY = 1;
        LeanTransform.Angle = 0;
        _animator.Play("lick");
    }

    private void UpdateLicking()
    {
        SquashTransform.ScaleX = 1;
        SquashTransform.ScaleY = 1;
        LeanTransform.Angle = 0;
        GroundShadow.Width = 188;
    }

    private void RefreshSurfaceMap(bool force = false, double delta = 0)
    {
        if (!force)
        {
            _surfaceRefreshRemaining -= delta;
            if (_surfaceRefreshRemaining > 0)
            {
                return;
            }
        }

        _surfaceRefreshRemaining = 0.06;
        try
        {
            _surfaceProvider.Refresh(_windowHandle, IsFeatureTestMode ? _featureTarget?.Handle ?? IntPtr.Zero : null);
        }
        catch (Exception exception)
        {
            DiagnosticsLog.Write($"Window surface refresh skipped: {exception.GetType().Name}.");
        }
    }

    private void UpdateWindowSideCollisions(double now)
    {
        if (_processedWindowBodyRefreshVersion == _surfaceProvider.RefreshVersion)
        {
            return;
        }

        _processedWindowBodyRefreshVersion = _surfaceProvider.RefreshVersion;
        var elapsed = now - _previousWindowBodySnapshotAt;
        var currentBodies = _surfaceProvider.WindowBodies;
        WindowSideImpact? strongestImpact = null;

        var canBeHit = _previousWindowBodySnapshotAt > 0 && _enclosureHandle == IntPtr.Zero &&
            !_isDragging && _state != PetState.Dragging && now >= _nextWindowSideImpactAt;
        if (canBeHit)
        {
            var petBounds = GetPetCollisionBounds();
            var foot = CanvasPointToScreen(_character.Geometry.FootCanvasX, _character.Geometry.FootCanvasY);
            var monitor = DisplayGeometry.FromPoint(
                new DrawingPoint((int)Math.Round(foot.X), (int)Math.Round(foot.Y)));
            var scale = monitor?.Scale ?? 1;

            foreach (var current in currentBodies)
            {
                if (IsPrankControlledWindow(current.SourceHandle)) continue;
                if (!_previousWindowBodies.TryGetValue(current.SourceHandle, out var previous))
                {
                    continue;
                }

                var impact = WindowSideCollisionPolicy.Resolve(
                    previous,
                    current,
                    petBounds,
                    elapsed,
                    scale);
                if (impact is not null &&
                    (strongestImpact is null ||
                     Math.Abs(impact.Value.EdgeVelocityX) > Math.Abs(strongestImpact.Value.EdgeVelocityX)))
                {
                    strongestImpact = impact;
                }
            }
        }

        _previousWindowBodies.Clear();
        foreach (var body in currentBodies)
        {
            _previousWindowBodies[body.SourceHandle] = body;
        }
        _previousWindowBodySnapshotAt = now;

        if (strongestImpact is not WindowSideImpact resolvedImpact)
        {
            return;
        }

        _nextWindowSideImpactAt = now + 0.32;
        var correctionX = (int)Math.Round(resolvedImpact.CorrectionX);
        if (correctionX != 0)
        {
            DisplayGeometry.OffsetWindowPhysical(_windowHandle, correctionX, 0);
        }
        DiagnosticsLog.WriteEvent(
            "WindowSideImpact",
            ("Window", FormatHandle(resolvedImpact.SourceHandle)),
            ("Direction", resolvedImpact.Direction),
            ("EdgeVelocityX", resolvedImpact.EdgeVelocityX),
            ("CorrectionX", correctionX),
            ("PetVelocityX", resolvedImpact.PetVelocityX),
            ("PetVelocityY", resolvedImpact.PetVelocityY));
        _voice.Play(VoiceCue.Scream, VoicePriority.Critical);
        BeginFall(
            resolvedImpact.PetVelocityY,
            "WindowSideImpact",
            resolvedImpact.PetVelocityX);
        // At window corners, the platform-top detector must not immediately
        // steal a side impact and turn it into a landing on the same window.
        _temporarilyIgnoredLandingSupport = resolvedImpact.SourceHandle;
        _ignoreLandingSupportUntil = now + 0.48;
    }

    private bool UpdateWindowEnclosurePhysics(double delta)
    {
        if (_enclosureHandle == IntPtr.Zero || _isDragging)
        {
            return false;
        }

        var liveBody = _surfaceProvider.FindLiveWindowBody(_enclosureHandle);
        if (liveBody is not WindowBodySnapshot currentBox)
        {
            var lostHandle = _enclosureHandle;
            ClearWindowEnclosure();
            DiagnosticsLog.WriteEvent(
                "WindowEnclosureReleased",
                ("Window", FormatHandle(lostHandle)),
                ("Reason", "WindowUnavailable"),
                ("Velocity", $"{_velocityX:0.0},{_velocityY:0.0}"));
            BeginFall(_velocityY, "EnclosureWindowLost", _velocityX);
            return false;
        }

        if (!_enclosureMotionInitialized)
        {
            _lastEnclosureBody = currentBox;
            _enclosureMotionInitialized = true;
        }

        var petBounds = GetPetCollisionBounds(_character.Geometry.FootCanvasY);
        var center = new DrawingPoint(
            (int)Math.Round((petBounds.Left + petBounds.Right) / 2),
            (int)Math.Round((petBounds.Top + petBounds.Bottom) / 2));
        var monitor = DisplayGeometry.FromPoint(center);
        var scale = monitor?.Scale ?? 1;
        var step = WindowEnclosurePhysics.Resolve(
            petBounds,
            _lastEnclosureBody,
            currentBox,
            _velocityX,
            _velocityY,
            delta,
            scale);
        _lastEnclosureBody = currentBox;
        _velocityX = step.VelocityX;
        _velocityY = step.VelocityY;

        var movementX = SubpixelMotion.TakeWholePixels(step.OffsetX, ref _movementPixelRemainderX);
        var movementY = SubpixelMotion.TakeWholePixels(step.OffsetY, ref _movementPixelRemainderY);
        if (step.HitHorizontal)
        {
            _movementPixelRemainderX = 0;
        }
        if (step.HitVertical)
        {
            _movementPixelRemainderY = 0;
        }
        if (movementX != 0 || movementY != 0)
        {
            DisplayGeometry.OffsetWindowPhysical(_windowHandle, movementX, movementY);
        }

        if (step.HitAny && step.ImpactSpeed >= 70 * scale)
        {
            _enclosureResting = false;
            _state = PetState.Falling;
            _stateElapsed = 0;
            _animator.Play(_velocityY < -40 * scale ? "toss" : "fall");
            DiagnosticsLog.WriteEventThrottled(
                $"window-enclosure-impact-{_enclosureHandle}",
                TimeSpan.FromMilliseconds(90),
                "WindowEnclosureImpact",
                ("Window", FormatHandle(_enclosureHandle)),
                ("Walls", $"L={step.HitLeft},T={step.HitTop},R={step.HitRight},B={step.HitBottom}"),
                ("ImpactSpeed", step.ImpactSpeed),
                ("Velocity", $"{_velocityX:0.0},{_velocityY:0.0}"));
            _voice.Play(VoiceCue.Scream, VoicePriority.Important, TimeSpan.FromSeconds(2));
        }
        else if (step.IsResting && !_enclosureResting)
        {
            _enclosureResting = true;
            _state = PetState.Idle;
            _stateElapsed = 0;
            _animator.Play("idle");
            DiagnosticsLog.WriteEvent(
                "WindowEnclosureSettled",
                ("Window", FormatHandle(_enclosureHandle)),
                ("Velocity", $"{_velocityX:0.0},{_velocityY:0.0}"));
        }
        else if (!step.IsResting && Math.Abs(_velocityY) > 35 * scale)
        {
            _enclosureResting = false;
            _state = PetState.Falling;
            if (!_animator.CurrentClip.Equals("toss", StringComparison.OrdinalIgnoreCase) &&
                !_animator.CurrentClip.Equals("fall", StringComparison.OrdinalIgnoreCase))
            {
                _animator.Play(_velocityY < 0 ? "toss" : "fall");
            }
        }

        if (Math.Abs(_velocityX) > 8)
        {
            FacingTransform.ScaleX = Math.Sign(_velocityX);
        }
        var enclosureStretch = _enclosureResting
            ? 0
            : Math.Clamp(Math.Abs(_velocityY) / Math.Max(1, 1200 * scale), 0, 1);
        SquashTransform.ScaleX = 1 - enclosureStretch * 0.015;
        SquashTransform.ScaleY = 1 + enclosureStretch * 0.022;
        LeanTransform.Angle = Math.Clamp(_velocityX / Math.Max(1, 1200 * scale) * 10, -10, 10);
        GroundShadow.Width = _enclosureResting ? 188 : 150;
        return true;
    }

    private PetCollisionBounds GetPetCollisionBounds(double? bottomCanvasY = null)
    {
        var topLeft = CanvasPointToScreen(BodyCollisionLeftCanvasX, BodyCollisionTopCanvasY);
        var bottomRight = CanvasPointToScreen(BodyCollisionRightCanvasX, bottomCanvasY ?? BodyCollisionBottomCanvasY);
        return new PetCollisionBounds(topLeft.X, topLeft.Y, bottomRight.X, bottomRight.Y);
    }

    private bool TryBeginWindowEnclosure(double velocityX, double velocityY)
    {
        var petBounds = GetPetCollisionBounds(_character.Geometry.FootCanvasY);
        var petWidth = petBounds.Right - petBounds.Left;
        var petHeight = petBounds.Bottom - petBounds.Top;
        var centerX = (petBounds.Left + petBounds.Right) / 2;
        var centerY = (petBounds.Top + petBounds.Bottom) / 2;
        var containingBody = _surfaceProvider.WindowBodies.FirstOrDefault(body =>
            body.Width >= petWidth + 12 && body.Height >= petHeight + 12 &&
            centerX > body.Left && centerX < body.Right &&
            centerY > body.Top && centerY < body.Bottom &&
            WindowEnclosureEligibilityPolicy.IsBoundedBody(
                new DrawingRectangle(body.Left, body.Top, body.Width, body.Height),
                DisplayGeometry.GetAllMonitors()));
        if (containingBody.SourceHandle == IntPtr.Zero)
        {
            return false;
        }

        _enclosureHandle = containingBody.SourceHandle;
        _lastEnclosureBody = containingBody;
        _enclosureMotionInitialized = true;
        _enclosureResting = false;
        _supportHandle = IntPtr.Zero;
        _supportIsDesktopFloor = false;
        _temporarilyIgnoredLandingSupport = IntPtr.Zero;
        _ignoreLandingSupportUntil = 0;
        ResetAirbornePlatformSupport();
        ResetSupportMotionTracking();
        _state = PetState.Falling;
        _movementPurpose = MovementPurpose.None;
        _stateElapsed = 0;
        _velocityX = velocityX;
        _velocityY = velocityY;
        _movementPixelRemainderX = 0;
        _movementPixelRemainderY = 0;
        _fallPixelRemainder = 0;
        ResetBodyDeformation();
        _animator.Play(velocityY < -1 ? "toss" : "fall");
        DiagnosticsLog.WriteEvent(
            "WindowEnclosureEntered",
            ("Window", FormatHandle(_enclosureHandle)),
            ("Bounds", $"{containingBody.Left},{containingBody.Top},{containingBody.Right},{containingBody.Bottom}"),
            ("Velocity", $"{velocityX:0.0},{velocityY:0.0}"));
        return true;
    }

    private void ClearWindowEnclosure()
    {
        _enclosureHandle = IntPtr.Zero;
        _lastEnclosureBody = default;
        _enclosureMotionInitialized = false;
        _enclosureResting = false;
    }

    private void ReleaseWindowEnclosureForMeal(DrawingPoint target)
    {
        if (_enclosureHandle == IntPtr.Zero)
        {
            return;
        }

        var enclosureHandle = _enclosureHandle;
        var petBounds = GetPetCollisionBounds(_character.Geometry.FootCanvasY);
        var centerX = (petBounds.Left + petBounds.Right) / 2;
        var centerY = (petBounds.Top + petBounds.Bottom) / 2;
        var deltaX = target.X - centerX;
        var deltaY = target.Y - centerY;
        var exitSide = Math.Abs(deltaX) >= Math.Abs(deltaY)
            ? deltaX < 0 ? "Left" : "Right"
            : deltaY < 0 ? "Top" : "Bottom";

        ClearWindowEnclosure();
        ResetBodyDeformation();
        _manualLickRequested = false;
        DiagnosticsLog.WriteEvent(
            "WindowEnclosureMealEscape",
            ("Window", FormatHandle(enclosureHandle)),
            ("ExitSide", exitSide),
            ("Target", $"{target.X},{target.Y}"));
    }

    private bool TryResolveGroundSupport(bool allowSnap)
    {
        var foot = CanvasPointToScreen(_character.Geometry.FootCanvasX, _character.Geometry.FootCanvasY);
        WindowSurface? surface = null;
        if (_supportHandle != IntPtr.Zero)
        {
            var tracked = _surfaceProvider.FindLiveTrackedSurface(
                _supportHandle,
                _supportIsDesktopFloor,
                (int)Math.Round(foot.X),
                foot.Y);
            if (tracked is not null && tracked.ContainsX((int)Math.Round(foot.X), 8))
            {
                var verticalGap = tracked.Top - foot.Y;
                if (Math.Abs(verticalGap) <= 8 || verticalGap < 0 && verticalGap >= -80)
                {
                    surface = tracked;
                }
            }
        }

        surface ??= _surfaceProvider.FindSupport((int)Math.Round(foot.X), foot.Y, tolerance: 8);
        if (surface is null)
        {
            return false;
        }

        var supportChanged = _supportHandle != surface.SourceHandle ||
            _supportIsDesktopFloor != surface.IsDesktopFloor;
        _supportHandle = surface.SourceHandle;
        _supportIsDesktopFloor = surface.IsDesktopFloor;
        if (supportChanged)
        {
            ResetSupportMotionTracking(surface);
        }
        if (allowSnap)
        {
            var offsetY = (int)Math.Round(surface.Top - foot.Y);
            if (offsetY != 0)
            {
                DisplayGeometry.OffsetWindowPhysical(_windowHandle, 0, offsetY);
            }
        }

        return true;
    }

    private void UpdateSupportedPlatformPhysics(double delta)
    {
        var isGroundBound = _state is PetState.Idle or PetState.Landing or PetState.Curious or
                PetState.Sleeping or PetState.Waking or PetState.Rolling ||
            _state == PetState.Running && _movementPurpose is
                MovementPurpose.Wander or MovementPurpose.Explore or MovementPurpose.Patrol or MovementPurpose.ClimbApproach;
        if (!isGroundBound || delta <= 0 || _supportHandle == IntPtr.Zero || _supportIsDesktopFloor)
        {
            if (_supportIsDesktopFloor || _supportHandle == IntPtr.Zero)
            {
                _supportMotionInitialized = false;
                _supportVelocityY = 0;
            }
            return;
        }

        var foot = CanvasPointToScreen(_character.Geometry.FootCanvasX, _character.Geometry.FootCanvasY);
        var surface = _surfaceProvider.FindLiveTrackedSurface(
            _supportHandle,
            false,
            (int)Math.Round(foot.X),
            foot.Y);
        if (surface is null)
        {
            var bounceMonitor = DisplayGeometry.FromPoint(
                new DrawingPoint((int)Math.Round(foot.X), (int)Math.Round(foot.Y)));
            var bounceCenterX = bounceMonitor is null
                ? foot.X
                : bounceMonitor.WorkArea.Left + bounceMonitor.WorkArea.Width / 2.0;
            var maximizeBounce = WindowExpansionPhysics.Evaluate(
                supportUnavailable: true,
                windowMaximized: NativeMethods.IsZoomed(_supportHandle),
                foot.X,
                bounceCenterX,
                bounceMonitor?.Scale ?? 1);
            if (maximizeBounce is not null)
            {
                DiagnosticsLog.WriteEvent(
                    "WindowMaximizeBounce",
                    ("Support", FormatHandle(_supportHandle)),
                    ("PreviousTop", _lastSupportTop),
                    ("Foot", $"{foot.X:0.0},{foot.Y:0.0}"),
                    ("Velocity", $"{maximizeBounce.VelocityX:0.0},{maximizeBounce.VelocityY:0.0}"));
                _voice.Play(VoiceCue.Scream, VoicePriority.Critical);
                BeginFall(maximizeBounce.VelocityY, "WindowMaximized", maximizeBounce.VelocityX);
                return;
            }

            if (PreserveTransientPlatformSupport())
            {
                return;
            }

            DiagnosticsLog.WriteEvent(
                "PlatformSupportLost",
                ("Support", FormatHandle(_supportHandle)),
                ("Foot", $"{foot.X:0.0},{foot.Y:0.0}"),
                ("State", _state));
            BeginFall();
            return;
        }

        if (_supportUnavailableSince is double unavailableSince)
        {
            DiagnosticsLog.WriteEvent(
                "PlatformSupportRecovered",
                ("Support", FormatHandle(surface.SourceHandle)),
                ("UnavailableMs", (_clock.Elapsed.TotalSeconds - unavailableSince) * 1000));
            _supportUnavailableSince = null;
        }

        if (!_supportMotionInitialized || _supportMotionHandle != surface.SourceHandle)
        {
            ResetSupportMotionTracking(surface);
            return;
        }

        var displacementY = surface.Top - _lastSupportTop;
        var rawVelocityY = displacementY / Math.Max(delta, 1d / 240);
        _supportVelocityY += (rawVelocityY - _supportVelocityY) * Math.Min(1, delta * 22);
        _lastSupportTop = surface.Top;
        if (Math.Abs(displacementY) < 0.5)
        {
            return;
        }

        var monitor = DisplayGeometry.FromPoint(
            new DrawingPoint((int)Math.Round(foot.X), (int)Math.Round(foot.Y)));
        var scale = monitor?.Scale ?? 1;
        var decision = PlatformRidePhysics.Evaluate(displacementY, rawVelocityY, scale);
        var verticalGap = surface.Top - foot.Y;
        if (decision.Response == PlatformMotionResponse.LaunchUpward)
        {
            if (Math.Abs(verticalGap) >= 0.5)
            {
                DisplayGeometry.OffsetWindowPhysical(_windowHandle, 0, (int)Math.Round(verticalGap));
            }

            DiagnosticsLog.WriteEvent(
                "PlatformLaunch",
                ("Support", FormatHandle(surface.SourceHandle)),
                ("PlatformTop", surface.Top),
                ("DisplacementY", displacementY),
                ("PlatformVelocityY", rawVelocityY),
                ("PetInitialVelocityY", decision.InitialPetVelocityY),
                ("Scale", scale));
            _voice.Play(VoiceCue.Scream, VoicePriority.Critical);
            BeginFall(decision.InitialPetVelocityY, "PlatformLaunch");
            return;
        }

        if (decision.Response == PlatformMotionResponse.ReleaseDownward)
        {
            DiagnosticsLog.WriteEvent(
                "PlatformDownwardRelease",
                ("Support", FormatHandle(surface.SourceHandle)),
                ("PlatformTop", surface.Top),
                ("DisplacementY", displacementY),
                ("PlatformVelocityY", rawVelocityY),
                ("Scale", scale));
            BeginFall(0, "PlatformDownwardRelease");
            return;
        }

        if (Math.Abs(verticalGap) >= 0.5)
        {
            DisplayGeometry.OffsetWindowPhysical(_windowHandle, 0, (int)Math.Round(verticalGap));
        }

        DiagnosticsLog.WriteEventThrottled(
            $"platform-follow-{surface.SourceHandle}",
            TimeSpan.FromMilliseconds(350),
            "PlatformFollow",
            ("Support", FormatHandle(surface.SourceHandle)),
            ("DisplacementY", displacementY),
            ("PlatformVelocityY", rawVelocityY),
            ("FilteredVelocityY", _supportVelocityY));
    }

    private void ResetSupportMotionTracking(WindowSurface? surface = null)
    {
        _supportMotionHandle = surface?.SourceHandle ?? IntPtr.Zero;
        _lastSupportTop = surface?.Top ?? 0;
        _supportVelocityY = 0;
        _supportUnavailableSince = null;
        _supportMotionInitialized = surface is not null && !surface.IsDesktopFloor;
    }

    private bool PreserveTransientPlatformSupport()
    {
        if (_supportHandle == IntPtr.Zero || _supportIsDesktopFloor)
        {
            return false;
        }

        var now = _clock.Elapsed.TotalSeconds;
        _supportUnavailableSince ??= now;
        var unavailableSeconds = now - _supportUnavailableSince.Value;
        if (!PlatformRidePhysics.ShouldPreserveTransientSupport(unavailableSeconds))
        {
            return false;
        }

        RefreshSurfaceMap(force: true);
        DiagnosticsLog.WriteEventThrottled(
            $"platform-support-grace-{_supportHandle}",
            TimeSpan.FromMilliseconds(120),
            "PlatformSupportGrace",
            ("Support", FormatHandle(_supportHandle)),
            ("UnavailableMs", unavailableSeconds * 1000));
        return true;
    }

    private bool MaintainGroundSupport()
    {
        if (TryResolveGroundSupport(allowSnap: true))
        {
            return true;
        }

        if (PreserveTransientPlatformSupport())
        {
            return true;
        }

        BeginFall();
        return false;
    }

    private void BeginFall(
        double initialVelocity = 0,
        string reason = "SupportLost",
        double initialVelocityX = 0)
    {
        if (_state == PetState.Pranking)
            CancelWindowPrank(reason, returnToIdle: false, keepHatless: true);
        _pendingClimb = null;
        _activeClimb = null;
        var foot = CanvasPointToScreen(_character.Geometry.FootCanvasX, _character.Geometry.FootCanvasY);
        _fallReason = reason;
        _velocityX = initialVelocityX;
        _velocityY = initialVelocity;
        var previousSupport = _supportHandle;
        if ((reason is "PlatformLaunch" or "PlatformDownwardRelease") &&
            previousSupport != IntPtr.Zero && !_supportIsDesktopFloor)
        {
            _airbornePlatformSupportHandle = previousSupport;
            _lastAirbornePlatformTop = (int)Math.Round(_lastSupportTop);
            _airbornePlatformInitialized = true;
        }
        else
        {
            ResetAirbornePlatformSupport();
        }
        if (reason == "PlatformDownwardRelease")
        {
            _temporarilyIgnoredLandingSupport = previousSupport;
            _ignoreLandingSupportUntil = _clock.Elapsed.TotalSeconds;
            RefreshSurfaceMap(force: true);
        }
        else if (reason is "PlatformLaunch" or "DragThrow" or
            "TerrainHop" or "TerrainJumpDown" or "EdgeSlideRelease" or "WindowMaximized")
        {
            _temporarilyIgnoredLandingSupport = previousSupport;
            _ignoreLandingSupportUntil = _clock.Elapsed.TotalSeconds +
                (reason == "WindowMaximized" ? 0.46 : 0.32);
            RefreshSurfaceMap(force: true);
        }
        else
        {
            _temporarilyIgnoredLandingSupport = IntPtr.Zero;
            _ignoreLandingSupportUntil = 0;
        }
        _fallStartFootY = foot.Y;
        var floor = _surfaceProvider.Surfaces
            .Where(surface => surface.IsDesktopFloor && surface.ContainsX((int)Math.Round(foot.X)))
            .OrderBy(surface => Math.Abs(surface.Top - foot.Y))
            .FirstOrDefault();
        if (floor is not null && foot.Y >= floor.Top - 2 &&
            initialVelocity >= -1 && Math.Abs(initialVelocityX) < 1)
        {
            LandOn(floor);
            return;
        }

        _state = PetState.Falling;
        _movementPurpose = MovementPurpose.None;
        _stateElapsed = 0;
        _movementPixelRemainderX = 0;
        _movementPixelRemainderY = 0;
        _fallPixelRemainder = 0;
        _supportHandle = IntPtr.Zero;
        _supportIsDesktopFloor = false;
        ResetSupportMotionTracking();
        var isSelfPropelled = reason is "DragThrow" or "TerrainHop" or "TerrainJumpDown" or "EdgeSlideRelease";
        ResetBodyDeformation();
        _animator.Play(initialVelocity < -1 ? isSelfPropelled ? "jump" : "toss" : "fall");
        FacingTransform.ScaleX = Math.Abs(initialVelocityX) > 1 ? Math.Sign(initialVelocityX) : 1;
        LeanTransform.Angle = 0;
        DiagnosticsLog.WriteEvent(
            "FallStarted",
            ("Reason", reason),
            ("InitialVelocityX", initialVelocityX),
            ("InitialVelocityY", initialVelocity),
            ("PreviousSupport", FormatHandle(previousSupport)),
            ("StartFoot", $"{foot.X:0.0},{foot.Y:0.0}"));
    }

    private System.Windows.Point ResolveAirbornePlatformContact(System.Windows.Point foot, double delta)
    {
        if (_airbornePlatformSupportHandle == IntPtr.Zero)
        {
            return foot;
        }

        var surface = _surfaceProvider.FindLiveTrackedSurface(
            _airbornePlatformSupportHandle,
            false,
            (int)Math.Round(foot.X),
            foot.Y);
        if (surface is null)
        {
            return foot;
        }

        if (!_airbornePlatformInitialized)
        {
            _lastAirbornePlatformTop = surface.Top;
            _airbornePlatformInitialized = true;
            return foot;
        }

        var previousPlatformTop = _lastAirbornePlatformTop;
        _lastAirbornePlatformTop = surface.Top;
        var monitor = DisplayGeometry.FromPoint(
            new DrawingPoint((int)Math.Round(foot.X), (int)Math.Round(foot.Y)));
        var contact = PlatformRidePhysics.ResolveAirborneContact(
            foot.Y,
            _velocityY,
            previousPlatformTop,
            surface.Top,
            delta,
            monitor?.Scale ?? 1);
        if (!contact.PushesPet)
        {
            return foot;
        }

        var correctionY = (int)Math.Round(contact.CorrectedFootY - foot.Y);
        if (correctionY != 0)
        {
            DisplayGeometry.OffsetWindowPhysical(_windowHandle, 0, correctionY);
        }
        _velocityY = contact.PetVelocityY;
        _fallPixelRemainder = 0;
        _ignoreLandingSupportUntil = _clock.Elapsed.TotalSeconds + 0.18;
        DiagnosticsLog.WriteEventThrottled(
            $"airborne-platform-push-{_airbornePlatformSupportHandle}",
            TimeSpan.FromMilliseconds(100),
            "AirbornePlatformPush",
            ("Support", FormatHandle(_airbornePlatformSupportHandle)),
            ("CorrectionY", correctionY),
            ("PlatformTop", surface.Top),
            ("PlatformVelocityY", contact.PlatformVelocityY),
            ("PetVelocityY", _velocityY));
        return new System.Windows.Point(foot.X, foot.Y + correctionY);
    }

    private void ResetAirbornePlatformSupport()
    {
        _airbornePlatformSupportHandle = IntPtr.Zero;
        _lastAirbornePlatformTop = 0;
        _airbornePlatformInitialized = false;
    }

    private IReadOnlyList<WindowSurface> GetLandingSurfaces(int footX, double footY)
    {
        if (_clock.Elapsed.TotalSeconds < _ignoreLandingSupportUntil)
        {
            return _surfaceProvider.Surfaces
                .Where(surface => surface.SourceHandle != _temporarilyIgnoredLandingSupport)
                .ToArray();
        }

        if (_temporarilyIgnoredLandingSupport == IntPtr.Zero)
        {
            return _surfaceProvider.Surfaces;
        }

        var liveSurface = _surfaceProvider.FindLiveTrackedSurface(
            _temporarilyIgnoredLandingSupport,
            false,
            footX,
            footY);
        if (liveSurface is null)
        {
            return _surfaceProvider.Surfaces;
        }

        return _surfaceProvider.Surfaces
            .Where(surface => surface.SourceHandle != _temporarilyIgnoredLandingSupport)
            .Append(liveSurface)
            .OrderBy(surface => surface.Top)
            .ThenByDescending(surface => surface.IsDesktopFloor)
            .ToArray();
    }

    private void UpdateFalling(double delta)
    {
        var foot = CanvasPointToScreen(_character.Geometry.FootCanvasX, _character.Geometry.FootCanvasY);
        foot = ResolveAirbornePlatformContact(foot, delta);
        var monitor = DisplayGeometry.FromPoint(
            new DrawingPoint((int)Math.Round(foot.X), (int)Math.Round(foot.Y)));
        var scale = monitor?.Scale ?? 1;
        _velocityX *= Math.Exp(-0.48 * delta);
        _velocityY = Math.Min(1350 * scale, _velocityY + 1650 * scale * delta);
        if (_velocityY >= 0 && _animator.CurrentClip.Equals("toss", StringComparison.OrdinalIgnoreCase))
        {
            _animator.Play("fall");
            DiagnosticsLog.WriteEvent(
                "TossApexReached",
                ("Foot", $"{foot.X:0.0},{foot.Y:0.0}"),
                ("VelocityY", _velocityY));
        }
        var movementX = SubpixelMotion.TakeWholePixels(_velocityX * delta, ref _movementPixelRemainderX);
        var movementY = SubpixelMotion.TakeWholePixels(_velocityY * delta, ref _fallPixelRemainder);
        var nextFootX = foot.X + movementX;
        var nextFootY = foot.Y + movementY;
        var boundaryMonitor = DisplayGeometry.FromPoint(
            new DrawingPoint((int)Math.Round(nextFootX), (int)Math.Round(nextFootY)));
        if (boundaryMonitor is not null)
        {
            var horizontal = AirborneBoundaryPolicy.ResolveHorizontal(
                nextFootX,
                _velocityX,
                boundaryMonitor.MonitorArea.Left,
                boundaryMonitor.MonitorArea.Right,
                8 * boundaryMonitor.Scale);
            if (horizontal.Corrected)
            {
                _velocityX = horizontal.VelocityX;
                movementX = (int)Math.Round(horizontal.NextFootX - foot.X);
                nextFootX = foot.X + movementX;
                _movementPixelRemainderX = 0;
                DiagnosticsLog.WriteEventThrottled(
                    "airborne-monitor-edge-bounce",
                    TimeSpan.FromMilliseconds(400),
                    "AirborneMonitorEdgeBounce",
                    ("Foot", $"{foot.X:0.0},{foot.Y:0.0}"),
                    ("CorrectedFootX", nextFootX),
                    ("VelocityX", _velocityX));
            }
        }
        if (movementY > 0)
        {
            var landingSurfaces = GetLandingSurfaces(
                (int)Math.Round(nextFootX),
                nextFootY);
            var recovery = boundaryMonitor is null
                ? null
                : WindowSurfaceProvider.FindDesktopRecoverySurface(
                    landingSurfaces,
                    boundaryMonitor.Handle,
                    (int)Math.Round(nextFootX),
                    foot.Y,
                    inset: 10);
            if (recovery is not null)
            {
                if (movementX != 0)
                {
                    DisplayGeometry.OffsetWindowPhysical(_windowHandle, movementX, 0);
                }

                DiagnosticsLog.WriteEvent(
                    "FallRecoveredFromOutsideWorkArea",
                    ("Foot", $"{foot.X:0.0},{foot.Y:0.0}"),
                    ("CorrectedFootX", nextFootX),
                    ("SurfaceTop", recovery.Top));
                LandOn(recovery);
                return;
            }

            var landing = WindowSurfaceProvider.FindLandingSurface(
                landingSurfaces,
                (int)Math.Round(nextFootX),
                foot.Y,
                nextFootY,
                inset: 10);
            if (landing is not null)
            {
                LandOn(landing);
                return;
            }
        }
        if (movementX != 0 || movementY != 0)
        {
            DisplayGeometry.OffsetWindowPhysical(_windowHandle, movementX, movementY);
        }
        var stretch = Math.Clamp(_velocityY / Math.Max(1, 1350 * scale), 0, 1);
        SquashTransform.ScaleX = 1 - stretch * 0.018;
        SquashTransform.ScaleY = 1 + stretch * 0.028;
        if (Math.Abs(_velocityX) > 8)
        {
            FacingTransform.ScaleX = Math.Sign(_velocityX);
        }
        LeanTransform.Angle = Math.Clamp(_velocityX / Math.Max(1, 1100 * scale) * 8, -8, 8);
        GroundShadow.Width = 175 - stretch * 35;
    }

    private void LandOn(WindowSurface surface)
    {
        var foot = CanvasPointToScreen(_character.Geometry.FootCanvasX, _character.Geometry.FootCanvasY);
        var offsetY = (int)Math.Round(surface.Top - foot.Y);
        if (offsetY != 0)
        {
            DisplayGeometry.OffsetWindowPhysical(_windowHandle, 0, offsetY);
        }

        var fallDistance = Math.Max(0, surface.Top - _fallStartFootY);
        var downwardImpactSpeed = Math.Max(0, _velocityY);
        var landingReason = _fallReason;
        var monitorScale = DisplayGeometry.FromPoint(
            new DrawingPoint((int)Math.Round(foot.X), surface.Top))?.Scale ?? 1;
        _supportHandle = surface.SourceHandle;
        _supportIsDesktopFloor = surface.IsDesktopFloor;
        ResetSupportMotionTracking(surface);
        _state = PetState.Landing;
        _movementPurpose = MovementPurpose.None;
        _stateElapsed = 0;
        _velocityX = 0;
        _velocityY = 0;
        _movementPixelRemainderX = 0;
        _movementPixelRemainderY = 0;
        _fallPixelRemainder = 0;
        _temporarilyIgnoredLandingSupport = IntPtr.Zero;
        _ignoreLandingSupportUntil = 0;
        ResetAirbornePlatformSupport();
        ResetBodyDeformation();
        _animator.Play("land");
        DiagnosticsLog.WriteEvent(
            "Landed",
            ("Support", FormatHandle(surface.SourceHandle)),
            ("Desktop", surface.IsDesktopFloor),
            ("SurfaceTop", surface.Top),
            ("FallDistance", fallDistance),
            ("ImpactSpeed", downwardImpactSpeed),
            ("Reason", landingReason));
        if (LandingVoicePolicy.ShouldReact(
                fallDistance,
                downwardImpactSpeed,
                landingReason,
                monitorScale))
        {
            _voice.Play(
                VoiceCue.Landing,
                VoicePriority.Important,
                TimeSpan.FromSeconds(12),
                minimumIntervalSinceAnyVoice: TimeSpan.FromSeconds(2.5));
        }
        _fallReason = "None";
    }

    private void UpdateLanding()
    {
        if (!MaintainGroundSupport())
        {
            return;
        }

        SquashTransform.ScaleX = 1;
        SquashTransform.ScaleY = 1;
        LeanTransform.Angle = 0;
        GroundShadow.Width = 202;
        if (_stateElapsed >= 1.05)
        {
            if (_character.SupportsFood && _foodQueue.Count > 0 && _settings.ReactToDeletes)
            {
                StartNextMeal();
            }
            else
            {
                ReturnToIdle();
            }
        }
    }

    private bool StartGroundMove(MovementPurpose purpose)
    {
        RefreshSurfaceMap(force: true);
        if (!TryResolveGroundSupport(allowSnap: true))
        {
            BeginFall();
            return false;
        }

        var foot = CanvasPointToScreen(_character.Geometry.FootCanvasX, _character.Geometry.FootCanvasY);
        var surface = _surfaceProvider.FindLiveTrackedSurface(
            _supportHandle,
            _supportIsDesktopFloor,
            (int)Math.Round(foot.X),
            foot.Y);
        if (surface is null)
        {
            BeginFall();
            return false;
        }

        var windowWidth = NativeMethods.GetWindowRect(_windowHandle, out var rectangle)
            ? rectangle.Right - rectangle.Left
            : (int)Math.Round(ActualWidth);
        var margin = Math.Clamp((int)Math.Round(windowWidth * 0.20), 24, 72);
        var minimumX = surface.Left + margin;
        var maximumX = surface.Right - margin;
        if (maximumX - minimumX < 42)
        {
            return false;
        }

        var travel = Random.Shared.Next(80, 241);
        var direction = Random.Shared.Next(2) == 0 ? -1 : 1;
        var targetX = Math.Clamp((int)Math.Round(foot.X) + direction * travel, minimumX, maximumX);
        if (Math.Abs(targetX - foot.X) < 36)
        {
            var distanceToLeft = Math.Abs(minimumX - foot.X);
            var distanceToRight = Math.Abs(maximumX - foot.X);
            targetX = distanceToLeft > distanceToRight ? minimumX : maximumX;
        }

        if (Math.Abs(targetX - foot.X) < 24)
        {
            return false;
        }

        _targetScreenX = targetX;
        _targetScreenY = surface.Top;
        _movementPurpose = purpose;
        _state = PetState.Running;
        _stateElapsed = 0;
        _velocityX = 0;
        _velocityY = 0;
        _movementPixelRemainderX = 0;
        _movementPixelRemainderY = 0;
        _animator.Play("walk");
        FacingTransform.ScaleX = targetX < foot.X ? -1 : 1;
        return true;
    }

    private bool StartContinuousPatrol(bool preferClimb)
    {
        RefreshSurfaceMap(force: true);
        if (!TryResolveGroundSupport(allowSnap: true))
        {
            BeginFall();
            return false;
        }

        _state = PetState.Running;
        _movementPurpose = MovementPurpose.Patrol;
        _stateElapsed = 0;
        _velocityX = 0;
        _velocityY = 0;
        _movementPixelRemainderX = 0;
        _movementPixelRemainderY = 0;
        _nextPatrolBehaviorCheck = _clock.Elapsed.TotalSeconds + LifeBehaviorPolicy.PatrolNeedCheckSeconds;
        _animator.Play("walk");
        if (!ChooseNextPatrolTarget(preferClimb))
        {
            ReturnToIdle();
            return false;
        }

        DiagnosticsLog.WriteEvent(
            "ContinuousPatrolStarted",
            ("PreferClimb", preferClimb),
            ("Support", FormatHandle(_supportHandle)),
            ("TargetX", _targetScreenX));
        return true;
    }

    private bool ChooseNextPatrolTarget(bool preferClimb)
    {
        var foot = CanvasPointToScreen(_character.Geometry.FootCanvasX, _character.Geometry.FootCanvasY);
        var surface = _surfaceProvider.FindLiveTrackedSurface(
            _supportHandle,
            _supportIsDesktopFloor,
            (int)Math.Round(foot.X),
            foot.Y);
        if (surface is null)
        {
            return false;
        }

        var monitor = DisplayGeometry.FromPoint(
            new DrawingPoint((int)Math.Round(foot.X), (int)Math.Round(foot.Y)));
        var scale = monitor?.Scale ?? 1;
        var now = _clock.Elapsed.TotalSeconds;
        var shouldLookForClimb = (!IsBehaviorControlMode || _behaviorAutonomyEnabled) && now >= _nextClimbAttemptAt &&
            (preferClimb || Random.Shared.NextDouble() < 0.34);
        if (shouldLookForClimb)
        {
            var climb = WindowClimbPlanner.Choose(
                _surfaceProvider.Surfaces,
                surface,
                (int)Math.Round(foot.X),
                scale,
                Random.Shared.NextDouble());
            if (climb is not null)
            {
                _pendingClimb = climb;
                _movementPurpose = MovementPurpose.ClimbApproach;
                _targetScreenX = climb.ApproachX;
                _targetScreenY = surface.Top;
                _patrolDirection = climb.ApproachX < foot.X ? -1 : 1;
                FacingTransform.ScaleX = _patrolDirection;
                _animator.Play("walk", restart: false);
                DiagnosticsLog.WriteEvent(
                    "ClimbTargetChosen",
                    ("Target", FormatHandle(climb.TargetHandle)),
                    ("Side", climb.Side),
                    ("ApproachX", climb.ApproachX),
                    ("LandingX", climb.LandingX),
                    ("Height", climb.Height));
                return true;
            }

            _nextClimbAttemptAt = now + 12;
        }

        _pendingClimb = null;
        _movementPurpose = MovementPurpose.Patrol;
        var windowWidth = NativeMethods.GetWindowRect(_windowHandle, out var rectangle)
            ? rectangle.Right - rectangle.Left
            : (int)Math.Round(ActualWidth * scale);
        var margin = Math.Clamp((int)Math.Round(windowWidth * 0.20), 24, 72);
        var minimumX = surface.Left + margin;
        var maximumX = surface.Right - margin;
        if (maximumX - minimumX < 42)
        {
            return false;
        }

        if (_patrolDirection == 0)
        {
            _patrolDirection = Random.Shared.Next(2) == 0 ? -1 : 1;
        }
        var targetX = _patrolDirection < 0 ? minimumX : maximumX;
        if (Math.Abs(targetX - foot.X) < 20)
        {
            _patrolDirection *= -1;
            targetX = _patrolDirection < 0 ? minimumX : maximumX;
        }

        _targetScreenX = targetX;
        _targetScreenY = surface.Top;
        FacingTransform.ScaleX = _patrolDirection;
        _animator.Play("walk", restart: false);
        DiagnosticsLog.WriteEventThrottled(
            "patrol-target",
            TimeSpan.FromSeconds(2),
            "PatrolTargetChanged",
            ("Support", FormatHandle(surface.SourceHandle)),
            ("TargetX", targetX),
            ("Direction", _patrolDirection));
        return true;
    }

    private void UpdateGroundRunning(double delta)
    {
        if (_movementPurpose is MovementPurpose.Patrol or MovementPurpose.ClimbApproach &&
            _clock.Elapsed.TotalSeconds >= _nextPatrolBehaviorCheck)
        {
            _nextPatrolBehaviorCheck = _clock.Elapsed.TotalSeconds + LifeBehaviorPolicy.PatrolNeedCheckSeconds;
            if (TryInterruptPatrolForNeed())
            {
                return;
            }
        }

        var foot = CanvasPointToScreen(_character.Geometry.FootCanvasX, _character.Geometry.FootCanvasY);
        var surface = _surfaceProvider.FindLiveTrackedSurface(
            _supportHandle,
            _supportIsDesktopFloor,
            (int)Math.Round(foot.X),
            foot.Y);
        if (surface is null || !surface.ContainsX((int)Math.Round(foot.X), 8))
        {
            BeginFall();
            return;
        }

        var verticalGap = surface.Top - foot.Y;
        if (verticalGap > 8 || verticalGap < -80)
        {
            BeginFall();
            return;
        }

        if (Math.Abs(verticalGap) >= 1)
        {
            DisplayGeometry.OffsetWindowPhysical(_windowHandle, 0, (int)Math.Round(verticalGap));
        }

        _targetScreenX = Math.Clamp(_targetScreenX, surface.Left + 20, surface.Right - 20);
        _targetScreenY = surface.Top;
        var deltaX = _targetScreenX - foot.X;
        var isContinuousPatrol = _movementPurpose is MovementPurpose.Patrol or MovementPurpose.ClimbApproach or
            MovementPurpose.EdgeSlideApproach or MovementPurpose.EdgeJumpApproach;
        if (Math.Abs(deltaX) <= 5 || !isContinuousPatrol && _stateElapsed > 8)
        {
            DisplayGeometry.OffsetWindowPhysical(_windowHandle, (int)Math.Round(deltaX), 0);
            if (_movementPurpose == MovementPurpose.ClimbApproach && _pendingClimb is not null)
            {
                BeginClimb(_pendingClimb);
                return;
            }
            if (_movementPurpose == MovementPurpose.EdgeSlideApproach)
            {
                BeginEdgeSlide(surface, _patrolDirection);
                return;
            }
            if (_movementPurpose == MovementPurpose.EdgeJumpApproach)
            {
                BeginTerrainJumpDown(_patrolDirection);
                return;
            }
            if (_movementPurpose == MovementPurpose.Patrol)
            {
                if (TryBeginTerrainActionAtEdge(surface))
                {
                    return;
                }
                _patrolDirection *= -1;
                if (!ChooseNextPatrolTarget(preferClimb: false))
                {
                    ReturnToIdle();
                }
                return;
            }
            FinishGroundMove();
            return;
        }

        var monitor = DisplayGeometry.FromPoint(
            new DrawingPoint((int)Math.Round(foot.X), (int)Math.Round(foot.Y)));
        var speed = (IsBehaviorControlMode && _activeBehavior == BehaviorControlAction.Run ? 250 :
            _movementPurpose is MovementPurpose.Explore or MovementPurpose.ClimbApproach or
                MovementPurpose.EdgeSlideApproach or MovementPurpose.EdgeJumpApproach ? 118 : 92) *
            (monitor?.Scale ?? 1) * _character.MovementSpeedMultiplier;
        var desiredVelocity = Math.Sign(deltaX) * speed;
        _velocityX += (desiredVelocity - _velocityX) * Math.Min(1, delta * 5.8);
        var movementX = SubpixelMotion.TakeWholePixels(_velocityX * delta, ref _movementPixelRemainderX);

        if (Math.Abs(movementX) > Math.Abs(deltaX))
        {
            movementX = (int)Math.Round(deltaX);
        }

        DisplayGeometry.OffsetWindowPhysical(_windowHandle, movementX, 0);
        FacingTransform.ScaleX = deltaX < 0 ? -1 : 1;
        SquashTransform.ScaleX = 1;
        SquashTransform.ScaleY = 1;
        LeanTransform.Angle = Math.Clamp(_velocityX / Math.Max(1, speed) * 2.2, -2.2, 2.2);
        GroundShadow.Width = 190;
    }

    private bool TryBeginTerrainActionAtEdge(WindowSurface surface)
    {
        if (IsBehaviorControlMode && !_behaviorAutonomyEnabled) return false;
        var now = _clock.Elapsed.TotalSeconds;
        if (now < _nextTerrainActionAt)
        {
            return false;
        }

        var action = TerrainBehaviorPlanner.ChooseAtEdge(surface.IsDesktopFloor, Random.Shared.NextDouble());
        if (action == TerrainEdgeAction.TurnAround)
        {
            return false;
        }

        _nextTerrainActionAt = now + 14;
        DiagnosticsLog.WriteEvent(
            "TerrainEdgeActionChosen",
            ("Action", action),
            ("Support", FormatHandle(surface.SourceHandle)),
            ("Desktop", surface.IsDesktopFloor),
            ("Direction", _patrolDirection));
        switch (action)
        {
            case TerrainEdgeAction.Hop:
                BeginTerrainHop(surface.IsDesktopFloor ? -_patrolDirection : _patrolDirection);
                return true;
            case TerrainEdgeAction.SlideDown:
                BeginEdgeSlide(surface, _patrolDirection);
                return true;
            case TerrainEdgeAction.JumpDown:
                BeginTerrainJumpDown(_patrolDirection);
                return true;
            default:
                return false;
        }
    }

    private bool BeginTerrainHop(int direction = 0)
    {
        RefreshSurfaceMap(force: true);
        if (!TryResolveGroundSupport(allowSnap: true))
        {
            BeginFall();
            return false;
        }

        var foot = CanvasPointToScreen(_character.Geometry.FootCanvasX, _character.Geometry.FootCanvasY);
        var monitor = DisplayGeometry.FromPoint(
            new DrawingPoint((int)Math.Round(foot.X), (int)Math.Round(foot.Y)));
        var scale = monitor?.Scale ?? 1;
        direction = direction == 0 ? Random.Shared.Next(2) == 0 ? -1 : 1 : Math.Sign(direction);
        _voice.Play(VoiceCue.Positive, VoicePriority.Ambient, TimeSpan.FromSeconds(12));
        DiagnosticsLog.WriteEvent(
            "TerrainHopStarted",
            ("Support", FormatHandle(_supportHandle)),
            ("Direction", direction),
            ("Velocity", $"{direction * 315 * scale:0.0},{-560 * scale:0.0}"));
        BeginFall(-560 * scale, "TerrainHop", direction * 315 * scale);
        return true;
    }

    private void BeginTerrainJumpDown(int direction)
    {
        var foot = CanvasPointToScreen(_character.Geometry.FootCanvasX, _character.Geometry.FootCanvasY);
        var monitor = DisplayGeometry.FromPoint(
            new DrawingPoint((int)Math.Round(foot.X), (int)Math.Round(foot.Y)));
        var scale = monitor?.Scale ?? 1;
        direction = direction == 0 ? 1 : Math.Sign(direction);
        DiagnosticsLog.WriteEvent(
            "TerrainJumpDownStarted",
            ("Support", FormatHandle(_supportHandle)),
            ("Direction", direction),
            ("Velocity", $"{direction * 255 * scale:0.0},{-175 * scale:0.0}"));
        BeginFall(-175 * scale, "TerrainJumpDown", direction * 255 * scale);
    }

    private void BeginEdgeSlide(WindowSurface surface, int direction)
    {
        var foot = CanvasPointToScreen(_character.Geometry.FootCanvasX, _character.Geometry.FootCanvasY);
        var monitor = DisplayGeometry.FromPoint(
            new DrawingPoint((int)Math.Round(foot.X), (int)Math.Round(foot.Y)));
        var scale = monitor?.Scale ?? 1;
        _slideSurface = surface;
        _slideDirection = direction == 0 ? 1 : Math.Sign(direction);
        _slideStartFootY = foot.Y;
        _slideEndFootY = Math.Max(
            _slideStartFootY + 56 * scale,
            Math.Min(surface.EffectiveBottom - 34 * scale, _slideStartFootY + 310 * scale));
        _slideDuration = Math.Clamp((_slideEndFootY - _slideStartFootY) / (145 * scale), 1.15, 2.6);
        _state = PetState.Sliding;
        _movementPurpose = MovementPurpose.None;
        _stateElapsed = 0;
        _velocityX = 0;
        _velocityY = 0;
        _movementPixelRemainderX = 0;
        _movementPixelRemainderY = 0;
        _animator.Play("slide");
        FacingTransform.ScaleX = _slideDirection < 0 ? 1 : -1;
        GroundShadow.Width = 0;
        DiagnosticsLog.WriteEvent(
            "EdgeSlideStarted",
            ("Support", FormatHandle(surface.SourceHandle)),
            ("Side", _slideDirection < 0 ? "Left" : "Right"),
            ("StartY", _slideStartFootY),
            ("EndY", _slideEndFootY),
            ("Duration", _slideDuration));
    }

    private void UpdateSliding()
    {
        if (_slideSurface is null || !NativeMethods.IsWindowVisible(_slideSurface.SourceHandle))
        {
            BeginFall(90, "EdgeSlideWindowLost", _slideDirection * 65);
            return;
        }

        var liveSurface = _surfaceProvider.Surfaces
            .Where(surface => surface.SourceHandle == _slideSurface.SourceHandle && !surface.IsDesktopFloor)
            .OrderBy(surface => Math.Abs(surface.Top - _slideSurface.Top))
            .FirstOrDefault() ?? _slideSurface;
        _slideSurface = liveSurface;
        var foot = CanvasPointToScreen(_character.Geometry.FootCanvasX, _character.Geometry.FootCanvasY);
        var progress = SmoothStep(Math.Clamp(_stateElapsed / Math.Max(0.001, _slideDuration), 0, 1));
        var contactX = _slideDirection < 0 ? SlideContactCanvasX : 512 - SlideContactCanvasX;
        var contact = CanvasPointToScreen(contactX, 245);
        var desiredContactX = _slideDirection < 0 ? liveSurface.Left : liveSurface.Right;
        var desiredY = Lerp(_slideStartFootY, _slideEndFootY, progress);
        DisplayGeometry.OffsetWindowPhysical(
            _windowHandle,
            (int)Math.Round(desiredContactX - contact.X),
            (int)Math.Round(desiredY - foot.Y));
        SquashTransform.ScaleX = 1;
        SquashTransform.ScaleY = 1;
        LeanTransform.Angle = _slideDirection * -3;
        GroundShadow.Width = 0;

        if (progress < 1)
        {
            return;
        }

        var direction = _slideDirection;
        _slideSurface = null;
        DiagnosticsLog.WriteEvent(
            "EdgeSlideReleased",
            ("Direction", direction),
            ("FootY", desiredY));
        BeginFall(105, "EdgeSlideRelease", direction * 85);
    }

    private bool TryInterruptPatrolForNeed()
    {
        if (IsBehaviorControlMode && !_behaviorAutonomyEnabled) return false;
        if (_settings.QuietMode)
        {
            return false;
        }

        if (_character.SupportsFood && _foodQueue.Count > 0 && _settings.ReactToDeletes)
        {
            StartNextMeal();
            return true;
        }

        if (_enclosureHandle != IntPtr.Zero)
        {
            return false;
        }

        var available = new HashSet<AutonomousAction>();
        var now = _clock.Elapsed.TotalSeconds;
        AddIfReady(AutonomousAction.Sleep, _lifeState.Sleepiness >= LifeBehaviorPolicy.SleepInterruptionThreshold);
        AddIfReady(AutonomousAction.AskForFood, _character.SupportsFood && _lifeState.Hunger >= LifeBehaviorPolicy.FoodAppealInterruptionThreshold);
        AddIfReady(AutonomousAction.LickIcon, _character.SupportsFood && _lifeState.Hunger >= LifeBehaviorPolicy.LickInterruptionThreshold);
        AddIfReady(AutonomousAction.Roll, _character.SupportsFood && _lifeState.Hunger >= LifeBehaviorPolicy.RollInterruptionThreshold);
        if (available.Count == 0)
        {
            return false;
        }

        var action = AutonomousBehaviorPlanner.Choose(
            _lifeState,
            hasGroundSupport: true,
            available,
            Random.Shared.NextDouble(),
            _character);
        switch (action)
        {
            case AutonomousAction.Sleep:
                MarkActionCooldown(action, 90);
                BeginSleep();
                return true;
            case AutonomousAction.AskForFood:
                MarkActionCooldown(action, 55);
                MarkHungerReactionCooldown(action);
                BeginCurious(completedExploration: false, hungryAppeal: true);
                return true;
            case AutonomousAction.LickIcon:
                MarkActionCooldown(action, 55);
                MarkHungerReactionCooldown(action);
                if (TryBeginIconLick(manualRequest: false))
                {
                    return true;
                }
                return BeginRoll();
            case AutonomousAction.Roll:
                MarkActionCooldown(action, 42);
                MarkHungerReactionCooldown(action);
                return BeginRoll();
            default:
                return false;
        }

        void AddIfReady(AutonomousAction action, bool condition)
        {
            if (condition &&
                PetModePolicy.AllowsAutonomousAction(action, EffectiveFastingMode, _settings.QuietMode) &&
                IsAutonomousActionReady(action, now))
            {
                available.Add(action);
            }
        }
    }

    private void FinishGroundMove()
    {
        var completedPurpose = _movementPurpose;
        _movementPurpose = MovementPurpose.None;
        _velocityX = 0;
        _velocityY = 0;
        _movementPixelRemainderX = 0;
        _movementPixelRemainderY = 0;
        LeanTransform.Angle = 0;
        if (completedPurpose == MovementPurpose.Explore)
        {
            BeginCurious(completedExploration: true);
        }
        else
        {
            ReturnToIdle();
        }
    }

    private void BeginClimb(WindowClimbPlan plan)
    {
        var foot = CanvasPointToScreen(_character.Geometry.FootCanvasX, _character.Geometry.FootCanvasY);
        var monitor = DisplayGeometry.FromPoint(
            new DrawingPoint((int)Math.Round(foot.X), (int)Math.Round(foot.Y)));
        var scale = monitor?.Scale ?? 1;
        _pendingClimb = null;
        _activeClimb = plan;
        _climbStartFootY = foot.Y;
        _climbDuration = Math.Clamp(plan.Height / Math.Max(1, 175 * scale * _character.MovementSpeedMultiplier), 2.0, 6.4);
        _climbFailureProgress = 0.56 + Random.Shared.NextDouble() * 0.20;
        var successChance = plan.Height > 700 * scale ? 0.58 : 0.74;
        _climbSucceeds = Random.Shared.NextDouble() < successChance;
        _state = PetState.Climbing;
        _movementPurpose = MovementPurpose.None;
        _stateElapsed = 0;
        _velocityX = 0;
        _velocityY = 0;
        _movementPixelRemainderX = 0;
        _movementPixelRemainderY = 0;
        FacingTransform.ScaleX = plan.Side == WindowClimbSide.Left ? 1 : -1;
        LeanTransform.Angle = 0;
        GroundShadow.Width = 0;
        _animator.Play("climb");
        _voice.Play(VoiceCue.Positive, VoicePriority.Normal, TimeSpan.FromSeconds(8));
        DiagnosticsLog.WriteEvent(
            "ClimbStarted",
            ("Target", FormatHandle(plan.TargetHandle)),
            ("Side", plan.Side),
            ("Height", plan.Height),
            ("Duration", _climbDuration),
            ("WillSucceed", _climbSucceeds),
            ("FailureProgress", _climbFailureProgress));
    }

    private void UpdateClimbing()
    {
        if (_activeClimb is null)
        {
            BeginFall(reason: "ClimbPlanMissing");
            return;
        }

        var plan = _activeClimb;
        var target = _surfaceProvider.Surfaces
            .Where(surface => !surface.IsDesktopFloor && surface.SourceHandle == plan.TargetHandle)
            .OrderBy(surface => Math.Abs(surface.Top - plan.TargetTop))
            .FirstOrDefault();
        if (target is null)
        {
            DiagnosticsLog.WriteEvent(
                "ClimbAborted",
                ("Target", FormatHandle(plan.TargetHandle)),
                ("Reason", "TargetWindowUnavailable"));
            _activeClimb = null;
            BeginFall(initialVelocity: 40, reason: "ClimbTargetLost");
            return;
        }

        var foot = CanvasPointToScreen(_character.Geometry.FootCanvasX, _character.Geometry.FootCanvasY);
        var monitor = DisplayGeometry.FromPoint(
            new DrawingPoint((int)Math.Round(foot.X), (int)Math.Round(foot.Y)));
        var scale = monitor?.Scale ?? 1;
        var sideGap = Math.Max(8, (int)Math.Round(14 * scale));
        var landingInset = Math.Max(20, (int)Math.Round(34 * scale));
        var approachX = plan.Side == WindowClimbSide.Left
            ? target.Left - sideGap
            : target.Right + sideGap;
        var landingX = plan.Side == WindowClimbSide.Left
            ? target.Left + landingInset
            : target.Right - landingInset;
        if (!_climbSucceeds && _stateElapsed / Math.Max(0.001, _climbDuration) >= _climbFailureProgress)
        {
            DiagnosticsLog.WriteEvent(
                "ClimbSlipped",
                ("Target", FormatHandle(plan.TargetHandle)),
                ("Progress", _climbFailureProgress),
                ("Foot", $"{foot.X:0.0},{foot.Y:0.0}"));
            _voice.Play(VoiceCue.Aggrieved, VoicePriority.Important, TimeSpan.FromSeconds(8));
            _activeClimb = null;
            _nextClimbAttemptAt = _clock.Elapsed.TotalSeconds + 24;
            BeginFall(initialVelocity: 80, reason: "ClimbSlip");
            return;
        }

        var progress = SmoothStep(Math.Clamp(_stateElapsed / Math.Max(0.001, _climbDuration), 0, 1));
        var horizontalProgress = SmoothStep(Math.Clamp((progress - 0.78) / 0.22, 0, 1));
        var desiredX = Lerp(approachX, landingX, horizontalProgress);
        var desiredY = Lerp(_climbStartFootY, target.Top, progress);
        DisplayGeometry.OffsetWindowPhysical(
            _windowHandle,
            (int)Math.Round(desiredX - foot.X),
            (int)Math.Round(desiredY - foot.Y));
        SquashTransform.ScaleX = 1;
        SquashTransform.ScaleY = 1;
        LeanTransform.Angle = 0;
        GroundShadow.Width = 0;

        if (progress < 1)
        {
            return;
        }

        var finalFoot = CanvasPointToScreen(_character.Geometry.FootCanvasX, _character.Geometry.FootCanvasY);
        DisplayGeometry.OffsetWindowPhysical(
            _windowHandle,
            (int)Math.Round(landingX - finalFoot.X),
            (int)Math.Round(target.Top - finalFoot.Y));
        _supportHandle = target.SourceHandle;
        _supportIsDesktopFloor = false;
        ResetSupportMotionTracking(target);
        _lifeState.RegisterExploration(12);
        _nextClimbAttemptAt = _clock.Elapsed.TotalSeconds + 20;
        DiagnosticsLog.WriteEvent(
            "ClimbSucceeded",
            ("Target", FormatHandle(target.SourceHandle)),
            ("Side", plan.Side),
            ("Landing", $"{landingX},{target.Top}"),
            ("Height", plan.Height));
        _voice.Play(VoiceCue.Affirmative, VoicePriority.Important, TimeSpan.FromSeconds(8));
        _activeClimb = null;
        if (_settings.QuietMode || _manualLickRequested || IsBehaviorControlMode && !_behaviorAutonomyEnabled)
        {
            ReturnToIdle();
        }
        else
        {
            StartContinuousPatrol(preferClimb: false);
        }
    }

    private bool BeginRoll()
    {
        if (!TryResolveGroundSupport(allowSnap: true))
        {
            BeginFall();
            return false;
        }

        _state = PetState.Rolling;
        _movementPurpose = MovementPurpose.None;
        _stateElapsed = 0;
        _velocityX = 0;
        _velocityY = 0;
        _pendingClimb = null;
        FacingTransform.ScaleX = Random.Shared.Next(2) == 0 ? -1 : 1;
        LeanTransform.Angle = 0;
        SquashTransform.ScaleX = 1;
        SquashTransform.ScaleY = 1;
        GroundShadow.Width = 204;
        _animator.Play("roll");
        _voice.Play(VoiceCue.Aggrieved, VoicePriority.Normal, TimeSpan.FromSeconds(10));
        DiagnosticsLog.WriteEvent(
            "HungryRollStarted",
            ("Hunger", _lifeState.Hunger),
            ("Support", FormatHandle(_supportHandle)));
        return true;
    }

    private void UpdateRolling()
    {
        if (!MaintainGroundSupport())
        {
            return;
        }

        SquashTransform.ScaleX = 1;
        SquashTransform.ScaleY = 1;
        LeanTransform.Angle = 0;
        GroundShadow.Width = 204;
    }

    private void BeginChomp()
    {
        if (!_character.SupportsFood) return;
        _state = PetState.Chomping;
        _movementPurpose = MovementPurpose.None;
        _stateElapsed = 0;
        _velocityX = 0;
        _velocityY = 0;
        FacingTransform.ScaleX = 1;
        LeanTransform.Angle = 0;
        _animator.Play("chomp");
        FoodScale.ScaleX = 1;
        FoodScale.ScaleY = 1;
        FoodRotate.Angle = 0;
        FoodVisual.Opacity = 1;

        if (_currentFood is not null && !_usingGhostIcon)
        {
            var icon = _currentFood.CachedIcon ?? ShellIconProvider.GetIcon(_currentFood.OriginalPath);
            FoodIcon.Source = icon;
            Canvas.SetLeft(FoodVisual, MouthCanvasX - 27);
            Canvas.SetTop(FoodVisual, MouthCanvasY - 27);
            FoodVisual.Visibility = Visibility.Visible;
        }

        DiagnosticsLog.Write($"Suction animation started for visual ghost. Path={_currentFood?.OriginalPath}");

        _voice.Play(VoiceCue.Positive, VoicePriority.Important, TimeSpan.FromSeconds(6));
    }

    private void UpdateChomping()
    {
        SquashTransform.ScaleX = 1;
        SquashTransform.ScaleY = 1;

        var anchors = GetFoodTrajectoryAnchors(_character.Geometry);
        var left = anchors.StartX;
        var top = anchors.StartY;
        var angle = 0.0;
        FoodVisual.Opacity = 1;
        if (_stateElapsed <= 0.55)
        {
            var progress = _stateElapsed / 0.55;
            left += Math.Sin(progress * Math.PI * 5) * 3;
            top += Math.Sin(progress * Math.PI * 4) * 2;
            var pulse = 1 + Math.Sin(progress * Math.PI * 4) * 0.035;
            FoodScale.ScaleX = pulse;
            FoodScale.ScaleY = pulse;
            angle = Math.Sin(progress * Math.PI * 5) * 4;
        }
        else if (_stateElapsed <= 1.22)
        {
            var progress = SmoothStep((_stateElapsed - 0.55) / 0.67);
            left = Lerp(anchors.StartX, anchors.MiddleX, progress);
            top = Lerp(anchors.StartY, anchors.EndY, progress) + Math.Sin(progress * Math.PI * 3) * 5;
            angle = Lerp(0, -12, progress) + Math.Sin(progress * Math.PI * 3) * 4;
            var shrinkProgress = SmoothStep(Math.Clamp((progress - 0.20) / 0.80, 0, 1));
            var suctionScale = Lerp(1, 0.48, shrinkProgress);
            FoodScale.ScaleX = suctionScale * Lerp(1, 1.12, progress);
            FoodScale.ScaleY = suctionScale * Lerp(1, 0.88, progress);
        }
        else
        {
            var progress = SmoothStep(Math.Clamp((_stateElapsed - 1.22) / 0.12, 0, 1));
            // Each character holds its calibrated open-mouth target during the final swallow.
            left = Lerp(anchors.MiddleX, anchors.EndX, progress);
            top = anchors.EndY + Math.Sin(progress * Math.PI * 3) * (1 - progress) * 4;
            angle = Lerp(-12, 0, progress);
            var vanishScale = Lerp(0.48, 0.04, progress);
            FoodScale.ScaleX = vanishScale * Lerp(1.18, 0.72, progress);
            FoodScale.ScaleY = vanishScale * Lerp(0.82, 1.08, progress);
            FoodVisual.Opacity = 1 - Math.Pow(progress, 4);
        }

        if (_usingGhostIcon && _currentGhostIconWindow is not null)
        {
            var screenCenter = CanvasPointToScreen(left + 27, top + 27);
            _currentGhostIconWindow.SetVisual(
                screenCenter.X,
                screenCenter.Y,
                FoodScale.ScaleX,
                FoodScale.ScaleY,
                angle,
                FoodVisual.Opacity);
        }
        else
        {
            Canvas.SetLeft(FoodVisual, left);
            Canvas.SetTop(FoodVisual, top);
            FoodRotate.Angle = angle;
        }

        if (_stateElapsed >= 1.35
            && (_currentGhostIconWindow?.IsGhostVisible == true || FoodVisual.Visibility == Visibility.Visible))
        {
            CompleteMealConsumption();
        }
    }

    private static (double StartX, double StartY, double MiddleX, double EndX, double EndY)
        GetFoodTrajectoryAnchors(PetCharacterGeometry geometry)
    {
        var startX = geometry.MouthCanvasX - 27;
        var endX = geometry.SuctionMouthCanvasX - 27;
        return (
            startX,
            geometry.MouthCanvasY - 27,
            Lerp(startX, endX, 105.0 / 146),
            endX,
            geometry.SuctionMouthCanvasY - 27);
    }

    private void CompleteMealConsumption()
    {
        if (!_character.SupportsFood) return;
        FoodVisual.Visibility = Visibility.Collapsed;
        CloseCurrentGhost();
        if (_currentMealNutritionApplied || _currentFood is null)
        {
            return;
        }

        _currentMealNutritionApplied = true;
        _lifeState.RegisterMeal(_currentFood.OriginalPath, GetMealNourishment(_currentFood.OriginalPath));
        DiagnosticsLog.WriteEvent(
            "GhostConsumed",
            ("QueueId", _currentFood.QueueId),
            ("Path", _currentFood.OriginalPath),
            ("Anchor", $"{_currentFood.ScreenX},{_currentFood.ScreenY}"),
            ("Hunger", _lifeState.Hunger),
            ("Fullness", _lifeState.Fullness),
            ("Remaining", _foodQueue.Count));
    }

    private static double GetMealNourishment(string path)
    {
        if (path.EndsWith(".snack", StringComparison.OrdinalIgnoreCase))
        {
            return 9;
        }

        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".zip" or ".7z" or ".rar" or ".iso" => 15,
            ".mp4" or ".mkv" or ".mov" => 14,
            ".exe" or ".msi" => 13,
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" => 10,
            ".lnk" or ".url" => 8,
            _ => 11,
        };
    }

    private System.Windows.Point CanvasPointToScreen(double canvasX, double canvasY)
    {
        var scale = Math.Max(1, ActualWidth - 12) / 512;
        return PointToScreen(new System.Windows.Point(
            6 + canvasX * scale,
            6 + canvasY * scale));
    }

    private void ResetBodyDeformation()
    {
        SquashTransform.ScaleX = 1;
        SquashTransform.ScaleY = 1;
        LeanTransform.Angle = 0;
    }

    private void UpdateSatisfied()
    {
        SquashTransform.ScaleX = 1;
        SquashTransform.ScaleY = 1;
        GroundShadow.Width = 202;
        if (_animator.CurrentFrameIndex >= 14)
        {
            var puffProgress = _animator.CurrentFrameIndex - 13;
            BurpPuff.Opacity = Math.Clamp(puffProgress * 0.72, 0, 1);
            BurpTranslate.X = _character.Geometry.BurpOffsetCanvasX - puffProgress * 8;
            BurpTranslate.Y = _character.Geometry.BurpOffsetCanvasY;
        }
        else
        {
            BurpPuff.Opacity = 0;
        }
    }

    private void UpdateIdleMotion()
    {
        if (!MaintainGroundSupport())
        {
            return;
        }

        SquashTransform.ScaleX = 1;
        SquashTransform.ScaleY = 1;
        GroundShadow.Width = 202;
        MaybePlayIdleVoice(_clock.Elapsed.TotalSeconds);
        if (_clock.Elapsed.TotalSeconds >= _nextAutonomousDecision)
        {
            DecideAutonomousBehavior();
        }
    }

    private void BeginCurious(bool completedExploration, bool hungryAppeal = false)
    {
        hungryAppeal &= _character.SupportsFood;
        _state = PetState.Curious;
        _movementPurpose = MovementPurpose.None;
        _stateElapsed = 0;
        _curiousDuration = Random.Shared.NextDouble() * 2.2 + 2.8;
        _animator.Play(hungryAppeal ? "hungry" : "curious");
        if (hungryAppeal)
        {
            _voice.Play(VoiceCue.Aggrieved, VoicePriority.Normal, TimeSpan.FromSeconds(20));
        }
        else if (completedExploration)
        {
            _voice.Play(VoiceCue.Question, VoicePriority.Ambient, TimeSpan.FromSeconds(18));
        }
        if (completedExploration)
        {
            _lifeState.RegisterExploration();
        }
    }

    private void UpdateCurious()
    {
        if (!MaintainGroundSupport())
        {
            return;
        }

        SquashTransform.ScaleX = 1;
        SquashTransform.ScaleY = 1;
        LeanTransform.Angle = 0;
        GroundShadow.Width = 198;
        if (_stateElapsed >= _curiousDuration)
        {
            ReturnToIdle();
        }
    }

    private void BeginSleep()
    {
        if (!TryResolveGroundSupport(allowSnap: true))
        {
            BeginFall();
            return;
        }

        _state = PetState.Sleeping;
        _movementPurpose = MovementPurpose.None;
        _stateElapsed = 0;
        _sleepDuration = Random.Shared.NextDouble() * 10 + 14;
        _animator.Play("sleep_enter");
        _voice.Play(VoiceCue.Calm, VoicePriority.Ambient, TimeSpan.FromSeconds(45));
    }

    private void UpdateSleeping()
    {
        if (!MaintainGroundSupport())
        {
            return;
        }

        var breath = (Math.Sin(_stateElapsed * 2.1) + 1) * 0.5;
        SquashTransform.ScaleX = 1 + breath * 0.025;
        SquashTransform.ScaleY = 1 - breath * 0.018;
        LeanTransform.Angle = -1.5;
        GroundShadow.Width = 206;
        if (IsBehaviorControlMode && _activeBehavior == BehaviorControlAction.Sleep && !_behaviorAutonomyEnabled) return;
        if (_stateElapsed >= _sleepDuration ||
            _stateElapsed >= LifeBehaviorPolicy.MinimumNapSeconds && _lifeState.Sleepiness <= 24)
        {
            BeginWake();
        }
    }

    private void BeginWake(bool forMeal = false, bool forManualLick = false)
    {
        forMeal &= _character.SupportsFood;
        forManualLick &= _character.SupportsFood;
        _wakeForMeal |= forMeal;
        _wakeForManualLick |= forManualLick;
        if (_state == PetState.Waking)
        {
            return;
        }

        _state = PetState.Waking;
        _movementPurpose = MovementPurpose.None;
        _stateElapsed = 0;
        _animator.Play("sleep_exit");
        DiagnosticsLog.WriteEvent(
            "WakeStarted",
            ("ForMeal", _wakeForMeal),
            ("ForManualLick", _wakeForManualLick));
    }

    private void UpdateWaking()
    {
        if (!MaintainGroundSupport())
        {
            return;
        }

        SquashTransform.ScaleX = 1;
        SquashTransform.ScaleY = 1;
        LeanTransform.Angle = 0;
        GroundShadow.Width = 202;
    }

    private void DecideAutonomousBehavior()
    {
        if (IsFeatureTestMode || IsBehaviorControlMode && !_behaviorAutonomyEnabled) return;
        if (_character.SupportsFood && _foodQueue.Count > 0 && _settings.ReactToDeletes && !_settings.QuietMode)
        {
            StartNextMeal();
            return;
        }

        if (_enclosureHandle != IntPtr.Zero)
        {
            return;
        }

        var now = _clock.Elapsed.TotalSeconds;
        var available = Enum.GetValues<AutonomousAction>()
            .Where(action =>
                PetModePolicy.AllowsAutonomousAction(action, EffectiveFastingMode, _settings.QuietMode) &&
                IsAutonomousActionReady(action, now))
            .ToHashSet();
        available.Add(AutonomousAction.Rest);
        var hasSupport = TryResolveGroundSupport(allowSnap: true);
        var action = AutonomousBehaviorPlanner.Choose(
            _lifeState,
            hasSupport,
            available,
            Random.Shared.NextDouble(),
            _character);

        switch (action)
        {
            case AutonomousAction.Wander:
                MarkActionCooldown(action, 9);
                StartContinuousPatrol(preferClimb: false);
                break;
            case AutonomousAction.Explore:
                MarkActionCooldown(action, 18);
                StartContinuousPatrol(preferClimb: true);
                break;
            case AutonomousAction.Sleep:
                MarkActionCooldown(action, 90);
                BeginSleep();
                break;
            case AutonomousAction.AskForFood:
                MarkActionCooldown(action, 65);
                MarkHungerReactionCooldown(action);
                BeginCurious(completedExploration: false, hungryAppeal: true);
                break;
            case AutonomousAction.LickIcon:
                MarkActionCooldown(action, 70);
                MarkHungerReactionCooldown(action);
                if (!TryBeginIconLick(manualRequest: false))
                {
                    StartContinuousPatrol(preferClimb: false);
                }
                break;
            case AutonomousAction.Roll:
                MarkActionCooldown(action, 42);
                MarkHungerReactionCooldown(action);
                BeginRoll();
                break;
            case AutonomousAction.Hop:
                MarkActionCooldown(action, 24);
                BeginTerrainHop();
                break;
            default:
                MarkActionCooldown(AutonomousAction.Rest, 5);
                if (_settings.QuietMode)
                {
                    ScheduleNextAutonomousDecision(
                        _character.MaximumIdleSeconds,
                        _character.MaximumIdleSeconds);
                }
                else
                {
                    StartContinuousPatrol(preferClimb: false);
                }
                break;
        }
    }

    private void MarkActionCooldown(AutonomousAction action, double seconds)
    {
        _actionCooldownUntil[action] = _clock.Elapsed.TotalSeconds + seconds;
    }

    private bool IsAutonomousActionReady(AutonomousAction action, double now) =>
        (!_actionCooldownUntil.TryGetValue(action, out var until) || now >= until) &&
        LifeBehaviorPolicy.IsHungerReactionReady(action, now, _nextHungerReactionAt);

    private void MarkHungerReactionCooldown(AutonomousAction action)
    {
        if (!LifeBehaviorPolicy.IsHungerReaction(action))
        {
            return;
        }

        _nextHungerReactionAt = _clock.Elapsed.TotalSeconds +
            LifeBehaviorPolicy.HungerReactionCooldownSeconds;
        DiagnosticsLog.WriteEvent(
            "HungerReactionCooldownStarted",
            ("Action", action),
            ("Seconds", LifeBehaviorPolicy.HungerReactionCooldownSeconds));
    }

    private void ScheduleNextAutonomousDecision(double minimumSeconds, double maximumSeconds)
    {
        _nextAutonomousDecision = _clock.Elapsed.TotalSeconds +
            minimumSeconds + Random.Shared.NextDouble() * Math.Max(0, maximumSeconds - minimumSeconds);
    }

    private void ScheduleNextIdleVoice()
    {
        _nextIdleVoiceAt = _character.UsesMischief
            ? _clock.Elapsed.TotalSeconds + IdleNameCallPolicy.NextDelay(Random.Shared.NextDouble())
            : double.PositiveInfinity;
    }

    private void MaybePlayIdleVoice(double now)
    {
        if (IsBehaviorControlMode && !_behaviorAutonomyEnabled) return;
        if (now < _nextIdleVoiceAt)
        {
            return;
        }

        if (_primaryWindow is null && _character.UsesMischief && !_settings.QuietMode && !_sessionLocked)
        {
            _voice.Play(VoiceCue.Neutral, VoicePriority.Ambient,
                TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(12));
        }
        ScheduleNextIdleVoice();
        DiagnosticsLog.WriteEvent("IdleVoiceAttempt", ("Character", _character.Id), ("NextAt", _nextIdleVoiceAt));
    }

    private void AnimationFinished(string clip)
    {
        if (_state == PetState.Pranking) return;
        if (clip.Equals("jump", StringComparison.OrdinalIgnoreCase) && _state == PetState.Falling)
        {
            _animator.Play("fall");
            DiagnosticsLog.WriteEvent(
                "TerrainJumpAnimationFinished",
                ("Velocity", $"{_velocityX:0.0},{_velocityY:0.0}"));
            return;
        }

        if (clip.Equals("sleep_enter", StringComparison.OrdinalIgnoreCase) && _state == PetState.Sleeping)
        {
            _animator.Play("sleep");
            return;
        }

        if (clip.Equals("sleep_exit", StringComparison.OrdinalIgnoreCase) && _state == PetState.Waking)
        {
            var startMeal = _wakeForMeal;
            var startManualLick = _wakeForManualLick;
            _wakeForMeal = false;
            _wakeForManualLick = false;
            if (_character.SupportsFood && !_settings.QuietMode && startMeal && _foodQueue.Count > 0 && _settings.ReactToDeletes)
            {
                StartNextMeal();
            }
            else if (_character.SupportsFood && !_settings.QuietMode && startManualLick)
            {
                _manualLickRequested = false;
                if (!TryBeginIconLick(manualRequest: true))
                {
                    BeginRoll();
                }
            }
            else
            {
                ReturnToIdle();
            }
            return;
        }

        if (clip.Equals("roll", StringComparison.OrdinalIgnoreCase) && _state == PetState.Rolling)
        {
            _lifeState.Hunger = Math.Min(100, _lifeState.Hunger + 0.25);
            _lifeState.Hunger = PetModePolicy.ResolveHunger(_lifeState.Hunger, EffectiveFastingMode);
            DiagnosticsLog.WriteEvent(
                "HungryRollFinished",
                ("Hunger", _lifeState.Hunger));
            ReturnToIdle();
            return;
        }

        if (clip.Equals("lick", StringComparison.OrdinalIgnoreCase) && _state == PetState.Licking)
        {
            _lifeState.Hunger = Math.Min(100, _lifeState.Hunger + 0.4);
            _lifeState.Hunger = PetModePolicy.ResolveHunger(_lifeState.Hunger, EffectiveFastingMode);
            _lifeState.Curiosity = Math.Max(0, _lifeState.Curiosity - 1.5);
            _voice.Play(VoiceCue.Neutral, VoicePriority.Ambient, TimeSpan.FromSeconds(15));
            DiagnosticsLog.Write(
                $"Desktop icon lick finished; Hunger={_lifeState.Hunger:0.0}; " +
                $"Curiosity={_lifeState.Curiosity:0.0}.");
            BeginFall();
            return;
        }

        if (clip.Equals("chomp", StringComparison.OrdinalIgnoreCase) && _state == PetState.Chomping)
        {
            CompleteMealConsumption();
            FoodVisual.Visibility = Visibility.Collapsed;
            _state = PetState.Satisfied;
            _stateElapsed = 0;
            _animator.Play(_foodQueue.Count > 0 ? "satisfied_quick" : "satisfied");
            _voice.Play(VoiceCue.Affirmative, VoicePriority.Important, TimeSpan.FromSeconds(5));
            return;
        }

        if (clip.StartsWith("satisfied", StringComparison.OrdinalIgnoreCase) && _state == PetState.Satisfied)
        {
            if (_foodQueue.Count > 0)
            {
                StartNextMeal();
            }
            else
            {
                ReturnToIdle();
            }
        }
    }

    private void ReturnToIdle()
    {
        ClearCheekPinch();
        _state = PetState.Idle;
        _movementPurpose = MovementPurpose.None;
        _stateElapsed = 0;
        _currentFood = null;
        _animator.Play("idle");
        FacingTransform.ScaleX = 1;
        SquashTransform.ScaleX = 1;
        SquashTransform.ScaleY = 1;
        LeanTransform.Angle = 0;
        FoodVisual.Visibility = Visibility.Collapsed;
        FoodVisual.Opacity = 1;
        FoodScale.ScaleX = 1;
        FoodScale.ScaleY = 1;
        CloseCurrentGhost();
        _usingGhostIcon = false;
        _currentMealNutritionApplied = false;
        BurpPuff.Opacity = 0;
        if (TryResumeHatRecovery()) return;
        _pendingClimb = null;
        _activeClimb = null;
        _slideSurface = null;
        ScheduleNextAutonomousDecision(
            _character.MaximumIdleSeconds,
            _character.MaximumIdleSeconds);

        if (_settings.QuietMode)
        {
            if (_manualMischiefPending)
            {
                _manualMischiefPending = false;
                DiagnosticsLog.WriteEvent("MischiefManualDeferredCancelled", ("Reason", "QuietMode"));
            }
            _manualLickRequested = false;
            return;
        }

        if (TryRunQueuedManualMischief()) return;

        if (_character.SupportsFood && _foodQueue.Count > 0 && _settings.ReactToDeletes)
        {
            StartNextMeal();
            return;
        }

        if (_character.SupportsFood && _manualLickRequested)
        {
            _manualLickRequested = false;
            if (!TryBeginIconLick(manualRequest: true))
            {
                BeginRoll();
            }
        }
    }

    private static double Lerp(double start, double end, double progress) => start + (end - start) * progress;

    private static string FormatHandle(IntPtr handle) => $"0x{handle.ToInt64():X}";

    private static double SmoothStep(double progress)
    {
        progress = Math.Clamp(progress, 0, 1);
        return progress * progress * (3 - 2 * progress);
    }

    private void PlayStartupVoice()
    {
        if (_character.UsesMischief && !_settings.QuietMode && !_sessionLocked)
        {
            _voice.Play(VoiceCue.Neutral, VoicePriority.Ambient,
                TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(12));
        }
    }

    private void UpdateProgressDisplayVisibility()
    {
        var visible = PetModePolicy.ShouldShowProgressDisplay(_character,
            _settings.FastingMode, _settings.InfiniteMode, _settings.CoexistenceMode);
        var hungerVisibility = visible && _character.SupportsFood ? Visibility.Visible : Visibility.Collapsed;
        var mischiefVisibility = visible && _character.UsesMischief ? Visibility.Visible : Visibility.Collapsed;
        if (HungerBadge.Visibility == hungerVisibility && MischiefBadge.Visibility == mischiefVisibility) return;

        HungerBadge.Visibility = hungerVisibility;
        MischiefBadge.Visibility = mischiefVisibility;
        DiagnosticsLog.WriteEvent("ProgressDisplayVisibilityChanged",
            ("Character", _character.Id), ("Visible", visible),
            ("FastingMode", _settings.FastingMode), ("InfiniteMode", _settings.InfiniteMode),
            ("CoexistenceMode", _settings.CoexistenceMode));
    }

    private void UpdateHungerDisplay(bool force = false)
    {
        UpdateProgressDisplayVisibility();
        if (HungerBadge.Visibility != Visibility.Visible)
        {
            _lastHungerBand = -1;
            return;
        }

        var now = _clock.Elapsed.TotalSeconds;
        if (!force && now < _nextHungerUiUpdate)
        {
            return;
        }

        _nextHungerUiUpdate = now + 0.25;
        var hunger = Math.Clamp(_lifeState.Hunger, 0, 100);
        var rounded = (int)Math.Round(hunger);
        HungerText.Text = $"饥饿 {rounded}";
        HungerBar.Value = hunger;
        var band = hunger >= 80 ? 2 : hunger >= 55 ? 1 : 0;
        if (band != _lastHungerBand)
        {
            var color = band switch
            {
                2 => System.Windows.Media.Color.FromRgb(235, 104, 126),
                1 => System.Windows.Media.Color.FromRgb(238, 167, 84),
                _ => System.Windows.Media.Color.FromRgb(118, 209, 151),
            };
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            var textBrush = new SolidColorBrush(band == 2
                ? System.Windows.Media.Color.FromRgb(133, 52, 69)
                : System.Windows.Media.Color.FromRgb(60, 81, 80));
            textBrush.Freeze();
            HungerBadge.BorderBrush = brush;
            HungerBar.Foreground = brush;
            HungerText.Foreground = textBrush;
            _lastHungerBand = band;
            DiagnosticsLog.WriteEvent(
                "HungerBandChanged",
                ("Hunger", hunger),
                ("Band", band switch { 2 => "VeryHungry", 1 => "Hungry", _ => "Comfortable" }));
        }
    }

    private void Character_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && _state == PetState.Interacting)
            CancelPairScene("PickedUp");
        if (_featureDemoRunning && e.ChangedButton == MouseButton.Left)
        {
            StopFeatureDemo("UserClickedPet");
            e.Handled = true;
            return;
        }
        if (e.ChangedButton != MouseButton.Left || _isDragging || !DragInteractionPolicy.CanStart(_state))
        {
            return;
        }

        var now = _clock.Elapsed.TotalSeconds;
        if (_trayIcon.IsMenuOpen || now < _suppressDragUntil)
        {
            DiagnosticsLog.WriteEvent("DragSuppressedAfterMenu",
                ("MenuOpen", _trayIcon.IsMenuOpen),
                ("RemainingMs", Math.Max(0, (_suppressDragUntil - now) * 1000)),
                ("State", _state));
            e.Handled = true;
            return;
        }

        if (TryBeginCheekPinch(e)) return;

        var interruptedState = _state;
        var interruptedMovement = _movementPurpose;
        CancelWindowPrank("PickedUp", returnToIdle: false, keepHatless: true);
        if (_enclosureHandle != IntPtr.Zero)
        {
            DiagnosticsLog.WriteEvent(
                "WindowEnclosureReleased",
                ("Window", FormatHandle(_enclosureHandle)),
                ("Reason", "PickedUp"));
            ClearWindowEnclosure();
        }
        RequeueActiveMealForDrag();
        _pendingClimb = null;
        _activeClimb = null;
        _movementPurpose = MovementPurpose.None;
        _velocityX = 0;
        _velocityY = 0;
        _movementPixelRemainderX = 0;
        _movementPixelRemainderY = 0;
        _isDragging = true;
        _state = PetState.Dragging;
        _stateElapsed = 0;
        _dragCursorStart = MouseMonitor.GetCursorPosition();
        _previousDragCursor = _dragCursorStart;
        _dragMotion.Reset(_dragCursorStart, _clock.Elapsed.TotalSeconds);
        if (NativeMethods.GetWindowRect(_windowHandle, out var windowRectangle))
        {
            _dragWindowLeft = windowRectangle.Left;
            _dragWindowTop = windowRectangle.Top;
        }
        else
        {
            var windowTopLeft = PointToScreen(new System.Windows.Point(0, 0));
            _dragWindowLeft = (int)Math.Round(windowTopLeft.X);
            _dragWindowTop = (int)Math.Round(windowTopLeft.Y);
        }
        var mouseCaptured = CharacterRoot.CaptureMouse();
        _animator.Play("drag");
        _voice.Play(VoiceCue.Aggrieved, VoicePriority.Normal, TimeSpan.FromSeconds(8));
        DiagnosticsLog.WriteEvent(
            "DragStarted",
            ("InterruptedState", interruptedState),
            ("InterruptedMovement", interruptedMovement),
            ("MouseCaptured", mouseCaptured),
            ("Cursor", $"{_dragCursorStart.X},{_dragCursorStart.Y}"));
        e.Handled = true;
    }

    private void RequeueActiveMealForDrag(bool characterChange = false)
    {
        if (_movementPurpose != MovementPurpose.Meal || _currentFood is null)
        {
            return;
        }

        _foodQueue.EnqueueFirst(_currentFood, _currentGhostIconWindow);
        DiagnosticsLog.WriteEvent(
            characterChange ? "MealRequeuedForCharacterChange" : "MealRequeuedForDrag",
            ("QueueId", _currentFood.QueueId),
            ("PendingCount", _foodQueue.Count));
        _currentFood = null;
        _currentGhostIconWindow = null;
        _usingGhostIcon = false;
        _currentMealNutritionApplied = false;
        FoodVisual.Visibility = Visibility.Collapsed;
    }

    private void Character_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (MoveCheekPinch(e)) return;
        if (!_isDragging || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var cursor = MouseMonitor.GetCursorPosition();
        _dragMotion.Add(cursor, _clock.Elapsed.TotalSeconds);
        DisplayGeometry.MoveWindowPhysical(
            _windowHandle,
            _dragWindowLeft + cursor.X - _dragCursorStart.X,
            _dragWindowTop + cursor.Y - _dragCursorStart.Y);

        var speedDeviceX = cursor.X - _previousDragCursor.X;
        var speedDeviceY = cursor.Y - _previousDragCursor.Y;
        _previousDragCursor = cursor;
        var transformFromDevice = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice
            ?? Matrix.Identity;
        var speedDip = transformFromDevice.Transform(new System.Windows.Point(speedDeviceX, speedDeviceY));
        FacingTransform.ScaleX = speedDeviceX < -1 ? -1 : speedDeviceX > 1 ? 1 : FacingTransform.ScaleX;
        var stretch = Math.Clamp(Math.Sqrt(speedDip.X * speedDip.X + speedDip.Y * speedDip.Y) / 120, 0, 0.18);
        SquashTransform.ScaleX = 1 - stretch * 0.55;
        SquashTransform.ScaleY = 1 + stretch;
        LeanTransform.Angle = Math.Clamp(speedDip.X * 0.55, -12, 12);
    }

    private void Character_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (ReleaseCheekPinch())
        {
            e.Handled = true;
            return;
        }
        if (!_isDragging)
        {
            return;
        }

        _isDragging = false;
        CharacterRoot.ReleaseMouseCapture();
        var cursor = MouseMonitor.GetCursorPosition();
        var monitor = DisplayGeometry.FromPoint(cursor);
        var release = _dragMotion.Release(cursor, _clock.Elapsed.TotalSeconds, monitor?.Scale ?? 1);
        RefreshSurfaceMap(force: true);
        var enteredEnclosure = TryBeginWindowEnclosure(
            release.HasInertia ? release.VelocityX : 0,
            release.HasInertia ? release.VelocityY : 0);
        if (release.HasInertia)
        {
            _voice.Play(VoiceCue.Scream, VoicePriority.Important, TimeSpan.FromSeconds(8));
            DiagnosticsLog.WriteEvent(
                "DragReleasedWithInertia",
                ("Cursor", $"{cursor.X},{cursor.Y}"),
                ("Velocity", $"{release.VelocityX:0.0},{release.VelocityY:0.0}"),
                ("Speed", release.Speed),
                ("SampleSeconds", release.SampleSeconds));
            if (!enteredEnclosure)
            {
                TryResolveGroundSupport(allowSnap: false);
                BeginFall(release.VelocityY, "DragThrow", release.VelocityX);
            }
        }
        else
        {
            if (!enteredEnclosure)
            {
                TryResolveGroundSupport(allowSnap: false);
                BeginFall();
            }
            DiagnosticsLog.WriteEvent(
                "DragReleasedGently",
                ("Cursor", $"{cursor.X},{cursor.Y}"),
                ("MeasuredSpeed", release.Speed),
                ("SampleSeconds", release.SampleSeconds));
        }
        if (_character.SupportsFood && enteredEnclosure && _foodQueue.Count > 0 && _settings.ReactToDeletes)
        {
            StartNextMeal();
        }
        e.Handled = true;
    }

    private void Character_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        ReleaseCheekPinch();
        _trayIcon.ShowMenu();
        e.Handled = true;
    }

    private void GiveTreat()
    {
        if (!_character.SupportsFood) return;
        if (_settings.QuietMode || _state is PetState.Running or PetState.Chomping)
        {
            return;
        }

        _usingGhostIcon = false;
        _currentFood = new DeletedItemInfo("treat.snack", "小点心", DateTimeOffset.UtcNow, false);
        _currentMealNutritionApplied = false;
        BeginChomp();
    }

    private void MakeHungryAndLick()
    {
        if (!_character.SupportsFood) return;
        if (EffectiveFastingMode)
        {
            _lifeState.Hunger = 0;
            UpdateHungerDisplay(force: true);
            return;
        }

        if (_settings.QuietMode)
        {
            return;
        }

        _lifeState.Hunger = 100;
        _lifeState.Fullness = Math.Min(_lifeState.Fullness, 8);
        _lifeState.ClampNeeds();
        SaveLifeState();

        if (!WindowEnclosureBehaviorPolicy.AllowsIconLick(_enclosureHandle != IntPtr.Zero))
        {
            _manualLickRequested = false;
            DiagnosticsLog.WriteEvent(
                "IconLickSuppressedInEnclosure",
                ("Window", FormatHandle(_enclosureHandle)),
                ("Manual", true));
            return;
        }

        if (_state is PetState.Sleeping or PetState.Waking)
        {
            _manualLickRequested = true;
            BeginWake(forManualLick: true);
            return;
        }

        var mustWait = _foodQueue.Count > 0 ||
            _state is PetState.Falling or PetState.Landing or PetState.Dragging or PetState.Pinching or PetState.Climbing or PetState.Rolling or
                PetState.Chomping or PetState.Satisfied ||
            _state == PetState.Running && _movementPurpose == MovementPurpose.Meal;
        if (mustWait)
        {
            _manualLickRequested = true;
            _voice.Play(VoiceCue.Aggrieved, VoicePriority.Important, TimeSpan.FromSeconds(8));
            return;
        }

        _manualLickRequested = false;
        if (!TryBeginIconLick(manualRequest: true))
        {
            BeginRoll();
        }
    }

    private void BringHome()
    {
        CancelPairScene("BringHome");
        CancelWindowPrank("BringHome", returnToIdle: false, keepHatless: false);
        if (_enclosureHandle != IntPtr.Zero)
        {
            DiagnosticsLog.WriteEvent(
                "WindowEnclosureReleased",
                ("Window", FormatHandle(_enclosureHandle)),
                ("Reason", "BringHome"));
            ClearWindowEnclosure();
        }

        var restoredFromTray = !IsVisible;
        if (restoredFromTray)
        {
            Show();
            StartRendering();
            DiagnosticsLog.WriteEvent("PetRestoredFromTray");
        }

        var cursor = MouseMonitor.GetCursorPosition();
        var targetPoint = cursor == DrawingPoint.Empty ? new DrawingPoint(0, 0) : cursor;
        var monitor = DisplayGeometry.FromPoint(targetPoint);
        if (monitor is not null)
        {
            var currentMonitor = DisplayGeometry.MonitorHandleFromWindow(_windowHandle);
            if (currentMonitor != monitor.Handle &&
                NativeMethods.GetWindowRect(_windowHandle, out var currentRectangle))
            {
                var currentWidth = Math.Max(1, currentRectangle.Right - currentRectangle.Left);
                var currentHeight = Math.Max(1, currentRectangle.Bottom - currentRectangle.Top);
                DisplayGeometry.MoveWindowPhysical(
                    _windowHandle,
                    monitor.WorkArea.Left + Math.Max(0, (monitor.WorkArea.Width - currentWidth) / 2),
                    monitor.WorkArea.Top + Math.Max(0, (monitor.WorkArea.Height - currentHeight) / 2));
                monitor = DisplayGeometry.FromWindow(_windowHandle) ?? monitor;
            }

            ApplyResponsivePetSize(monitor);
            var physicalSize = DisplayGeometry.ProjectDipSize(Width, Height, monitor);
            var footOffset = ProjectCanvasAnchorOffset(
                _character.Geometry.FootCanvasX,
                _character.Geometry.FootCanvasY,
                monitor);
            var horizontalMargin = (int)Math.Round(24 * monitor.Scale);
            var companionOffset = _primaryWindow is null ? 0 : physicalSize.Width + horizontalMargin;
            DisplayGeometry.MoveWindowPhysical(
                _windowHandle,
                monitor.WorkArea.Right - physicalSize.Width - horizontalMargin - companionOffset,
                monitor.WorkArea.Bottom - 1 - footOffset.Y);
            _currentMonitor = monitor.Handle;
            _supportHandle = monitor.Handle;
            _supportIsDesktopFloor = true;
            RefreshSurfaceMap(force: true);
            QueueDisplayAdjustment(clampToWorkArea: false);
        }
        ReturnToIdle();
        if (_primaryWindow is null)
            _voice.Play(VoiceCue.Neutral, VoicePriority.Normal, TimeSpan.FromSeconds(5));
    }

    internal void BringHomeFromExternalRequest()
    {
        if (!_isClosing)
        {
            BringBothHome();
        }
    }

    private DrawingPoint ProjectCanvasAnchorOffset(double canvasX, double canvasY, MonitorGeometry monitor)
    {
        var contentSize = Math.Max(1, Width - 12);
        return new DrawingPoint(
            (int)Math.Round((6 + canvasX * contentSize / 512) * monitor.Scale),
            (int)Math.Round((6 + canvasY * contentSize / 512) * monitor.Scale));
    }

    private bool TryReadStartupRegistration()
    {
        try
        {
            return _startupRegistration.IsEnabled();
        }
        catch (Exception exception)
        {
            DiagnosticsLog.Write("Startup registration could not be read.", exception);
            return false;
        }
    }

    private void ToggleStartWithWindows(bool enabled)
    {
        if (IsFeatureTestMode || IsBehaviorControlMode) return;
        try
        {
            _startupRegistration.SetEnabled(enabled);
            var actual = _startupRegistration.IsEnabled();
            _trayIcon.SetStartWithWindows(actual);
            DiagnosticsLog.WriteEvent("StartupRegistrationChanged", ("Enabled", actual));
        }
        catch (Exception exception)
        {
            var actual = TryReadStartupRegistration();
            _trayIcon.SetStartWithWindows(actual);
            DiagnosticsLog.Write("Startup registration could not be changed.", exception);
        }
    }

    private void ToggleFastingMode(bool enabled)
    {
        _settings.FastingMode = enabled;
        ApplyToBoth(pet =>
        {
            if (pet._character.SupportsFood)
            {
                pet._lifeState.Hunger = PetModePolicy.ResolveHunger(pet._lifeState.Hunger, pet.EffectiveFastingMode);
                if (pet.EffectiveFastingMode) pet.SuppressHungerBehaviorForMode();
                pet.SaveLifeState();
            }
            pet.UpdateHungerDisplay(force: true);
        });
        _settingsStore.Save(_settings);
        DiagnosticsLog.WriteEvent(
            "FastingModeChanged",
            ("Enabled", enabled),
            ("CoexistenceMode", _settings.CoexistenceMode));
    }

    private void ToggleQuietMode(bool enabled)
    {
        if (enabled) CancelWindowPrank("QuietMode", returnToIdle: false, keepHatless: false);
        _settings.QuietMode = enabled;
        if (enabled) CancelPairScene("QuietMode");
        _settingsStore.Save(_settings);
        if (enabled)
        {
            HideSpeech();
            if (_primaryWindow is null) CancelPairDialogue();
        }

        if (enabled)
        {
            _foodQueue.Clear(CloseGhostWindow);
            _manualLickRequested = false;
            _wakeForMeal = false;
            _wakeForManualLick = false;
            _pendingClimb = null;
            _activeClimb = null;

            if (_state is PetState.Falling or PetState.Landing or PetState.Dragging)
            {
                _movementPurpose = MovementPurpose.None;
                _currentFood = null;
                CloseCurrentGhost();
                FoodVisual.Visibility = Visibility.Collapsed;
            }
            else if (_state is not (PetState.Sleeping or PetState.Waking))
            {
                ReturnToIdle();
            }
        }
        else if (_state == PetState.Idle)
        {
            ScheduleNextAutonomousDecision(
                _character.MaximumIdleSeconds,
                _character.MaximumIdleSeconds);
        }

        DiagnosticsLog.WriteEvent("QuietModeChanged", ("Enabled", enabled), ("State", _state));
    }

    private void ToggleDeleteReaction(bool enabled)
    {
        _settings.ReactToDeletes = enabled;
        _settingsStore.Save(_settings);
        if (!enabled)
        {
            CancelPairScene("DeleteReactionDisabled");
            _foodQueue.Clear(CloseGhostWindow);
            if (_currentFood is not null &&
                !_currentFood.OriginalPath.EndsWith(".snack", StringComparison.OrdinalIgnoreCase))
            {
                CloseCurrentGhost();
                ReturnToIdle();
            }
        }

        _voice.Play(
            enabled ? VoiceCue.Affirmative : VoiceCue.Calm,
            VoicePriority.Normal,
            TimeSpan.FromSeconds(3));
    }

    private void ToggleVoice(bool enabled)
    {
        _settings.SoundEnabled = enabled;
        _settingsStore.Save(_settings);
        _voice.SetEnabled(enabled && _character.HasVoice);
        if (enabled && _character.HasVoice && _primaryWindow is null)
        {
            _voice.Play(VoiceCue.Neutral, VoicePriority.Important, TimeSpan.Zero);
        }
    }

    private void ExitApplication()
    {
        _isClosing = true;
        if (_companionWindow is { } companion)
        {
            _companionWindow = null;
            companion.CloseForModeChange();
        }
        Close();
        System.Windows.Application.Current.Shutdown();
    }

    private static void OpenDiagnosticsLogFolder()
    {
        try
        {
            var directory = Path.GetDirectoryName(DiagnosticsLog.FilePath)!;
            Directory.CreateDirectory(directory);
            var arguments = File.Exists(DiagnosticsLog.FilePath)
                ? $"/select,\"{DiagnosticsLog.FilePath}\""
                : $"\"{directory}\"";
            Process.Start(new ProcessStartInfo("explorer.exe", arguments)
            {
                UseShellExecute = true,
            });
            DiagnosticsLog.WriteEvent("DiagnosticsFolderOpened", ("Path", DiagnosticsLog.FilePath));
        }
        catch (Exception exception)
        {
            DiagnosticsLog.Write("Diagnostics folder could not be opened.", exception);
        }
    }

    private void SaveLifeState()
    {
        NormalizeCharacterNeeds();
        SaveMischiefState();
        try
        {
            _lifeStateStore.Save(_lifeState);
        }
        catch (Exception exception)
        {
            DiagnosticsLog.Write($"Life state save skipped: {exception.GetType().Name}.");
        }
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        ClearCheekPinch();
        CancelPairScene("WindowClosing");
        if (_primaryWindow is null) CancelPairDialogue();
        CloseSpeechBubble();
        if (IsFeatureTestMode) { _isClosing = true; StopFeatureDemo("WindowClosing"); }
        CancelWindowPrank("WindowClosing", returnToIdle: false, keepHatless: false);
        if (!_isClosing)
        {
            e.Cancel = true;
            StopRendering();
            Hide();
            DiagnosticsLog.WriteEvent("PetHiddenToTray");
            return;
        }

        StopRendering();
        _deferredStartupCancellation.Cancel();
        _spriteWarmupCancellation?.Cancel();
        DetachAnimator(_animator);
        _windowSource?.RemoveHook(WindowMessageHook);
        SaveLifeState();
        _mouseMonitor.Dispose();
        _deletionWatcher?.Dispose();
        DisposeWindowPranks();
        CloseCurrentGhost();
        _foodQueue.Clear(CloseGhostWindow);
        _trayIcon.MenuOpenChanged -= TrayMenuOpenChanged;
        if (_primaryWindow is null) _trayIcon.Dispose();
        _voice.Dispose();
        DiagnosticsLog.WriteEvent("ProcessStopped", ("Character", _character.Id), ("Reason", "UserExit"));
    }
}
