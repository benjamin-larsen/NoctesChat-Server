namespace NoctesChat;

public class SnowflakeGen
{
    private Int64 _timestamp;
    private Int64 lastTimestamp;
    private Int32 sequence = 0;
    private readonly object _lock = new();

    private static Int32 maxSequence = (1 << 16) - 1;
    private static Int64 maxTimestamp = ((Int64)1 << 48) - 1;

    public static Int64 GetTime()
    {
        Int64 now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        return now;
    }

    public SnowflakeGen(Int64 timestamp)
    {
        _timestamp = timestamp;
    }

    public UInt64 Generate()
    {
        var time = GetTime();
        var relativeTime = time - _timestamp;
        lock (_lock) {

        if (time > lastTimestamp) sequence = 0;
        lastTimestamp = time;

        var Sequence = sequence++;

        if (Sequence > maxSequence || relativeTime > maxTimestamp)
        {
            throw new OverflowException("Sequence and/or Timestamp overflow their boundary.");
        }

        return (uint)Sequence | ((UInt64)relativeTime << 16);
        }
    }
}