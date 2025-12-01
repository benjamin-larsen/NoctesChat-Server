using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MySqlConnector;
using NoctesChat.RequestModels;

namespace NoctesChat;

public class APIHandler {
    private static async Task<IResult> GetMessages(HttpContext ctx, string _channelId) {
        if (!ulong.TryParse(_channelId, out var channelId)) {
            return Results.Json(new { error = "Invalid channel id." }, statusCode: 400);
        }

        return Results.Json(new { error = "Not Implemented" }, statusCode: 501);
    }
    
    private static async Task<IResult> PostMessage(HttpContext ctx, string _channelId) {
        if (!ulong.TryParse(_channelId, out var channelId)) {
            return Results.Json(new { error = "Invalid channel id." }, statusCode: 400);
        }
        
        PostMessageBody? reqBody = null;

        try {
            reqBody = await ctx.Request.ReadFromJsonAsync<PostMessageBody>();
        } catch {}
        
        if (reqBody == null)
            return Results.Json(new { error = "Invalid JSON" }, statusCode: 400);

        var result = PostMessageValidator.Instance.Validate(reqBody);
        
        if (!result.IsValid)
            return Results.Json(new { error = result.Errors[0].ErrorMessage }, statusCode: 400);

        ulong messageId;
        long creationTime;

        var userId = (ulong)ctx.Items["authId"]!;
        object? user = null;

        await using var conn = await Database.GetConnection();
        await using var txn = await conn.BeginTransactionAsync();

        try {
            await using (var cmd = conn.CreateCommand()) {
                cmd.Transaction = txn;
                cmd.CommandText = "SELECT id, username, created_at FROM users WHERE id = @id;";

                cmd.Parameters.AddWithValue("@id", userId);
            
                await using var reader = await cmd.ExecuteReaderAsync();

                if (!await reader.ReadAsync()) throw new Exception("Failed to get user");

                user = new {
                    id = reader.GetFieldValue<ulong>(0 /* User ID */).ToString(),
                    username = reader.GetFieldValue<string>(1 /* Username */),
                    created_at = reader.GetFieldValue<long>(2 /* Created At */),
                };
            }

            if (!await Database.ExistsInChannel(userId, channelId, conn, txn)) {
                await txn.RollbackAsync();
                return Results.Json(new { error = "Unknown Channel." }, statusCode: 404);
            }
            
            messageId = Database.ChannelIDGenerator.Generate();
            creationTime = Utils.GetTime();

            await using (var cmd = conn.CreateCommand()) {
                cmd.Transaction = txn;
                cmd.CommandText = """
                                  INSERT INTO messages
                                  (id, channel_id, author_id, content, `timestamp`, edited_timestamp)
                                  VALUES(@id, @channel_id, @user_id, @content, @timestamp, NULL);
                                  """;
            
                cmd.Parameters.AddWithValue("@id", messageId);
                cmd.Parameters.AddWithValue("@channel_id", channelId);
                cmd.Parameters.AddWithValue("@user_id", userId);
                cmd.Parameters.AddWithValue("@content", reqBody.Content);
                cmd.Parameters.AddWithValue("@timestamp", creationTime);
            
                var rowsInserted = await cmd.ExecuteNonQueryAsync();
            
                if (rowsInserted != 1) throw new  Exception("Failed to insert message.");
            }

            await txn.CommitAsync();
        }
        catch {
            await txn.RollbackAsync();
            throw;
        }
        
        // Send WS stuff here
        
        return Results.Json(new {
            id = messageId.ToString(),
            channel = channelId.ToString(),
            author = user,
            content = reqBody.Content,
            timestamp = creationTime,
            edited = (long?)null
        }, statusCode: 200);
    }
    
    private static async Task<IResult> GetChannels(HttpContext ctx) {
        var userId = (ulong)ctx.Items["authId"]!;
        var channelList = new List<object>();
        
        await using var conn = await Database.GetConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
                          SELECT
                              cm.channel_id AS id,
                              cm.last_accessed,
                              c.name,
                              c.member_count,
                              c.created_at,
                              o.id AS owner_id,
                              o.username AS owner_username,
                              o.created_at AS owner_created_at
                          FROM channel_members cm
                          JOIN channels c ON cm.channel_id = c.id
                          LEFT JOIN users o ON c.owner = o.id
                          WHERE cm.user_id = @user_id;
                          """;
        
        cmd.Parameters.AddWithValue("@user_id", userId);
        
        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync()) {
            var hasOwner = !reader.IsDBNull(5 /* Owner ID */);
            
            channelList.Add(new {
                id = reader.GetFieldValue<ulong>(0 /* Channel ID */).ToString(),
                name = reader.GetFieldValue<string>(2 /* Channel Name */),
                owner = hasOwner ? new {
                    id = reader.GetFieldValue<ulong>(5 /* Owner ID */).ToString(),
                    username = reader.GetFieldValue<string>(6 /* Owner Username */),
                    created_at = reader.GetFieldValue<long>(7 /* Owner Created At */),
                } : null,
                member_count = reader.GetFieldValue<uint>(3 /* Channel Member Count */),
                created_at = reader.GetFieldValue<long>(4 /* Channel Created At */),
                last_accessed = reader.GetFieldValue<long>(1 /* Last Accessed */)
            });
        }
        
        return Results.Json(new {
            channels = channelList,
        }, statusCode: 200);
    }

    private static async Task<IResult> CreateChannel(HttpContext ctx) {
        var userId = (ulong)ctx.Items["authId"]!;

        CreateChannelBody? reqBody = null;

        try {
            reqBody = await ctx.Request.ReadFromJsonAsync<CreateChannelBody>();
        } catch {}
        
        if (reqBody == null)
            return Results.Json(new { error = "Invalid JSON" }, statusCode: 400);

        var result = CreateChannelValidator.Instance.Validate(reqBody);
        
        if (!result.IsValid)
            return Results.Json(new { error = result.Errors[0].ErrorMessage }, statusCode: 400);
        
        if (reqBody.Members.Contains(userId))
            return Results.Json(new { error = "You are already implicitly added in this channel." }, statusCode: 400);

        var channelId = Database.ChannelIDGenerator.Generate();
        var creationTime = Utils.GetTime();
        object? user = null;
        
        await using var conn = await Database.GetConnection();
        await using var txn = await conn.BeginTransactionAsync();

        try {
            await using (var cmd = conn.CreateCommand()) {
                cmd.Transaction = txn;
                cmd.CommandText = "SELECT id, username, created_at FROM users WHERE id = @id;";

                cmd.Parameters.AddWithValue("@id", userId);
            
                await using var reader = await cmd.ExecuteReaderAsync();

                if (!await reader.ReadAsync()) throw new Exception("Failed to get user");

                user = new {
                    id = reader.GetFieldValue<ulong>(0 /* User ID */).ToString(),
                    username = reader.GetFieldValue<string>(1 /* Username */),
                    created_at = reader.GetFieldValue<long>(2 /* Created At */),
                };
            }

            await using (var cmd = conn.CreateCommand()) {
                cmd.Transaction = txn;
                cmd.CommandText =
                    "INSERT INTO channels (id, owner, name, member_count, created_at) VALUES(@id, @owner, @name, 0, @created_at);";

                cmd.Parameters.AddWithValue("@id", channelId);
                cmd.Parameters.AddWithValue("@owner", userId);
                cmd.Parameters.AddWithValue("@name", reqBody.Name);
                cmd.Parameters.AddWithValue("@created_at", creationTime);

                var rowsInserted = await cmd.ExecuteNonQueryAsync();

                if (rowsInserted != 1) throw new Exception("Failed to insert channel");
            }
            
            var sb = new StringBuilder("INSERT INTO channel_members (user_id, channel_id, last_accessed) VALUES");
            sb.Append($"({userId.ToString()},@channel_id,@last_accessed)");

            foreach (var member in reqBody.Members) {
                // Normally inserting User Input (Member ID) directly into a SQL Query would be unsafe.
                // However, because Members is an Array of ULONG (Unsigned 64-Bit Integers) which has been parsed by ASP.NET.
                // It would mean any unsafe input would've caused a Parse Fault.
                // It would also be logically impossible for an unsafe input to be put into a number that is converted back into a string.

                sb.Append($",({member.ToString()},@channel_id,@last_accessed)");
            }

            await using (var cmd = conn.CreateCommand()) {
                cmd.Transaction = txn;
                cmd.CommandText = sb.ToString();

                cmd.Parameters.AddWithValue("@channel_id", channelId);
                cmd.Parameters.AddWithValue("@last_accessed", creationTime);

                var rowsInserted = await cmd.ExecuteNonQueryAsync();

                if (rowsInserted != (reqBody.Members.Length + 1)) throw new Exception("Failed to insert channel members");
            }

            await txn.CommitAsync();
        }
        catch (MySqlException ex) when (ex.ErrorCode == MySqlErrorCode.NoReferencedRow2) {
            await txn.RollbackAsync();
            
            if (ex.Message.Contains("FOREIGN KEY (`user_id`)"))
                return Results.Json(new { error = "One or more members doesn't exist." }, statusCode: 400);

            throw;
        }
        catch {
            await txn.RollbackAsync();
            throw;
        }

        return Results.Json(new {
            id = channelId.ToString(),
            name = reqBody.Name,
            owner = user,
            member_count = reqBody.Members.Length + 1,
            created_at = creationTime,
        }, statusCode: 200);
    }

    private static async Task<IResult> Logout(HttpContext ctx) {
        var userId = (ulong)ctx.Items["authId"]!;
        var keyHash = (byte[])ctx.Items["authKeyHash"]!;
        
        await using var conn = await Database.GetConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM user_tokens WHERE user_id = @user_id AND key_hash = @key_hash";

        cmd.Parameters.AddWithValue("@user_id", userId);
        cmd.Parameters.AddWithValue("@key_hash", keyHash);
        
        var rowsDeleted = await cmd.ExecuteNonQueryAsync();

        if (rowsDeleted != 1)
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
        
        byte[] token;
        User? user;
        
        await using var conn = await Database.GetConnection();
        await using var txn = await conn.BeginTransactionAsync();

        try {
            user = await Database.GetUserForLogin(reqBody.Email, conn, txn);
            
            if (user == null || !CryptographicOperations.FixedTimeEquals(
                    User.HashPassword(reqBody.Password, user.PasswordSalt!), 
                    user.PasswordHash!)) {
                await txn.RollbackAsync();
                return Results.Json(new { error = "Email or password is wrong." }, statusCode: 400);
            }
            
            token = UserToken.GenerateToken();
            var tokenHash = SHA256.HashData(token);

            if (!await Database.InsertUserToken(user.ID, tokenHash, Utils.GetTime(), conn, txn))
                throw new Exception("Failed to insert user token into Database.");

            await txn.CommitAsync();
        }
        catch {
            await txn.RollbackAsync();
            throw;
        }
        
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
            ID = Database.UserIDGenerator.Generate(),
            Username = reqBody.Username,
            Email = reqBody.Email,
            EmailVerified = false,
            PasswordHash = pwdHash,
            PasswordSalt = pwdSalt,
            CreatedAt = Utils.GetTime()
        };

        byte[] token;
        
        await using var conn = await Database.GetConnection();
        await using var txn = await conn.BeginTransactionAsync();

        try {
            if (!await Database.InsertUser(user, conn, txn))
                throw new Exception("Failed to insert user into Database.");
            
            token = UserToken.GenerateToken();
            var tokenHash = SHA256.HashData(token);

            if (!await Database.InsertUserToken(user.ID, tokenHash, user.CreatedAt, conn, txn))
                throw new Exception("Failed to insert user token into Database.");

            await txn.CommitAsync();
        }
        catch (MySqlException ex) when (ex.ErrorCode == MySqlErrorCode.DuplicateKeyEntry) {
            await txn.RollbackAsync();
            
            var index = Utils.DecodeDuplicateKeyError(ex.Message);

            switch (index) {
                case "users_email":
                    return Results.Json(new { error = "Email already exists." }, statusCode: 400);
                
                case "users_username":
                    return Results.Json(new { error = "Username already exists." }, statusCode: 400);
                
                default:
                    throw;
            }
        }
        catch {
            await txn.RollbackAsync();
            throw;
        }

        return Results.Json(new { token = UserToken.EncodeToken(user.ID, token), id = user.ID.ToString() }, statusCode: 200);
    }

    private static async Task<IResult> GetUser(string _id) {
        if (!ulong.TryParse(_id, out var id)) {
            return Results.Json(new { error = "Invalid user id." }, statusCode: 400);
        }

        var result = await Database.GetUserById(id, false, false);

        if (result == null)
            return Results.Json(new { error = "User doesn't exist." }, statusCode: 404);

        return Results.Json(
            new {
                id = result.ID.ToString(),
                username = result.Username,
                created_at = result.CreatedAt
            }, statusCode: 200);
    }

    private static async Task<IResult> GetUserByUsername(string username) {
        if (!Regex.IsMatch(username, "^[a-z0-9_]{3,20}$"))
            return Results.Json(new { error = "Invalid username." }, statusCode: 400);

        await using var conn = await Database.GetConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, username, created_at FROM users WHERE username = @username;";
        cmd.Parameters.AddWithValue("@username", username);
        
        await using var reader = await cmd.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
            return Results.Json(new { error = "User doesn't exist." }, statusCode: 404);
        
        return Results.Json(
            new {
                id = reader.GetFieldValue<ulong>(0 /* User ID */).ToString(),
                username = reader.GetFieldValue<string>(1 /* Username */),
                created_at = reader.GetFieldValue<long>(2 /* Created At */)
            }, statusCode: 200);
    }

    private static async Task<IResult> GetSelfUser(HttpContext ctx) {
        var userId = (ulong)ctx.Items["authId"]!;
        var user = (await Database.GetUserById(userId, true, false))!;
        
        return Results.Json(
            new {
                id = user.ID.ToString(),
                username = user.Username,
                email = user.Email,
                email_verified = user.EmailVerified,
            }, statusCode: 200);
    }

    private static IResult NotFound() {
        return Results.Json(new { error = "API Endpoint: Not Found." }, statusCode: 404);
    }

    private static async ValueTask<object?> AuthMiddleware(
        EndpointFilterInvocationContext context, EndpointFilterDelegate next) {
        var headers = context.HttpContext.Request.Headers;

        if (!headers.TryGetValue("Authorization", out var key))
            return Results.Json(new { error = "You need to be logged in." }, statusCode: 401);

        var parsedToken = UserToken.DecodeToken(key!);
        
        if (!parsedToken.success)
            return Results.Json(new { error = "Invalid token." }, statusCode: 400);
        
        var keyHash = SHA256.HashData(parsedToken.token);
        
        var hasToken = await Database.HasUserToken(parsedToken.userID, keyHash);
        
        if (!hasToken)
            return Results.Json(new { error = "You've been logged out. Please log in and try again." }, statusCode: 401);
        
        context.HttpContext.Items["authId"] = parsedToken.userID;
        context.HttpContext.Items["authKeyHash"] = keyHash;
        
        return await next(context);
    }

    public static void Use(WebApplication app) {
        var apiRouter = app.MapGroup("/api");

        // Authentication
        apiRouter.MapPost("/auth/logout", (Delegate)Logout).AddEndpointFilter(AuthMiddleware);
        apiRouter.MapPost("/auth/login", (Delegate)Login);
        apiRouter.MapPost("/auth/register", (Delegate)Register);

        apiRouter.MapGet("/usernames/{username}", GetUserByUsername);
        apiRouter.MapGet("/users/{_id}", GetUser);
        apiRouter.MapGet("/users/@me", (Delegate)GetSelfUser).AddEndpointFilter(AuthMiddleware);
        apiRouter.MapGet("/channels", (Delegate)GetChannels).AddEndpointFilter(AuthMiddleware);
        apiRouter.MapPost("/channels", (Delegate)CreateChannel).AddEndpointFilter(AuthMiddleware);
        apiRouter.MapPost("/channels/{_channelId}/messages", PostMessage).AddEndpointFilter(AuthMiddleware);
        apiRouter.MapGet("/channels/{_channelId}/messages", GetMessages).AddEndpointFilter(AuthMiddleware);

        apiRouter.MapFallback(NotFound);
    }
}