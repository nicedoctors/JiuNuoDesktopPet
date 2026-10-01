namespace SoftMochiPet.Core;

public sealed record QueuedMealVisual<TMeal, TVisual>(TMeal Meal, TVisual Visual);

/// <summary>
/// Keeps one already-created visual per pending meal. Creating the visual at
/// enqueue time is the key illusion: every deleted desktop item remains where
/// it was until the pet reaches that specific queue entry.
/// </summary>
public sealed class MealVisualQueue<TMeal, TVisual>
{
    private readonly Queue<QueuedMealVisual<TMeal, TVisual>> _entries = new();

    public int Count => _entries.Count;

    public IReadOnlyList<QueuedMealVisual<TMeal, TVisual>> Snapshot => _entries.ToArray();

    public QueuedMealVisual<TMeal, TVisual> Enqueue(
        TMeal meal,
        Func<TMeal, TVisual> createVisibleVisual)
    {
        var entry = new QueuedMealVisual<TMeal, TVisual>(meal, createVisibleVisual(meal));
        _entries.Enqueue(entry);
        return entry;
    }

    /// <summary>
    /// Restores an interrupted active meal without recreating or losing its
    /// already-visible desktop ghost.
    /// </summary>
    public QueuedMealVisual<TMeal, TVisual> EnqueueFirst(TMeal meal, TVisual visual)
    {
        var entry = new QueuedMealVisual<TMeal, TVisual>(meal, visual);
        if (_entries.Count == 0)
        {
            _entries.Enqueue(entry);
            return entry;
        }

        var pending = _entries.ToArray();
        _entries.Clear();
        _entries.Enqueue(entry);
        foreach (var existing in pending)
        {
            _entries.Enqueue(existing);
        }

        return entry;
    }

    public bool TryDequeue(out QueuedMealVisual<TMeal, TVisual>? entry)
    {
        return _entries.TryDequeue(out entry);
    }

    public void Clear(Action<TVisual> closeVisual)
    {
        while (_entries.TryDequeue(out var entry))
        {
            closeVisual(entry.Visual);
        }
    }
}
