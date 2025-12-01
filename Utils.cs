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

    public static T GetQueryParameter<T>(
        IQueryCollection query,
        string key,
        T defaultValue,
        Func<string, T> parser) {
        if (query.TryGetValue(key, out var values)) {
            if (values.Count > 1)
                throw new APIException($"Multiple '{key}' parameters defined.", 400);//Results.Json(new { error = $"Multiple '{key}' parameters defined." }, statusCode: 400);

            var parsed = parser(values[0]!);

            return parsed;
        }

        return defaultValue;
    }
}