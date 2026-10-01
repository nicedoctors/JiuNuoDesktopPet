using SoftMochiPet.Core;
using SoftMochiPet.Interop;
using SoftMochiPet.Services;

namespace SoftMochiPet;

public partial class MainWindow
{
    private SpeechBubbleWindow? _speechBubble;
    private PetDialogueSelector? _speechSelector;
    private PetState _lastSpeechState = PetState.Idle;
    private double _speechUntil;
    private double _nextSpeechAt;
    private double _nextAmbientSpeechAt;
    private double _nextPairDialogueAt;
    private double _pairDialogueStepAt;
    private double _manualPairDialogueUntil;
    private bool _manualPairDialoguePrepared;
    private int _pairDialogueStep;
    private int _previousPairDialogueIndex = -1;
    private PairDialogue _activePairDialogue;

    private void ToggleSpeechBubbles(bool enabled)
    {
        _settings.ShowSpeechBubbles = enabled;
        _settingsStore.Save(_settings);
        if (!enabled)
        {
            _manualPairDialogueUntil = 0;
            _manualPairDialoguePrepared = false;
            CancelPairDialogue();
            ApplyToBoth(pet => pet.HideSpeech());
        }
        else
        {
            _nextAmbientSpeechAt = _clock.Elapsed.TotalSeconds + 20;
            _nextPairDialogueAt = _clock.Elapsed.TotalSeconds + 7;
            TrySpeak(DialogueCue.Startup, bypassCooldown: true);
        }
        DiagnosticsLog.WriteEvent("SpeechBubblesChanged", ("Enabled", enabled));
    }

    private void RequestPairDialogue()
    {
        if (!_settings.ShowSpeechBubbles || _settings.QuietMode || _companionWindow is null) return;
        _manualPairDialogueUntil = _clock.Elapsed.TotalSeconds + 30;
        _manualPairDialoguePrepared = false;
    }

    private void PrepareManualPairDialogue()
    {
        if (_pairScene is not null) return;
        ApplyToBoth(pet =>
        {
            if (pet._state == PetState.Curious ||
                pet._state == PetState.Running &&
                pet._movementPurpose is MovementPurpose.Wander or MovementPurpose.Explore or MovementPurpose.Patrol)
                pet.ReturnToIdle();
            else if (pet._state == PetState.Sleeping) pet.BeginWake();
        });
    }

    private void UpdateSpeechOverlay()
    {
        var now = _clock.Elapsed.TotalSeconds;
        if (_isClosing || !IsVisible || !_settings.ShowSpeechBubbles || _settings.QuietMode ||
            IsFeatureTestMode || IsBehaviorControlMode || _sessionLocked)
        {
            HideSpeech();
            _lastSpeechState = _state;
            if (_primaryWindow is null) CancelPairDialogue();
            return;
        }

        if (_state == PetState.Interacting)
        {
            HideSpeech();
            _lastSpeechState = _state;
            if (_primaryWindow is null) CancelPairDialogue();
            return;
        }

        if (_state != _lastSpeechState)
        {
            var cue = _state switch
            {
                PetState.Sleeping => DialogueCue.Sleep,
                PetState.Waking => DialogueCue.WakeNormally,
                PetState.Chomping => DialogueCue.Suction,
                PetState.Satisfied => DialogueCue.ComfortablyFull,
                PetState.Dragging => DialogueCue.PickedUp,
                PetState.Climbing => DialogueCue.ConfidentClimb,
                PetState.Licking => DialogueCue.CuriousIconLick,
                PetState.Rolling => DialogueCue.HungryRoll,
                _ => (DialogueCue?)null,
            };
            _lastSpeechState = _state;
            if (cue is { } selected && (_primaryWindow ?? this)._pairDialogueStep == 0)
                TrySpeak(selected);
        }

        if (_speechBubble is { IsVisible: true } bubble)
        {
            if (now >= _speechUntil) HideSpeech();
            else if (NativeMethods.GetWindowRect(_windowHandle, out var bounds) &&
                     DisplayGeometry.FromWindow(_windowHandle) is { } monitor)
                bubble.Position(bounds, monitor.WorkArea);
        }

        if (_primaryWindow is null)
        {
            if (_companionWindow is null) MaybeSpeakAlone(now);
            else UpdatePairDialogue(now);
        }
    }

    private void MaybeSpeakAlone(double now)
    {
        if (now < _nextAmbientSpeechAt || _state != PetState.Idle || _trayIcon.IsMenuOpen) return;
        var cue = _character.SupportsFood && !_settings.FastingMode && !_settings.InfiniteMode &&
                  _lifeState.Hunger >= 72 ? DialogueCue.VeryHungry
            : _lifeState.Sleepiness >= 75 ? DialogueCue.Sleep
            : DialogueCue.Exploration;
        TrySpeak(cue);
        _nextAmbientSpeechAt = now + 28 + Random.Shared.Next(23);
    }

    private bool TrySpeak(DialogueCue cue, bool bypassCooldown = false)
    {
        if (!_settings.ShowSpeechBubbles || _settings.QuietMode || !IsVisible || _isClosing ||
            (!bypassCooldown && _clock.Elapsed.TotalSeconds < _nextSpeechAt)) return false;
        _speechSelector ??= new PetDialogueSelector(profile: _character);
        return ShowSpeech(_speechSelector.Choose(cue), 2.6);
    }

    private bool ShowSpeech(string line, double seconds)
    {
        if (_windowHandle == IntPtr.Zero || !NativeMethods.GetWindowRect(_windowHandle, out var bounds) ||
            DisplayGeometry.FromWindow(_windowHandle) is not { } monitor) return false;
        _speechBubble ??= new SpeechBubbleWindow(_character.UsesMischief);
        _speechBubble.ShowLine(line, bounds, monitor.WorkArea);
        _speechUntil = _clock.Elapsed.TotalSeconds + seconds;
        _nextSpeechAt = _speechUntil + 5;
        return true;
    }

    private void HideSpeech() => _speechBubble?.HideLine();

    private void CloseSpeechBubble()
    {
        _speechBubble?.Close();
        _speechBubble = null;
    }

    private void UpdatePairDialogue(double now)
    {
        if (_pairDialogueStep != 0)
        {
            if (!CanPairTalk()) { CancelPairDialogue(); return; }
            if (now < _pairDialogueStepAt) return;
            if (_pairDialogueStep == 1)
            {
                var replyPet = SpeakerFor(_activePairDialogue, first: false);
                SpeakerFor(_activePairDialogue, first: true).HideSpeech();
                replyPet.ShowSpeech(_activePairDialogue.Reply, 2.6);
                _pairDialogueStep = 2;
                _pairDialogueStepAt = now + 2.8;
            }
            else FinishPairDialogue();
            return;
        }

        if (_trayIcon.IsMenuOpen) return;
        if (_manualPairDialogueUntil > now && !_manualPairDialoguePrepared)
        {
            PrepareManualPairDialogue();
            _manualPairDialoguePrepared = true;
        }
        if (!CanPairTalk()) return;
        if (_manualPairDialogueUntil > now)
        {
            StartPairDialogue(now);
            _manualPairDialogueUntil = 0;
            _manualPairDialoguePrepared = false;
        }
        else if (now >= _nextPairDialogueAt &&
                 _speechBubble?.IsVisible != true && _companionWindow?._speechBubble?.IsVisible != true)
            StartPairDialogue(now);
    }

    private bool CanPairTalk() => _companionWindow is { _isClosing: false } companion &&
        !_isClosing && _pairScene is null && !_settings.QuietMode && _settings.ShowSpeechBubbles &&
        !_sessionLocked && !companion._sessionLocked && IsVisible && companion.IsVisible &&
        PairInteractionPolicy.CanInvite(_state, _movementPurpose) &&
        PairInteractionPolicy.CanInvite(companion._state, companion._movementPurpose) &&
        !_isDragging && !companion._isDragging &&
        _foodQueue.Count == 0 && companion._foodQueue.Count == 0 &&
        _currentFood is null && companion._currentFood is null &&
        !_manualMischiefPending && !companion._manualMischiefPending &&
        _prankPhase == PrankPhase.None && companion._prankPhase == PrankPhase.None &&
        _windowPranks is not { HasActiveOperation: true, IsHeld: false } &&
        companion._windowPranks is not { HasActiveOperation: true, IsHeld: false } &&
        !_animator.Hatless && !companion._animator.Hatless;

    private void StartPairDialogue(double now)
    {
        var choices = PairDialogueCatalog.Exchanges;
        var index = Random.Shared.Next(choices.Count);
        if (choices.Count > 1 && index == _previousPairDialogueIndex) index = (index + 1) % choices.Count;
        _previousPairDialogueIndex = index;
        _activePairDialogue = choices[index];
        ApplyToBoth(pet =>
        {
            if (pet._state != PetState.Idle) pet.ReturnToIdle();
        });
        ApplyToBoth(pet => pet.HideSpeech());
        if (!SpeakerFor(_activePairDialogue, first: true).ShowSpeech(_activePairDialogue.First, 2.6)) return;
        ApplyToBoth(pet => pet._nextAutonomousDecision = Math.Max(
            pet._nextAutonomousDecision, pet._clock.Elapsed.TotalSeconds + 6.2));
        _pairDialogueStep = 1;
        _pairDialogueStepAt = now + 2.9;
        _nextPairDialogueAt = now + 38 + Random.Shared.Next(23);
        DiagnosticsLog.WriteEvent("PairDialogueStarted", ("Exchange", index));
    }

    private MainWindow SpeakerFor(PairDialogue dialogue, bool first)
    {
        var wantsFeibi = first ? dialogue.FeibiFirst : !dialogue.FeibiFirst;
        return _character.UsesMischief == wantsFeibi ? this : _companionWindow!;
    }

    private void CancelPairDialogue()
    {
        _manualPairDialogueUntil = 0;
        _manualPairDialoguePrepared = false;
        if (_pairDialogueStep == 0) return;
        _pairDialogueStep = 0;
        ApplyToBoth(pet => pet.HideSpeech());
        _nextPairDialogueAt = _clock.Elapsed.TotalSeconds + 20;
    }

    private void FinishPairDialogue()
    {
        _pairDialogueStep = 0;
        ApplyToBoth(pet => pet.HideSpeech());
    }
}
