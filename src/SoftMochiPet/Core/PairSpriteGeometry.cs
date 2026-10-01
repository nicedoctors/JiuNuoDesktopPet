using System.IO;
using System.Text.Json;
using Point = System.Windows.Point;

namespace SoftMochiPet.Core;

/// <summary>Keeps mouth, hand and prop contacts attached to calibrated pair cels.</summary>
public static class PairSpriteGeometry
{
    private sealed record Transform(double Scale, double X, double Y);
    private static readonly Lazy<Dictionary<string, Transform[]>> Calibration = new(() =>
        JsonSerializer.Deserialize<Dictionary<string, Transform[]>>(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "assets", "pair_interactions", "calibration.json")))
        ?? throw new InvalidDataException("双人动作锚点文件为空。"));

    public static Point Map(PetCharacterProfile character, string folder, int frame, double x, double y)
    {
        if (!Calibration.Value.TryGetValue($"{character.Id}/{folder}", out var frames)) return new Point(x,y);
        if (frames.Length != 16) throw new InvalidDataException("双人动作锚点帧数不匹配。");
        var transform = frames[Math.Clamp(frame,0,15)];
        return new Point(x*transform.Scale+transform.X, y*transform.Scale+transform.Y);
    }
}
