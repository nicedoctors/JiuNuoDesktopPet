using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;
using Point = System.Windows.Point;

namespace SoftMochiPet.Core;

public sealed class PrankArtwork
{
    private readonly string _directory;
    private readonly Dictionary<string, PrankClipArtwork> _clips;
    private readonly Dictionary<string, BitmapSource> _images = new(StringComparer.OrdinalIgnoreCase);

    public PrankArtwork(string characterDirectory)
    {
        _directory = characterDirectory;
        var manifest = JsonSerializer.Deserialize<PrankArtworkManifest>(
            File.ReadAllText(Path.Combine(characterDirectory, "prank-animations.json")),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("Missing prank artwork manifest.");
        if (manifest.Version != 1 || manifest.CanvasSize != 512 || manifest.Facing != "left")
            throw new InvalidDataException("Unsupported prank artwork coordinates.");
        _clips = new Dictionary<string, PrankClipArtwork>(manifest.Clips, StringComparer.OrdinalIgnoreCase);
        foreach (var definition in MischiefAnimationCatalog.All)
        {
            var clip = Clip(definition.Name);
            if (Math.Abs(clip.FrameSeconds - definition.FrameSeconds) > 0.000001 || clip.ContactFrame is < 0 or > 15)
                throw new InvalidDataException("Prank artwork timing does not match the animation.");
            if (definition.Name is "kick" or "punch" or "charge" or "bare_kick" or "bare_tear")
                for (var index = 0; index < 16; index++) _ = clip.ContactAt(index);
            if (definition.Name == "bare_tear")
                for (var index = 0; index < 16; index++)
                { _ = clip.TearUpperHandAt(index); _ = clip.TearLowerHandAt(index); }
            if (definition.Name is "hat_throw" or "hat_pickup")
            {
                if (clip.HatTransferFrame is not (>= 0 and <= 15))
                    throw new InvalidDataException("Missing measured hat transfer frame.");
                _ = clip.HatTransferAt(clip.HatTransferFrame.Value);
                if (!double.IsFinite(clip.HatTransferWidth) || clip.HatTransferWidth <= 0 ||
                    !double.IsFinite(clip.HatTransferRotation))
                    throw new InvalidDataException("Missing measured hat transfer dimensions.");
            }
            if (definition.Name is "hat_open" or "hat_store" or "hat_hold" or "hat_return")
                for (var index = 0; index < 16; index++)
                {
                    _ = clip.HatMouthAt(index);
                    _ = clip.HatWidthAt(index);
                    if (string.IsNullOrWhiteSpace(clip.ForegroundMaskFolder) ||
                        !File.Exists(ResolvePath(Path.Combine(clip.ForegroundMaskFolder, $"frame_{index:00}.png"))))
                        throw new InvalidDataException("Missing hat foreground artwork.");
                }
        }
        if (!File.Exists(ResolvePath("props/hat.png")))
            throw new InvalidDataException("Missing independent hat artwork.");
    }

    public PrankClipArtwork Clip(string name) => _clips.TryGetValue(name, out var clip)
        ? clip : throw new InvalidDataException($"Missing prank artwork geometry: {name}.");

    public BitmapSource Hat => LoadImage("props/hat.png");

    public BitmapSource? Foreground(string clip, int frame)
    {
        if (!_clips.TryGetValue(clip, out var artwork) || string.IsNullOrWhiteSpace(artwork.ForegroundMaskFolder))
            return null;
        return LoadImage(Path.Combine(artwork.ForegroundMaskFolder, $"frame_{Math.Clamp(frame, 0, 15):00}.png"));
    }

    private string ResolvePath(string relativePath)
    {
        var fullPath = Path.GetFullPath(Path.Combine(_directory, relativePath));
        var root = Path.GetFullPath(_directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Artwork path leaves character directory.");
        return fullPath;
    }

    private BitmapSource LoadImage(string relativePath)
    {
        var fullPath = ResolvePath(relativePath);
        if (_images.TryGetValue(fullPath, out var cached)) return cached;
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(fullPath);
        bitmap.EndInit();
        bitmap.Freeze();
        _images.Add(fullPath, bitmap);
        return bitmap;
    }

    private sealed class PrankArtworkManifest
    {
        public int Version { get; set; }
        public int CanvasSize { get; set; }
        public string Facing { get; set; } = "";
        public Dictionary<string, PrankClipArtwork> Clips { get; set; } = [];
    }
}

public sealed class PrankClipArtwork
{
    public double FrameSeconds { get; set; } = 0.1;
    public int ContactFrame { get; set; } = 8;
    public ArtworkPoint? ContactPoint { get; set; }
    public ArtworkPoint[]? Contact { get; set; }
    public ArtworkPoint[]? HatMouth { get; set; }
    public double[]? HatWidth { get; set; }
    public int? HatTransferFrame { get; set; }
    public ArtworkPoint? HatTransferPoint { get; set; }
    public double HatTransferWidth { get; set; }
    public double HatTransferRotation { get; set; }
    public int? HatTransferSourceFrame { get; set; }
    public ArtworkPoint[]? TearUpperHand { get; set; }
    public ArtworkPoint[]? TearLowerHand { get; set; }
    public string? ForegroundMaskFolder { get; set; }

    public Point ContactAt(int frame) => ReadPoint(Contact, frame, ContactPoint);
    public Point HatTransferAt(int frame) => ReadPoint(null, frame, HatTransferPoint);
    public Point TearUpperHandAt(int frame) => ReadPoint(TearUpperHand, frame, null);
    public Point TearLowerHandAt(int frame) => ReadPoint(TearLowerHand, frame, null);
    public Point HatMouthAt(int frame) => ReadPoint(HatMouth, frame, null);
    public double HatWidthAt(int frame) => HatWidth is { Length: 16 } &&
        double.IsFinite(HatWidth[Math.Clamp(frame, 0, 15)]) && HatWidth[Math.Clamp(frame, 0, 15)] > 0
        ? Math.Max(1, HatWidth[Math.Clamp(frame, 0, 15)])
        : throw new InvalidDataException("Missing hat opening width.");

    private static Point ReadPoint(ArtworkPoint[]? points, int frame, ArtworkPoint? fallback)
    {
        var point = points is { Length: 16 } ? points[Math.Clamp(frame, 0, 15)] : fallback;
        if (point is null || !double.IsFinite(point.X) || !double.IsFinite(point.Y))
            throw new InvalidDataException("Missing measured character contact point.");
        return new Point(point.X, point.Y);
    }
}

public sealed class ArtworkPoint
{
    public double X { get; set; }
    public double Y { get; set; }
}
