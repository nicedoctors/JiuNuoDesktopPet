namespace SoftMochiPet.Core;

public static class FeibiVoiceCatalog
{
    public const string BurstCallFileName = "菲比啾比_菲八啾比.wav";

    private static readonly string[] NumberedFiles =
    [
        "菲比啾比_001.wav", "菲比啾比_004.wav", "菲比啾比_006.wav", "菲比啾比_014.wav",
        "菲比啾比_017.wav", "菲比啾比_034.wav", "菲比啾比_039.wav", "菲比啾比_041.wav", "菲比啾比_047.wav",
    ];

    private static readonly IReadOnlyDictionary<VoiceCue, string[]> Daily = new Dictionary<VoiceCue, string[]>
    {
        [VoiceCue.Neutral] =
        [
            "菲比啾比_正常.wav", "菲比啾比_小声平静嘟哝.wav", "菲比啾比_菲八啾比.wav",
            .. NumberedFiles,
        ],
        [VoiceCue.Affirmative] = ["菲比啾比_积极.wav", "菲比啾比_俏皮.wav"],
        [VoiceCue.Sad] = ["菲比啾比_伤心难过.wav", "菲比啾比_失落轻声.wav"],
        [VoiceCue.Positive] = ["菲比啾比_积极.wav", "菲比啾比_兴奋.wav"],
        [VoiceCue.Scream] = NumberedFiles,
        [VoiceCue.Panic] = NumberedFiles,
        [VoiceCue.Calm] = ["菲比啾比_困倦.wav"],
        [VoiceCue.Aggrieved] = ["菲比啾比_委屈.wav", "菲比啾比_失落轻声.wav"],
        [VoiceCue.Question] = ["菲比啾比_疑惑.wav"],
        [VoiceCue.Landing] = ["菲比啾比_委屈.wav", "菲比啾比_小声平静嘟哝.wav"],
    };

    private static readonly IReadOnlyDictionary<MischiefCue, string[]> Pranks = new Dictionary<MischiefCue, string[]>
    {
        [MischiefCue.Teased] = ["菲比啾比_俏皮小声.wav", "菲比啾比_小声俏皮.wav", "菲比啾比_菲八啾比.wav"],
        [MischiefCue.Sneak] = ["菲比啾比_悄悄话.wav", "菲比啾比_小声俏皮.wav"],
        [MischiefCue.Kick] = ["菲比啾比_积极.wav", "菲比啾比_俏皮.wav"],
        [MischiefCue.Punch] = ["菲比啾比_积极.wav", "菲比啾比_咬牙切齿.wav"],
        [MischiefCue.Charge] = ["菲比啾比_兴奋.wav", "菲比啾比_积极.wav"],
        [MischiefCue.Recoil] = ["菲比啾比_委屈.wav", "菲比啾比_小声平静嘟哝.wav"],
        [MischiefCue.HideWindow] = ["菲比啾比_悄悄话.wav", "菲比啾比_俏皮小声.wav"],
        [MischiefCue.HoldingWindow] = ["菲比啾比_小声俏皮.wav", "菲比啾比_俏皮小声.wav"],
        [MischiefCue.ReturnWindow] = ["菲比啾比_正常.wav", "菲比啾比_积极.wav", "菲比啾比_俏皮.wav"],
        [MischiefCue.Burst] = [BurstCallFileName],
        [MischiefCue.Smash] = ["菲比啾比_兴奋.wav", "菲比啾比_积极.wav"],
        [MischiefCue.Tear] = ["菲比啾比_咬牙切齿.wav"],
        [MischiefCue.Finished] = ["菲比啾比_俏皮.wav", "菲比啾比_菲八啾比.wav"],
        [MischiefCue.Interrupted] = ["菲比啾比_委屈.wav", "菲比啾比_失落轻声.wav"],
        [MischiefCue.NoTarget] = ["菲比啾比_疑惑.wav", "菲比啾比_伤心难过.wav", "菲比啾比_失落轻声.wav"],
    };

    public static IReadOnlyList<string> GetFileNames(VoiceCue cue) => Daily[cue];
    public static IReadOnlyList<string> GetFileNames(MischiefCue cue) => Pranks[cue];

    public static IReadOnlyList<string> AllFileNames { get; } = Daily.Values.Concat(Pranks.Values)
        .SelectMany(files => files).Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(file => file, StringComparer.OrdinalIgnoreCase).ToArray();
}
