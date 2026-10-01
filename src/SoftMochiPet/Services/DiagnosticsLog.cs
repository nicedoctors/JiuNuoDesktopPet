using System.IO;
using System.Globalization;
using System.Text;
using System.Diagnostics;

namespace SoftMochiPet.Services;

public static class DiagnosticsLog
{
    private static readonly object SyncRoot = new();
    private static readonly Dictionary<string, DateTimeOffset> LastThrottledWrite = new(StringComparer.Ordinal);
    private const long MaximumLogBytes = 8 * 1024 * 1024;

    public static string SessionId { get; } = $"{DateTimeOffset.Now:yyyyMMdd-HHmmss}-{Environment.ProcessId}";

    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SoftMochiPet",
        "activity.log");

    public static void Write(string message, Exception? exception = null)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} [PID {Environment.ProcessId}] {DiagnosticPrivacy.Message(message)}";
            if (exception is not null)
            {
                line += $" | {exception.GetType().Name} HResult=0x{exception.HResult:X8}";
                var frames = new StackTrace(exception, fNeedFileInfo: false).GetFrames();
                if (frames is { Length: > 0 })
                    line += " | Stack: " + string.Join(" <- ", frames.Take(12).Select(frame =>
                    {
                        var method = frame.GetMethod();
                        return method is null ? "unknown" : $"{method.DeclaringType?.FullName}.{method.Name}";
                    }));
            }

            lock (SyncRoot)
            {
                RotateIfNeeded();
                File.AppendAllText(FilePath, line + Environment.NewLine, new UTF8Encoding(false));
            }
        }
        catch
        {
            // Diagnostics must never interrupt the pet.
        }
    }

    public static void WriteEvent(string eventName, params (string Key, object? Value)[] fields)
    {
        var message = new StringBuilder(128)
            .Append("TRACE session=")
            .Append(SessionId)
            .Append(" event=")
            .Append(SanitizeToken(eventName));
        foreach (var (key, value) in fields)
        {
            message.Append(' ')
                .Append(SanitizeToken(key))
                .Append('=')
                .Append(FormatValue(key, value));
        }

        Write(message.ToString());
    }

    public static void WriteEventThrottled(
        string throttleKey,
        TimeSpan minimumInterval,
        string eventName,
        params (string Key, object? Value)[] fields)
    {
        var now = DateTimeOffset.UtcNow;
        lock (SyncRoot)
        {
            if (LastThrottledWrite.TryGetValue(throttleKey, out var previous) &&
                now - previous < minimumInterval)
            {
                return;
            }

            LastThrottledWrite[throttleKey] = now;
        }

        WriteEvent(eventName, fields);
    }

    private static string FormatValue(string key, object? value)
    {
        if (value is null)
        {
            return "null";
        }

        var text = value switch
        {
            double number => number.ToString("0.###", CultureInfo.InvariantCulture),
            float number => number.ToString("0.###", CultureInfo.InvariantCulture),
            decimal number => number.ToString(CultureInfo.InvariantCulture),
            DateTimeOffset date => date.ToString("O", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty,
        };
        text = value is string ? DiagnosticPrivacy.Field(key, text) : DiagnosticPrivacy.Message(text);
        return $"\"{text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
    }

    private static string SanitizeToken(string value)
    {
        return string.Concat(value.Select(character =>
            char.IsLetterOrDigit(character) || character is '_' or '-' or '.' ? character : '_'));
    }

    private static void RotateIfNeeded()
    {
        var file = new FileInfo(FilePath);
        if (!file.Exists || file.Length < MaximumLogBytes)
        {
            return;
        }

        var previousPath = Path.Combine(file.DirectoryName!, "activity.previous.log");
        File.Move(FilePath, previousPath, overwrite: true);
    }
}
