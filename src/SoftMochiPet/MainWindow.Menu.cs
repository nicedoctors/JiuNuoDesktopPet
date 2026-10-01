using System.Diagnostics;
using SoftMochiPet.Core;
using SoftMochiPet.Interop;
using SoftMochiPet.Services;

namespace SoftMochiPet;

public partial class MainWindow
{
    private double _menuOpenedAt;
    private double _nextMenuSafetyCheck;

    private void TrayMenuOpenChanged(bool open)
    {
        Dispatcher.VerifyAccess();
        var now = _clock.Elapsed.TotalSeconds;
        _lastTickSeconds = now;
        if (open)
        {
            ReleaseCheekPinch();
            _menuOpenedAt = now;
            _nextMenuSafetyCheck = now;
            var foreground = NativeMethods.GetForegroundWindow();
            _menuForegroundHandle = _lastExternalForegroundHandle != IntPtr.Zero
                ? _lastExternalForegroundHandle
                : foreground != _windowHandle ? foreground : IntPtr.Zero;
            RefreshBehaviorControlAvailability();
        }
        else
        {
            // Ignore a possible stale WPF mouse-down generated while the
            // WinForms tray menu is dismissed. This prevents a menu command
            // from turning into PickedUp/Falling.
            _suppressDragUntil = now + 0.25;
            // A platform may have moved while the menu was being used. Re-sample
            // it on the next frame without treating the pause as a launch impulse.
            _surfaceRefreshRemaining = 0;
            _previousWindowBodies.Clear();
            ResetSupportMotionTracking();
            ResetAirbornePlatformSupport();
            DiagnosticsLog.WriteEvent("PetMenuDragGuardArmed",
                ("DurationMs", 250), ("State", _state));
        }
        DiagnosticsLog.WriteEvent(open ? "PetMenuOpened" : "PetMenuClosed",
            ("State", _state), ("Animation", _animator.CurrentClip),
            ("HeldSeconds", open ? 0 : Math.Max(0, now - _menuOpenedAt)));
    }

    private bool PauseFrameForMenu(double now)
    {
        if (!_trayIcon.IsMenuOpen) return false;
        if (now >= _nextMenuSafetyCheck)
        {
            _nextMenuSafetyCheck = now + 0.2;
            CheckStoredPrankSafety();
            if (_state == PetState.Pranking)
            {
                _windowPranks?.CheckSafety();
                if (_prankSystemEngaged && !_prankAwaitingSystem && _windowPranks?.CancelReason is { } reason)
                    CancelWindowPrank(reason, true, keepHatless: true);
            }
            else UpdateHeldPrank(0);
        }
        return true;
    }

    private void TraceSlowUiFrame(long started, PetState before, string clipBefore)
    {
        var milliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        if (milliseconds < 100) return;
        DiagnosticsLog.WriteEventThrottled("ui-frame-slow", TimeSpan.FromSeconds(5), "UiFrameSlow",
            ("Milliseconds", milliseconds), ("StateBefore", before), ("StateAfter", _state),
            ("AnimationBefore", clipBefore), ("AnimationAfter", _animator.CurrentClip),
            ("MenuOpen", _trayIcon.IsMenuOpen), ("LoadedSpriteFolders", _animator.LoadedAssetFolderCount));
    }
}
