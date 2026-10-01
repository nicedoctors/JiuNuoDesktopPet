namespace SoftMochiPet.Core;

/// <summary>
/// A captured desktop selection is consumed exactly once per deletion event.
/// This prevents a burst of deleted files from all reusing the final click.
/// </summary>
public sealed class DeletionAnchorBuffer<T> where T : class
{
    private readonly Queue<(T Anchor, DateTimeOffset CapturedAt)> _anchors = new();

    public int Count => _anchors.Count;

    public void Clear() => _anchors.Clear();

    public void Replace(IEnumerable<T> anchors, DateTimeOffset capturedAt)
    {
        _anchors.Clear();
        foreach (var anchor in anchors)
        {
            _anchors.Enqueue((anchor, capturedAt));
        }
    }

    public bool TryTake(DateTimeOffset now, TimeSpan maximumAge, out T? anchor)
    {
        while (_anchors.TryDequeue(out var entry))
        {
            if (now - entry.CapturedAt <= maximumAge)
            {
                anchor = entry.Anchor;
                return true;
            }
        }

        anchor = default;
        return false;
    }

    public bool TryTakeMatching(
        Func<T, bool> predicate,
        DateTimeOffset now,
        TimeSpan maximumAge,
        out T? anchor)
    {
        anchor = default;
        var retained = new Queue<(T Anchor, DateTimeOffset CapturedAt)>();
        while (_anchors.TryDequeue(out var entry))
        {
            if (now - entry.CapturedAt > maximumAge)
            {
                continue;
            }

            if (anchor is null && predicate(entry.Anchor))
            {
                anchor = entry.Anchor;
                continue;
            }

            retained.Enqueue(entry);
        }

        while (retained.TryDequeue(out var entry))
        {
            _anchors.Enqueue(entry);
        }

        return anchor is not null;
    }
}
