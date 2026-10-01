using System.IO;
using System.Windows.Media;
using System.Windows.Threading;
using SoftMochiPet.Core;

namespace SoftMochiPet.Services;

public sealed class VoicePlaybackService : IDisposable
{
    private static readonly TimeSpan DefaultCueCooldown = TimeSpan.FromSeconds(1.8);

    private readonly string _voiceDirectory;
    private readonly PetCharacterProfile _character;
    private readonly MediaPlayer _player = new();
    private readonly Dictionary<string, DateTimeOffset> _lastCueStarted = [];
    private readonly Dictionary<string, string> _lastFileByCue = [];
    private readonly Dictionary<string, DateTimeOffset> _lastFileStarted =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Random _random = new();
    private DateTimeOffset _lastStartedAt = DateTimeOffset.MinValue;
    private VoicePriority _currentPriority;
    private string? _lastFileName;
    private string? _currentFileName;
    private bool _isPlaying;
    private bool _disposed;
    private MediaPlayer? _burstPlayer;
    private Task<bool>? _burstTask;
    private Action? _stopBurst;

    public VoicePlaybackService(string voiceDirectory, bool enabled, double volume, PetCharacterProfile? character = null)
    {
        _voiceDirectory = voiceDirectory;
        _character = character ?? PetCharacterProfile.Nuonuo;
        IsEnabled = enabled && _character.HasVoice;
        Volume = Math.Clamp(volume, 0, 1);
        _player.Volume = Volume;
        _player.MediaEnded += Player_MediaEnded;
        _player.MediaFailed += Player_MediaFailed;
    }

    public bool IsEnabled { get; private set; }
    public double Volume { get; private set; }

    public void SetEnabled(bool enabled)
    {
        IsEnabled = enabled && _character.HasVoice;
        if (!IsEnabled)
        {
            Stop();
        }

        DiagnosticsLog.WriteEvent("VoiceEnabledChanged", ("Enabled", IsEnabled), ("Character", _character.Id));
    }

    public void SetVolume(double volume)
    {
        Volume = Math.Clamp(volume, 0, 1);
        _player.Volume = PlaybackVolume(_currentFileName);
        if (_burstPlayer is { } burst)
        {
            burst.Volume = PlaybackVolume(FeibiVoiceCatalog.BurstCallFileName);
            if (Volume <= 0) _stopBurst?.Invoke();
        }
        DiagnosticsLog.WriteEvent("VoiceVolumeChanged", ("Volume", Volume));
    }

    public bool Play(
        VoiceCue cue,
        VoicePriority priority = VoicePriority.Normal,
        TimeSpan? cueCooldown = null,
        TimeSpan? minimumIntervalSinceAnyVoice = null)
        => PlayFiles(cue.ToString(), VoiceCueCatalog.GetFileNames(cue, _character), priority,
            cueCooldown, minimumIntervalSinceAnyVoice);

    public bool PlayMischief(MischiefCue cue) => _character.UsesMischief &&
        PlayFiles($"Mischief.{cue}", FeibiVoiceCatalog.GetFileNames(cue), VoicePriority.Normal,
            TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(3.5));

    public async Task<bool> PlayBurstCallAsync(CancellationToken cancellationToken = default)
    {
        _player.Dispatcher.VerifyAccess();
        cancellationToken.ThrowIfCancellationRequested();
        if (_burstTask is { } pending) return await pending.WaitAsync(cancellationToken);
        if (_disposed || !_character.UsesMischief || !IsEnabled || Volume <= 0) return false;
        var path = Path.Combine(_voiceDirectory, FeibiVoiceCatalog.BurstCallFileName);
        if (!File.Exists(path))
        {
            DiagnosticsLog.WriteEvent("VoiceBurstSkipped", ("Reason", "missing_recording"));
            return false;
        }

        Stop();
        // This one-shot player cannot receive a completion event left over from
        // ordinary chatter. The action gate opens successfully only on MediaEnded.
        var player = new MediaPlayer { Volume = PlaybackVolume(FeibiVoiceCatalog.BurstCallFileName) };
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var watchdog = new DispatcherTimer(DispatcherPriority.Background, player.Dispatcher)
            { Interval = TimeSpan.FromSeconds(8) };
        var started = DateTimeOffset.UtcNow;
        var finished = false;
        void Finish(bool completed, string reason, bool canceled = false)
        {
            if (finished) return;
            finished = true;
            watchdog.Stop();
            if (ReferenceEquals(_burstPlayer, player))
            {
                _burstPlayer = null;
                _burstTask = null;
                _stopBurst = null;
            }
            try { player.Stop(); player.Close(); }
            catch (Exception exception) { DiagnosticsLog.Write("Burst voice cleanup failed.", exception); }
            DiagnosticsLog.WriteEvent("VoiceBurstFinished", ("File", FeibiVoiceCatalog.BurstCallFileName),
                ("Completed", completed), ("Reason", reason),
                ("ElapsedMs", (DateTimeOffset.UtcNow - started).TotalMilliseconds));
            if (canceled) completion.TrySetCanceled(cancellationToken);
            else completion.TrySetResult(completed);
        }
        EventHandler opened = (_, _) =>
        {
            if (finished) return;
            // A missing media event is a playback failure, not permission to
            // leave a full-meter action waiting indefinitely.
            if (player.NaturalDuration.HasTimeSpan)
                watchdog.Interval = TimeSpan.FromSeconds(Math.Clamp(player.NaturalDuration.TimeSpan.TotalSeconds + 3, 4, 30));
            watchdog.Stop();
            watchdog.Start();
        };
        EventHandler ended = (_, _) => Finish(true, "media_ended");
        EventHandler<ExceptionEventArgs> failed = (_, args) =>
        {
            DiagnosticsLog.Write("Burst voice media failed.", args.ErrorException);
            Finish(false, "media_failed");
        };
        EventHandler timedOut = (_, _) => Finish(false, "playback_timeout");
        _burstPlayer = player;
        _burstTask = completion.Task;
        _stopBurst = () => Finish(false, "stopped_or_muted");
        player.MediaOpened += opened;
        player.MediaEnded += ended;
        player.MediaFailed += failed;
        watchdog.Tick += timedOut;
        using var cancellation = cancellationToken.Register(() => player.Dispatcher.BeginInvoke(
            () => Finish(false, "canceled", canceled: true)));
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            _lastFileName = FeibiVoiceCatalog.BurstCallFileName;
            _lastStartedAt = started;
            _lastCueStarted["Mischief.Burst"] = started;
            _lastFileByCue["Mischief.Burst"] = FeibiVoiceCatalog.BurstCallFileName;
            _lastFileStarted[FeibiVoiceCatalog.BurstCallFileName] = started;
            watchdog.Start();
            player.Open(new Uri(path, UriKind.Absolute));
            player.Play();
            DiagnosticsLog.WriteEvent("VoiceBurstStarted", ("File", FeibiVoiceCatalog.BurstCallFileName),
                ("Character", _character.Id), ("Volume", Volume), ("EffectiveVolume", player.Volume));
        }
        catch (OperationCanceledException) { Finish(false, "canceled", canceled: true); }
        catch (Exception exception)
        {
            DiagnosticsLog.Write("Burst voice playback failed.", exception);
            Finish(false, "playback_failed");
        }
        try { return await completion.Task; }
        finally
        {
            watchdog.Stop();
            watchdog.Tick -= timedOut;
            player.MediaOpened -= opened;
            player.MediaEnded -= ended;
            player.MediaFailed -= failed;
        }
    }

    private bool PlayFiles(string cue, IReadOnlyList<string> requestedFiles, VoicePriority priority,
        TimeSpan? cueCooldown, TimeSpan? minimumIntervalSinceAnyVoice)
    {
        if (_disposed || !IsEnabled)
        {
            return false;
        }
        if (_burstPlayer is not null) return false;

        var now = DateTimeOffset.UtcNow;
        var cooldown = cueCooldown ?? DefaultCueCooldown;
        if (minimumIntervalSinceAnyVoice is { } anyVoiceInterval &&
            VoicePlaybackPolicy.IsCoolingDown(now, _lastStartedAt, anyVoiceInterval))
        {
            DiagnosticsLog.WriteEventThrottled(
                $"voice-recent-{cue}",
                TimeSpan.FromSeconds(2),
                "VoiceSkipped",
                ("Cue", cue),
                ("Reason", "RecentVoiceInSameEventChain"),
                ("CooldownMs", anyVoiceInterval.TotalMilliseconds),
                ("PreviousFile", _lastFileName));
            return false;
        }

        if (_lastCueStarted.TryGetValue(cue, out var lastCue) &&
            VoicePlaybackPolicy.IsCoolingDown(now, lastCue, cooldown))
        {
            DiagnosticsLog.WriteEventThrottled(
                $"voice-cooldown-{cue}",
                TimeSpan.FromSeconds(2),
                "VoiceSkipped",
                ("Cue", cue),
                ("Reason", "CueCooldown"),
                ("CooldownMs", cooldown.TotalMilliseconds));
            return false;
        }

        if (_isPlaying)
        {
            var canInterrupt = priority == VoicePriority.Critical ||
                priority > _currentPriority ||
                priority == _currentPriority && priority >= VoicePriority.Important;
            if (!canInterrupt)
            {
                DiagnosticsLog.WriteEventThrottled(
                    $"voice-busy-{cue}",
                    TimeSpan.FromSeconds(2),
                    "VoiceSkipped",
                    ("Cue", cue),
                    ("Reason", "HigherPriorityVoicePlaying"),
                    ("CurrentFile", _currentFileName),
                    ("CurrentPriority", _currentPriority),
                    ("RequestedPriority", priority));
                return false;
            }
        }

        var availableFiles = requestedFiles
            .Where(fileName => File.Exists(Path.Combine(_voiceDirectory, fileName)))
            .ToArray();
        if (availableFiles.Length == 0)
        {
            DiagnosticsLog.WriteEvent(
                "VoiceMissing",
                ("Cue", cue),
                ("Directory", _voiceDirectory),
                ("Expected", string.Join(';', requestedFiles)),
                ("Character", _character.Id));
            return false;
        }

        var freshFiles = VoicePlaybackPolicy.GetFreshFiles(
            availableFiles,
            _lastFileStarted,
            now);
        if (freshFiles.Count == 0)
        {
            DiagnosticsLog.WriteEventThrottled(
                $"voice-file-repeat-{cue}",
                TimeSpan.FromSeconds(2),
                "VoiceSkipped",
                ("Cue", cue),
                ("Reason", "FileRepeatCooldown"),
                ("CooldownMs", VoicePlaybackPolicy.MinimumFileRepeatInterval.TotalMilliseconds));
            return false;
        }

        _lastFileByCue.TryGetValue(cue, out var previousFileForCue);
        var candidates = VoicePlaybackPolicy.GetRotationCandidates(
            freshFiles,
            previousFileForCue,
            _lastFileName);
        var selected = candidates[_random.Next(candidates.Count)];
        var path = Path.Combine(_voiceDirectory, selected);
        try
        {
            if (_isPlaying)
            {
                _player.Stop();
            }

            _player.Open(new Uri(path, UriKind.Absolute));
            _player.Volume = PlaybackVolume(selected);
            _player.Play();
            _isPlaying = true;
            _currentPriority = priority;
            _currentFileName = selected;
            _lastFileName = selected;
            _lastStartedAt = now;
            _lastCueStarted[cue] = now;
            _lastFileByCue[cue] = selected;
            _lastFileStarted[selected] = now;
            DiagnosticsLog.WriteEvent(
                "VoicePlayed",
                ("Cue", cue),
                ("Character", _character.Id),
                ("File", selected),
                ("Priority", priority),
                ("Volume", Volume), ("EffectiveVolume", _player.Volume));
            return true;
        }
        catch (Exception exception)
        {
            _isPlaying = false;
            _currentFileName = null;
            DiagnosticsLog.Write("Voice playback failed.", exception);
            return false;
        }
    }

    private double PlaybackVolume(string? file) => VoiceLoudness.Packaged.Volume(_character.Id, file, Volume);

    public void Stop()
    {
        _stopBurst?.Invoke();
        if (!_isPlaying)
        {
            return;
        }

        _player.Stop();
        DiagnosticsLog.WriteEvent(
            "VoiceStopped",
            ("File", _currentFileName),
            ("ElapsedMs", (DateTimeOffset.UtcNow - _lastStartedAt).TotalMilliseconds));
        _isPlaying = false;
        _currentFileName = null;
    }

    private void Player_MediaEnded(object? sender, EventArgs e)
    {
        DiagnosticsLog.WriteEvent(
            "VoiceCompleted",
            ("File", _currentFileName),
            ("ElapsedMs", (DateTimeOffset.UtcNow - _lastStartedAt).TotalMilliseconds));
        _isPlaying = false;
        _currentFileName = null;
    }

    private void Player_MediaFailed(object? sender, ExceptionEventArgs e)
    {
        _isPlaying = false;
        DiagnosticsLog.WriteEvent("VoiceMediaFailed", ("Character", _character.Id), ("File", _currentFileName));
        DiagnosticsLog.Write("Voice media failed.", e.ErrorException);
        _currentFileName = null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _stopBurst?.Invoke();
        _player.MediaEnded -= Player_MediaEnded;
        _player.MediaFailed -= Player_MediaFailed;
        _player.Stop();
        _player.Close();
        _isPlaying = false;
    }
}
