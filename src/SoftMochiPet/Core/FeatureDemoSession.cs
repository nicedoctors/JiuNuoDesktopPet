using System.IO;
using System.Text.Json;
using SoftMochiPet.Models;

namespace SoftMochiPet.Core;

public sealed class FeatureDemoSession
{
    private FeatureDemoSession(string directoryPath, PetSettings settings)
    {
        DirectoryPath = directoryPath;
        Settings = settings;
        SettingsStore = new SettingsStore(directoryPath);
        SettingsStore.Save(settings);
    }

    public string DirectoryPath { get; }
    public SettingsStore SettingsStore { get; }
    public PetSettings Settings { get; }

    public static FeatureDemoSession Create(string? realSettingsPath = null, string? temporaryRoot = null)
    {
        realSettingsPath ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SoftMochiPet", "settings.json");
        var settings = ReadPresentationPreferences(realSettingsPath);
        var directory = Path.GetFullPath(Path.Combine(
            temporaryRoot ?? Path.GetTempPath(), "SoftMochiPet.FeatureTest", Guid.NewGuid().ToString("N")));
        return new FeatureDemoSession(directory, settings);
    }

    public string LifeStateDirectory(PetCharacterProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return Path.Combine(DirectoryPath, "characters", profile.Id);
    }

    private static PetSettings ReadPresentationPreferences(string path)
    {
        var settings = new PetSettings
        {
            CharacterId = PetCharacterProfile.FeibiJiubi.Id,
            ReactToDeletes = false,
            QuietMode = false,
            AllowMischief = false,
            // If the existing preference cannot be read, stay silent rather
            // than guessing that the user enabled sound.
            SoundEnabled = false,
        };
        try
        {
            // SettingsStore.Load can migrate and save. Only this presentation
            // whitelist is read; watched paths and life state are never copied.
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var source = document.RootElement;
            if (source.ValueKind != JsonValueKind.Object) return settings;
            if (source.TryGetProperty(nameof(PetSettings.SoundEnabled), out var sound) &&
                sound.ValueKind is JsonValueKind.True or JsonValueKind.False)
                settings.SoundEnabled = sound.GetBoolean();
            if (source.TryGetProperty(nameof(PetSettings.VoiceVolume), out var volume) &&
                volume.ValueKind == JsonValueKind.Number && volume.TryGetDouble(out var voiceVolume) &&
                double.IsFinite(voiceVolume))
                settings.VoiceVolume = Math.Clamp(voiceVolume, 0, 1);
            if (source.TryGetProperty(nameof(PetSettings.PetSize), out var size) &&
                size.ValueKind == JsonValueKind.Number && size.TryGetDouble(out var petSize))
                settings.PetSize = PetSizePolicy.ClampPreferredSize(petSize);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            // Demo defaults are independent of missing or damaged real settings.
        }
        return settings;
    }
}
