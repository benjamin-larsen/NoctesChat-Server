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

    public static ulong UInt64FromBin(byte[] bytes) {
        if (BitConverter.IsLittleEndian) {
            var flippedBytes = bytes.ToArray();
            Array.Reverse(flippedBytes);
            
            return BitConverter.ToUInt64(flippedBytes);
        }
        
        return BitConverter.ToUInt64(bytes);
    }
}