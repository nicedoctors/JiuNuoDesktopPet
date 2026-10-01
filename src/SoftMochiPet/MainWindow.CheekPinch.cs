using System.Windows;
using System.Windows.Input;
using SoftMochiPet.Core;
using SoftMochiPet.Services;

namespace SoftMochiPet;

public partial class MainWindow
{
    private readonly CheekPinchDynamics _cheekPinch = new();
    private readonly CheekPinchFrameRenderer _cheekFrames = new();
    private System.Windows.Point _pinchStart;
    private double _pinchUnitsPerDip;
    private System.Windows.Media.Imaging.BitmapSource? _pinchSource;

    private bool TryBeginCheekPinch(MouseButtonEventArgs e)
    {
        if (_isClosing || _isDragging || _cheekPinch.Active ||
            _state is not (PetState.Idle or PetState.Curious or PetState.Sleeping or PetState.Running))
            return false;

        var point = e.GetPosition(PetSprite);
        var geometry = CheekPinchGeometry.For(_character);
        var side = geometry.HitTest(point.X * 384 / 512, point.Y * 384 / 512);
        if (side is null) return false;

        var interruptedState = _state;
        RequeueActiveMealForDrag();
        _movementPurpose = MovementPurpose.None;
        _velocityX = _velocityY = 0;
        _movementPixelRemainderX = _movementPixelRemainderY = 0;
        _pendingClimb = null;
        _activeClimb = null;
        _state = PetState.Pinching;
        _stateElapsed = 0;
        SquashTransform.ScaleX = SquashTransform.ScaleY = 1;
        LeanTransform.Angle = 0;
        ResetSupportMotionTracking();
        _pinchStart = e.GetPosition(Root);
        _pinchUnitsPerDip = 384 / Math.Max(1, Width - 12);
        _cheekPinch.Begin(side.Value);
        _animator.Play("idle");
        _pinchSource = _animator.FrameAt(0);
        RenderCheekPinch();
        var captured = CharacterRoot.CaptureMouse();
        if (!captured) _cheekPinch.Release();
        DiagnosticsLog.WriteEvent("CheekPinchStarted", ("Character", _character.Id),
            ("Side", side), ("InterruptedState", interruptedState), ("MouseCaptured", captured));
        e.Handled = true;
        return true;
    }

    private bool MoveCheekPinch(System.Windows.Input.MouseEventArgs e)
    {
        if (!_cheekPinch.Held) return false;
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            ReleaseCheekPinch();
            return true;
        }
        var point = e.GetPosition(Root);
        _cheekPinch.Drag((point.X - _pinchStart.X) * _pinchUnitsPerDip * FacingTransform.ScaleX,
            (point.Y - _pinchStart.Y) * _pinchUnitsPerDip);
        e.Handled = true;
        return true;
    }

    private bool ReleaseCheekPinch()
    {
        if (!_cheekPinch.Held) return false;
        _cheekPinch.Release();
        if (CharacterRoot.IsMouseCaptured) CharacterRoot.ReleaseMouseCapture();
        DiagnosticsLog.WriteEvent("CheekPinchReleased", ("Character", _character.Id),
            ("Pull", $"{_cheekPinch.X:0.0},{_cheekPinch.Y:0.0}"));
        return true;
    }

    private void Character_LostMouseCapture(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_cheekPinch.Held) ReleaseCheekPinch();
    }

    private void TickCheekPinch(double delta)
    {
        _stateElapsed += delta;
        _cheekPinch.Tick(delta);
        if (!_cheekPinch.Active)
        {
            ResetSupportMotionTracking();
            _surfaceRefreshRemaining = 0;
            ReturnToIdle();
            DiagnosticsLog.WriteEvent("CheekPinchSettled", ("Character", _character.Id));
            return;
        }
        RenderCheekPinch();
    }

    private void RenderCheekPinch()
    {
        if (!_cheekPinch.Active || _pinchSource is null) return;
        PetSprite.Source = _cheekFrames.Render(_pinchSource, CheekPinchGeometry.For(_character),
            _cheekPinch.Side, _cheekPinch.X, _cheekPinch.Y, _cheekPinch.Nibble);
        // Rotate around the planted feet; window size and hit geometry stay fixed.
        LeanTransform.Angle = -_cheekPinch.X / CheekPinchDynamics.MaximumHorizontal * 3.5 * FacingTransform.ScaleX;
    }

    private void ClearCheekPinch()
    {
        var wasActive = _cheekPinch.Active;
        _cheekPinch.Reset();
        _cheekFrames.Clear();
        _pinchSource = null;
        if (wasActive && CharacterRoot.IsMouseCaptured) CharacterRoot.ReleaseMouseCapture();
    }
}
