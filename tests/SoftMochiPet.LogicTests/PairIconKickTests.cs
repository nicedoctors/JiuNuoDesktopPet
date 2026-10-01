using System.Drawing;
using SoftMochiPet.Core;

internal static class PairIconKickTests
{
    public static void LeftEdgeIconReboundsIntoVisibleMouth()
    {
        var area = new Rectangle(0, 0, 1920, 1080);
        var plan = MakePlan(area, new Point(35, 100));
        Assert(plan is { Rebound: true } && plan.Stage.X >= 35 && plan.Stage.X - 35 <= 16 &&
            plan.Stage.Y >= 100 && plan.Mouth.X > plan.Stage.X &&
            plan.NuonuoTopLeft.X >= area.Left && plan.FeibiTopLeft.X >= area.Left - 8,
            "A first-column desktop icon needs an on-screen rebound shot, not a skipped pair meal.");
    }

    public static void CentralAndRightEdgeIconsTakeDirectShots()
    {
        var area = new Rectangle(-1920, 0, 1920, 1200);
        var direct = MakePlan(area, new Point(-1300, 100));
        Assert(direct is { Rebound: false } && direct.Mouth.X < direct.Stage.X &&
            direct.FeibiTopLeft.X > direct.NuonuoTopLeft.X,
            "A central icon should travel straight from Feibi's left-facing kick to Nuonuo.");
        var edge = MakePlan(area, new Point(-30, 100));
        Assert(edge is { Rebound: false } && edge.FeibiTopLeft.X + 220 <= area.Right - 4,
            "A right-edge icon should move into kick range after approach, with the kicker wholly on screen.");
        Assert(MakePlan(new Rectangle(0, 0, 350, 250), new Point(300, 100)) is null,
            "A work area too small for both characters must still use the solo meal.");
    }

    public static void RealCalibratedContactsFitDesktopCornersAtMultipleSizesAndDpis()
    {
        var mouth = PairSpriteGeometry.Map(PetCharacterProfile.Nuonuo, "pair_feed", 9, 290, 240);
        var kick = PairSpriteGeometry.Map(PetCharacterProfile.FeibiJiubi, "pair_icon_kick", 8, 80, 370);
        foreach (var dpi in new[] { 1.0, 1.25, 1.5 })
        foreach (var percent in new[] { 60, 100, 200 })
        {
            var width = PetSizePolicy.FromPercent(percent);
            var size = new Size((int)Math.Round(width * dpi), (int)Math.Round(width * dpi));
            Point Offset(System.Windows.Point p) => new(
                (int)Math.Round((6 + p.X / 512 * (width - 12)) * dpi),
                (int)Math.Round((6 + p.Y / 512 * (width - 12)) * dpi));
            var area = new Rectangle(-1920, -120, 1920, 1160);
            foreach (var icon in new[]
            {
                new Point(area.Left + 35, area.Top + 60), new Point(area.Right - 35, area.Top + 60),
                new Point(area.Left + 35, area.Bottom - 45), new Point(area.Right - 35, area.Bottom - 45),
            })
            {
                var plan = PairIconKickPlanner.Plan(area, icon, size, size, Offset(mouth), Offset(kick),
                    (int)Math.Round(1.12 * size.Width));
                Assert(plan is not null, $"Real corner layout must exist at {percent}% size / {dpi} DPI.");
                Assert(area.Contains(new Rectangle(plan!.NuonuoTopLeft, size)) &&
                    area.Contains(new Rectangle(plan.FeibiTopLeft, size)), "Both actors must fit inside the work area.");
                Assert(plan.Mouth == new Point(plan.NuonuoTopLeft.X + Offset(mouth).X,
                    plan.NuonuoTopLeft.Y + Offset(mouth).Y), "Flight must end at the real calibrated mouth.");
                Assert(plan.Stage == new Point(plan.FeibiTopLeft.X + Offset(kick).X,
                    plan.FeibiTopLeft.Y + Offset(kick).Y), "Contact must match the real calibrated kicking foot.");
            }
        }
    }

    private static PairIconKickLayout? MakePlan(Rectangle area, Point icon) =>
        PairIconKickPlanner.Plan(area, icon, new Size(220, 220), new Size(220, 220),
            new Point(128, 104), new Point(40, 156), 246);

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
