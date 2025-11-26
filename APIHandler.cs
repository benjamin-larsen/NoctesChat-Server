using System.Security.Cryptography;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using NoctesChat.RequestModels;

namespace NoctesChat;

public class APIHandler {
    private static async Task<IResult> GetChannels(HttpContext ctx) {
        var auth = (User)ctx.Items["auth"]!;

        var channelBuilder = Database.ChannelMembers.Aggregate()
                                     // Find Channels the User is a Member of.
                                     .Match(u=> u.UserID == auth.ID)
                                     
                                     // Make it so that the User sees the most recently accessed channels first.
                                     .SortByDescending(m=> m.LastAccessed)

                                     // Lookup the Channel Document.
                                     .Lookup<ChannelMembership, Channel, BsonDocument>(
                                         foreignCollection: Database.Channels,
                                         localField: u => u.ChannelID,
                                         foreignField: c => c.ID,
                                         @as: doc => doc["_channel"])
                                     
                                     // Collapse it into one channel document.
                                     .Unwind(doc => doc["_channel"])
                                     
                                     // Count the Members of the Channel.
                                     .Lookup<BsonDocument, ChannelMembership, BsonDocument>(
                                         foreignCollection: Database.ChannelMembers,
                                         localField: doc => doc["channel"],
                                         foreignField: c => c.ChannelID,
                                         @as: doc => doc["MemberCount"])
                                     
                                     // Collapse MemberCount to a number, rather than the Array of Memberships.
                                     .AppendStage<BsonDocument>(new BsonDocument(
                                         "$addFields",
                                         new BsonDocument(
                                             "MemberCount",
                                             new BsonDocument("$size", "$MemberCount")
                                         )
                                     ));
        
        var channelList = await channelBuilder.ToListAsync();
        
        return Results.Json(new {
            channels = channelList.Select(doc=>new {
                id = ((UInt64)doc["channel"].AsInt64).ToString(),
                name = doc["_channel"]["name"].AsString,
                owner = ((UInt64)doc["_channel"]["owner"].AsInt64).ToString(),
                member_count = doc["MemberCount"].AsInt32,
                last_accessed = doc["last_accessed"].AsInt64
            }),
        }, statusCode: 200);
    }

    private static async Task<IResult> CreateChannel(HttpContext ctx) {
        var auth = (User)ctx.Items["auth"]!;
        CreateChannelBody? reqBody = null;

        try {
            reqBody = await ctx.Request.ReadFromJsonAsync<CreateChannelBody>();
        } catch {}
        
        if (reqBody == null)
            return Results.Json(new { error = "Invalid JSON" }, statusCode: 400);
        
        var result = CreateChannelValidator.Instance.Validate(reqBody);
        
        if (!result.IsValid)
            return Results.Json(new { error = result.Errors[0].ErrorMessage }, statusCode: 400);
        
        if (reqBody.Members.Contains(auth.ID.ToString()))
            return Results.Json(new { error = "You are already implicitly added in this channel." }, statusCode: 400);

        var memberIds = reqBody.Members.Select(ulong.Parse);

        var filter = Builders<User>.Filter.In(u => u.ID, memberIds);
        var existingMemberCount = await Database.Users.Find(filter).CountDocumentsAsync();
        
        if (existingMemberCount != reqBody.Members.Length)
            return Results.Json(new { error = "One or more members don't exist." }, statusCode: 400);

        await Database.ChannelMembers.InsertManyAsync();

        return Results.Json(new { test = "test" }, statusCode: 200);
    }

    private static async Task<IResult> Logout(HttpContext ctx) {
        var auth = (User)ctx.Items["auth"]!;
        var keyHash = (byte[])ctx.Items["authKeyHash"]!;

        var filter = Builders<User>.Filter.Eq("id", auth.ID);
        var update = Builders<User>.Update.PullFilter(
            u => u.Tokens,
            Builders<UserToken>.Filter.Eq(t => t.KeyHash, keyHash)
        );
        
        var result = await Database.Users.UpdateOneAsync(filter, update);

        if (result == null || !result.IsAcknowledged || result.ModifiedCount != 1)
            return Results.Json(new { error = "Logout Failed." }, statusCode: 500);

        return Results.Json(new { ok = true }, statusCode: 200);
    }
    
    private static async Task<IResult> Login(HttpContext ctx) {
        LoginBody? reqBody = null;

        try {
            reqBody = await ctx.Request.ReadFromJsonAsync<LoginBody>();
        } catch {}
        
        if (reqBody == null)
            return Results.Json(new { error = "Invalid JSON" }, statusCode: 400);

        var result = LoginValidator.Instance.Validate(reqBody);
        
        if (!result.IsValid)
            return Results.Json(new { error = result.Errors[0].ErrorMessage }, statusCode: 400);
        
        var filter = Builders<User>.Filter.Eq("email", reqBody.Email);
        var user = await Database.Users.Find(filter).FirstOrDefaultAsync();
        
        if (user == null || User.HashPassword(reqBody.Password, user.PasswordSalt) != user.PasswordHash)
            return Results.Json(new { error = "Email or password is wrong." }, statusCode: 400);

        var token = UserToken.GenerateToken();
        var tokenHash = SHA256.HashData(token);

        var dbResult = await Database.Users.UpdateOneAsync(
            Builders<User>.Filter.Eq("email", reqBody.Email),
            Builders<User>.Update.Push(u => u.Tokens,
                new UserToken {
                    KeyHash = tokenHash,
                    CreatedAt = Utils.GetTime()
                })
            );
        
        if (dbResult == null || !dbResult.IsAcknowledged || dbResult.ModifiedCount != 1)
            return Results.Json(new { error = "Login Failed." }, statusCode: 500);
            
        return Results.Json(new { token = UserToken.EncodeToken(user.ID, token), id = user.ID.ToString() }, statusCode: 200);
    }

    private static async Task<IResult> Register(HttpContext ctx) {
        RegisterBody? reqBody = null;

        try {
            reqBody = await ctx.Request.ReadFromJsonAsync<RegisterBody>();
        } catch {}
        
        if (reqBody == null)
            return Results.Json(new { error = "Invalid JSON" }, statusCode: 400);

        var result = RegisterValidator.Instance.Validate(reqBody);
        
        if (!result.IsValid)
            return Results.Json(new { error = result.Errors[0].ErrorMessage }, statusCode: 400);

        var (pwdHash, pwdSalt) = User.HashPassword(reqBody.Password);

        var user = new User {
            ID = Database._userIDGenerator.Generate(),
            Username = reqBody.Username,
            Email = reqBody.Email,
            EmailVerified = false,
            PasswordHash = pwdHash,
            PasswordSalt = pwdSalt,
        };

        var token = UserToken.GenerateToken();
        var tokenHash = SHA256.HashData(token);
        
        user.Tokens.Add(new UserToken {
            KeyHash = tokenHash,
            CreatedAt = Utils.GetTime()
        });

        try {
            await Database.Users.InsertOneAsync(user);
            
            return Results.Json(new { token = UserToken.EncodeToken(user.ID, token), id = user.ID.ToString() }, statusCode: 200);
        }
        catch(MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey) {
            var index = Utils.DecodeDuplicateKeyError(ex.WriteError.Message);

            switch (index) {
                case "email":
                    return Results.Json(new { error = "Email already exists." }, statusCode: 400);
                
                case "username":
                    return Results.Json(new { error = "Username already exists." }, statusCode: 400);
                
                default:
                    throw;
            }
        }
    }

    private static async Task<IResult> GetUser(string _id) {
        if (!UInt64.TryParse(_id, out ulong id)) {
            return Results.Json(new { error = "Invalid user id." }, statusCode: 400);
        }

        User? result = await Database.FindUserByID(id);

        if (result == null) {
            return Results.Json(new { error = "User doesn't exist." }, statusCode: 404);
        }

        return Results.Json(new { id = result.ID.ToString(), username = result.Username, }, statusCode: 200);
    }

    private static IResult GetSelfUser(HttpContext ctx) {
        var user = (User)ctx.Items["auth"]!;

        return Results.Json(
            new {
                id = user.ID.ToString(),
                username = user.Username,
                email = user.Email,
                email_verified = user.EmailVerified,
            },
            statusCode: 200
            );
    }

    private static IResult NotFound() {
        return Results.Json(new { error = "API Endpoint: Not Found." }, statusCode: 404);
    }

    private static async ValueTask<object?> AuthMiddleware(
        EndpointFilterInvocationContext context, EndpointFilterDelegate next) {
        var headers = context.HttpContext.Request.Headers;

        if (!headers.TryGetValue("Authorization", out var key))
            return Results.Json(new { error = "You need to be logged in." }, statusCode: 401);

        var parsedToken = UserToken.DecodeToken(key);
        
        if (!parsedToken.success)
            return Results.Json(new { error = "Invalid token." }, statusCode: 400);
        
        var keyHash = SHA256.HashData(parsedToken.token);
        
        var filter = Builders<User>.Filter.Eq("id", parsedToken.userID) & Builders<User>.Filter.Eq("tokens.key", keyHash);
        var DBResult = await Database.Users.Find(filter).FirstOrDefaultAsync();
        
        if (DBResult == null)
            return Results.Json(new { error = "You've been logged out. Please log in and try again." }, statusCode: 401);
        
        context.HttpContext.Items["auth"] = DBResult;
        context.HttpContext.Items["authKeyHash"] = keyHash;
        
        return await next(context);
    }

    public static void Use(WebApplication app) {
        var apiRouter = app.MapGroup("/api");

        // Authentication
        apiRouter.MapPost("/auth/logout", (Delegate)Logout).AddEndpointFilter(AuthMiddleware);
        apiRouter.MapPost("/auth/login", (Delegate)Login);
        apiRouter.MapPost("/auth/register", (Delegate)Register);

        apiRouter.MapGet("/users/{_id}", GetUser);
        apiRouter.MapGet("/users/@me", GetSelfUser).AddEndpointFilter(AuthMiddleware);
        apiRouter.MapGet("/channels", (Delegate)GetChannels).AddEndpointFilter(AuthMiddleware);
        apiRouter.MapPost("/channels", (Delegate)CreateChannel).AddEndpointFilter(AuthMiddleware);

        apiRouter.MapFallback(NotFound);
    }
}