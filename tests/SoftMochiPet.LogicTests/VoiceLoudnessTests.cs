using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Media;
using SoftMochiPet.Core;
using SoftMochiPet.Services;

internal static class VoiceLoudnessTests
{
    public static void IndexMatchesEveryOriginalRecording()
    {
        using var index = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "assets/audio/voice-levels.json")));
        var clips = index.RootElement.GetProperty("Clips").EnumerateArray().ToArray();
        Assert(clips.Length == 46, "All 46 original recordings need measurements.");
        foreach (var profile in PetCharacterProfile.All)
            foreach (var file in Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, profile.VoiceRelativeDirectory), "*.wav"))
            {
                var clip = clips.Single(c => c.GetProperty("Character").GetString() == profile.Id && c.GetProperty("File").GetString() == Path.GetFileName(file));
                Assert(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))).Equals(clip.GetProperty("Sha256").GetString(), StringComparison.OrdinalIgnoreCase),
                    "The index must match the actual packaged WAV, without rewriting it.");
                var gain = VoiceLoudness.Packaged.Gain(profile.Id, Path.GetFileName(file));
                Assert(gain is > 0 and <= 1, "Balancing must only attenuate.");
                var db = 20 * Math.Log10(gain);
                Assert(clip.GetProperty("IntegratedLufs").GetDouble() + db <= -23.99 &&
                    clip.GetProperty("TruePeakDbtp").GetDouble() + db <= -1.99, "Every clip must obey loudness and peak ceilings.");
            }
    }

    public static void MainAndBurstPlayersKeepGainWhenSliderChanges()
    {
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var profile = PetCharacterProfile.FeibiJiubi;
        using var voice = new VoicePlaybackService("", false, .72, profile);
        typeof(VoicePlaybackService).GetField("_currentFileName", flags)!.SetValue(voice, "菲比啾比_014.wav");
        var burst = new MediaPlayer();
        typeof(VoicePlaybackService).GetField("_burstPlayer", flags)!.SetValue(voice, burst);
        voice.SetVolume(.4);
        var main = (MediaPlayer)typeof(VoicePlaybackService).GetField("_player", flags)!.GetValue(voice)!;
        Assert(Math.Abs(main.Volume - .4 * VoiceLoudness.Packaged.Gain(profile.Id, "菲比啾比_014.wav")) < 1e-8,
            "The slider must not reset a balanced ordinary recording to raw volume.");
        Assert(Math.Abs(burst.Volume - .4 * VoiceLoudness.Packaged.Gain(profile.Id, FeibiVoiceCatalog.BurstCallFileName)) < 1e-8,
            "The independent burst player must use the same gain rule.");
        voice.SetVolume(0);
        Assert(main.Volume == 0 && burst.Volume == 0, "Mute must affect both players.");
        burst.Close();
        Assert(VoiceLoudness.Packaged.Volume(profile.Id, "unindexed.wav", .5) == .5, "Unknown recordings must remain usable.");
    }
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
