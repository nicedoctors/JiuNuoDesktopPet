using System.Drawing;
using SoftMochiPet.Core;
using SoftMochiPet.Services;

internal static class WindowPrankTests
{
    public static void WindowPrankEligibilityFailsClosed()
    {
        bool Eligible(bool visible = true, bool minimized = false, bool maximized = false, bool foreground = false,
            bool own = false, bool ordinary = true, bool responsive = true, bool protectedContent = false,
            bool contained = true, bool unoccluded = true) => WindowPrankGeometry.IsEligible(visible, minimized,
                maximized, foreground, own, ordinary, responsive, protectedContent, contained, unoccluded);
        Assert(Eligible(), "A visible ordinary inactive window may be considered.");
        Assert(!Eligible(visible: false) && !Eligible(minimized: true) && !Eligible(maximized: true) &&
            !Eligible(own: true) && !Eligible(ordinary: false) &&
            !Eligible(responsive: false) && !Eligible(protectedContent: true) && !Eligible(contained: false) &&
            !Eligible(unoccluded: false), "Every protected or untrustworthy condition must exclude a target.");
    }

    public static void WindowPrankOrdinaryRejectionsAreSpecific()
    {
        const long ordinary = 0x00CA0000;
        string? Reject(long style = ordinary, long extended = 0, bool owner = false,
            bool dialog = false, bool enabled = true) =>
            WindowPrankSelectionPolicy.OrdinaryWindowRejection(style, extended, owner, dialog, enabled);
        Assert(Reject() is null, "A standard captioned, minimizable application is eligible by style.");
        Assert(Reject(style: ordinary | 0x40000000) == "child_window" &&
            Reject(style: ordinary & ~0x00C00000L) == "missing_caption" &&
            Reject(style: ordinary & ~0x00080000L) == "missing_system_menu" &&
            Reject(style: ordinary & ~0x00020000L) == "not_minimizable",
            "Style failures must identify the exact unsupported window property.");
        Assert(Reject(extended: 0x80) == "tool_window" && Reject(extended: 0x08000000) == "noactivate_window" &&
            Reject(extended: 8) == "topmost_window" && Reject(extended: 0x80000) == "layered_target" &&
            Reject(owner: true) == "owned_window" && Reject(dialog: true) == "dialog_window" &&
            Reject(enabled: false) == "disabled_window",
            "Diagnostics must preserve every protected window category rather than label all failures occluded.");
        var monitor = new Rectangle(0, 0, 1920, 1080);
        var workArea = new Rectangle(0, 0, 1920, 1040);
        Assert(WindowPrankSelectionPolicy.IsFullScreen(monitor, monitor, workArea) &&
            WindowPrankSelectionPolicy.IsFullScreen(workArea, monitor, workArea) &&
            !WindowPrankSelectionPolicy.IsFullScreen(new Rectangle(200, 120, 960, 720), monitor, workArea),
            "Full-screen and work-area-sized surfaces must remain excluded while ordinary bounded windows stay eligible.");
    }

    public static void WindowPrankOcclusionRequiresVisibleCoverageEvidence()
    {
        bool Ignore(bool layered = true, bool attributes = true, uint flags = 2, byte alpha = 255,
            int regionType = 0, bool intersects = true) =>
            WindowPrankSelectionPolicy.IsProvablyNonCovering(layered, attributes, flags, alpha, regionType, intersects);
        Assert(Ignore(alpha: 0), "A successfully queried global alpha of zero proves no visible coverage.");
        Assert(!Ignore(alpha: 1) && !Ignore(alpha: 128) && !Ignore(),
            "Even a faint nonzero-alpha overlay still changes captured pixels and must block capture.");
        Assert(!Ignore(attributes: false, alpha: 0) && !Ignore(flags: 1, alpha: 0) && !Ignore(layered: false, alpha: 0),
            "Unknown per-pixel opacity, color keys, or unverified attributes must not be treated as transparent.");
        Assert(Ignore(regionType: 1) && Ignore(regionType: 2, intersects: false) && Ignore(regionType: 3, intersects: false),
            "An empty region or a proven hole over the target cannot occlude it.");
        Assert(!Ignore(regionType: 2) && !Ignore(regionType: 3) && !Ignore(regionType: 0, intersects: false),
            "Intersecting regions and unavailable regions remain potential occluders.");
        var relative = WindowPrankSelectionPolicy.ToWindowRegionCoordinates(
            new Rectangle(-810, 240, 30, 40), new Rectangle(-908, 142, 816, 616));
        Assert(relative == new Rectangle(98, 98, 30, 40),
            "Region coordinates are relative to the outer window origin, not the client area or DWM visible border.");
    }

    public static void WindowPrankCompletionRequiresNativeAcknowledgement()
    {
        var initial = new Rectangle(800, 200, 640, 400);
        var destination = new Rectangle(550, 200, 640, 400);
        Assert(WindowPrankSelectionPolicy.IsConfirmedMotion(initial, destination, destination, false),
            "A real position change acknowledged at the requested destination confirms motion.");
        Assert(!WindowPrankSelectionPolicy.IsConfirmedMotion(initial, destination, initial, false) &&
            !WindowPrankSelectionPolicy.IsConfirmedMotion(initial, destination, new Rectangle(551, 200, 641, 400), false) &&
            !WindowPrankSelectionPolicy.IsConfirmedMotion(initial, destination, destination, true) &&
            !WindowPrankSelectionPolicy.IsConfirmedMotion(initial, initial, initial, false),
            "A requested but unperformed move, resize, minimize, or unchanged window is not a completed kick.");
        Assert(WindowPrankSelectionPolicy.IsConfirmedRestore(true, false, true, initial, initial),
            "Restore evidence must follow a confirmed minimize and match the original actual bounds.");
        Assert(!WindowPrankSelectionPolicy.IsConfirmedRestore(false, false, true, initial, initial) &&
            !WindowPrankSelectionPolicy.IsConfirmedRestore(true, true, true, initial, initial) &&
            !WindowPrankSelectionPolicy.IsConfirmedRestore(true, false, false, initial, initial) &&
            !WindowPrankSelectionPolicy.IsConfirmedRestore(true, false, true, initial, destination),
            "A restore request alone, continued minimization, or user reposition must not report success.");
    }

    public static void WindowPrankOwnUiPreservesSafetyPause()
    {
        Assert(!WindowPrankHoldPolicy.ShouldAbortForForegroundChange(held: false,
                foregroundChanged: true, foregroundIsOwnUi: true),
            "An own-process menu or overlay must not abort a paused active action.");
        Assert(WindowPrankHoldPolicy.ShouldAbortForForegroundChange(held: false,
                foregroundChanged: true, foregroundIsOwnUi: false) &&
            WindowPrankHoldPolicy.ShouldAbortForForegroundChange(held: false, foregroundChanged: true),
            "A new external foreground window, including unknown ownership, keeps the active safety cancellation.");
        Assert(!WindowPrankHoldPolicy.ShouldAbortForForegroundChange(held: false,
                foregroundChanged: false, foregroundIsOwnUi: false) &&
            !WindowPrankHoldPolicy.ShouldAbortForForegroundChange(held: true,
                foregroundChanged: true, foregroundIsOwnUi: false),
            "The unchanged foreground and held-result rules retain their existing behavior.");
        Assert(WindowPrankHoldPolicy.ShouldRelinquishOwnership(true, true, false) &&
            WindowPrankHoldPolicy.ShouldRelinquishOwnership(false, false, false) &&
            WindowPrankHoldPolicy.ShouldRelinquishOwnership(true, false, true) &&
            WindowPrankGeometry.ShouldRelinquishSnapshot(restoring: false, iconic: false, placementUnchanged: true),
            "Target takeover, identity loss, maximization and external restore still release ownership while a menu is open.");
    }

    public static void WindowPrankMotionPreservesSizeAndWorkArea()
    {
        var area = new Rectangle(-1920, -180, 1920, 1040);
        var small = new Rectangle(-1300, 60, 500, 350);
        var large = new Rectangle(-1400, -100, 1000, 850);
        foreach (var kind in Enum.GetValues<WindowPrankMotionKind>())
        foreach (var direction in new[] { -1, 1 })
        {
            var end = WindowPrankGeometry.MotionDestination(small, area, kind, direction);
            Assert(area.Contains(end) && end.Size == small.Size, "Motion must keep the real window reachable without resizing it.");
            var previous = small;
            for (var frame = 0; frame <= 120; frame++)
            {
                var current = WindowPrankGeometry.AtMotionProgress(small, end, frame / 120d);
                Assert(area.Contains(current) && current.Size == small.Size, "Every intermediate position must be safe.");
                Assert(direction < 0 ? current.Left <= previous.Left : current.Left >= previous.Left,
                    "Impulse motion must not jitter or reverse before settling.");
                previous = current;
            }
            Assert(previous == end, "Low or high display cadence must end at the same destination.");
        }
        var smallMove = WindowPrankGeometry.MotionDestination(small, area, WindowPrankMotionKind.Kick, -1);
        var largeMove = WindowPrankGeometry.MotionDestination(large, area, WindowPrankMotionKind.Kick, -1);
        Assert(small.Left - smallMove.Left > large.Left - largeMove.Left, "Larger windows should feel heavier.");
        var edge = new Rectangle(area.Left, area.Top, 500, 350);
        Assert(WindowPrankGeometry.MotionDestination(edge, area, WindowPrankMotionKind.Charge, -1) == edge,
            "A kick toward the monitor edge must not move a window out of reach.");
    }

    public static void WindowPrankImpactFrontLoadsMotionAndDifferentiatesAttacks()
    {
        var area = new Rectangle(0, 0, 4096, 1440);
        var start = new Rectangle(2400, 300, 640, 400);
        int Distance(WindowPrankMotionKind kind) => start.Left -
            WindowPrankGeometry.MotionDestination(start, area, kind, -1).Left;
        var punch = WindowPrankGeometry.MotionDuration(WindowPrankMotionKind.Punch);
        var kick = WindowPrankGeometry.MotionDuration(WindowPrankMotionKind.Kick);
        var charge = WindowPrankGeometry.MotionDuration(WindowPrankMotionKind.Charge);
        Assert(punch >= 0.2 && punch < kick && kick < charge && charge <= 0.6,
            "A punch settles sharply, a kick carries farther, and a charge remains forceful without a long glide.");
        Assert(Distance(WindowPrankMotionKind.Punch) < Distance(WindowPrankMotionKind.Kick) &&
            Distance(WindowPrankMotionKind.Kick) < Distance(WindowPrankMotionKind.Charge),
            "A charge must carry farther than a kick, while a punch stays compact.");
        foreach (var kind in Enum.GetValues<WindowPrankMotionKind>())
        {
            var end = WindowPrankGeometry.MotionDestination(start, area, kind, -1);
            var distance = start.Left - end.Left;
            var early = WindowPrankGeometry.AtMotionProgress(start, end, 0.1);
            var second = WindowPrankGeometry.AtMotionProgress(start, end, 0.2);
            Assert(start.Left - early.Left >= distance * 0.40 && start.Left - second.Left >= distance * 0.66,
                "The opening impact must carry substantial displacement immediately, not ease into a push.");
            var previous = start;
            var previousTravel = distance;
            for (var slice = 1; slice <= 10; slice++)
            {
                var current = WindowPrankGeometry.AtMotionProgress(start, end, slice / 10d);
                var travel = previous.Left - current.Left;
                Assert(travel >= 0 && travel <= previousTravel + 1,
                    "Equal time slices lose speed monotonically, allowing one pixel of rounding but no second shove or rebound.");
                previous = current;
                previousTravel = travel;
            }
            foreach (var framesPerSecond in new[] { 15, 30, 60, 144 })
            {
                previous = start;
                var duration = WindowPrankGeometry.MotionDuration(kind);
                for (var frame = 0; frame <= Math.Ceiling(duration * framesPerSecond); frame++)
                {
                    var current = WindowPrankGeometry.AtMotionProgress(start, end, frame / (duration * framesPerSecond));
                    Assert(area.Contains(current) && current.Size == start.Size && current.Left <= previous.Left,
                        "Impact cadence must preserve the real window's size, direction, and reachable bounds.");
                    previous = current;
                }
                Assert(previous == end, "Every display cadence must settle on the exact clamped destination.");
            }
            Assert(WindowPrankGeometry.AtMotionProgress(start, end, -0.2) == start &&
                WindowPrankGeometry.AtMotionProgress(start, end, 1.2) == end,
                "Early or late samples must not move past either endpoint.");
        }
    }

    public static void WindowPrankDwmInsetsRemainPhysical()
    {
        var visible = new Rectangle(-900, 150, 800, 600);
        var outer = new Rectangle(-908, 142, 816, 616);
        var next = new Rectangle(-1037, 150, 800, 600);
        var converted = WindowPrankGeometry.ToOuterBounds(next, visible, outer);
        Assert(converted == new Rectangle(-1045, 142, 816, 616), "DWM invisible borders must not shift the contact anchor.");
        Assert(WindowPrankGeometry.SameBounds(converted, new Rectangle(-1044, 143, 816, 616)) &&
            !WindowPrankGeometry.SameBounds(converted, new Rectangle(-1030, 142, 816, 616)),
            "Pixel rounding is tolerated, but a user move must not be mistaken for an acknowledged request.");
    }

    public static void WindowPrankRejectsBlackOrUniformCaptures()
    {
        const int width = 100, height = 100, stride = width * 4;
        var pixels = new byte[stride * height];
        Assert(!WindowPrankGeometry.HasUsefulPixels(pixels, stride, width, height), "A black capture must never replace a real window.");
        Array.Fill(pixels, (byte)240);
        Assert(!WindowPrankGeometry.HasUsefulPixels(pixels, stride, width, height), "An uninformative constant capture is skipped conservatively.");
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var index = y * stride + x * 4;
            pixels[index] = pixels[index + 1] = pixels[index + 2] = (byte)(x < 50 ? 50 : 230);
        }
        Assert(WindowPrankGeometry.HasUsefulPixels(pixels, stride, width, height), "A synthetic nonuniform window can be animated.");
        Assert(!WindowPrankGeometry.HasUsefulPixels([1, 2, 3], stride, width, height), "Truncated capture buffers fail closed.");
    }

    public static void WindowPrankShardsShareSeamsAndCoverWholeImage()
    {
        var shards = WindowPrankGeometry.CreateShards(800, 600);
        Assert(shards.Length == 16, "Shatter should use a readable number of large cartoony pieces.");
        double area = 0;
        foreach (var shard in shards)
        {
            Assert(shard.Length == 4 && shard.All(p => p.X >= 0 && p.X <= 800 && p.Y >= 0 && p.Y <= 600),
                "Every fragment must originate from real pixels within the captured image.");
            for (var i = 0; i < 4; i++)
                area += (double)shard[i].X * shard[(i + 1) % 4].Y / 2 - (double)shard[(i + 1) % 4].X * shard[i].Y / 2;
        }
        Assert(Math.Abs(area - 800 * 600) < 1, "Fragments must reassemble without holes or overlapping source area.");
        for (var y = 0; y < 4; y++)
        for (var x = 0; x < 3; x++)
        {
            var left = shards[y * 4 + x];
            var right = shards[y * 4 + x + 1];
            Assert(left[1] == right[0] && left[2] == right[3], "Adjacent fragments must have the exact same seam.");
        }
    }

    public static void WindowPrankTearHasOneSharedIrregularSeam()
    {
        var seam = WindowPrankGeometry.CreateTearSeam(800, 600);
        Assert(seam.Length == 13 && seam[0] == new PointF(800, 300) && seam[^1] == new PointF(0, 300),
            "The two halves must join at the source image boundaries.");
        Assert(seam.Skip(1).Take(11).Any(p => p.Y != 300) && seam.All(p => p.Y > 250 && p.Y < 350),
            "The seam should look torn rather than a straight cut, while keeping the content recognizable.");
        for (var i = 1; i < seam.Length; i++)
            Assert(seam[i].X < seam[i - 1].X, "The right-side tear must progress left without self-intersection.");
        var lowGrip = WindowPrankGeometry.CreateTearSeam(800, 600, 450);
        Assert(lowGrip[0] == new PointF(800, 450), "The tear starts where the actual two-handed grip touches the right edge.");
    }

    public static void WindowPrankRestoreAcknowledgementDoesNotLookLikeTakeover()
    {
        Assert(!WindowPrankGeometry.ShouldRelinquishSnapshot(restoring: true, iconic: false, placementUnchanged: true),
            "The frame tick must not cancel its own asynchronously acknowledged restore.");
        Assert(!WindowPrankGeometry.ShouldRelinquishSnapshot(restoring: true, iconic: true, placementUnchanged: true),
            "A restore request may remain queued while its target thread catches up.");
        Assert(WindowPrankGeometry.ShouldRelinquishSnapshot(restoring: false, iconic: false, placementUnchanged: true),
            "A user restore during a held snapshot immediately ends pet ownership.");
        Assert(WindowPrankGeometry.ShouldRelinquishSnapshot(restoring: true, iconic: false, placementUnchanged: false) &&
            WindowPrankGeometry.ShouldRelinquishSnapshot(restoring: true, iconic: true, placementUnchanged: false),
            "Restoring does not authorize overwriting a user reposition, even before the queued request is acknowledged.");
    }

    public static void WindowPrankShatterUsesActualContact()
    {
        var center = new PointF(400, 300);
        var leftHit = WindowPrankGeometry.ShatterOffset(center, new PointF(0, 300), 1);
        var rightHit = WindowPrankGeometry.ShatterOffset(center, new PointF(800, 300), 1);
        Assert(leftHit.X > 0 && rightHit.X < 0 && Math.Abs(leftHit.X + rightHit.X) < 0.01,
            "The same shard must travel away from the actual contacted edge, not a fixed preset blast center.");
        Assert(WindowPrankGeometry.ShatterOffset(center, new PointF(800, 300), 0) == PointF.Empty,
            "Reassembling to zero progress restores every source pixel's original position.");
        Assert(WindowPrankGeometry.ClampImpact(new PointF(-100, 700), new RectangleF(0, 0, 800, 600)) == new PointF(0, 600),
            "Off-frame contact rounding must remain on the original window outline.");
    }

    public static void WindowPrankTearHandsStayAttachedAndReassemble()
    {
        var grip = new PointF(800, 300);
        var upper = new PointF(815, 240);
        var lower = new PointF(810, 370);
        var stretch = new WindowPrankTearStretch(1, 0);
        var top = WindowPrankRenderGeometry.TearHalfPose(grip, upper, stretch, 1, false, true);
        var bottom = WindowPrankRenderGeometry.TearHalfPose(grip, lower, stretch, 1, false, false);
        Assert(top.Grip == upper && bottom.Grip == lower,
            "Once the grip is engaged, each source seam anchor must coincide with its own measured hand.");
        Assert(top.Grip.Y < grip.Y && bottom.Grip.Y > grip.Y, "The upper and lower pieces must follow the real upward/downward pull.");
        var halfway = WindowPrankRenderGeometry.TearHalfPose(grip, upper, stretch, 0.5, true, true);
        Assert(Math.Abs(halfway.Grip.X - (grip.X + upper.X) * 0.5) < 0.001 &&
            Math.Abs(halfway.Grip.Y - (grip.Y + upper.Y) * 0.5) < 0.001,
            "Reassembly must bring the frozen hand-directed pose back continuously.");
        Assert(WindowPrankRenderGeometry.TearHalfPose(grip, upper, stretch, 0, true, true).Grip == grip,
            "The final reassembled image must return to its original rectangle, regardless of the hand's released position.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
