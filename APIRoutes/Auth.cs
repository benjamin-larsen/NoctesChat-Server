using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text.Json;
using MySqlConnector;
using NoctesChat.DataModels;
using NoctesChat.RequestModels;
using NoctesChat.ResponseModels;
using NoctesChat.WSRequestModels;

namespace NoctesChat.APIRoutes;

public static class Auth {
    internal static async Task<IResult> Logout(HttpContext ctx) {
        var userId = (ulong)ctx.Items["authId"]!;
        var keyHash = (byte[])ctx.Items["authKeyHash"]!;
        
        var ct = ctx.RequestAborted;
        
        await using var conn = await Database.GetConnection(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM user_tokens WHERE user_id = @user_id AND key_hash = @key_hash";

        cmd.Parameters.AddWithValue("@user_id", userId);
        cmd.Parameters.AddWithValue("@key_hash", keyHash);

        var rowsDeleted = await cmd.ExecuteNonQueryAsync(ct);

        if (rowsDeleted != 1)
            return Results.Json(new ErrorResponse("Logout Failed."), statusCode: 500);

        var userToken = new UserToken(userId, keyHash);

        WSServer.UserTokens.SendCloseAndForget(
            userToken,
            new WSAuthError("You've been logged out.", 401),
            WebSocketCloseStatus.NormalClosure,
            "You've been logged out."
        );

        return Results.Json(new { ok = true }, statusCode: 200);
    }
    
    internal static async Task<IResult> Login(HttpContext ctx) {
        LoginBody? reqBody = null;
        
        var ct = ctx.RequestAborted;

        try {
            reqBody = await ctx.Request.ReadFromJsonAsync<LoginBody>(ct);
        } catch {}
        
        if (reqBody == null)
            return Results.Json(new ErrorResponse("Invalid JSON"), statusCode: 400);

        var result = LoginValidator.Instance.Validate(reqBody);
        
        if (!result.IsValid)
            return Results.Json(new ErrorResponse(result.Errors[0].ErrorMessage), statusCode: 400);
        
        byte[] token;
        UserLoginData? loginData;
        
        await using var conn = await Database.GetConnection(ct);
        await using var txn = await conn.BeginTransactionAsync(ct);

        try {
            loginData = await Database.GetUserForLogin(reqBody.Email, conn, txn, ct);
            
            if (loginData == null || !CryptographicOperations.FixedTimeEquals(
                    User.HashPassword(reqBody.Password, loginData.PasswordSalt!), 
                    loginData.PasswordHash!)) {
                await txn.RollbackAsync();
                return Results.Json(new ErrorResponse("Email or password is wrong."), statusCode: 400);
            }
            
            token = UTokenService.GenerateToken();
            var tokenHash = SHA256.HashData(token);

            if (!await Database.InsertUserToken(loginData.ID, tokenHash, Utils.GetTime(), conn, txn, ct))
                throw new Exception("Failed to insert user token into Database.");

            await txn.CommitAsync(ct);
        }
        catch {
            await txn.RollbackAsync();
            throw;
        }
        
        return Results.Json(new { token = UTokenService.EncodeToken(loginData.ID, token), id = loginData.ID.ToString() }, statusCode: 200);
    }

    internal static async Task<IResult> Register(HttpContext ctx) {
        RegisterBody? reqBody = null;
        
        var ct = ctx.RequestAborted;

        try {
            reqBody = await ctx.Request.ReadFromJsonAsync<RegisterBody>(ct);
        } catch {}
        
        if (reqBody == null)
            return Results.Json(new ErrorResponse("Invalid JSON"), statusCode: 400);

        var result = RegisterValidator.Instance.Validate(reqBody);
        
        if (!result.IsValid)
            return Results.Json(new ErrorResponse(result.Errors[0].ErrorMessage), statusCode: 400);

        var (pwdHash, pwdSalt) = User.HashPassword(reqBody.Password);

        var user = new User {
            ID = await Database.UserIDGenerator.Generate(ct),
            Username = reqBody.Username,
            Email = reqBody.Email,
            EmailVerified = false,
            PasswordHash = pwdHash,
            PasswordSalt = pwdSalt,
            CreatedAt = Utils.GetTime()
        };

        byte[] token;
        
        await using var conn = await Database.GetConnection(ct);
        await using var txn = await conn.BeginTransactionAsync(ct);

        try {
            if (!await Database.InsertUser(user, conn, txn, ct))
                throw new Exception("Failed to insert user into Database.");
            
            token = UTokenService.GenerateToken();
            var tokenHash = SHA256.HashData(token);

            if (!await Database.InsertUserToken(user.ID, tokenHash, user.CreatedAt, conn, txn, ct))
                throw new Exception("Failed to insert user token into Database.");

            await txn.CommitAsync(ct);
        }
        catch (MySqlException ex) when (ex.ErrorCode == MySqlErrorCode.DuplicateKeyEntry) {
            await txn.RollbackAsync();
            
            var index = Utils.DecodeDuplicateKeyError(ex.Message);

            switch (index) {
                case "users_email":
                    return Results.Json(new ErrorResponse("Email already exists."), statusCode: 400);
                
                case "users_username":
                    return Results.Json(new ErrorResponse("Username already exists."), statusCode: 400);
                
                default:
                    throw;
            }
        }
        catch {
            await txn.RollbackAsync();
            throw;
        }

        return Results.Json(new { token = UTokenService.EncodeToken(user.ID, token), id = user.ID.ToString() }, statusCode: 200);
    }
    
    internal static async ValueTask<object?> Middleware(
        EndpointFilterInvocationContext context, EndpointFilterDelegate next) {
        var headers = context.HttpContext.Request.Headers;

        if (!headers.TryGetValue("Authorization", out var key))
            return Results.Json(new ErrorResponse("You need to be logged in."), statusCode: 401);

        var parsedToken = UTokenService.DecodeToken(key!);
        
        if (!parsedToken.success)
            return Results.Json(new ErrorResponse("Invalid token."), statusCode: 400);
        
        var keyHash = SHA256.HashData(parsedToken.token);
        
        var ct = context.HttpContext.RequestAborted;
        
        var hasToken = await Database.HasUserToken(parsedToken.userID, keyHash, ct);
        
        if (!hasToken)
            return Results.Json(new ErrorResponse("You've been logged out. Please log in and try again."), statusCode: 401);
        
        context.HttpContext.Items["authId"] = parsedToken.userID;
        context.HttpContext.Items["authKeyHash"] = keyHash;
        
        return await next(context);
    }
}