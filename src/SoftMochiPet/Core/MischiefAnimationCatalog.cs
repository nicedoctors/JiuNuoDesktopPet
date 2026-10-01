namespace SoftMochiPet.Core;

public sealed record MischiefAnimationMarker(string Name, int FrameIndex);

public sealed record MischiefAnimationDefinition(
    string Name,
    double FrameSeconds,
    bool Loop,
    IReadOnlyList<MischiefAnimationMarker> Markers)
{
    public double DurationSeconds => 16 * FrameSeconds;
}

public static class MischiefAnimationCatalog
{
    public static IReadOnlyList<MischiefAnimationDefinition> All { get; } = Array.AsReadOnly(new[]
    {
        Clip("prank_sneak", 0.105, true),
        Clip("kick", 0.090, false, new MischiefAnimationMarker("contact", 8)),
        Clip("punch", 0.090, false, new MischiefAnimationMarker("contact", 8)),
        Clip("charge", 0.090, false, new MischiefAnimationMarker("contact", 8)),
        Clip("recoil", 0.090, false),
        Clip("hat_open", 0.110, false, new MischiefAnimationMarker("hat_open", 8)),
        Clip("hat_store", 0.110, false, new MischiefAnimationMarker("store", 8)),
        Clip("hat_hold", 0.140, true),
        Clip("hat_return", 0.110, false, new MischiefAnimationMarker("return", 8)),
        Clip("gloat", 0.090, false),
        Clip("hat_throw", 0.090, false, new MischiefAnimationMarker("release_hat", 7)),
        Clip("bare_idle", 0.120, true),
        Clip("bare_run", 0.058, true),
        Clip("bare_kick", 0.110, false, new MischiefAnimationMarker("contact", 8)),
        Clip("bare_tear", 0.170, false, new("grab", 3), new("tear", 8)),
        Clip("bare_recover", 0.090, false),
        Clip("bare_drag", 0.080, true),
        Clip("bare_fall", 0.090, true),
        Clip("hat_pickup", 0.110, false, new MischiefAnimationMarker("pickup_hat", 4)),
        Clip("hat_wear", 0.110, false, new MischiefAnimationMarker("wear_hat", 8)),
    });

    public static MischiefAnimationDefinition? Find(string name) =>
        All.FirstOrDefault(clip => string.Equals(clip.Name, name, StringComparison.OrdinalIgnoreCase));

    private static MischiefAnimationDefinition Clip(string name, double frameSeconds, bool loop,
        params MischiefAnimationMarker[] markers) => new(name, frameSeconds, loop, Array.AsReadOnly(markers));
}
