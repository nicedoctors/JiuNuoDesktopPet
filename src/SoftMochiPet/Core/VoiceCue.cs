namespace SoftMochiPet.Core;

public enum VoiceCue
{
    Neutral,
    Affirmative,
    Sad,
    Positive,
    Scream,
    Panic,
    Calm,
    Aggrieved,
    Question,
    Landing,
}

public enum VoicePriority
{
    Ambient,
    Normal,
    Important,
    Critical,
}

public static class VoiceCueCatalog
{
    private static readonly IReadOnlyDictionary<VoiceCue, string[]> Files =
        new Dictionary<VoiceCue, string[]>
        {
            [VoiceCue.Neutral] =
            [
                "糯糯_002.wav",
                "糯糯_015.wav",
                "糯糯_017.wav",
                "糯糯_018.wav",
                "糯糯_019.wav",
                "糯糯_024.wav",
                "糯糯_030.wav",
                "糯糯_正常.wav",
                "糯糯_正常02.wav",
                "糯糯_正常03.wav",
                "糯糯_正常04.wav",
            ],
            [VoiceCue.Affirmative] = ["糯糯_表示肯定.wav"],
            [VoiceCue.Sad] = ["糯糯_超级伤心.wav"],
            [VoiceCue.Positive] = ["糯糯_积极.wav"],
            [VoiceCue.Scream] =
            [
                "糯糯_快速.wav",
                "糯糯_015.wav",
                "糯糯_017.wav",
                "糯糯_024.wav",
                "糯糯_正常.wav",
                "糯糯_正常03.wav",
                "糯糯_有点委屈.wav",
            ],
            [VoiceCue.Panic] = ["糯糯_惊恐_控制不住自己.wav"],
            [VoiceCue.Calm] = ["糯糯_冷静.wav"],
            [VoiceCue.Aggrieved] = ["糯糯_委屈.wav", "糯糯_有点委屈.wav"],
            [VoiceCue.Question] = ["糯糯_疑问.wav"],
            [VoiceCue.Landing] = ["糯糯_软.wav", "糯糯_有点委屈.wav"],
        };

    public static IReadOnlyList<string> GetFileNames(VoiceCue cue, PetCharacterProfile? character = null) =>
        character?.UsesMischief == true ? FeibiVoiceCatalog.GetFileNames(cue) : Files[cue];

    public static IReadOnlyList<string> AllFileNamesFor(PetCharacterProfile character) =>
        character.UsesMischief ? FeibiVoiceCatalog.AllFileNames : AllFileNames;

    public static IReadOnlyList<string> AllFileNames { get; } = Files.Values
        .SelectMany(fileNames => fileNames)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(fileName => fileName, StringComparer.OrdinalIgnoreCase)
        .ToArray();
}
