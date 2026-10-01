using System.IO;
using System.Text.Json;
using SoftMochiPet.Core;

namespace SoftMochiPet.Models;

public sealed class PetSettings
{
    private string _characterId = PetCharacterProfile.Nuonuo.Id;

    public string CharacterId
    {
        get => _characterId;
        set => _characterId = PetCharacterProfile.Get(value).Id;
    }

    public int Version { get; set; } = 5;
    public bool ReactToDeletes { get; set; } = true;
    public bool SoundEnabled { get; set; } = true;
    public bool FastingMode { get; set; }
    public bool InfiniteMode { get; set; }
    public bool CoexistenceMode { get; set; }
    public bool ShowSpeechBubbles { get; set; }
    public bool QuietMode { get; set; }
    public bool AllowMischief { get; set; } = true;
    public double VoiceVolume { get; set; } = 0.72;
    public double PetSize { get; set; } = PetSizePolicy.DefaultSize;
    public List<string> WatchedFolders { get; set; } = [];
}

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public SettingsStore(string? settingsDirectory = null)
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        SettingsDirectory = settingsDirectory ?? Path.Combine(appData, "SoftMochiPet");
        SettingsPath = Path.Combine(SettingsDirectory, "settings.json");
    }

    public string SettingsDirectory { get; }
    public string SettingsPath { get; }

    public PetSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var settings = JsonSerializer.Deserialize<PetSettings>(File.ReadAllText(SettingsPath), JsonOptions)
                    ?? new PetSettings();
                var migrated = false;
                if (settings.Version < 2)
                {
                    settings.Version = 2;
                    settings.PetSize = PetSizePolicy.DefaultSize;
                    migrated = true;
                }

                if (settings.Version < 3)
                {
                    // Version 2 exposed no voice control, so a false value was
                    // only the old default and not an explicit user opt-out.
                    settings.Version = 3;
                    settings.SoundEnabled = true;
                    settings.VoiceVolume = 0.72;
                    migrated = true;
                }

                if (settings.Version < 4)
                {
                    settings.Version = 4;
                    settings.FastingMode = false;
                    settings.QuietMode = false;
                    migrated = true;
                }

                if (settings.Version < 5)
                {
                    // This release explicitly enables autonomous mischief for existing
                    // installations once; version-five opt-outs remain user preferences.
                    settings.Version = 5;
                    settings.AllowMischief = true;
                    migrated = true;
                }


                settings.VoiceVolume = Math.Clamp(settings.VoiceVolume, 0, 1);
                var normalizedPetSize = PetSizePolicy.ClampPreferredSize(settings.PetSize);
                if (Math.Abs(settings.PetSize - normalizedPetSize) >= 0.01)
                {
                    settings.PetSize = normalizedPetSize;
                    migrated = true;
                }
                if (migrated)
                {
                    Save(settings);
                }

                return settings;
            }
        }
        catch
        {
            // A damaged settings file should never stop the pet from appearing.
        }

        return new PetSettings();
    }

    public void Save(PetSettings settings)
    {
        Directory.CreateDirectory(SettingsDirectory);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, JsonOptions));
    }
}
