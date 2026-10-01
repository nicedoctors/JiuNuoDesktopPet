using Point = System.Windows.Point;
using SoftMochiPet.Services;

namespace SoftMochiPet;

public partial class MainWindow
{
    // Keep the original controller with its bitmap, ownership checks and restore
    // evidence. Moving another window must not overwrite the stored result.
    private sealed record StoredPrank(WindowPrankController Controller, WindowPrankTarget? Target,
        WindowPrankSnapshot? Snapshot, PrankKind Kind, bool Large, Point Contact,
        Point HatMouth, double HatWidth, bool PresentationFinished);

    private StoredPrank? _storedPrank;
    private bool HasStoredPrank => _storedPrank?.Controller.IsHeld == true;
    private bool HasHeldPrank => HasStoredPrank || _windowPranks?.IsHoldingWindow == true;

    private void ParkHeldPrank()
    {
        if (_storedPrank is not null || _windowPranks?.IsHeld != true || _prankPhase != PrankPhase.None) return;
        _storedPrank = new StoredPrank(_windowPranks, _prankTarget, _prankSnapshot, _prankKind,
            _prankLarge, _screenStrikeContact, _prankHatMouth, _prankHatMouthWidth, _prankPresentationFinished);
        _windowPranks = new WindowPrankController(_windowHandle);
        _prankTarget = null;
        _prankSnapshot = null;
        DiagnosticsLog.WriteEvent("PrankStorageParked", ("Kind", _storedPrank.Kind));
    }

    private void TakeStoredPrankForReturn()
    {
        if (_storedPrank is null) return;
        CancelWindowPrank("ManualRestorePreparation", false, keepHatless: false);
        ActivateStoredPrank();
    }

    private void ActivateStoredPrank()
    {
        if (_storedPrank is not { } stored) return;
        _windowPranks?.Dispose();
        _windowPranks = stored.Controller;
        _prankTarget = stored.Target;
        _prankSnapshot = stored.Snapshot;
        _prankKind = stored.Kind;
        _prankLarge = stored.Large;
        _screenStrikeContact = stored.Contact;
        _prankHatMouth = stored.HatMouth;
        _prankHatMouthWidth = stored.HatWidth;
        _prankPresentationFinished = stored.PresentationFinished;
        _storedPrank = null;
        DiagnosticsLog.WriteEvent("PrankStorageReturnRequested", ("Kind", _prankKind));
    }

    private void CheckStoredPrankSafety()
    {
        if (_storedPrank is not { } stored) return;
        stored.Controller.CheckSafety();
        if (stored.Controller.IsHeld) return;
        var reason = stored.Controller.CancelReason;
        ReleaseStoredPrank();
        _trayIcon.SetWindowReturnAvailable(HasHeldPrank);
        DiagnosticsLog.WriteEvent("PrankHeldWindowReleased", ("Reason", reason), ("Slot", "stored"));
    }

    private void ReleaseStoredPrank()
    {
        if (_storedPrank is not { } stored) return;
        _storedPrank = null;
        stored.Controller.Dispose();
        stored.Snapshot?.Dispose();
    }
}
