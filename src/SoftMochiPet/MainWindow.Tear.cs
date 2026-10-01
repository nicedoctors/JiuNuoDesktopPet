using System.Windows;
using SoftMochiPet.Core;
using SoftMochiPet.Interop;
using SoftMochiPet.Services;
using Point = System.Windows.Point;

namespace SoftMochiPet;

public partial class MainWindow
{
    private double _tearNormalSize;
    private double _tearGrownSize;
    private double _tearDpiScale;
    private Point _tearGrowthStartFoot;
    private Point _tearStageFoot;
    // The normal-size foot anchor to use after the oversized tear pose.  Keep
    // this separate from the live canvas point: layout changes during the
    // resize can otherwise leave the pet one frame away from its approach
    // platform before the landing resolver runs.
    private Point _tearRestoreFoot;
    private bool _tearRestoreFootValid;
    private bool _tearSnapshotRequested;

    private void BeginTearGrowth()
    {
        var target = _prankTarget!;
        var dpi = PetPhysicalScale();
        _tearDpiScale = dpi;
        _tearNormalSize = Width;
        _tearGrownSize = TearPrankPresentation.ExpectedGrowSize(Width, target.VisibleBounds.Height,
            target.WorkArea.Width, target.WorkArea.Height, dpi);
        _tearGrowthStartFoot = PrankFoot();
        _tearRestoreFoot = _prankApproachFoot;
        _tearRestoreFootValid = double.IsFinite(_tearRestoreFoot.X) && double.IsFinite(_tearRestoreFoot.Y);
        _tearSnapshotRequested = false;
        var clip = _prankArtwork!.Clip("bare_tear");
        var upper = clip.TearUpperHandAt(3);
        var lower = clip.TearLowerHandAt(3);
        var scale = (_tearGrownSize - 12) * dpi / 512;
        var anchorX = (upper.X + lower.X) / 2;
        var anchorY = (upper.Y + lower.Y) / 2;
        var work = target.WorkArea;
        var size = _tearGrownSize * dpi;
        var left = _prankApproachDirection < 0
            ? target.VisibleBounds.Left - (512 - anchorX) * scale + 6 * dpi
            : target.VisibleBounds.Right - anchorX * scale - 6 * dpi;
        left = Math.Clamp(left, work.Left, Math.Max(work.Left, work.Right - size));
        var top = Math.Clamp(target.VisibleBounds.Top + target.VisibleBounds.Height * 0.5 - anchorY * scale - 6 * dpi,
            work.Top, Math.Max(work.Top, work.Bottom - size));
        _tearStageFoot = new Point(left + 6 * dpi + _character.Geometry.FootCanvasX * scale,
            top + 6 * dpi + _character.Geometry.FootCanvasY * scale);
        BeginPrankPhase(PrankPhase.TearGrow, "bare_idle", 0.8);
        DiagnosticsLog.WriteEvent("PrankTearGrowth", ("NormalDip", _tearNormalSize),
            ("GrownDip", _tearGrownSize), ("Dpi", dpi), ("SettingsChanged", false));
    }

    private void UpdateTearGrowth(double progress)
    {
        var amount = SmoothStep(progress);
        SetTearSizeAtFoot(Lerp(_tearNormalSize, _tearGrownSize, amount),
            new Point(Lerp(_tearGrowthStartFoot.X, _tearStageFoot.X, amount),
                Lerp(_tearGrowthStartFoot.Y, _tearStageFoot.Y, amount)));
    }

    private void UpdateTearPresentation(double progress)
    {
        // The source hands begin releasing at frame ten, after the peak at nine.
        var release = SmoothStep(Math.Clamp((progress * 16 - 10) / 6, 0, 1));
        var body = TearPrankPresentation.At(progress);
        SquashTransform.ScaleX = body.BodyScaleX;
        SquashTransform.ScaleY = body.BodyScaleY;
        LeanTransform.Angle = 0;
        SetTearSizeAtFoot(Lerp(_tearGrownSize, _tearNormalSize, release),
            new Point(Lerp(_tearStageFoot.X, _prankApproachFoot.X, release),
                Lerp(_tearStageFoot.Y, _prankApproachFoot.Y, release)));
    }

    private void SetTearSizeAtFoot(double size, Point foot)
    {
        // Resize first, then place from the measured screen-space foot anchor.
        // The old two-step path moved the old-sized HWND to a predicted corner,
        // resized it, and corrected by an offset.  At fractional DPI that made
        // the anchor alternate by a pixel as the width crossed rounding
        // thresholds, which was especially visible during tear growth.
        SetTearSize(size);
        if (NativeMethods.GetWindowRect(_windowHandle, out var bounds))
        {
            var current = TearCanvasPointToScreen(_character.Geometry.FootCanvasX,
                _character.Geometry.FootCanvasY);
            DisplayGeometry.MoveWindowPhysical(_windowHandle,
                bounds.Left + (int)Math.Round(foot.X - current.X),
                bounds.Top + (int)Math.Round(foot.Y - current.Y));
        }
        else
        {
            MoveTearFoot(foot);
        }
    }

    private void SetTearSize(double size)
    {
        if (Math.Abs(Width - size) < 0.05) return;
        Width = Height = size;
        // Resolve the Viewbox before using its physical hand and foot coordinates.
        UpdateLayout();
    }

    private Point TearCanvasPointToScreen(double x, double y) => PetSprite.PointToScreen(new Point(x, y));

    private void MoveTearFoot(Point desired)
    {
        var current = TearCanvasPointToScreen(_character.Geometry.FootCanvasX, _character.Geometry.FootCanvasY);
        DisplayGeometry.OffsetWindowPhysical(_windowHandle,
            (int)Math.Round(desired.X - current.X), (int)Math.Round(desired.Y - current.Y));
    }

    private void ResetTearPresentation()
    {
        PetSprite.Opacity = 1;
        if (_tearNormalSize <= 0) return;
        // Keep the enlarged pose's physical foot fixed while returning to the
        // normal size; measuring after the resize avoids a one-frame landing
        // jump caused by stale Viewbox coordinates.
        var measuredFoot = TearCanvasPointToScreen(_character.Geometry.FootCanvasX,
            _character.Geometry.FootCanvasY);
        var desiredFoot = _tearRestoreFootValid ? _tearRestoreFoot : measuredFoot;
        ResetBodyDeformation();
        SetTearSize(_tearNormalSize);
        if (NativeMethods.GetWindowRect(_windowHandle, out var bounds))
        {
            var current = TearCanvasPointToScreen(_character.Geometry.FootCanvasX,
                _character.Geometry.FootCanvasY);
            DisplayGeometry.MoveWindowPhysical(_windowHandle,
                bounds.Left + (int)Math.Round(desiredFoot.X - current.X),
                bounds.Top + (int)Math.Round(desiredFoot.Y - current.Y));
        }
        else
        {
            MoveTearFoot(desiredFoot);
        }
        DiagnosticsLog.WriteEvent("PrankTearSizeRestored", ("ActualDip", Width));
        _tearNormalSize = _tearGrownSize = 0;
        _tearSnapshotRequested = false;
        _tearRestoreFootValid = false;
        UpdateSizeDependentUi();
    }
}
