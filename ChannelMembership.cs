namespace NoctesChat;

public class ChannelMembership {
    public ulong UserID { get; set; }
    
    public ulong ChannelID { get; set; }
    
    public long LastAccessed { get; set; }
}