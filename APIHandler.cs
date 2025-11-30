using System.Security.Cryptography;
using MySqlConnector;
using NoctesChat.RequestModels;

namespace NoctesChat;

public class APIHandler {
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
            var hasOwner = !reader.IsDBNull(4 /* Owner ID */);
            
            channelList.Add(new {
                id = reader.GetFieldValue<ulong>(0 /* Channel ID */).ToString(),
                name = reader.GetFieldValue<string>(2 /* Channel Name */),
                owner = hasOwner ? new {
                    id = reader.GetFieldValue<ulong>(4 /* Owner ID */).ToString(),
                    username = reader.GetFieldValue<string>(5 /* Owner Username */),
                    created_at = reader.GetFieldValue<long>(6 /* Owner Created At */),
                } : null,
                member_count = reader.GetFieldValue<uint>(3 /* Channel Member Count */),
                last_accessed = reader.GetFieldValue<long>(1 /* Last Accessed */)
            });
        }
        
        return Results.Json(new {
            channels = channelList,
        }, statusCode: 200);
    }

    private static IResult CreateChannel(HttpContext ctx) {
        return Results.Json(new { error = "Not Implemented" }, statusCode: 501);
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
            user = await Database.GetUserByEmail(reqBody.Email, conn, txn);
            
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

        apiRouter.MapGet("/users/{_id}", GetUser);
        apiRouter.MapGet("/users/@me", (Delegate)GetSelfUser).AddEndpointFilter(AuthMiddleware);
        apiRouter.MapGet("/channels", (Delegate)GetChannels).AddEndpointFilter(AuthMiddleware);
        apiRouter.MapPost("/channels", (Delegate)CreateChannel).AddEndpointFilter(AuthMiddleware);

        apiRouter.MapFallback(NotFound);
    }
}