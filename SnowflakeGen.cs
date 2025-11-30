namespace NoctesChat;

public class SnowflakeGen
{
    private long _timestamp;
    private long lastTimestamp;
    private int sequence = 0;
    private readonly object _lock = new();

    private static int maxSequence = (1 << 16) - 1;
    private static long maxTimestamp = (1L << 48) - 1;

    public SnowflakeGen(long timestamp)
    {
        _timestamp = timestamp;
    }

    public ulong Generate()
    {
        // if time < lastTimestamp sleep for lastTimestamp - time and goto start
        // if currentSequence > maxSequence sleep for 1ms and goto start
        lock (_lock) {
            var time = Utils.GetTime();
            var relativeTime = time - _timestamp;

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