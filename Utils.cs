namespace NoctesChat;

public class Utils
{
    public static long GetTime()
    {
        return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }
}