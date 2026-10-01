using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SoftMochiPet.Core;
using SoftMochiPet.Interop;
using SoftMochiPet.Services;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;
using Pen = System.Windows.Media.Pen;

namespace SoftMochiPet;

public partial class MainWindow
{
    private sealed record PairLifeLane(int Left, int Right, int Ground, IntPtr Support,
        int SupportLeft, int SupportTop, int SupportRight);
    private PairLifeLane? _pairLifeLane;
    private double _nextPairContextAt = 10, _nextPairContextPoll;
    private double _nextPairFollowAt, _nextPairNuzzleAt, _nextPairBallAt = 35, _nextPairWatchAt;
    private double _nextPairWindowSample, _recentPairEventAt, _recentPairEventX;
    private bool _pairWindowSampleReady, _pairLifeReversed, _pairFollowStationary;
    private readonly Dictionary<IntPtr, WindowBodySnapshot> _pairWindowPositions = [];
    private double _pairLifeUnit, _pairWatchX, _pairLifeSupportCheck;
    private int _pairFollowDirection;
    private static BitmapSource? _pairBallImage;

    private static bool IsPairLifeScene(PairScene scene) =>
        scene is PairScene.Follow or PairScene.Nuzzle or PairScene.Ball or PairScene.Watch;

    private void ObserveCompanionWindows(double now)
    {
        if (_settings.QuietMode || now < _nextPairWindowSample || _surfaceProvider.RefreshVersion == 0) return;
        _nextPairWindowSample = now + 1;
        var monitor = DisplayGeometry.FromWindow(_windowHandle);
        if (monitor is null) return;
        var current = CompanionWatchWindows();
        foreach (var body in current)
        {
            var centerX = (body.Left + body.Right) / 2d;
            var centerY = (body.Top + body.Bottom) / 2d;
            if (!monitor.WorkArea.Contains((int)centerX, (int)centerY)) continue;
            if (_pairWindowSampleReady &&
                (!_pairWindowPositions.TryGetValue(body.SourceHandle, out var previous) ||
                 Math.Abs(previous.Left - body.Left) + Math.Abs(previous.Top - body.Top) >= 64 * monitor.Scale))
            {
                _recentPairEventAt = now;
                _recentPairEventX = centerX;
            }
        }
        _pairWindowPositions.Clear();
        foreach (var body in current) _pairWindowPositions[body.SourceHandle] = body;
        _pairWindowSampleReady = true;
    }

    private IReadOnlyList<WindowBodySnapshot> CompanionWatchWindows()
    {
        var windows = _surfaceProvider.WindowBodies.ToList();
        var foreground = _lastExternalForegroundHandle;
        if (foreground != IntPtr.Zero && !windows.Any(b => b.SourceHandle == foreground) &&
            NativeMethods.IsWindowVisible(foreground) && !NativeMethods.IsIconic(foreground) &&
            NativeMethods.GetWindowRect(foreground, out var bounds))
            windows.Add(new(foreground, bounds.Left, bounds.Top, bounds.Right, bounds.Bottom));
        return windows;
    }

    private static bool RejectPairLife(PairScene scene, string reason)
    {
        DiagnosticsLog.WriteEventThrottled($"pair-life-reject-{scene}-{reason}", TimeSpan.FromSeconds(15),
            "PairLifeUnavailable", ("Scene", scene), ("Reason", reason));
        return false;
    }

    private bool TryStartContextualPair(double now, MainWindow nuonuo, MainWindow feibi)
    {
        if (_settings.QuietMode || now < _nextPairContextAt || now < _nextPairContextPoll) return false;
        _nextPairContextPoll = now + 1;
        if (!NativeMethods.GetWindowRect(nuonuo._windowHandle, out var n) ||
            !NativeMethods.GetWindowRect(feibi._windowHandle, out var f)) return false;
        var width = f.Right - f.Left;
        var distance = Math.Abs((n.Left + n.Right - f.Left - f.Right) / 2d);
        if (now >= _nextPairWatchAt && CompanionLifePolicy.EventFresh(now, _recentPairEventAt) &&
            TryStartPairScene(PairScene.Watch)) return true;
        var moving = nuonuo._state == PetState.Running || feibi._state == PetState.Running;
        if (now >= _nextPairNuzzleAt && CompanionLifePolicy.IsPassing(distance, width, moving) &&
            TryStartPairScene(PairScene.Nuzzle)) return true;
        var away = feibi._state == PetState.Running && Math.Sign(feibi._velocityX) == Math.Sign(f.Left - n.Left);
        return now >= _nextPairFollowAt && CompanionLifePolicy.ShouldFollow(distance, width, away) &&
            TryStartPairScene(PairScene.Follow);
    }

    private bool StartPairLifeScene(PairScene scene, MainWindow nuonuo, MainWindow feibi,
        NativeMethods.NativeRect n, NativeMethods.NativeRect f, MonitorGeometry monitor)
    {
        var nFoot = nuonuo.ProjectCanvasAnchorOffset(nuonuo._character.Geometry.FootCanvasX,
            nuonuo._character.Geometry.FootCanvasY, monitor);
        var fFoot = feibi.ProjectCanvasAnchorOffset(feibi._character.Geometry.FootCanvasX,
            feibi._character.Geometry.FootCanvasY, monitor);
        var nx = n.Left + nFoot.X;
        var fx = f.Left + fFoot.X;
        var ny = n.Top + nFoot.Y;
        var fy = f.Top + fFoot.Y;
        var unit = feibi.Width * monitor.Scale;
        if (Math.Abs(ny - fy) > 16 * monitor.Scale) return RejectPairLife(scene, "DifferentHeight");
        PairLifeLane? lane = null;
        if (Math.Abs(ny - (monitor.WorkArea.Bottom - 1)) <= 16 * monitor.Scale &&
            Math.Abs(fy - (monitor.WorkArea.Bottom - 1)) <= 16 * monitor.Scale)
            lane = new(monitor.WorkArea.Left, monitor.WorkArea.Right, monitor.WorkArea.Bottom - 1,
                IntPtr.Zero, 0, 0, 0);
        else
        {
            var surface = _surfaceProvider.Surfaces.FirstOrDefault(s => !s.IsDesktopFloor &&
                Math.Abs(s.Top - ny) <= 10 * monitor.Scale && Math.Abs(s.Top - fy) <= 10 * monitor.Scale &&
                s.ContainsX(nx) && s.ContainsX(fx));
            if (surface is not null && NativeMethods.GetWindowRect(surface.SourceHandle, out var support))
                lane = new(Math.Max(surface.Left, monitor.WorkArea.Left), Math.Min(surface.Right, monitor.WorkArea.Right),
                    surface.Top, surface.SourceHandle, support.Left, support.Top, support.Right);
        }
        if (lane is null || lane.Ground - unit < monitor.WorkArea.Top)
            return RejectPairLife(scene, "NoSharedGround");
        var left = lane.Left + unit / 2;
        var right = lane.Right - unit / 2;
        if (right <= left) return RejectPairLife(scene, "NarrowGround");
        var now = _clock.Elapsed.TotalSeconds;
        if (scene == PairScene.Watch)
        {
            if (CompanionLifePolicy.EventFresh(now, _recentPairEventAt))
                _pairWatchX = _recentPairEventX;
            else
            {
                var target = CompanionWatchWindows().FirstOrDefault(b =>
                    monitor.WorkArea.Contains((b.Left + b.Right) / 2, (b.Top + b.Bottom) / 2));
                if (target.SourceHandle == IntPtr.Zero) return RejectPairLife(scene, "NoVisibleWindow");
                _pairWatchX = (target.Left + target.Right) / 2d;
            }
        }
        _pairLifeReversed = nx > fx;
        double nTarget, fTarget;
        if (scene == PairScene.Follow)
        {
            _pairFollowDirection = fx >= nx ? 1 : -1;
            var available = _pairFollowDirection > 0 ? right - fx : fx - left;
            var travel = Math.Max(0, Math.Min(unit * 1.8, available));
            _pairFollowStationary = travel < unit * .2;
            if (_pairFollowStationary) travel = 0;
            fTarget = fx + travel * _pairFollowDirection;
            nTarget = fTarget - unit * .85 * _pairFollowDirection;
            if (nTarget < left || nTarget > right) return RejectPairLife(scene, "FollowerOutsideGround");
        }
        else
        {
            var gap = unit * (scene == PairScene.Ball ? 2.2 : scene == PairScene.Nuzzle ? .5 : .85);
            if (right - left < gap) return RejectPairLife(scene, "NotEnoughSpace");
            var center = Math.Clamp((nx + fx) / 2d, left + gap / 2, right - gap / 2);
            nTarget = center + (_pairLifeReversed ? gap / 2 : -gap / 2);
            fTarget = center + (_pairLifeReversed ? -gap / 2 : gap / 2);
        }
        _pairNuonuoPosition = new(n.Left, n.Top, (int)Math.Round(nTarget - nFoot.X), lane.Ground - nFoot.Y);
        _pairFeibiPosition = new(f.Left, f.Top, (int)Math.Round(fTarget - fFoot.X), lane.Ground - fFoot.Y);
        _pairNuonuo = nuonuo;
        _pairFeibi = feibi;
        _pairLifeUnit = unit;
        _pairLifeLane = lane;
        _pairLifeSupportCheck = 0;
        _pairScene = scene;
        _pairSceneElapsed = 0;
        _pairActionStarted = false;
        _pairApproachSeconds = scene == PairScene.Follow ? 0 :
            PairApproachDuration(_pairNuonuoPosition, _pairFeibiPosition, monitor.Scale);
        CancelPairDialogue();
        feibi.ParkHeldPrank();
        PreparePairActor(nuonuo, scene == PairScene.Follow ? "idle" : "walk");
        PreparePairActor(feibi, "walk");
        _nextPairContextAt = now + CompanionLifePolicy.ContextGap;
        switch (scene)
        {
            case PairScene.Follow: _nextPairFollowAt = now + CompanionLifePolicy.FollowCooldown; break;
            case PairScene.Nuzzle: _nextPairNuzzleAt = now + CompanionLifePolicy.NuzzleCooldown; break;
            case PairScene.Ball: _nextPairBallAt = now + CompanionLifePolicy.BallCooldown; break;
            case PairScene.Watch: _nextPairWatchAt = now + CompanionLifePolicy.WatchCooldown; _recentPairEventAt = 0; break;
        }
        DiagnosticsLog.WriteEvent("PairSceneStarted", ("Scene", scene), ("ApproachSeconds", _pairApproachSeconds),
            ("Ground", lane.Support == IntPtr.Zero ? "Desktop" : "Window"));
        return true;
    }

    private void UpdatePairLifeScene(double delta, PairScene scene, MainWindow nuonuo, MainWindow feibi)
    {
        if (_pairLifeLane is not { } lane || _pairNuonuoPosition is not { } n ||
            _pairFeibiPosition is not { } f) { CancelPairScene("MissingLane"); return; }
        _pairSceneElapsed += delta;
        if (_pairSceneElapsed >= _pairLifeSupportCheck)
        {
            _pairLifeSupportCheck = _pairSceneElapsed + .25;
            if (lane.Support != IntPtr.Zero &&
                (!NativeMethods.IsWindowVisible(lane.Support) || NativeMethods.IsIconic(lane.Support) ||
                 !NativeMethods.GetWindowRect(lane.Support, out var bounds) ||
                 bounds.Left != lane.SupportLeft || bounds.Top != lane.SupportTop || bounds.Right != lane.SupportRight))
            { CancelPairScene("SupportChanged"); return; }
        }
        if (scene == PairScene.Follow)
        {
            UpdatePairFollowing(_pairSceneElapsed, delta, nuonuo, feibi, n, f);
            return;
        }
        var approach = Math.Min(1, _pairSceneElapsed / _pairApproachSeconds);
        if (approach < 1)
        {
            nuonuo._animator.Tick(delta);
            feibi._animator.Tick(delta);
            MovePairPet(nuonuo, n, approach);
            MovePairPet(feibi, f, approach);
            return;
        }
        MovePairPet(nuonuo, n, 1);
        MovePairPet(feibi, f, 1);
        var t = _pairSceneElapsed - _pairApproachSeconds;
        if (scene == PairScene.Watch)
        {
            var nCenter = n.TargetLeft + _pairLifeUnit / 2;
            var fCenter = f.TargetLeft + _pairLifeUnit / 2;
            nuonuo.FacingTransform.ScaleX = _pairWatchX >= nCenter ? 1 : -1;
            feibi.FacingTransform.ScaleX = _pairWatchX <= fCenter ? 1 : -1;
            PairLifeClip(feibi, "pair_notice", Math.Min(t, 2.88));
            PairLifeClip(nuonuo, "pair_notice", Math.Max(0, Math.Min(t - .55, 2.88)));
            if (t >= 4.2) CancelPairScene("Completed");
        }
        else
        {
            nuonuo.FacingTransform.ScaleX = feibi.FacingTransform.ScaleX = _pairLifeReversed ? -1 : 1;
            if (scene == PairScene.Nuzzle)
            {
                PairLifeClip(nuonuo, "pair_nuzzle", Math.Max(0, t - .18));
                PairLifeClip(feibi, "pair_nuzzle", t);
                if (t >= 3.6) CancelPairScene("Completed");
            }
            else UpdatePairBall(t, nuonuo, feibi, lane);
        }
    }

    private static void PairLifeClip(MainWindow pet, string clip, double time)
    {
        if (pet._animator.CurrentClip != clip || time + .000001 < pet._animator.ElapsedSeconds)
            pet._animator.Play(clip);
        pet._animator.AdvanceTo(Math.Max(0, time) + .0000001);
    }

    private void UpdatePairFollowing(double t, double delta, MainWindow nuonuo, MainWindow feibi,
        PairPosition n, PairPosition f)
    {
        var progress = CompanionLifePolicy.FollowProgress(t);
        // Progress already contains easing and a look-back pause; do not ease it twice.
        static void Move(MainWindow pet, PairPosition p, double value) =>
            DisplayGeometry.MoveWindowPhysical(pet._windowHandle,
                (int)Math.Round(p.StartLeft + (p.TargetLeft - p.StartLeft) * value),
                (int)Math.Round(p.StartTop + (p.TargetTop - p.StartTop) * value));
        Move(feibi, f, progress.Leader);
        Move(nuonuo, n, progress.Follower);
        feibi.FacingTransform.ScaleX = _pairFollowDirection;
        nuonuo.FacingTransform.ScaleX = _pairFollowDirection;
        if (_pairFollowStationary || t is >= 2.4 and < 3.4 || t >= 6)
        {
            feibi.FacingTransform.ScaleX = _pairFollowDirection > 0 ? 1 : -1;
            PairLifeClip(feibi, "pair_notice", _pairFollowStationary ? Math.Min(t, 2.88)
                : t < 3.4 ? (t - 2.4) * 1.3 : t - 6);
        }
        else
        {
            if (feibi._animator.CurrentClip != "walk") feibi._animator.Play("walk");
            feibi._animator.Tick(delta);
        }
        if (t < .8) PairLifeClip(nuonuo, "pair_notice", t);
        else if (t >= 6.5) PairLifeClip(nuonuo, "pair_notice", t - 6.5);
        else
        {
            if (nuonuo._animator.CurrentClip != "run") nuonuo._animator.Play("run");
            nuonuo._animator.Tick(delta);
        }
        if (t >= 8.2) CancelPairScene("Completed");
    }

    private void UpdatePairBall(double t, MainWindow nuonuo, MainWindow feibi, PairLifeLane lane)
    {
        var radius = Math.Max(7, _pairLifeUnit * .085);
        // These are the outer shoe tips in the selected frame 7 (384px canvas),
        // converted to the existing 512px character coordinate system.
        var nContact = nuonuo.CanvasPointToScreen(404, 392);
        var fContact = feibi.CanvasPointToScreen(100, 384);
        var sign = _pairLifeReversed ? -1 : 1;
        var nx = nContact.X + sign * radius;
        var fx = fContact.X - sign * radius;
        var hat = feibi.CanvasPointToScreen(235, 424);
        var sample = CompanionBallTimeline.Sample(t, nx, nContact.Y, fx, fContact.Y,
            hat.X, hat.Y, lane.Ground, radius, _pairLifeUnit);
        PairLifeClip(nuonuo, sample.Nuonuo.Clip, sample.Nuonuo.Seconds);
        PairLifeClip(feibi, sample.Feibi.Clip, sample.Feibi.Seconds);
        if (_pairTreatWindow is null && sample.Opacity > 0)
        {
            _pairTreatWindow = new GhostIconWindow();
            _pairTreatWindow.ShowAt(PairBallBitmap(), "", sample.X, sample.Y,
                (int)(radius * 2), (int)(radius * 2), 24, 14, labelOffsetY: 0);
        }
        _pairTreatWindow?.SetVisual(sample.X, sample.Y, 1, 1, t * 170, sample.Opacity);
        if (sample.Complete) CancelPairScene("Completed");
    }

    private static BitmapSource PairBallBitmap()
    {
        if (_pairBallImage is not null) return _pairBallImage;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var outline = new Pen(new SolidColorBrush(Color.FromRgb(87, 63, 88)), 3);
            dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(255, 189, 133)), outline, new Point(32, 32), 28, 28);
            dc.PushClip(new EllipseGeometry(new Point(32, 32), 27, 27));
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(151, 208, 237)), null,
                new Rect(8, 26, 55, 12), 6, 6);
            dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(190, 255, 255, 255)), null, new Point(22, 18), 8, 5);
            dc.Pop();
        }
        var bitmap = new RenderTargetBitmap(64, 64, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return _pairBallImage = bitmap;
    }
}
