using System.Security.Cryptography;
using MySqlConnector;
using NoctesChat.RequestModels;

namespace NoctesChat.APIRoutes;

public class Auth {
    internal static async Task<IResult> Logout(HttpContext ctx) {
        var userId = (ulong)ctx.Items["authId"]!;
        var keyHash = (byte[])ctx.Items["authKeyHash"]!;
        
        var conn = (MySqlConnection)ctx.Items["conn"]!;
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM user_tokens WHERE user_id = @user_id AND key_hash = @key_hash";

        cmd.Parameters.AddWithValue("@user_id", userId);
        cmd.Parameters.AddWithValue("@key_hash", keyHash);
        
        var rowsDeleted = await cmd.ExecuteNonQueryAsync();

        if (rowsDeleted != 1)
            return Results.Json(new { error = "Logout Failed." }, statusCode: 500);

        return Results.Json(new { ok = true }, statusCode: 200);
    }
    
    internal static async Task<IResult> Login(HttpContext ctx) {
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

    internal static async Task<IResult> Register(HttpContext ctx) {
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
    
    internal static async ValueTask<object?> Middleware(
        EndpointFilterInvocationContext context, EndpointFilterDelegate next) {
        var headers = context.HttpContext.Request.Headers;

        if (!headers.TryGetValue("Authorization", out var key))
            return Results.Json(new { error = "You need to be logged in." }, statusCode: 401);

        var parsedToken = UserToken.DecodeToken(key!);
        
        if (!parsedToken.success)
            return Results.Json(new { error = "Invalid token." }, statusCode: 400);
        
        var keyHash = SHA256.HashData(parsedToken.token);
        
        await using var conn = await Database.GetConnection();
        var hasToken = await Database.HasUserToken(parsedToken.userID, keyHash, conn);
        
        if (!hasToken)
            return Results.Json(new { error = "You've been logged out. Please log in and try again." }, statusCode: 401);
        
        context.HttpContext.Items["authId"] = parsedToken.userID;
        context.HttpContext.Items["authKeyHash"] = keyHash;
        context.HttpContext.Items["conn"] = conn;
        
        return await next(context);
    }
}