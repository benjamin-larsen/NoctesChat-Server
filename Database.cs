namespace NoctesChat;

using MongoDB.Driver;
using MongoDB.Bson.Serialization;

// "C:\Program Files\MongoDB\Server\8.0\bin\mongod" --port 27017 --dbpath "C:\Users\User\RiderProjects\NoctesChat\data"

public class Database
{
    private static SnowflakeGen _userIDGenerator = new (1735689600000);
    private static SnowflakeGen _msgIDGenerator = new (1735689600000);
    private static SnowflakeGen _channelIDGenerator = new (1735689600000);

    public static MongoClient Client;
    public static IMongoDatabase DB;
    public static IMongoCollection<User> Users;
    public static IMongoCollection<Message> Messages;
    public static IMongoCollection<Channel> Channels;
    public static IMongoCollection<ChannelMembership> ChannelMembers;
    
    private static bool _isSetup = false;

    public static void Setup()
    {
        if (_isSetup) throw new Exception("Database is already initialized");
        _isSetup = true;

        // Setup Serializers for MongoDB
        BsonSerializer.RegisterSerializer(typeof(ulong), new UInt64DBSerializer());
        Client = new MongoClient(Environment.GetEnvironmentVariable("db_conn") ?? "mongodb://localhost:27017/");

        DB = Client.GetDatabase("main");
        Users = DB.GetCollection<User>("users");
        Messages = DB.GetCollection<Message>("messages");
        Channels = DB.GetCollection<Channel>("channels");
        ChannelMembers = DB.GetCollection<ChannelMembership>("members");
        
        var user_channelList_Index = new CreateIndexModel<ChannelMembership>(
            Builders<ChannelMembership>.IndexKeys.Ascending(m => m.UserID).Descending(u => u.LastAccessed)
        );
        
        var channelMember_Index = new CreateIndexModel<ChannelMembership>(
            Builders<ChannelMembership>.IndexKeys.Ascending(m => m.ChannelID).Ascending(m => m.UserID)
        );
        
        ChannelMembers.Indexes.CreateMany([user_channelList_Index, channelMember_Index]);
        
        var channelID_Index = new CreateIndexModel<Channel>(
            Builders<Channel>.IndexKeys.Ascending(c => c.ID),
            new CreateIndexOptions { Unique = true }
        );

        Channels.Indexes.CreateOne(channelID_Index);

        var userID_Index = new CreateIndexModel<User>(
            Builders<User>.IndexKeys.Ascending(u => u.ID),
            new CreateIndexOptions { Unique = true }
        );
        
        var userEmail_Index = new CreateIndexModel<User>(
            Builders<User>.IndexKeys.Ascending(u => u.Email),
            new CreateIndexOptions { Unique = true }
        );
        
        var userName_Index = new CreateIndexModel<User>(
            Builders<User>.IndexKeys.Ascending(u => u.Username),
            new CreateIndexOptions { Unique = true }
        );

        Users.Indexes.CreateMany([userID_Index, userEmail_Index, userName_Index]);
        
        var msgID_Index = new CreateIndexModel<Message>(
            Builders<Message>.IndexKeys.Ascending(m => m.ID),
            new CreateIndexOptions { Unique = true }
        );
        
        var msg_channel_Index = new CreateIndexModel<Message>(
            Builders<Message>.IndexKeys.Descending(m => m.ID).Ascending(m => m.ChannelID)
        );
        
        Messages.Indexes.CreateMany([msgID_Index, msg_channel_Index]);
        
        var filter = Builders<Message>.Filter.Empty;
        var allMessages = Messages.Find(filter);
        
        using (var cursor = allMessages.ToCursor())
        {
            foreach (var r in cursor.ToEnumerable())
            {
                Console.WriteLine($"ID: {r.ID}, Author: {r.Author}, Content: {r.Content}, Timestamp: {r.Timestamp}, Edited: {r.EditedTimestamp?.ToString() ?? "Not Edited"}");
            }
        }
        
        Console.WriteLine("Connected to MongoDB");
    }

    public static async Task<Message> InsertMessage(ulong author, ulong channel, string content)
    {
        Message message = new Message
        {
            ID = _msgIDGenerator.Generate(),
            ChannelID = channel,
            Author = author,
            Content = content,
            Timestamp = Utils.GetTime()
        };

        await Messages.InsertOneAsync(message);
        return message;
    }
    
    public static async Task<User?> FindUserByID(ulong id)
    {
        var filter = Builders<User>.Filter.Eq("id", id);
        return await Users.Find(filter).FirstOrDefaultAsync();
    }
}