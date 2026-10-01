using System.IO;
using System.Text.Json;

namespace SoftMochiPet.Core;

public sealed class MischiefState
{
    public double Value { get; set; }
    public double CooldownRemainingSeconds { get; set; }
}

/// <summary>Active-time playfulness; paused or offline time never queues a performance.</summary>
public sealed class MischiefController
{
    public const double FullValue = 100;
    public const double SecondsToFill = 300;
    public const double BurstCooldownSeconds = 120;
    public const double InterruptedCooldownSeconds = 90;
    public const double TargetRetrySeconds = 10;
    private readonly Random _random;
    private double _smallActionRemainingSeconds;
    private double _targetRetryRemainingSeconds;
    private double _teaseCooldown;
    private bool _enabled;
    private bool _paused;
    private bool _burstBlocked;

    public MischiefController(MischiefState? state = null, Random? random = null)
    {
        _random = random ?? Random.Shared;
        // A saved full meter resumes just below full, without an offline burst intent.
        Value = SafeClamp(state?.Value ?? 0, 0, 99);
        CooldownRemainingSeconds = SafeClamp(state?.CooldownRemainingSeconds ?? 0, 0, BurstCooldownSeconds);
        ResetSmallActionTimer();
    }

    public double Value { get; private set; }
    public bool PendingBurst { get; private set; }
    public bool IsPerforming { get; private set; }
    public double CooldownRemainingSeconds { get; private set; }
    public double SmallActionRemainingSeconds => _smallActionRemainingSeconds;
    public bool AutonomousActionDue => _enabled && !_paused && !IsPerforming &&
        CooldownRemainingSeconds <= 0 && _targetRetryRemainingSeconds <= 0 &&
        (PendingBurst && !_burstBlocked || _smallActionRemainingSeconds <= 0);

    public double? SecondsUntilNextAttempt
    {
        get
        {
            if (!_enabled || _paused || IsPerforming) return null;
            var nextIntent = _burstBlocked ? _smallActionRemainingSeconds
                : Math.Min(_smallActionRemainingSeconds, PendingBurst ? 0 : (FullValue - Value) * SecondsToFill / FullValue);
            return CooldownRemainingSeconds + Math.Max(_targetRetryRemainingSeconds, nextIntent);
        }
    }

    public void Tick(double deltaSeconds, bool enabled, bool sleeping = false,
        bool quiet = false, bool sessionLocked = false, bool burstBlocked = false)
    {
        _enabled = enabled;
        _paused = sleeping || quiet || sessionLocked;
        _burstBlocked = burstBlocked;
        if (!_enabled || _paused)
        {
            var cancelledBurst = PendingBurst;
            PendingBurst = false;
            if (_paused || cancelledBurst)
            {
                Value = Math.Min(Value, 99);
            }
            // Returning from a pause should not immediately launch an overdue small action.
            _smallActionRemainingSeconds = Math.Max(_smallActionRemainingSeconds, 30);
        }

        // Rendering/session gaps are not interactive time and must not charge the meter.
        if (!double.IsFinite(deltaSeconds) || deltaSeconds <= 0 || deltaSeconds > 5)
        {
            return;
        }

        _teaseCooldown = CountDown(_teaseCooldown, deltaSeconds);
        var activeSeconds = Math.Max(0, deltaSeconds - CooldownRemainingSeconds);
        CooldownRemainingSeconds = CountDown(CooldownRemainingSeconds, deltaSeconds);
        if (!_enabled || _paused || IsPerforming || activeSeconds <= 0)
        {
            return;
        }

        _targetRetryRemainingSeconds = CountDown(_targetRetryRemainingSeconds, activeSeconds);
        _smallActionRemainingSeconds = CountDown(_smallActionRemainingSeconds, activeSeconds);
        Value = Math.Min(FullValue, Value + activeSeconds * FullValue / SecondsToFill);
        if (FullValue - Value <= 0.00000001)
        {
            Value = FullValue;
            PendingBurst = true;
        }
    }

    public bool Tease()
    {
        if (_paused || IsPerforming || _teaseCooldown > 0 || CooldownRemainingSeconds > 0)
        {
            return false;
        }

        _teaseCooldown = 1.5;
        Value = Math.Min(FullValue, Value + 12);
        PendingBurst = _enabled && Value >= FullValue;
        return true;
    }

    public bool TryStart(bool manual, out bool large, bool allowLarge = true)
    {
        large = false;
        if (_paused || IsPerforming || (!manual && (!AutonomousActionDue ||
                !allowLarge && _smallActionRemainingSeconds > 0)))
        {
            return false;
        }

        // A deliberate menu action may preview a small prank during cooldown; a
        // large prank still waits for a charged meter and its recovery interval.
        large = allowLarge && !_burstBlocked && Value >= FullValue && CooldownRemainingSeconds <= 0;
        IsPerforming = true;
        if (large) PendingBurst = false;
        _targetRetryRemainingSeconds = 0;
        return true;
    }

    // The isolated behavior-control build selects an action explicitly. Its
    // caller still validates the session, hat ownership and real window target.
    public bool TryStartDirect(bool large)
    {
        if (_paused || IsPerforming) return false;
        IsPerforming = true;
        if (large) PendingBurst = false;
        _targetRetryRemainingSeconds = 0;
        return true;
    }

    public bool FillForDirectControl()
    {
        if (_paused || IsPerforming) return false;
        Value = FullValue;
        CooldownRemainingSeconds = 0;
        _targetRetryRemainingSeconds = 0;
        PendingBurst = _enabled;
        return true;
    }

    public void Complete(bool large)
    {
        if (!IsPerforming)
        {
            return;
        }

        IsPerforming = false;
        if (large)
        {
            Value = 0;
            CooldownRemainingSeconds = BurstCooldownSeconds;
        }
        PendingBurst = _enabled && !_paused && Value >= FullValue;
        _targetRetryRemainingSeconds = 0;
        ResetSmallActionTimer();
    }

    public void Interrupt()
    {
        IsPerforming = false;
        PendingBurst = false;
        _targetRetryRemainingSeconds = 0;
        Value = Math.Min(Value, 85);
        CooldownRemainingSeconds = Math.Max(CooldownRemainingSeconds, InterruptedCooldownSeconds);
        ResetSmallActionTimer();
    }

    public void TargetUnavailable()
    {
        IsPerforming = false;
        PendingBurst = _enabled && !_paused && Value >= FullValue;
        _targetRetryRemainingSeconds = TargetRetrySeconds;
        _smallActionRemainingSeconds = TargetRetrySeconds;
    }

    public MischiefState CaptureState() => new()
    {
        Value = Math.Min(Value, 99),
        CooldownRemainingSeconds = IsPerforming
            ? Math.Max(CooldownRemainingSeconds, InterruptedCooldownSeconds)
            : CooldownRemainingSeconds,
    };

    private void ResetSmallActionTimer() => _smallActionRemainingSeconds = 60 + _random.NextDouble() * 60;

    private static double CountDown(double remaining, double elapsed) =>
        remaining - elapsed <= 0.00000001 ? 0 : remaining - elapsed;

    private static double SafeClamp(double value, double minimum, double maximum) =>
        double.IsFinite(value) ? Math.Clamp(value, minimum, maximum) : minimum;
}

public sealed class MischiefStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public MischiefStateStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        StateDirectory = directory;
        StatePath = Path.Combine(directory, "mischief-state.json");
    }

    public string StateDirectory { get; }
    public string StatePath { get; }

    public MischiefState Load()
    {
        try
        {
            var state = File.Exists(StatePath)
                ? JsonSerializer.Deserialize<MischiefState>(File.ReadAllText(StatePath), JsonOptions)
                : null;
            return new MischiefController(state).CaptureState();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return new MischiefState();
        }
    }

    public void Save(MischiefState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        Directory.CreateDirectory(StateDirectory);
        var sanitized = new MischiefController(state).CaptureState();
        var temporaryPath = Path.Combine(StateDirectory, $".mischief-state.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(sanitized, JsonOptions));
            File.Move(temporaryPath, StatePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
