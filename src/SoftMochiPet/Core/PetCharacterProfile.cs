using System.IO;

namespace SoftMochiPet.Core;

public sealed record PetCharacterGeometry(
    double FootCanvasX,
    double FootCanvasY,
    double MouthCanvasX,
    double MouthCanvasY,
    double LickContactCanvasX,
    double LickContactCanvasY,
    double SlideContactCanvasX,
    double BodyCollisionLeftCanvasX,
    double BodyCollisionTopCanvasY,
    double BodyCollisionRightCanvasX,
    double BodyCollisionBottomCanvasY,
    double SuctionMouthCanvasX,
    double SuctionMouthCanvasY,
    double BurpOffsetCanvasX = 0,
    double BurpOffsetCanvasY = 0);

public sealed class PetCharacterProfile
{
    private PetCharacterProfile(
        string id,
        string displayName,
        string runtimeRelativeDirectory,
        string voiceRelativeDirectory,
        string iconRelativePath,
        bool hasVoice,
        double movementSpeedMultiplier,
        double maximumIdleSeconds,
        PetCharacterGeometry geometry)
    {
        Id = id;
        DisplayName = displayName;
        RuntimeRelativeDirectory = runtimeRelativeDirectory;
        VoiceRelativeDirectory = voiceRelativeDirectory;
        IconRelativePath = iconRelativePath;
        HasVoice = hasVoice;
        MovementSpeedMultiplier = movementSpeedMultiplier;
        MaximumIdleSeconds = maximumIdleSeconds;
        Geometry = geometry;
    }

    public string Id { get; }
    public string DisplayName { get; }
    public string RuntimeRelativeDirectory { get; }
    public string VoiceRelativeDirectory { get; }
    public string IconRelativePath { get; }
    public bool HasVoice { get; }
    public bool SupportsFood => Id == Nuonuo.Id;
    public bool UsesMischief => Id == FeibiJiubi.Id;
    public double MovementSpeedMultiplier { get; }
    public double MaximumIdleSeconds { get; }
    public PetCharacterGeometry Geometry { get; }

    public static PetCharacterProfile Nuonuo { get; } = new(
        "nuonuo", "糯糯",
        Path.Combine("assets", "sprites", "runtime"),
        Path.Combine("assets", "audio", "voice"),
        Path.Combine("assets", "pet.ico"),
        hasVoice: true,
        movementSpeedMultiplier: 1,
        maximumIdleSeconds: 5,
        geometry: new PetCharacterGeometry(
            SpriteGeometry.FootCanvasX, SpriteGeometry.FootCanvasY,
            45, 267, 60, 334, 407, 96, 92, 416, 466, 191, 304));

    public static PetCharacterProfile FeibiJiubi { get; } = new(
        "feibijiubi", "菲比啾比",
        Path.Combine("assets", "characters", "feibijiubi", "runtime"),
        Path.Combine("assets", "characters", "feibijiubi", "audio"),
        Path.Combine("assets", "characters", "feibijiubi", "pet.ico"),
        hasVoice: true,
        movementSpeedMultiplier: 1.15,
        maximumIdleSeconds: 4,
        geometry: new PetCharacterGeometry(
            SpriteGeometry.FootCanvasX, SpriteGeometry.FootCanvasY,
            55, 284, 95, 372, 436, 88, 100, 424, 470, 180, 284, 130, 100));

    public static IReadOnlyList<PetCharacterProfile> All { get; } =
        Array.AsReadOnly(new[] { Nuonuo, FeibiJiubi });

    public static PetCharacterProfile Get(string? id) =>
        All.FirstOrDefault(profile => string.Equals(profile.Id, id, StringComparison.OrdinalIgnoreCase))
        ?? Nuonuo;

    public string LifeStateDirectory(string appData) =>
        Id == Nuonuo.Id
            ? Path.Combine(appData, "SoftMochiPet")
            : Path.Combine(appData, "SoftMochiPet", "characters", Id);

    internal double ResolveFrameSeconds(string clipName, double defaultSeconds) =>
        Id != FeibiJiubi.Id ? defaultSeconds : clipName switch
        {
            "idle" => 0.120,
            "walk" => 0.094,
            "run" => 0.058,
            "curious" => 0.112,
            "climb" => 0.071,
            "jump" => 0.063,
            "land" => 0.054,
            "roll" => 0.074,
            _ => defaultSeconds,
        };
}
