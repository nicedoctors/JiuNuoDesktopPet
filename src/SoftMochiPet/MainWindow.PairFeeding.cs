using System.IO;
using System.Windows.Media.Imaging;
using SoftMochiPet.Core;
using SoftMochiPet.Interop;
using SoftMochiPet.Services;

namespace SoftMochiPet;

public partial class MainWindow
{
    private sealed record FeedSupport(IntPtr Handle, NativeMethods.NativeRect Bounds);
    private FeedSupport? _feedNuonuoSupport, _feedFeibiSupport;
    private PairFeedFlight _feedFlight;
    private bool _feedLaunched, _feedConsumed;
    private double _feedSupportCheck, _feedScale, _feedCeiling;

    private static FeedSupport? CaptureFeedSupport(MainWindow pet) => pet._supportHandle != IntPtr.Zero &&
        NativeMethods.GetWindowRect(pet._supportHandle, out var bounds) ? new(pet._supportHandle, bounds) : null;

    private static bool FeedSupportUnchanged(FeedSupport? support) => support is null ||
        NativeMethods.IsWindowVisible(support.Handle) && !NativeMethods.IsIconic(support.Handle) &&
        NativeMethods.GetWindowRect(support.Handle, out var now) && now.Equals(support.Bounds);

    private bool StartRemotePairFeed(MainWindow nuonuo, MainWindow feibi,
        NativeMethods.NativeRect n, NativeMethods.NativeRect f)
    {
        var nm = DisplayGeometry.FromWindow(nuonuo._windowHandle);
        var fm = DisplayGeometry.FromWindow(feibi._windowHandle);
        if (nm is null || fm is null) return false;
        CancelPairDialogue();
        _feedNuonuoSupport = CaptureFeedSupport(nuonuo);
        _feedFeibiSupport = CaptureFeedSupport(feibi);
        _pairNuonuoPosition = new(n.Left, n.Top, n.Left, n.Top);
        _pairFeibiPosition = new(f.Left, f.Top, f.Left, f.Top);
        _pairNuonuo = nuonuo;
        _pairFeibi = feibi;
        _pairScene = PairScene.Feed;
        _previousPairActivity = CompanionActivity.Feed;
        _pairSceneElapsed = _feedSupportCheck = 0;
        _pairActionStarted = _pairSleepHolding = false;
        _feedLaunched = _feedConsumed = false;
        _pairTreatName = new[] { "apple", "cake", "donut", "pudding" }[Random.Shared.Next(4)];
        feibi.ParkHeldPrank();
        PreparePairActor(nuonuo, "pair_feed_wait");
        PreparePairActor(feibi, "hat_open");
        var facing = n.Left + n.Right <= f.Left + f.Right ? 1 : -1;
        nuonuo.FacingTransform.ScaleX = feibi.FacingTransform.ScaleX = facing;
        _feedScale = (nm.Scale + fm.Scale) / 2;
        _feedCeiling = Math.Min(nm.MonitorArea.Top, fm.MonitorArea.Top) + 16 * _feedScale;
        _feedFlight = PlanFeedFlight(nuonuo, feibi);
        DiagnosticsLog.WriteEvent("PairSceneStarted", ("Scene", PairScene.Feed), ("Treat", _pairTreatName),
            ("KeepPositions", true), ("Distance", (_feedFlight.End - _feedFlight.Start).Length),
            ("FlightSeconds", _feedFlight.Duration), ("DifferentMonitors", nm.Handle != fm.Handle));
        return true;
    }

    private PairFeedFlight PlanFeedFlight(MainWindow nuonuo, MainWindow feibi) => PairFeedFlight.Create(
        feibi.PairPointToScreen("pair_feed", 12, 95, 310),
        nuonuo.PairPointToScreen("pair_feed", 9, 290, 240), _feedScale, _feedCeiling);

    private void UpdateRemotePairFeed(double delta, MainWindow nuonuo, MainWindow feibi)
    {
        _pairSceneElapsed += delta;
        var t = _pairSceneElapsed;
        if (t >= _feedSupportCheck)
        {
            _feedSupportCheck = t + .15;
            if (!FeedSupportUnchanged(_feedNuonuoSupport) || !FeedSupportUnchanged(_feedFeibiSupport))
            {
                CancelPairScene("FeedSupportMoved");
                return;
            }
        }
        if (t < 1.76) PairLifeClip(feibi, "hat_open", t);
        else if (t < 4.56) PairLifeClip(feibi, "pair_feed", t - 1.76);
        else PairLifeClip(feibi, "hat_wear", t - 4.56);

        if (!_feedLaunched && t >= PairFeedFlight.ThrowAt)
        {
            // Capture screen-pixel endpoints once, after facing and DPI transforms.
            _feedFlight = PlanFeedFlight(nuonuo, feibi);
            _feedLaunched = true;
        }
        if (t < _feedFlight.ReadyStart) PairLifeClip(nuonuo, "pair_feed_wait", t);
        else if (t < _feedFlight.CatchStart) PairLifeClip(nuonuo, "pair_feed_ready", t - _feedFlight.ReadyStart);
        else PairLifeClip(nuonuo, "pair_feed_catch", t - _feedFlight.CatchStart);

        UpdateRemoteTreat(t, nuonuo, feibi);
        if (_pairScene != PairScene.Feed) return;
        if (!_feedConsumed && t >= _feedFlight.Arrival)
        {
            _feedConsumed = true;
            _pairTreatWindow?.HideGhost();
            if (!_settings.InfiniteMode && !_settings.FastingMode)
            {
                nuonuo._lifeState.Hunger = Math.Max(0, nuonuo._lifeState.Hunger - 9);
                nuonuo.SaveLifeState();
                nuonuo.UpdateHungerDisplay(force: true);
            }
            DiagnosticsLog.WriteEvent("PairFeedConsumed", ("Treat", _pairTreatName));
        }
        if (t >= _feedFlight.Finished) CancelPairScene("Completed");
    }

    private void UpdateRemoteTreat(double time, MainWindow nuonuo, MainWindow feibi)
    {
        if (time < 2.78 || time >= _feedFlight.Arrival) return;
        var hat = feibi.PairPointToScreen("pair_feed", 6, 256, 388);
        if (_pairTreatWindow is null)
        {
            var file = Path.Combine(AppContext.BaseDirectory, "assets", "pair_interactions", "props", $"{_pairTreatName}.png");
            if (!File.Exists(file)) { CancelPairScene("TreatAssetMissing"); return; }
            var image = new BitmapImage(new Uri(file, UriKind.Absolute));
            image.Freeze();
            _pairTreatWindow = new GhostIconWindow();
            var pixels = (int)Math.Round(Math.Clamp((nuonuo.Width - 12) * _feedScale * .23, 16, 100));
            _pairTreatWindow.ShowAt(image, "", hat.X, hat.Y, iconPixelWidth: pixels, iconPixelHeight: pixels,
                labelPixelWidth: 24, labelPixelHeight: 14, labelOffsetY: 0);
        }
        var hand = feibi.PairPointToScreen("pair_feed", 9, 112, 255);
        var release = _feedFlight.Start;
        System.Windows.Point p;
        double scale, spin = 0;
        if (time < 3.45)
        {
            var t = PairEase((time - 2.78) / .67);
            p = hat + (hand - hat) * t;
            scale = .28 + .72 * t;
        }
        else if (time < PairFeedFlight.ThrowAt)
        {
            var t = PairEase((time - 3.45) / (PairFeedFlight.ThrowAt - 3.45));
            p = hand + (release - hand) * t;
            scale = 1;
        }
        else
        {
            var t = (time - PairFeedFlight.ThrowAt) / _feedFlight.Duration;
            p = _feedFlight.At(t);
            scale = 1 - .8 * PairEase((t - .78) / .22);
            spin = 360 * t * Math.Sign(_feedFlight.End.X - _feedFlight.Start.X);
        }
        _pairTreatWindow.SetVisual(p.X, p.Y, scale, scale, spin, 1);
    }
}
