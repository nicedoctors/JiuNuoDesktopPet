namespace SoftMochiPet.Core;

public sealed class DeletionBurstTracker
{
    private readonly Queue<DateTimeOffset> _recent = new();

    public int Count => _recent.Count;

    public bool Register(DateTimeOffset detectedAt, TimeSpan? window = null)
    {
        var activeWindow = window ?? TimeSpan.FromSeconds(6);
        while (_recent.Count > 0 && detectedAt - _recent.Peek() > activeWindow)
        {
            _recent.Dequeue();
        }

        _recent.Enqueue(detectedAt);
        return _recent.Count >= 2;
    }
}
