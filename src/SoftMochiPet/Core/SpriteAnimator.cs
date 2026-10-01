using System.Collections.Concurrent;
using System.IO;
using System.Windows.Media.Imaging;

namespace SoftMochiPet.Core;

public sealed class SpriteAnimator
{
    private sealed record Clip(
        string Name,
        string AssetFolder,
        int FirstFrame,
        int LastFrame,
        double FrameSeconds,
        bool Loop,
        bool PingPong,
        bool StartReversed = false);

    private static readonly Dictionary<string, Clip> DefaultClips = new(StringComparer.OrdinalIgnoreCase)
    {
        ["idle"] = new("idle", "idle", 0, 15, 0.135, true, false),
        ["run"] = new("run", "run", 0, 15, 0.070, true, false),
        ["walk"] = new("walk", "walk", 0, 15, 0.110, true, false),
        ["toss"] = new("toss", "toss", 0, 7, 0.055, false, false),
        ["fall"] = new("fall", "fall", 0, 15, 0.090, true, false),
        ["land"] = new("land", "land", 0, 15, 0.060, false, false),
        ["curious"] = new("curious", "curious", 0, 15, 0.135, true, false),
        ["sleep_enter"] = new("sleep_enter", "sleep", 0, 7, 0.145, false, false),
        ["sleep"] = new("sleep", "sleep", 8, 15, 0.185, true, true),
        ["sleep_exit"] = new("sleep_exit", "sleep", 0, 7, 0.105, false, false, true),
        ["hungry"] = new("hungry", "hungry", 0, 15, 0.135, true, false),
        ["climb"] = new("climb", "climb", 0, 15, 0.082, true, false),
        ["drag"] = new("drag", "drag", 0, 15, 0.080, true, true),
        ["jump"] = new("jump", "jump", 0, 15, 0.075, false, false),
        ["slide"] = new("slide", "slide", 0, 15, 0.090, true, false),
        ["roll"] = new("roll", "roll", 0, 15, 0.085, false, false),
        ["lick"] = new("lick", "lick", 0, 15, 0.110, false, false),
        ["chomp"] = new("chomp", "chomp", 0, 15, 0.150, false, false),
        ["satisfied"] = new("satisfied", "satisfied", 0, 15, 0.095, false, false),
        ["satisfied_quick"] = new("satisfied_quick", "satisfied", 0, 9, 0.085, false, false),
        ["pair_cheek"] = new("pair_cheek", "pair_cheek", 0, 15, 0.145, false, false),
        ["pair_notice"] = new("pair_notice", "pair_notice", 0, 15, 0.18, false, false),
        ["pair_nuzzle"] = new("pair_nuzzle", "pair_nuzzle", 0, 15, 0.20, false, false),
        ["pair_ball"] = new("pair_ball", "pair_ball", 0, 15, CompanionLifePolicy.KickFrameSeconds, false, false),
        ["pair_feed"] = new("pair_feed", "pair_feed", 0, 15, 0.175, false, false),
        ["pair_feed_wait"] = new("pair_feed_wait", "pair_feed", 0, 2, 0.400, true, true),
        ["pair_feed_ready"] = new("pair_feed_ready", "pair_feed", 3, 5, 0.250, false, false),
        ["pair_feed_catch"] = new("pair_feed_catch", "pair_feed", 6, 15, 0.175, false, false),
        ["pair_icon_kick"] = new("pair_icon_kick", "pair_icon_kick", 0, 15, 0.105, false, false),
        ["pair_sleep_enter"] = new("pair_sleep_enter", "pair_sleep", 0, 15, 0.165, false, false),
        ["pair_sleep_hold"] = new("pair_sleep_hold", "pair_sleep", 10, 15, 0.32, true, true),
    };

    public static IReadOnlyList<string> RequiredAssetFolders { get; } = Array.AsReadOnly(
        DefaultClips.Values.Select(clip => clip.AssetFolder).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());

    private static readonly HashSet<string> FoodAssetFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "hungry", "lick", "chomp", "satisfied",
    };

    private static readonly IReadOnlyDictionary<string, string> HatlessAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["idle"] = "bare_idle", ["walk"] = "bare_run", ["run"] = "bare_run",
        ["drag"] = "bare_drag", ["fall"] = "bare_fall", ["toss"] = "bare_fall",
        ["land"] = "bare_recover",
    };

    private static readonly IReadOnlyList<string> FeibiAssetFolders = Array.AsReadOnly(
        RequiredAssetFolders.Where(folder => !FoodAssetFolders.Contains(folder))
            .Concat(MischiefAnimationCatalog.All.Select(clip => clip.Name)).ToArray());

    private static readonly IReadOnlyList<string> NuonuoAssetFolders = Array.AsReadOnly(
        RequiredAssetFolders.Where(folder => folder != "pair_icon_kick").ToArray());

    public static IReadOnlyList<string> RequiredAssetFoldersFor(PetCharacterProfile? profile) =>
        profile?.UsesMischief == true ? FeibiAssetFolders : NuonuoAssetFolders;

    private readonly Dictionary<string, Clip> _clips = new(DefaultClips, StringComparer.OrdinalIgnoreCase);
    private readonly string _runtimeDirectory;
    private readonly string[] _assetFolders;
    private readonly PetCharacterProfile _profile;
    private readonly ConcurrentDictionary<string, Lazy<BitmapSource[]>> _assets =
        new(StringComparer.OrdinalIgnoreCase);
    private Clip _current;
    private int _frameIndex;
    private double _elapsed;
    private bool _completed;
    private int _direction = 1;
    private string _requestedClipName = "idle";
    private double _playbackSeconds;
    private int _playGeneration;
    private readonly HashSet<string> _emittedMarkers = new(StringComparer.Ordinal);

    public SpriteAnimator(string runtimeDirectory, PetCharacterProfile? profile = null)
    {
        _runtimeDirectory = runtimeDirectory;
        profile ??= PetCharacterProfile.Nuonuo;
        _profile = profile;
        foreach (var (name, clip) in _clips.ToArray())
        {
            _clips[name] = clip with { FrameSeconds = profile.ResolveFrameSeconds(name, clip.FrameSeconds) };
        }

        if (profile.Id == PetCharacterProfile.FeibiJiubi.Id)
        {
            foreach (var key in _clips.Where(pair => FoodAssetFolders.Contains(pair.Value.AssetFolder))
                         .Select(pair => pair.Key).ToArray())
            {
                _clips.Remove(key);
            }
            foreach (var clip in MischiefAnimationCatalog.All)
            {
                _clips[clip.Name] = new Clip(clip.Name, clip.Name, 0, 15,
                    clip.FrameSeconds, clip.Loop, false);
            }
            _clips["curious"] = _clips["curious"] with
            {
                FirstFrame = 0,
                LastFrame = 7,
                Loop = true,
                PingPong = true,
            };
        }
        else
        {
            _clips.Remove("pair_icon_kick");
        }

        _assetFolders = RequiredAssetFoldersFor(profile).ToArray();

        // The idle sheet is the only artwork required for the first frame.
        // Every other behavior is warmed in the background after the window is visible.
        _ = GetFrames("idle");

        _current = _clips["idle"];
        _frameIndex = _current.FirstFrame;
    }

    public event Action<BitmapSource>? FrameChanged;
    public event Action<string>? AnimationFinished;
    public event Action<string, string>? MarkerReached;

    public BitmapSource CurrentFrame => GetCurrentFrame();
    public string CurrentClip => _requestedClipName;
    public string CurrentAssetFolder => _current.AssetFolder;
    public bool Hatless { get; private set; }
    public double DurationSeconds => (_current.LastFrame - _current.FirstFrame + 1) * _current.FrameSeconds;
    public double ElapsedSeconds => _current.Loop ? _playbackSeconds : Math.Min(_playbackSeconds, DurationSeconds);
    public double Progress => Math.Clamp(ElapsedSeconds / DurationSeconds, 0, 1);
    public bool IsCompleted => _completed;
    public int CurrentFrameIndex => _frameIndex;
    public int LoadedAssetFolderCount => _assets.Values.Count(asset => asset.IsValueCreated);
    public int TotalAssetFolderCount => _assetFolders.Length;

    public static bool HasCompleteAssets(string runtimeDirectory, PetCharacterProfile? profile = null) =>
        !string.IsNullOrWhiteSpace(runtimeDirectory) && Directory.Exists(runtimeDirectory) &&
        RequiredAssetFoldersFor(profile).All(folder => Enumerable.Range(0, 16).All(index =>
            File.Exists(Path.Combine(runtimeDirectory, folder, $"frame_{index:00}.png"))));

    public bool HasClip(string name) => _clips.ContainsKey(name);

    public void SetHatless(bool hatless)
    {
        hatless &= _profile.UsesMischief;
        if (Hatless == hatless)
        {
            return;
        }
        Hatless = hatless;
        if (HatlessAliases.ContainsKey(_requestedClipName))
        {
            Play(_requestedClipName);
        }
    }

    public void AdvanceTo(double clipSeconds)
    {
        if (double.IsFinite(clipSeconds) && clipSeconds > _playbackSeconds)
        {
            Tick(clipSeconds - _playbackSeconds);
        }
    }

    public Task PreloadRemainingAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            foreach (var folder in _assetFolders)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _ = GetFrames(folder);
            }
        }, cancellationToken);

    public void Play(string clipName, bool restart = true)
    {
        if (!restart && string.Equals(_requestedClipName, clipName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var next = _clips[clipName];
        if (Hatless && HatlessAliases.TryGetValue(clipName, out var hatlessClip))
        {
            var bare = _clips[hatlessClip];
            next = bare with
            {
                Name = clipName, FirstFrame = next.FirstFrame, LastFrame = next.LastFrame,
                Loop = next.Loop, PingPong = next.PingPong, StartReversed = next.StartReversed,
            };
        }

        // An interaction can arrive before background warm-up completes. Load
        // only its requested sheet synchronously so the first frame is ready.
        _ = GetFrames(next.AssetFolder);
        _current = next;
        _requestedClipName = clipName;
        _playGeneration++;
        _playbackSeconds = 0;
        _emittedMarkers.Clear();
        _frameIndex = next.StartReversed ? next.LastFrame : next.FirstFrame;
        _elapsed = 0;
        _completed = false;
        _direction = next.StartReversed ? -1 : 1;
        FrameChanged?.Invoke(GetCurrentFrame());
    }

    public void ShowFrame(int frameIndex)
    {
        var next = Math.Clamp(frameIndex, _current.FirstFrame, _current.LastFrame);
        if (next == _frameIndex) return;
        _frameIndex = next;
        FrameChanged?.Invoke(GetCurrentFrame());
    }

    public BitmapSource FrameAt(int frameIndex) =>
        GetFrames(_current.AssetFolder)[Math.Clamp(frameIndex, _current.FirstFrame, _current.LastFrame)];

    public void Tick(double deltaSeconds)
    {
        if (_completed || !double.IsFinite(deltaSeconds) || deltaSeconds <= 0)
        {
            return;
        }

        var generation = _playGeneration;
        _playbackSeconds += deltaSeconds;
        _elapsed += deltaSeconds;
        while (_elapsed >= _current.FrameSeconds)
        {
            _elapsed -= _current.FrameSeconds;
            var nextFrame = _frameIndex + _direction;
            if (_direction > 0 && nextFrame > _current.LastFrame)
            {
                if (_current.Loop)
                {
                    if (_current.PingPong)
                    {
                        _direction = -1;
                        nextFrame = Math.Max(_current.FirstFrame, _current.LastFrame - 1);
                    }
                    else
                    {
                        nextFrame = _current.FirstFrame;
                    }
                }
                else
                {
                    _completed = true;
                    EmitMarkersThrough(DurationSeconds);
                    if (generation == _playGeneration)
                    {
                        AnimationFinished?.Invoke(_requestedClipName);
                    }
                    return;
                }
            }
            else if (_direction < 0 && nextFrame < _current.FirstFrame)
            {
                if (_current.Loop)
                {
                    _direction = 1;
                    nextFrame = Math.Min(_current.LastFrame, _current.FirstFrame + 1);
                }
                else
                {
                    _completed = true;
                    EmitMarkersThrough(DurationSeconds);
                    if (generation == _playGeneration)
                    {
                        AnimationFinished?.Invoke(_requestedClipName);
                    }
                    return;
                }
            }

            _frameIndex = nextFrame;
            FrameChanged?.Invoke(GetCurrentFrame());
            if (generation != _playGeneration)
            {
                return;
            }
            EmitMarkersThrough(_playbackSeconds - _elapsed);
            if (generation != _playGeneration)
            {
                return;
            }
        }
    }

    private void EmitMarkersThrough(double elapsedSeconds)
    {
        var definition = MischiefAnimationCatalog.Find(_current.AssetFolder);
        if (definition is null)
        {
            return;
        }

        var generation = _playGeneration;
        foreach (var marker in definition.Markers)
        {
            if (elapsedSeconds + 0.0000001 >= marker.FrameIndex * _current.FrameSeconds &&
                _emittedMarkers.Add(marker.Name))
            {
                MarkerReached?.Invoke(_requestedClipName, marker.Name);
                if (generation != _playGeneration)
                {
                    return;
                }
            }
        }
    }

    private BitmapSource GetCurrentFrame() => GetFrames(_current.AssetFolder)[_frameIndex];

    private BitmapSource[] GetFrames(string folder) =>
        _assets.GetOrAdd(
            folder,
            assetFolder => new Lazy<BitmapSource[]>(
                () => Enumerable.Range(0, 16)
                    .Select(index => LoadBitmap(Path.Combine(
                        _runtimeDirectory,
                        assetFolder,
                        $"frame_{index:00}.png")))
                    .ToArray(),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;

    private static BitmapSource LoadBitmap(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("缺少桌宠序列帧。", path);
        }

        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(path, UriKind.Absolute);
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }
}
