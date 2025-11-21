namespace NoctesChat;

using MongoDB.Driver;
using MongoDB.Bson;
using MongoDB.Bson.IO;

// mongod --port 27017 --dbpath "C:\Users\User\RiderProjects\NoctesChat\data"

public class Database
{
    
    public MongoClient Client;
    public IMongoDatabase DB;
    public IMongoCollection<User> Users;

    public Database()
    {
        Client = new MongoClient("mongodb://localhost:27017/");

        DB = Client.GetDatabase("main");
        Users = DB.GetCollection<User>("users");
        
        Users.InsertOne(new User
        {
            Name = "John Doe",
            Password =  "password"
        });
    }

    public async Task<User?> FindUserByName(string name)
    {
        var filter = Builders<User>.Filter.Eq("Name", name);
        return await Users.Find(filter).FirstOrDefaultAsync();
    }

}