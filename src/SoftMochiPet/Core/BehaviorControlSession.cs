using System.IO;
using System.Text.Json;
using SoftMochiPet.Models;

namespace SoftMochiPet.Core;

/// <summary>Persistent manual-control data, isolated from ordinary settings and character saves.</summary>
public sealed class BehaviorControlSession
{
    private BehaviorControlSession(string directory, PetSettings settings)
    {
        DirectoryPath = directory;
        Settings = settings;
        SettingsStore = new SettingsStore(directory);
        SettingsStore.Save(settings);
    }

    public string DirectoryPath { get; }
    public PetSettings Settings { get; }
    public SettingsStore SettingsStore { get; }

    public static BehaviorControlSession Create(string? realSettingsPath = null, string? applicationDataRoot = null)
    {
        var appData = applicationDataRoot ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var directory = Path.GetFullPath(Path.Combine(appData, "SoftMochiPet", "behavior-control"));
        var ownPath = Path.Combine(directory, "settings.json");
        var settings = Defaults();
        if (File.Exists(ownPath))
        {
            CopyPresentationPreferences(ownPath, settings, includeControlPreferences: true);
        }
        else
        {
            realSettingsPath ??= Path.Combine(appData, "SoftMochiPet", "settings.json");
            CopyPresentationPreferences(realSettingsPath, settings);
        }
        settings.Version = Math.Max(5, settings.Version);
        settings.PetSize = PetSizePolicy.ClampPreferredSize(settings.PetSize);
        settings.VoiceVolume = double.IsFinite(settings.VoiceVolume) ? Math.Clamp(settings.VoiceVolume, 0, 1) : 0.72;
        settings.ReactToDeletes = false;
        settings.WatchedFolders = [];
        return new BehaviorControlSession(directory, settings);
    }

    public string LifeStateDirectory(PetCharacterProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return Path.Combine(DirectoryPath, "characters", profile.Id);
    }

    private static PetSettings Defaults() => new() { AllowMischief = false, ReactToDeletes = false, WatchedFolders = [] };

    private static void CopyPresentationPreferences(string path, PetSettings settings, bool includeControlPreferences = false)
    {
        try
        {
            // The ordinary SettingsStore.Load can migrate and rewrite the user's
            // configuration; read only the four presentation preferences here.
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var source = document.RootElement;
            if (source.ValueKind != JsonValueKind.Object) return;
            if (source.TryGetProperty(nameof(PetSettings.CharacterId), out var character) && character.ValueKind == JsonValueKind.String)
                settings.CharacterId = character.GetString()!;
            if (source.TryGetProperty(nameof(PetSettings.SoundEnabled), out var sound) && sound.ValueKind is JsonValueKind.True or JsonValueKind.False)
                settings.SoundEnabled = sound.GetBoolean();
            if (source.TryGetProperty(nameof(PetSettings.PetSize), out var size) && size.ValueKind == JsonValueKind.Number && size.TryGetDouble(out var petSize))
                settings.PetSize = petSize;
            if (source.TryGetProperty(nameof(PetSettings.VoiceVolume), out var volume) && volume.ValueKind == JsonValueKind.Number && volume.TryGetDouble(out var voiceVolume))
                settings.VoiceVolume = voiceVolume;
            if (includeControlPreferences)
            {
                if (source.TryGetProperty(nameof(PetSettings.AllowMischief), out var mischief) && mischief.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    settings.AllowMischief = mischief.GetBoolean();
                if (source.TryGetProperty(nameof(PetSettings.QuietMode), out var quiet) && quiet.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    settings.QuietMode = quiet.GetBoolean();
                if (source.TryGetProperty(nameof(PetSettings.FastingMode), out var fasting) && fasting.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    settings.FastingMode = fasting.GetBoolean();
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            // Missing or malformed source preferences never invoke normal migration.
        }
    }
}
