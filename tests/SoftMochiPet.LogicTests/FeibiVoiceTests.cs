using System.Security.Cryptography;
using SoftMochiPet.Core;

internal static class FeibiVoiceTests
{
    private const string RetainedFallFile = "菲比啾比_坠落.wav";

    private static readonly string[] NumberedFiles =
    [
        "菲比啾比_001.wav", "菲比啾比_004.wav", "菲比啾比_006.wav", "菲比啾比_014.wav",
        "菲比啾比_017.wav", "菲比啾比_034.wav", "菲比啾比_039.wav", "菲比啾比_041.wav", "菲比啾比_047.wav",
    ];

    public static void VoiceDirectoriesAndCatalogsRemainCharacterSpecific()
    {
        var feibi = PetCharacterProfile.FeibiJiubi;
        var nuonuo = PetCharacterProfile.Nuonuo;
        Assert(feibi.HasVoice && nuonuo.HasVoice, "Both characters have their own supplied voice recordings.");
        Assert(feibi.VoiceRelativeDirectory == Path.Combine("assets", "characters", "feibijiubi", "audio") &&
            nuonuo.VoiceRelativeDirectory == Path.Combine("assets", "audio", "voice"),
            "Feibi's directory must not redirect Nuonuo's legacy recordings.");
        foreach (var cue in Enum.GetValues<VoiceCue>())
        {
            Assert(VoiceCueCatalog.GetFileNames(cue, feibi).All(file => file.StartsWith("菲比啾比_", StringComparison.Ordinal)),
                $"Feibi must never borrow Nuonuo's recording for {cue}.");
            Assert(VoiceCueCatalog.GetFileNames(cue, nuonuo).SequenceEqual(VoiceCueCatalog.GetFileNames(cue)) &&
                VoiceCueCatalog.GetFileNames(cue).All(file => file.StartsWith("糯糯_", StringComparison.Ordinal)),
                $"The default and explicit Nuonuo voice routing must remain identical for {cue}.");
        }
        Assert(!VoiceCueCatalog.AllFileNamesFor(feibi).Intersect(VoiceCueCatalog.AllFileNamesFor(nuonuo)).Any(),
            "Character catalogs must not share recording filenames.");
    }

    public static void ActiveFeibiCatalogExcludesTheRetainedFallRecording()
    {
        var assigned = Enum.GetValues<VoiceCue>().SelectMany(FeibiVoiceCatalog.GetFileNames)
            .Concat(Enum.GetValues<MischiefCue>().SelectMany(FeibiVoiceCatalog.GetFileNames))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert(FeibiVoiceCatalog.AllFileNames.Count == 24 && assigned.Count == 24 &&
            assigned.SetEquals(FeibiVoiceCatalog.AllFileNames) &&
            assigned.SetEquals(VoiceCueCatalog.AllFileNamesFor(PetCharacterProfile.FeibiJiubi)),
            "All 24 active recordings must be reachable from the character catalog.");
        Assert(!assigned.Contains(RetainedFallFile),
            "The retained original fall recording must not be reachable from any daily or prank cue.");
        foreach (var files in Enum.GetValues<VoiceCue>().Select(FeibiVoiceCatalog.GetFileNames)
            .Concat(Enum.GetValues<MischiefCue>().Select(FeibiVoiceCatalog.GetFileNames)))
        {
            Assert(files.Count > 0 && files.Distinct(StringComparer.OrdinalIgnoreCase).Count() == files.Count &&
                files.All(file => Path.GetFileName(file) == file && file.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)),
                "Every daily and prank cue needs distinct, local WAV candidates.");
        }
    }

    public static void AllNineNumberedRecordingsParticipateInDailyChatter()
    {
        var neutral = FeibiVoiceCatalog.GetFileNames(VoiceCue.Neutral);
        Assert(NumberedFiles.All(neutral.Contains) && neutral.Count == 12,
            "Daily chatter must include all nine confirmed numbered recordings plus the three named variants.");
        Assert(neutral.Contains("菲比啾比_正常.wav") && neutral.Contains("菲比啾比_小声平静嘟哝.wav") &&
            neutral.Contains("菲比啾比_菲八啾比.wav"),
            "Adding numbered recordings must preserve the existing named daily variants.");
        var fresh = VoicePlaybackPolicy.GetFreshFiles(neutral, new Dictionary<string, DateTimeOffset>(), DateTimeOffset.UtcNow);
        Assert(NumberedFiles.All(fresh.Contains), "Every numbered daily recording must be eligible before first use.");
    }

    public static void FallAndThrowCuesUseExactlyTheNineNumberedRecordings()
    {
        foreach (var cue in new[] { VoiceCue.Scream, VoiceCue.Panic })
        {
            var files = FeibiVoiceCatalog.GetFileNames(cue);
            Assert(files.Count == NumberedFiles.Length && NumberedFiles.All(files.Contains),
                $"{cue} must choose only from the nine numbered recordings.");
            Assert(VoiceCueCatalog.GetFileNames(cue, PetCharacterProfile.FeibiJiubi).SequenceEqual(files),
                $"Character-aware {cue} routing must expose all nine numbered candidates.");
            var rotation = VoicePlaybackPolicy.GetRotationCandidates(files, NumberedFiles[0], NumberedFiles[1]);
            Assert(rotation.Count == 7 && NumberedFiles.Skip(2).All(rotation.Contains),
                $"{cue} random rotation must avoid the cue's previous take and the globally last take when alternatives exist.");
        }
        NumberedDailyAndFallFilesShareTheTwelveSecondCooldown();
        FallCuesStaySilentUntilNumberedRecordingsFinishCoolingDown();
    }

    public static void FeibiEmotionAndPrankCuesUseAppropriateNamedRecordings()
    {
        Assert(FeibiVoiceCatalog.GetFileNames(VoiceCue.Calm).SequenceEqual(["菲比啾比_困倦.wav"]) &&
            FeibiVoiceCatalog.GetFileNames(VoiceCue.Question).SequenceEqual(["菲比啾比_疑惑.wav"]) &&
            FeibiVoiceCatalog.GetFileNames(VoiceCue.Landing).SequenceEqual(["菲比啾比_委屈.wav", "菲比啾比_小声平静嘟哝.wav"]),
            "Sleep, curiosity, and landing must retain their existing named recordings.");
        Assert(FeibiVoiceCatalog.GetFileNames(MischiefCue.Sneak).Contains("菲比啾比_悄悄话.wav") &&
            FeibiVoiceCatalog.GetFileNames(MischiefCue.HideWindow).Contains("菲比啾比_悄悄话.wav") &&
            FeibiVoiceCatalog.GetFileNames(MischiefCue.Burst).SequenceEqual([FeibiVoiceCatalog.BurstCallFileName]) &&
            FeibiVoiceCatalog.GetFileNames(MischiefCue.Tear).SequenceEqual(["菲比啾比_咬牙切齿.wav"]) &&
            FeibiVoiceCatalog.GetFileNames(MischiefCue.Interrupted).Contains("菲比啾比_委屈.wav"),
            "Sneaking, hiding, tearing, and interruption retain their emotional choices; the burst uses its fixed opening line.");
    }

    public static void BurstCallUsesOnlyTheRequestedOriginalRecording()
    {
        Assert(FeibiVoiceCatalog.BurstCallFileName == "菲比啾比_菲八啾比.wav" &&
            FeibiVoiceCatalog.GetFileNames(MischiefCue.Burst).SequenceEqual([FeibiVoiceCatalog.BurstCallFileName]),
            "A full mischief meter must use the exact requested line, with no random alternative.");
        var path = Path.Combine(AppContext.BaseDirectory, PetCharacterProfile.FeibiJiubi.VoiceRelativeDirectory,
            FeibiVoiceCatalog.BurstCallFileName);
        Assert(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) ==
            "D4E687696790B103A73A8710D7EC78ED7BB84FDAA2C130E608974192254D5135",
            "The burst opening must retain the user's original WAV bytes, without transcoding or replacement.");
    }

    public static void ProductionFeibiRecordingsAreCompleteValidOriginalPcm()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, PetCharacterProfile.FeibiJiubi.VoiceRelativeDirectory);
        var packagedFiles = Directory.GetFiles(directory, "*.wav").Select(Path.GetFileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var suppliedFiles = FeibiVoiceCatalog.AllFileNames.Append(RetainedFallFile).ToArray();
        Assert(packagedFiles.Count == 25 && packagedFiles.SetEquals(suppliedFiles),
            "The production output must retain all 25 supplied WAV files, including the unused original fall recording.");
        var hashes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in suppliedFiles)
        {
            var bytes = File.ReadAllBytes(Path.Combine(directory, file));
            Assert(hashes.Add(Convert.ToHexString(SHA256.HashData(bytes))), $"A different recording was duplicated as {file}.");
            using var stream = new MemoryStream(bytes, writable: false);
            using var reader = new BinaryReader(stream);
            Assert(reader.ReadUInt32() == 0x46464952 && reader.ReadUInt32() == bytes.Length - 8 &&
                reader.ReadUInt32() == 0x45564157, $"RIFF/WAVE header or file length is invalid: {file}.");
            byte[]? format = null;
            byte[]? pcm = null;
            while (stream.Position < stream.Length)
            {
                Assert(stream.Length - stream.Position >= 8, $"Truncated chunk header: {file}.");
                var id = reader.ReadUInt32();
                var length = reader.ReadUInt32();
                Assert(length <= stream.Length - stream.Position && length <= int.MaxValue, $"Truncated chunk: {file}.");
                var body = reader.ReadBytes((int)length);
                if (id == 0x20746D66)
                {
                    Assert(format is null, $"Duplicate PCM format chunk: {file}.");
                    format = body;
                }
                else if (id == 0x61746164)
                {
                    Assert(pcm is null, $"Duplicate PCM data chunk: {file}.");
                    pcm = body;
                }
                if ((length & 1) != 0)
                    reader.ReadByte();
            }
            Assert(format is { Length: >= 40 } && pcm is { Length: > 0 }, $"Missing extensible PCM data: {file}.");
            Assert(BitConverter.ToUInt16(format!, 0) == 0xFFFE && BitConverter.ToUInt16(format!, 2) == 1 &&
                BitConverter.ToUInt32(format!, 4) == 48000 && BitConverter.ToUInt32(format!, 8) == 144000 &&
                BitConverter.ToUInt16(format!, 12) == 3 && BitConverter.ToUInt16(format!, 14) == 24 &&
                BitConverter.ToUInt16(format!, 16) >= 22 && BitConverter.ToUInt16(format!, 18) == 24 &&
                format!.AsSpan(24, 16).SequenceEqual(Convert.FromHexString("0100000000001000800000AA00389B71")),
                $"The supplied 48 kHz mono 24-bit integer PCM format must remain intact: {file}.");
            Assert(pcm!.Length % 3 == 0 && pcm.Any(value => value != 0), $"Empty, silent, or incomplete PCM frames: {file}.");
        }
    }

    public static void SharedDailyAndPrankFilesRespectTheSameTwelveSecondCooldown()
    {
        var started = DateTimeOffset.UnixEpoch.AddMinutes(1);
        var history = new Dictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase)
        {
            ["菲比啾比_积极.wav"] = started,
        };
        Assert(VoicePlaybackPolicy.MinimumFileRepeatInterval == TimeSpan.FromSeconds(12),
            "Shared recordings must retain the 12-second per-file repeat guard.");
        foreach (var files in new[]
        {
            FeibiVoiceCatalog.GetFileNames(VoiceCue.Positive),
            FeibiVoiceCatalog.GetFileNames(MischiefCue.Kick),
            FeibiVoiceCatalog.GetFileNames(MischiefCue.Charge),
        })
        {
            Assert(!VoicePlaybackPolicy.GetFreshFiles(files, history, started.AddSeconds(11.999)).Contains("菲比啾比_积极.wav") &&
                VoicePlaybackPolicy.GetFreshFiles(files, history, started.AddSeconds(12)).Contains("菲比啾比_积极.wav"),
                "Changing from daily speech to a prank must not bypass a shared recording's repeat timer.");
        }
        var rotation = VoicePlaybackPolicy.GetRotationCandidates(FeibiVoiceCatalog.GetFileNames(VoiceCue.Neutral),
            NumberedFiles[0], NumberedFiles[1]);
        Assert(!rotation.Contains(NumberedFiles[0]) && !rotation.Contains(NumberedFiles[1]),
            "Daily random rotation must avoid both the cue's previous take and the globally last take when alternatives exist.");
    }

    private static void NumberedDailyAndFallFilesShareTheTwelveSecondCooldown()
    {
        var started = DateTimeOffset.UnixEpoch.AddMinutes(1);
        foreach (var previousFile in NumberedFiles)
        {
            var history = new Dictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase)
            {
                [previousFile] = started,
            };
            foreach (var cue in new[] { VoiceCue.Neutral, VoiceCue.Scream, VoiceCue.Panic })
            {
                var files = FeibiVoiceCatalog.GetFileNames(cue);
                var fresh = VoicePlaybackPolicy.GetFreshFiles(files, history, started.AddSeconds(11.999));
                Assert(!fresh.Contains(previousFile) && fresh.Count == files.Count - 1 &&
                    files.Where(file => file != previousFile).All(fresh.Contains),
                    $"{cue} must exclude a numbered recording used by any daily or fall cue while preserving every other candidate.");
                var rotation = VoicePlaybackPolicy.GetRotationCandidates(fresh, previousFile, previousFile);
                Assert(rotation.SequenceEqual(fresh),
                    $"{cue} must keep the remaining fresh candidates available to random selection.");
                Assert(VoicePlaybackPolicy.GetFreshFiles(files, history, started.AddSeconds(12)).SequenceEqual(files),
                    $"{cue} must make a shared numbered recording eligible again exactly 12 seconds after playback.");
            }
        }
    }

    private static void FallCuesStaySilentUntilNumberedRecordingsFinishCoolingDown()
    {
        var started = DateTimeOffset.UnixEpoch.AddMinutes(1);
        var history = NumberedFiles.ToDictionary(file => file, _ => started, StringComparer.OrdinalIgnoreCase);
        foreach (var cue in new[] { VoiceCue.Scream, VoiceCue.Panic })
        {
            var files = FeibiVoiceCatalog.GetFileNames(cue);
            var fresh = VoicePlaybackPolicy.GetFreshFiles(files, history, started.AddSeconds(11.999));
            Assert(fresh.Count == 0 &&
                VoicePlaybackPolicy.GetRotationCandidates(fresh, NumberedFiles[0], NumberedFiles[1]).Count == 0,
                $"{cue} must stay silent when all numbered recordings are cooling down, without falling back to named audio.");
            var restored = VoicePlaybackPolicy.GetFreshFiles(files, history, started.AddSeconds(12));
            Assert(restored.Count == NumberedFiles.Length && NumberedFiles.All(restored.Contains),
                $"{cue} must restore all nine numbered candidates at the 12-second boundary.");
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
