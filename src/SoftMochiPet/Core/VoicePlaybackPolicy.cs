namespace SoftMochiPet.Core;

public static class VoicePlaybackPolicy
{
    public static readonly TimeSpan MinimumFileRepeatInterval = TimeSpan.FromSeconds(12);

    public static bool IsCoolingDown(
        DateTimeOffset now,
        DateTimeOffset lastStartedAt,
        TimeSpan cooldown) =>
        cooldown > TimeSpan.Zero && now - lastStartedAt < cooldown;

    public static IReadOnlyList<string> GetFreshFiles(
        IEnumerable<string> availableFiles,
        IReadOnlyDictionary<string, DateTimeOffset> lastFileStarted,
        DateTimeOffset now,
        TimeSpan? minimumRepeatInterval = null)
    {
        var interval = minimumRepeatInterval ?? MinimumFileRepeatInterval;
        return availableFiles
            .Where(fileName =>
                !lastFileStarted.TryGetValue(fileName, out var lastStarted) ||
                !IsCoolingDown(now, lastStarted, interval))
            .ToArray();
    }

    public static IReadOnlyList<string> GetRotationCandidates(
        IEnumerable<string> freshFiles,
        string? previousFileForCue,
        string? previousFileOverall)
    {
        var files = freshFiles.ToArray();
        if (files.Length <= 1)
        {
            return files;
        }

        var candidates = files
            .Where(fileName => !fileName.Equals(previousFileForCue, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (candidates.Length == 0)
        {
            candidates = files;
        }

        if (candidates.Length > 1)
        {
            var withoutImmediateRepeat = candidates
                .Where(fileName => !fileName.Equals(previousFileOverall, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (withoutImmediateRepeat.Length > 0)
            {
                candidates = withoutImmediateRepeat;
            }
        }

        return candidates;
    }
}
