using System.IO;
using SoftMochiPet.Core;
using SoftMochiPet.Interop;
using SoftMochiPet.Services;

namespace SoftMochiPet;

public partial class MainWindow
{
    private MainWindow? MischiefPet => _character.UsesMischief ? this
        : _companionWindow is { } companion && companion._character.UsesMischief ? companion : null;

    internal void StartCompanionIfEnabled()
    {
        if (_primaryWindow is not null || IsFeatureTestMode || IsBehaviorControlMode || !_settings.CoexistenceMode)
            return;
        if (IsLoaded) SetCoexistenceMode(true);
        else Loaded += StartCompanionAfterLoaded;
    }

    private void StartCompanionAfterLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        Loaded -= StartCompanionAfterLoaded;
        if (_settings.CoexistenceMode) SetCoexistenceMode(true);
    }

    private void ApplyToBoth(Action<MainWindow> change)
    {
        change(this);
        if (_companionWindow is { } companion && !companion._isClosing) change(companion);
    }

    private void BringBothHome() => ApplyToBoth(pet => pet.BringHome());

    private void SetCoexistenceMode(bool enabled)
    {
        if (_primaryWindow is not null || IsFeatureTestMode || IsBehaviorControlMode || _isClosing) return;
        if (!enabled)
        {
            CancelPairScene("CoexistenceDisabled");
            CancelPairDialogue();
        }
        if (enabled && _companionWindow is null)
        {
            var other = _character == PetCharacterProfile.Nuonuo
                ? PetCharacterProfile.FeibiJiubi : PetCharacterProfile.Nuonuo;
            var directory = Path.Combine(AppContext.BaseDirectory, other.RuntimeRelativeDirectory);
            if (!HasCharacterAssets(directory, other))
            {
                DiagnosticsLog.WriteEvent("CoexistenceRejected", ("Reason", "IncompleteSprites"),
                    ("Character", other.Id));
                _settings.CoexistenceMode = false;
                _trayIcon.SetCoexistence(false);
                UpdateHungerDisplay(force: true);
                _settingsStore.Save(_settings);
                return;
            }

            MainWindow? companion = null;
            try
            {
                companion = new MainWindow(primaryWindow: this, companionCharacter: other);
                if (companion._character != other)
                    throw new InvalidOperationException("Companion artwork could not be loaded.");
                _companionWindow = companion;
                _settings.CoexistenceMode = true;
                _trayIcon.SetCoexistence(true);
                ApplyToBoth(pet => pet.UpdateHungerDisplay(force: true));
                companion.Show();
                PlaceCompanionNearPrimary(companion);
                _nextPairDialogueAt = _clock.Elapsed.TotalSeconds + 8;
                _nextPairSceneAt = _clock.Elapsed.TotalSeconds + PairInteractionPolicy.FirstInvitationSeconds;
                _pairWindowSampleReady = false;
                _pairWindowPositions.Clear();
                _recentPairEventAt = 0;
                _nextPairContextAt = _clock.Elapsed.TotalSeconds + 10;
            }
            catch (Exception exception)
            {
                DiagnosticsLog.Write("Companion could not be shown.", exception);
                _companionWindow = null;
                _settings.CoexistenceMode = false;
                _trayIcon.SetCoexistence(false);
                companion?.CloseForModeChange();
            }
        }
        else if (!enabled && _companionWindow is { } previous)
        {
            _companionWindow = null;
            _settings.CoexistenceMode = false;
            previous.CloseForModeChange();
            _trayIcon.SetCoexistence(false);
            _trayIcon.SetCharacter(_character);
        }
        else if (!enabled)
        {
            _settings.CoexistenceMode = false;
            _trayIcon.SetCoexistence(false);
        }

        ApplyToBoth(pet => pet.UpdateHungerDisplay(force: true));
        _settingsStore.Save(_settings);
        DiagnosticsLog.WriteEvent("CoexistenceModeChanged", ("Enabled", _companionWindow is not null),
            ("Primary", _character.Id), ("Companion", _companionWindow?._character.Id));
    }

    private void ToggleInfiniteMode(bool enabled)
    {
        _settings.InfiniteMode = enabled;
        ApplyToBoth(pet =>
        {
            pet.NormalizeCharacterNeeds();
            if (enabled && pet._character.SupportsFood) pet.SuppressHungerBehaviorForMode();
            if (pet._character.SupportsFood) pet.SaveLifeState();
            pet.UpdateHungerDisplay(force: true);
        });
        _settingsStore.Save(_settings);
        DiagnosticsLog.WriteEvent("InfiniteModeChanged", ("Enabled", enabled),
            ("CoexistenceMode", _settings.CoexistenceMode));
    }

    private void PlaceCompanionNearPrimary(MainWindow companion)
    {
        if (_windowHandle == IntPtr.Zero || companion._windowHandle == IntPtr.Zero ||
            !NativeMethods.GetWindowRect(_windowHandle, out var primaryBounds)) return;
        var monitor = DisplayGeometry.FromWindow(_windowHandle);
        if (monitor is null) return;

        companion.ApplyResponsivePetSize(monitor);
        var size = DisplayGeometry.ProjectDipSize(companion.Width, companion.Height, monitor);
        var margin = (int)Math.Round(12 * monitor.Scale);
        var left = primaryBounds.Left - size.Width - margin;
        if (left < monitor.WorkArea.Left) left = primaryBounds.Right + margin;
        left = Math.Clamp(left, monitor.WorkArea.Left, Math.Max(monitor.WorkArea.Left, monitor.WorkArea.Right - size.Width));
        var primaryFoot = ProjectCanvasAnchorOffset(_character.Geometry.FootCanvasX,
            _character.Geometry.FootCanvasY, monitor);
        var companionFoot = companion.ProjectCanvasAnchorOffset(companion._character.Geometry.FootCanvasX,
            companion._character.Geometry.FootCanvasY, monitor);
        var top = Math.Clamp(primaryBounds.Top + primaryFoot.Y - companionFoot.Y,
            monitor.WorkArea.Top, Math.Max(monitor.WorkArea.Top, monitor.WorkArea.Bottom - companionFoot.Y));
        DisplayGeometry.MoveWindowPhysical(companion._windowHandle, left, top);
        companion._currentMonitor = monitor.Handle;
        companion.RefreshSurfaceMap(force: true);
        if (!companion.TryResolveGroundSupport(allowSnap: false)) companion.BeginFall(reason: "CompanionSpawn");
    }

    private void CloseForModeChange()
    {
        _isClosing = true;
        Close();
    }

    private void SuppressHungerBehaviorForMode()
    {
        _manualLickRequested = false;
        _wakeForManualLick = false;
        var hungerActionIsActive = _state is PetState.Rolling or PetState.Licking ||
            _state == PetState.Curious && _animator.CurrentClip.Equals("hungry", StringComparison.OrdinalIgnoreCase) ||
            _state == PetState.Running && _movementPurpose == MovementPurpose.IconLick;
        if (hungerActionIsActive) ReturnToIdle();
    }
}
