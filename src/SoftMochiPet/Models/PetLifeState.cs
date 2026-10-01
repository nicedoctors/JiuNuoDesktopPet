using System.IO;
using System.Text.Json;

namespace SoftMochiPet.Models;

public sealed class PetLifeState
{
    public int Version { get; set; } = 1;
    public double Hunger { get; set; } = 36;
    public double Fullness { get; set; } = 24;
    public double Sleepiness { get; set; } = 20;
    public double Curiosity { get; set; } = 48;
    public int TotalMeals { get; set; }
    public string FavoriteFoodType { get; set; } = string.Empty;
    public Dictionary<string, int> FoodTypeCounts { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public DateTimeOffset LastUpdatedUtc { get; set; } = DateTimeOffset.UtcNow;

    public void Advance(TimeSpan elapsed, bool sleeping)
    {
        var minutes = Math.Clamp(elapsed.TotalMinutes, 0, 12 * 60);
        if (minutes <= 0)
        {
            return;
        }

        if (sleeping)
        {
            Hunger += 0.12 * minutes;
            Fullness -= 0.20 * minutes;
            Sleepiness -= 36 * minutes;
            Curiosity += 0.08 * minutes;
        }
        else
        {
            Hunger += 0.18 * minutes;
            Fullness -= 0.32 * minutes;
            Sleepiness += 0.13 * minutes;
            Curiosity += 0.22 * minutes;
        }

        ClampNeeds();
    }

    public void RegisterMeal(string path, double nourishment)
    {
        nourishment = Math.Clamp(nourishment, 2, 24);
        Hunger -= nourishment * 0.92;
        Fullness += nourishment;
        Sleepiness += nourishment * 0.055;
        Curiosity -= Math.Min(3, nourishment * 0.12);
        TotalMeals++;

        var foodType = GetFoodType(path);
        FoodTypeCounts[foodType] = FoodTypeCounts.GetValueOrDefault(foodType) + 1;
        FavoriteFoodType = FoodTypeCounts
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .First().Key;
        ClampNeeds();
    }

    public void RegisterExploration(double amount = 18)
    {
        Curiosity -= Math.Clamp(amount, 2, 30);
        Hunger += 0.8;
        ClampNeeds();
    }

    public void ClampNeeds()
    {
        Hunger = ClampNeed(Hunger, 36);
        Fullness = ClampNeed(Fullness, 24);
        Sleepiness = ClampNeed(Sleepiness, 20);
        Curiosity = ClampNeed(Curiosity, 48);
    }

    private static double ClampNeed(double value, double fallback) =>
        double.IsFinite(value) ? Math.Clamp(value, 0, 100) : fallback;

    private static string GetFoodType(string path)
    {
        if (path.EndsWith(".snack", StringComparison.OrdinalIgnoreCase))
        {
            return "点心";
        }

        var extension = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        return string.IsNullOrWhiteSpace(extension) ? "文件夹" : extension;
    }
}

public sealed class PetLifeStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly Func<DateTimeOffset> _utcNow;

    public PetLifeStateStore(string? stateDirectory = null, Func<DateTimeOffset>? utcNow = null)
    {
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        StateDirectory = stateDirectory ?? Path.Combine(appData, "SoftMochiPet");
        StatePath = Path.Combine(StateDirectory, "life-state.json");
    }

    public string StateDirectory { get; }
    public string StatePath { get; }

    public PetLifeState Load()
    {
        var now = _utcNow();
        try
        {
            if (File.Exists(StatePath))
            {
                var state = JsonSerializer.Deserialize<PetLifeState>(File.ReadAllText(StatePath), JsonOptions)
                    ?? new PetLifeState();
                state.FoodTypeCounts = new Dictionary<string, int>(
                    state.FoodTypeCounts ?? new Dictionary<string, int>(),
                    StringComparer.OrdinalIgnoreCase);
                var offlineElapsed = now - state.LastUpdatedUtc;
                state.Advance(TimeSpan.FromMinutes(Math.Clamp(offlineElapsed.TotalMinutes, 0, 12 * 60)), sleeping: false);
                state.LastUpdatedUtc = now;
                state.ClampNeeds();
                return state;
            }
        }
        catch
        {
            // Life-state corruption should reset gently instead of preventing startup.
        }

        return new PetLifeState { LastUpdatedUtc = now };
    }

    public void Save(PetLifeState state)
    {
        state.ClampNeeds();
        Directory.CreateDirectory(StateDirectory);
        var previousUpdatedAt = state.LastUpdatedUtc;
        var savedAt = _utcNow();
        var temporaryPath = Path.Combine(
            StateDirectory,
            $".life-state.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp");

        try
        {
            state.LastUpdatedUtc = savedAt;
            var json = JsonSerializer.Serialize(state, JsonOptions);
            state.LastUpdatedUtc = previousUpdatedAt;
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, StatePath, overwrite: true);
            state.LastUpdatedUtc = savedAt;
        }
        catch
        {
            state.LastUpdatedUtc = previousUpdatedAt;
            try
            {
                File.Delete(temporaryPath);
            }
            catch
            {
                // A failed cleanup must not hide the original save failure.
            }

            throw;
        }
    }
}
