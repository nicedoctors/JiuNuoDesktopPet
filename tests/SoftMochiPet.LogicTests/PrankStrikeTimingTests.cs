using System.Windows.Media.Imaging;
using SoftMochiPet.Core;

internal static class PrankStrikeTimingTests
{
    private static readonly string[] ExpectedClips = ["kick", "punch", "charge", "bare_kick", "bare_tear"];
    private static readonly int[] RefreshRates = [30, 60, 144];

    public static void DefinitionsCoverEveryStrikeWithPositivePhases()
    {
        Assert(PrankStrikeTiming.All.Count == ExpectedClips.Length &&
            PrankStrikeTiming.All.Select(timing => timing.Clip).ToHashSet(StringComparer.Ordinal).SetEquals(ExpectedClips),
            "Strike timing must cover exactly the five production attack clips without duplicates.");
        foreach (var timing in PrankStrikeTiming.All)
        {
            var phases = PhaseDurations(timing);
            Assert(phases.All(seconds => double.IsFinite(seconds) && seconds > 0),
                $"Every attack phase needs a positive finite duration: {timing.Clip}.");
            Assert(timing.WindupFrame is > 0 and < 8,
                $"Windup must finish before the measured frame-eight contact: {timing.Clip}.");
            Close(timing.ContactSeconds, timing.WindupSeconds + timing.StrikeSeconds,
                $"Contact timing must follow windup and strike: {timing.Clip}.");
            Close(timing.DurationSeconds, phases.Sum(),
                $"The complete attack includes all five phases: {timing.Clip}.");
            Assert(PrankStrikeTiming.Find(timing.Clip) == timing &&
                PrankStrikeTiming.Find(timing.Clip.ToUpperInvariant()) == timing,
                $"Strike timing lookup must match the animator's case-insensitive clip names: {timing.Clip}.");
            var definition = MischiefAnimationCatalog.Find(timing.Clip);
            Assert(definition is { Loop: false } && definition.Markers.Any(marker =>
                    marker.FrameIndex == 8 && marker.Name == (timing.Clip == "bare_tear" ? "tear" : "contact")),
                $"Retiming must preserve the existing production clip and contact marker: {timing.Clip}.");
        }
        Assert(PrankStrikeTiming.Find("idle") is null && PrankStrikeTiming.Find("hat_store") is null &&
            PrankStrikeTiming.Find("missing_clip") is null,
            "Daily and hat-storage clips must not accidentally acquire attack timing.");

        foreach (var timing in PrankStrikeTiming.All)
        {
            Assert(timing.WindupSeconds >= timing.StrikeSeconds * 3 &&
                timing.FollowThroughSeconds >= timing.StrikeSeconds * 2,
                $"Anticipation and visible follow-through must contrast with a fast attack, not uniform slow motion: {timing.Clip}.");
            Assert(timing.ImpactPauseSeconds is >= 0.1 and <= 0.18,
                $"Contact needs a readable but short local stop: {timing.Clip}.");
        }
        var heavyKick = PrankStrikeTiming.Find("bare_kick")!;
        var tear = PrankStrikeTiming.Find("bare_tear")!;
        Assert(heavyKick.DurationSeconds >= 2.5 && tear.DurationSeconds >= 3.7 &&
            heavyKick.StrikeSeconds <= 0.16 && tear.StrikeSeconds == 0.3,
            "Large attacks need dramatic staging; the two-handed tear keeps a readable pulling stroke.");
        Assert(heavyKick.FollowThroughSeconds >= 0.55 && tear.FollowThroughSeconds >= 0.8 &&
            heavyKick.RecoverySeconds >= 0.65 && tear.RecoverySeconds >= 0.65,
            "The fractured window and stretched tear need time to unfold before the native release poses.");
        Close(heavyKick.SourceProgress(heavyKick.WindupSeconds * 0.54), 5 / 16d,
            "The small kick feint starts at its real native frame.");
        Close(heavyKick.SourceProgress(heavyKick.WindupSeconds * 0.605), 6 / 16d,
            "The light feint must pass quickly into the coiled heavy-kick pose.");
        Assert(heavyKick.WindupSeconds * 0.395 >= 0.35 && heavyKick.WindupSeconds * 0.065 <= 0.08,
            "Heavy kick tension belongs in the coiled frame, not the preliminary light kick.");
        Close(tear.SourceProgress(0.45), 3 / 16d,
            "The hands must grab before the long two-hand strain, preserving the frame-three anchor.");
        Close(tear.WindupSeconds - 0.45, 1.3,
            "The held grip has a full 1.3 seconds of tension before the frame-eight tear starts.");
        Close(tear.DurationSeconds, 3.73, "The complete tear keeps its dedicated near-four-second staging.");
        var tearPauseEnd = tear.ContactSeconds + tear.ImpactPauseSeconds;
        Close(tear.SourceProgress(tearPauseEnd + 0.2), 9 / 16d,
            "The full tear reaches native frame nine before the release.");
        Assert(tear.SourceProgress(tearPauseEnd + 0.79) is >= 9 / 16d and < 10 / 16d,
            "The full frame-nine tear must stay readable for 0.6 seconds before the hands release.");
    }

    public static void SystemWaitConsumesPauseAndReactionSettlesOnce()
    {
        foreach (var timing in PrankStrikeTiming.All)
        {
            Close(timing.ConsumeImpactPause(timing.ContactSeconds, 10), timing.ContactSeconds + timing.ImpactPauseSeconds,
                "Native minimize wait must consume, but never advance past, the contact pause.");
            Close(timing.ConsumeImpactPause(timing.ContactSeconds, timing.ImpactPauseSeconds / 2),
                timing.ContactSeconds + timing.ImpactPauseSeconds / 2,
                "A quick native acknowledgement must leave only the unused pause.");
            Close(timing.PoseWeight(0), 0, "Anticipation must start at the staging point.");
            Close(timing.PoseWeight(0.5), 1, "Contact must reach the measured anchor exactly.");
            Close(timing.PoseWeight(1), 0, "Reaction must settle back at the staging point.");
            var positions = Enumerable.Range(0, 1001).Select(index => timing.PoseWeight(index / 1000d)).ToArray();
            Assert(positions.All(value => double.IsFinite(value) && value is >= -0.17 and <= 1.000001),
                "The one-shot reaction must stay bounded without screen shaking.");
            var afterContact = positions.Skip(501).ToArray();
            Assert(afterContact.Zip(afterContact.Skip(1)).Count(pair => pair.First >= 0 && pair.Second < 0) <= 1,
                "Recovery must not repeatedly cross the staging point.");
        }
    }

    public static void ProgressIsContinuousMonotonicAndHoldsExactContact()
    {
        foreach (var timing in PrankStrikeTiming.All)
        {
            var pauseEnd = timing.ContactSeconds + timing.ImpactPauseSeconds;
            var followEnd = pauseEnd + timing.FollowThroughSeconds;
            Close(timing.SourceProgress(-1), 0, $"Negative time must stay at the first frame: {timing.Clip}.");
            Close(timing.SourceProgress(0), 0, $"The attack starts at zero progress: {timing.Clip}.");
            Close(timing.SourceProgress(timing.WindupSeconds), timing.WindupFrame / 16d,
                $"Windup must reach its exact source-frame boundary: {timing.Clip}.");
            Close(timing.SourceProgress(timing.ContactSeconds), 8 / 16d,
                $"Contact must coincide with native frame eight: {timing.Clip}.");
            Close(timing.SourceProgress(followEnd), (timing.Clip == "bare_tear" ? 10 : 11) / 16d,
                $"Follow-through must finish at the clip's native release frame: {timing.Clip}.");
            Close(timing.SourceProgress(timing.DurationSeconds), 1,
                $"The exact endpoint must complete the source clip: {timing.Clip}.");
            Close(timing.SourceProgress(timing.DurationSeconds + 1), 1,
                $"Time beyond the attack must remain completed: {timing.Clip}.");

            for (var sample = 0; sample <= 64; sample++)
                Close(timing.SourceProgress(timing.ContactSeconds + timing.ImpactPauseSeconds * sample / 64), 0.5,
                    $"Impact pause must hold contact without advancing or rewinding: {timing.Clip}, sample {sample}.");

            var previous = 0d;
            for (var sample = 0; sample <= 4096; sample++)
            {
                var progress = timing.SourceProgress(timing.DurationSeconds * sample / 4096);
                Assert(double.IsFinite(progress) && progress is >= 0 and <= 1 && progress + 1e-12 >= previous,
                    $"Source progress must remain bounded and monotonic throughout every phase: {timing.Clip}, sample {sample}.");
                previous = progress;
            }

            foreach (var boundary in new[] { timing.WindupSeconds, timing.ContactSeconds, pauseEnd, followEnd })
            {
                var epsilon = PhaseDurations(timing).Min() * 1e-7;
                var left = timing.SourceProgress(boundary - epsilon);
                var right = timing.SourceProgress(boundary + epsilon);
                Assert(right + 1e-12 >= left && Math.Abs(right - left) < 1e-5,
                    $"The phase boundary must not jump in either direction: {timing.Clip}, time {boundary}.");
            }

            var internalWindupBoundaries = timing.Clip switch
            {
                "bare_kick" => new[] { 0.28, 0.54, 0.605 },
                "bare_tear" => new[] { 0.45 / 1.75 },
                _ => Array.Empty<double>(),
            };
            foreach (var fraction in internalWindupBoundaries)
            {
                var time = timing.WindupSeconds * fraction;
                var left = timing.SourceProgress(time - 1e-9);
                var right = timing.SourceProgress(time + 1e-9);
                Assert(right >= left && right - left < 1e-6,
                    $"The measured heavy-attack windup must not jump or rewind between native poses: {timing.Clip}.");
            }
            if (timing.Clip == "bare_tear")
            {
                var fullTearTime = pauseEnd + 0.2;
                Assert(timing.SourceProgress(fullTearTime + 1e-9) >= timing.SourceProgress(fullTearTime - 1e-9) &&
                    timing.SourceProgress(fullTearTime + 1e-9) - timing.SourceProgress(fullTearTime - 1e-9) < 1e-6,
                    "The full frame-nine tear must not jump at the beginning of its readable hold.");
            }
        }
    }

    public static void MotionDeltaExcludesOnlyImpactPauseAcrossPartitions()
    {
        foreach (var timing in PrankStrikeTiming.All)
        {
            var pauseStart = timing.ContactSeconds;
            var pauseEnd = pauseStart + timing.ImpactPauseSeconds;
            var expectedTotal = timing.DurationSeconds - timing.ImpactPauseSeconds;
            Close(timing.MotionDelta(0, timing.DurationSeconds), expectedTotal,
                $"A large update must exclude the entire impact pause exactly once: {timing.Clip}.");
            Close(timing.MotionDelta(pauseStart, pauseEnd), 0,
                $"The window must not move during impact pause: {timing.Clip}.");
            Close(timing.MotionDelta(pauseStart + timing.ImpactPauseSeconds * 0.2,
                pauseStart + timing.ImpactPauseSeconds * 0.8), 0,
                $"A slice wholly within impact pause has no motion time: {timing.Clip}.");
            Close(timing.MotionDelta(pauseStart, pauseStart), 0,
                $"A repeated instant has no motion time: {timing.Clip}.");
            Assert(timing.MotionDelta(pauseEnd, pauseStart) >= 0,
                $"Reversed sample order must never return negative motion time: {timing.Clip}.");

            var partitions = RefreshRates.Select(rate => SampleTimes(timing.DurationSeconds, rate)).ToList();
            partitions.Add([0, timing.WindupSeconds, pauseStart, pauseEnd,
                pauseEnd + timing.FollowThroughSeconds, timing.DurationSeconds]);
            partitions.Add([0, pauseStart * 0.75, pauseStart + timing.ImpactPauseSeconds * 0.25,
                pauseEnd - timing.ImpactPauseSeconds * 0.25, pauseEnd + timing.RecoverySeconds * 0.25,
                timing.DurationSeconds]);
            foreach (var times in partitions)
            {
                var total = 0d;
                for (var index = 1; index < times.Length; index++)
                {
                    var before = times[index - 1];
                    var after = times[index];
                    var overlap = Math.Max(0, Math.Min(after, pauseEnd) - Math.Max(before, pauseStart));
                    var delta = timing.MotionDelta(before, after);
                    Assert(double.IsFinite(delta) && delta >= 0,
                        $"Every motion slice must be finite and nonnegative: {timing.Clip}.");
                    Close(delta, after - before - overlap,
                        $"Only the slice's overlap with impact pause may be removed: {timing.Clip}.");
                    total += delta;
                }
                Close(total, expectedTotal,
                    $"Partitioning updates must not lose or duplicate window motion time: {timing.Clip}.");
            }
        }
    }

    public static void ProductionAnimatorMarkersCompleteOnceAcrossRefreshRates()
    {
        var character = PetCharacterProfile.FeibiJiubi;
        var directory = Path.Combine(AppContext.BaseDirectory, character.RuntimeRelativeDirectory);
        Assert(SpriteAnimator.HasCompleteAssets(directory, character),
            "Retiming tests require the complete real Feibi production package; missing assets cannot be skipped.");
        var animator = new SpriteAnimator(directory, character);
        foreach (var timing in PrankStrikeTiming.All)
        {
            var definition = MischiefAnimationCatalog.Find(timing.Clip)!;
            foreach (var rate in RefreshRates)
            {
                var markerCounts = new Dictionary<string, int>(StringComparer.Ordinal);
                var markerOrder = new List<string>();
                var finishedCount = 0;
                var visitedFrames = new HashSet<int>();
                var previousFrame = -1;
                void OnFrame(BitmapSource frame)
                {
                    Assert(animator.CurrentFrameIndex >= previousFrame,
                        $"Retimed frames must never rewind: {timing.Clip}, {rate} Hz.");
                    previousFrame = animator.CurrentFrameIndex;
                    visitedFrames.Add(animator.CurrentFrameIndex);
                    ReadPixels(frame, timing.Clip);
                }
                void OnMarker(string clip, string marker)
                {
                    Assert(clip == timing.Clip,
                        $"Retiming must not emit a marker from another clip: {timing.Clip}, {rate} Hz.");
                    markerCounts[marker] = markerCounts.GetValueOrDefault(marker) + 1;
                    markerOrder.Add(marker);
                }
                void OnFinished(string clip)
                {
                    Assert(clip == timing.Clip,
                        $"The finished event must identify this exact attack: {timing.Clip}, {rate} Hz.");
                    finishedCount++;
                }
                animator.FrameChanged += OnFrame;
                animator.MarkerReached += OnMarker;
                animator.AnimationFinished += OnFinished;
                try
                {
                    animator.Play(timing.Clip);
                    foreach (var seconds in SampleTimes(timing.DurationSeconds, rate))
                    {
                        var progress = timing.SourceProgress(seconds);
                        animator.AdvanceTo(progress * animator.DurationSeconds);
                        if (seconds >= timing.ContactSeconds && seconds <= timing.ContactSeconds + timing.ImpactPauseSeconds)
                        {
                            Assert(animator.CurrentFrameIndex == 8,
                                $"The production sprite must visibly hold frame eight during impact: {timing.Clip}, {rate} Hz.");
                            var impact = timing.Clip == "bare_tear" ? "tear" : "contact";
                            Assert(markerCounts.GetValueOrDefault(impact) == 1,
                                $"The exact contact frame must emit one impact marker even while held: {timing.Clip}, {rate} Hz.");
                        }
                    }
                    Assert(animator.IsCompleted && finishedCount == 1 && animator.CurrentFrameIndex == 15,
                        $"The exact retimed endpoint must finish once on the last production frame: {timing.Clip}, {rate} Hz.");
                    Assert(visitedFrames.SetEquals(Enumerable.Range(0, 16)),
                        $"All native frames must remain decodable through retiming: {timing.Clip}, {rate} Hz.");
                    Assert(markerCounts.Count == definition.Markers.Count && definition.Markers.All(marker =>
                            markerCounts.GetValueOrDefault(marker.Name) == 1) &&
                        markerOrder.SequenceEqual(definition.Markers.OrderBy(marker => marker.FrameIndex).Select(marker => marker.Name)),
                        $"Every contact/grab/tear marker must be emitted once in source-frame order: {timing.Clip}, {rate} Hz.");
                    animator.AdvanceTo(animator.DurationSeconds);
                    animator.Tick(1);
                    Assert(finishedCount == 1 && markerCounts.Values.All(count => count == 1),
                        $"Repeated completion updates must not replay markers or completion: {timing.Clip}, {rate} Hz.");
                }
                finally
                {
                    animator.FrameChanged -= OnFrame;
                    animator.MarkerReached -= OnMarker;
                    animator.AnimationFinished -= OnFinished;
                }
            }
        }
    }

    private static double[] PhaseDurations(PrankStrikeTiming timing) =>
        [timing.WindupSeconds, timing.StrikeSeconds, timing.ImpactPauseSeconds, timing.FollowThroughSeconds, timing.RecoverySeconds];

    private static double[] SampleTimes(double duration, int rate) => Enumerable.Range(0, (int)Math.Ceiling(duration * rate))
        .Select(frame => frame / (double)rate).Append(duration).ToArray();

    private static void ReadPixels(BitmapSource frame, string clip)
    {
        Assert(frame.PixelWidth == 384 && frame.PixelHeight == 384,
            $"Retiming must use the native 384 × 384 production sprites: {clip}.");
        var stride = checked((frame.PixelWidth * frame.Format.BitsPerPixel + 7) / 8);
        var pixels = new byte[checked(stride * frame.PixelHeight)];
        frame.CopyPixels(pixels, stride, 0);
        Assert(pixels.Any(value => value != 0), $"The production frame must contain real decodable art: {clip}.");
    }

    private static void Close(double actual, double expected, string message) =>
        Assert(double.IsFinite(actual) && Math.Abs(actual - expected) <= 1e-9, $"{message} Expected {expected:R}, actual {actual:R}.");

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
