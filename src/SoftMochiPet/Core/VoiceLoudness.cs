using System.IO;
using System.Text.Json;

namespace SoftMochiPet.Core;

/// <summary>Offline measurements keep playback cheap and preserve the original recordings.</summary>
public sealed class VoiceLoudness
{
    private readonly Dictionary<string, double> _gains = new(StringComparer.OrdinalIgnoreCase);
    public static VoiceLoudness Packaged { get; } = Load(Path.Combine(
        AppContext.BaseDirectory, "assets", "audio", "voice-levels.json"));

    public static VoiceLoudness Load(string path)
    {
        var levels = new VoiceLoudness();
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            foreach (var clip in document.RootElement.GetProperty("Clips").EnumerateArray())
            {
                var character = clip.GetProperty("Character").GetString();
                var file = clip.GetProperty("File").GetString();
                var gain = clip.GetProperty("Gain").GetDouble();
                if (!string.IsNullOrWhiteSpace(character) && !string.IsNullOrWhiteSpace(file) &&
                    double.IsFinite(gain) && gain is > 0 and <= 1)
                    levels._gains[$"{character}/{file}"] = gain;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            Services.DiagnosticsLog.Write("Voice loudness index unavailable; using original recording levels.", exception);
        }
        return levels;
    }

    public double Gain(string character, string? file) => file is not null &&
        _gains.TryGetValue($"{character}/{file}", out var gain) ? gain : 1;

    public double Volume(string character, string? file, double requested) =>
        (double.IsFinite(requested) ? Math.Clamp(requested, 0, 1) : 0) * Gain(character, file);
}
