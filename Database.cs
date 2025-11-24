namespace NoctesChat;

using MongoDB.Driver;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Bson.Serialization;

// "C:\Program Files\MongoDB\Server\8.0\bin\mongod" --port 27017 --dbpath "C:\Users\User\RiderProjects\NoctesChat\data"

public class Database
{
    private SnowflakeGen _userIDGenerator = new (1735689600000);
    private SnowflakeGen _msgIDGenerator = new (1735689600000);
    private SnowflakeGen _channelIDGenerator = new (1735689600000);

    public MongoClient Client;
    public IMongoDatabase DB;
    public IMongoCollection<User> Users;
    public IMongoCollection<Message> Messages;
    public IMongoCollection<Channel> Channels;

    static Database()
    {
        // Setup Serializers for MongoDB
        BsonSerializer.RegisterSerializer(typeof(UInt64), new UInt64DBSerializer());
    }

    public Database()
    {
        Client = new MongoClient("mongodb://localhost:27017/");

        DB = Client.GetDatabase("main");
        Users = DB.GetCollection<User>("users");
        Messages = DB.GetCollection<Message>("messages");
        Channels = DB.GetCollection<Channel>("channels");
        
        var channelID_Index = new CreateIndexModel<Channel>(
            Builders<Channel>.IndexKeys.Ascending(c => c.ID),
            new CreateIndexOptions { Unique = true }
        );

        Channels.Indexes.CreateOne(channelID_Index);

        var userID_Index = new CreateIndexModel<User>(
            Builders<User>.IndexKeys.Ascending(u => u.ID),
            new CreateIndexOptions { Unique = true }
        );

        Users.Indexes.CreateOne(userID_Index);
        
        var msgID_Index = new CreateIndexModel<Message>(
            Builders<Message>.IndexKeys.Ascending(m => m.ID),
            new CreateIndexOptions { Unique = true }
        );

        Messages.Indexes.CreateOne(msgID_Index);

        //InsertMessage(18446740003709551610, "Test");
        
        var filter = Builders<Message>.Filter.Empty;
        var allMessages = Messages.Find(filter);
        
        using (var cursor = allMessages.ToCursor())
        {
            foreach (var r in cursor.ToEnumerable())
            {
                Console.WriteLine($"ID: {r.ID}, Author: {r.Author}, Content: {r.Content}, Timestamp: {r.Timestamp}");
            }
        }

        Users.InsertOne(new User
        {
            ID = _userIDGenerator.Generate(),
            Name = "John Doe",
            Password =  "password"
        });
        
        Console.WriteLine("Connected to MongoDB");
    }

    public async Task<Message> InsertMessage(UInt64 author, string content)
    {
        Message message = new Message
        {
            ID = _msgIDGenerator.Generate(),
            Author = author,
            Content = content,
            Timestamp = Utils.GetTime()
        };

        try
        {
            await Messages.InsertOneAsync(message);
            return message;
        }
        catch (Exception e)
        {
            Console.WriteLine(e.Message);
            throw;
        }
    }

    public async Task<User?> FindUserByName(string name)
    {
        var filter = Builders<User>.Filter.Eq("Name", name);
        return await Users.Find(filter).FirstOrDefaultAsync();
    }

}