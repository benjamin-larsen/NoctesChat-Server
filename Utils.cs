using System.Text.RegularExpressions;

namespace NoctesChat;

public static class Utils
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

    public static bool TryParseBool(string str, out bool value) {
        switch (str.Trim().ToLowerInvariant()) {
            case "true":
            case "yes":
            case "t":
            case "y":
            case "1":
            case "yuh":
            case "yup":
            case "yeah": {
                value = true;
                return true;
            }

            case "false":
            case "no":
            case "f":
            case "n":
            case "0":
            case "nah":
            case "nope":
            case "no way": {
                value = false;
                return true;
            }

            default: {
                value = false;
                return false;
            }
        }
    }

    public static T GetQueryParameter<T>(
        IQueryCollection query,
        string key,
        T defaultValue,
        Func<string, T> parser) {
        if (query.TryGetValue(key, out var values)) {
            if (values.Count > 1)
                throw new APIException($"Multiple '{key}' parameters defined.", 400);

            var parsed = parser(values[0]!);

            return parsed;
        }

        return defaultValue;
    }
}