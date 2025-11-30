namespace NoctesChat;

public class Message
{
    public ulong ID { get; set; }
    
    public ulong ChannelID { get; set; }

    public ulong Author { get; set; }

    public string Content { get; set; }
    
    public long Timestamp { get; set; }
    
    public long? EditedTimestamp { get; set; }
}