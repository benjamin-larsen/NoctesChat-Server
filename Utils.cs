using System.Text.RegularExpressions;

namespace NoctesChat;

public class Utils
{
    public static long GetTime()
    {
        return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    public static byte[] UInt64ToBin(ulong value) {
        var bytes = BitConverter.GetBytes(value);

        if (BitConverter.IsLittleEndian) {
            Array.Reverse(bytes);
        }
        
        return bytes;
    }

    public static string? DecodeDuplicateKeyError(string message) {
        var match = Regex.Match(message, @"^E11000 duplicate key error.*index: ([^ ]+) dup key");
        if (match.Groups.Count != 2) return null;
        
        return match.Groups[1].Value;
    }

    public static ulong UInt64FromBin(byte[] bytes) {
        if (BitConverter.IsLittleEndian) {
            var flippedBytes = bytes.ToArray();
            Array.Reverse(flippedBytes);
            
            return BitConverter.ToUInt64(flippedBytes);
        }
        
        return BitConverter.ToUInt64(bytes);
    }
}