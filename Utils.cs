using System.Text.RegularExpressions;

namespace NoctesChat;

public class Utils
{
    public static long GetTime()
    {
        return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    public static string? DecodeDuplicateKeyError(string message) {
        var match = Regex.Match(message, @"^Duplicate entry .* for key '(?:.*\.)?(.*)'$");
        if (match.Groups.Count != 2) return null;
        
        return match.Groups[1].Value;
    }
}