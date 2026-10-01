using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace SoftMochiPet.Services;

/// <summary>Session-scoped references preserve correlation without publishing user content.</summary>
public static class DiagnosticPrivacy
{
    private static readonly byte[] SessionKey = RandomNumberGenerator.GetBytes(32);
    private static readonly Regex LocalPath = new(
        @"(?i)(?:\b[a-z]:[\\/]|\\\\)[^\r\n""<>|]*",
        RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));
    private static readonly Regex Email = new(
        @"[\w.%+-]+@[\w.-]+\.[a-zA-Z]{2,}",
        RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));

    public static string Reference(string value)
    {
        var digest = HMACSHA256.HashData(SessionKey, Encoding.UTF8.GetBytes(value));
        return "[private:" + Convert.ToHexString(digest.AsSpan(0, 8)) + "]";
    }

    public static string Field(string key, string value)
    {
        if (key.Contains("path", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("directory", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("folder", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("title", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("caption", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("username", StringComparison.OrdinalIgnoreCase) ||
            key.Equals("Name", StringComparison.OrdinalIgnoreCase) ||
            key.Equals("DisplayName", StringComparison.OrdinalIgnoreCase) ||
            key.Equals("Text", StringComparison.OrdinalIgnoreCase))
            return Reference(value);
        return Message(value);
    }

    public static string Message(string value)
    {
        try
        {
            var text = LocalPath.Replace(value, match => Reference(match.Value));
            text = Email.Replace(text, match => Reference(match.Value));
            return text.Replace('\r', ' ').Replace('\n', ' ');
        }
        catch (RegexMatchTimeoutException)
        {
            return Reference(value);
        }
    }
}
