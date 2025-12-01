namespace NoctesChat;

public class SnowflakeGen
{
    public long BaseTimestamp;
    private long lastTimestamp;
    private int sequence = 0;
    private readonly object _lock = new();

    public static int maxSequence = (1 << 16) - 1;
    public static long maxTimestamp = (1L << 48) - 1;

    public SnowflakeGen(long timestamp)
    {
        BaseTimestamp = timestamp;
    }

    public ulong ConvertFromTimestamp(long time, int _sequence) {
        var relativeTime = time - BaseTimestamp;
        
        if (_sequence > maxSequence || relativeTime > maxTimestamp || _sequence < 0 || relativeTime < 0)
        {
            throw new OverflowException("Sequence and/or Timestamp overflow their boundary.");
        }
        
        return (uint)_sequence | ((ulong)relativeTime << 16);
    }

    public ulong Generate()
    {
        // if time < lastTimestamp sleep for lastTimestamp - time and goto start
        // if currentSequence > maxSequence sleep for 1ms and goto start
        lock (_lock) {
            var time = Utils.GetTime();
            var relativeTime = time - BaseTimestamp;

            if (time > lastTimestamp) sequence = 0;
            lastTimestamp = time;

            var currentSequence = sequence++;

            if (currentSequence > maxSequence || relativeTime > maxTimestamp || currentSequence < 0 || relativeTime < 0)
            {
                throw new OverflowException("Sequence and/or Timestamp overflow their boundary.");
            }

            return (uint)currentSequence | ((ulong)relativeTime << 16);
        }
    }
}