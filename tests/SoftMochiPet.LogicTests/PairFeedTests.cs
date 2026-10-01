using System.Windows;
using System.Text.Json;
using SoftMochiPet.Core;
using Point = System.Windows.Point;

internal static class PairFeedTests
{
    public static void FlightHasNoRangeOrSameHeightLimit()
    {
        foreach (var scale in new[] { 1d, 1.25, 1.5, 2 })
            foreach (var (start, end) in new[]{(new Point(400,900),new Point(200,900)),
            (new Point(1900,1050),new Point(50,100)),(new Point(-1800,950),new Point(2400,250)),
            (new Point(300,900),new Point(300,-800)),(new Point(5000,900),new Point(-5000,800))})
            {
                var flight = PairFeedFlight.Create(start, end, scale, -1200);
                Assert(flight.At(0) == start && flight.At(1) == end, "Food must start in the throwing hand and finish at the receiving mouth.");
                Assert(flight.Duration is >= .7 and <= 4 && flight.CatchStart > PairFeedFlight.ThrowAt,
                    "Even the closest throw must leave time to ready the mouth.");
                Assert(Math.Abs(flight.Arrival - flight.CatchStart - .525) < 1e-8, "The open-mouth frame must coincide with arrival at any distance.");
                Assert(flight.Finished >= flight.CatchStart + 1.75, "Chewing must finish before the interaction releases ownership.");
                for (var t = 0d; t <= 1; t += .01)
                {
                    var point = flight.At(t);
                    Assert(double.IsFinite(point.X) && point.Y >= -1200, "The arc must remain finite and below the display ceiling.");
                }
            }
    }

    public static void LongThrowsAreReadableAndMirrorSymmetrically()
    {
        var near = PairFeedFlight.Create(new(0, 800), new(200, 800), 1, 0);
        var far = PairFeedFlight.Create(new(0, 800), new(3000, 500), 1, 0);
        var mirror = PairFeedFlight.Create(new(0, 800), new(-3000, 500), 1, 0);
        Assert(far.Duration > near.Duration, "Long throws must not reuse the short-flight duration.");
        for (var t = 0d; t <= 1; t += .01)
            Assert(Math.Abs(far.At(t).X + mirror.At(t).X) < 1e-8 && Math.Abs(far.At(t).Y - mirror.At(t).Y) < 1e-8,
                "Either pet ordering must use the same throw.");
    }

    public static void WritePreviewTrace(string directory)
    {
        Directory.CreateDirectory(directory);
        const double size = 200;
        foreach (var (name, nx, ny, fx, fy) in new[] { ("long", 80d, 320d, 1000d, 320d), ("reverse-height", 980d, 130d, 100d, 330d) })
        {
            var facing = nx < fx ? 1 : -1;
            Point Anchor(PetCharacterProfile profile, int frame, double x, double y, double left, double top)
            {
                var a = PairSpriteGeometry.Map(profile, "pair_feed", frame, x, y);
                return new(left + (facing == 1 ? a.X : 512 - a.X) * size / 512, top + a.Y * size / 512);
            }
            var n = PetCharacterProfile.Nuonuo;
            var f = PetCharacterProfile.FeibiJiubi;
            var hat = Anchor(f, 6, 256, 388, fx, fy);
            var hand = Anchor(f, 9, 112, 255, fx, fy);
            var release = Anchor(f, 12, 95, 310, fx, fy);
            var mouth = Anchor(n, 9, 290, 240, nx, ny);
            var flight = PairFeedFlight.Create(release, mouth, 1, 90);
            var na = new SpriteAnimator(Path.Combine(AppContext.BaseDirectory, n.RuntimeRelativeDirectory), n);
            var fa = new SpriteAnimator(Path.Combine(AppContext.BaseDirectory, f.RuntimeRelativeDirectory), f);
            static void Clip(SpriteAnimator animator, string clip, double time)
            {
                if (animator.CurrentClip != clip) animator.Play(clip);
                animator.AdvanceTo(Math.Max(0, time) + 1e-7);
            }
            static double Ease(double t) { t = Math.Clamp(t, 0, 1); return t * t * (3 - 2 * t); }
            var frames = new List<object>();
            for (var i = 0; i < Math.Ceiling((flight.Finished + .5) * 30); i++)
            {
                var t = i / 30d;
                var folder = t < 1.76 ? "hat_open" : t < 4.56 ? "pair_feed" : "hat_wear";
                Clip(fa, folder, t < 1.76 ? t : t < 4.56 ? t - 1.76 : t - 4.56);
                Clip(na, t < flight.ReadyStart ? "pair_feed_wait" : t < flight.CatchStart ? "pair_feed_ready" : "pair_feed_catch",
                    t < flight.ReadyStart ? t : t < flight.CatchStart ? t - flight.ReadyStart : t - flight.CatchStart);
                var p = hat;
                double scale = 0, spin = 0;
                if (t >= 2.78 && t < 3.45) { var q = Ease((t - 2.78) / .67); p = hat + (hand - hat) * q; scale = .28 + .72 * q; }
                else if (t >= 3.45 && t < PairFeedFlight.ThrowAt) { var q = Ease((t - 3.45) / (PairFeedFlight.ThrowAt - 3.45)); p = hand + (release - hand) * q; scale = 1; }
                else if (t >= PairFeedFlight.ThrowAt && t < flight.Arrival)
                { var q = (t - PairFeedFlight.ThrowAt) / flight.Duration; p = flight.At(q); scale = 1 - .8 * Ease((q - .78) / .22); spin = 360 * q * Math.Sign(mouth.X - release.X); }
                frames.Add(new
                {
                    t,
                    nFrame = na.CurrentFrameIndex,
                    fFrame = fa.CurrentFrameIndex,
                    fFolder = folder,
                    x = p.X,
                    y = p.Y,
                    scale,
                    spin
                });
            }
            File.WriteAllText(Path.Combine(directory, name + ".json"), JsonSerializer.Serialize(new { size, nx, ny, fx, fy, facing, arrival = flight.Arrival, frames }));
        }
    }
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
