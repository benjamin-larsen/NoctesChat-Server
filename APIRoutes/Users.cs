using System.Text.RegularExpressions;
using MySqlConnector;
using NoctesChat.ResponseModels;

namespace NoctesChat.APIRoutes;

public static class Users {
    internal static async Task<IResult> Get(CancellationToken ct, string _id) {
        if (!ulong.TryParse(_id, out var id)) {
            return Results.Json(new ErrorResponse("Invalid user id."), statusCode: 400);
        }

        var result = await Database.GetUserById(id, false, ct);

        if (result == null)
            return Results.Json(new ErrorResponse("User doesn't exist."), statusCode: 404);

        return Results.Json(result, statusCode: 200);
    }

    internal static async Task<IResult> GetByUsername(CancellationToken ct, string username) {
        if (!Regex.IsMatch(username, "^[a-z0-9_]{3,20}$"))
            return Results.Json(new ErrorResponse("Invalid username."), statusCode: 400);

        await using var conn = await Database.GetConnection(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, username, created_at FROM users WHERE username = @username;";
        cmd.Parameters.AddWithValue("@username", username);
        
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        if (!await reader.ReadAsync(ct))
            return Results.Json(new ErrorResponse("User doesn't exist."), statusCode: 404);
        
        return Results.Json(UserResponse.FromReader(reader), statusCode: 200);
    }

    internal static async Task<IResult> GetSelf(HttpContext ctx) {
        var ct = ctx.RequestAborted;
        
        var userId = (ulong)ctx.Items["authId"]!;
        var baseUser = (await Database.GetUserById(userId, true, ct))!;
        
        if (baseUser is not AuthenticatedUserResponse authUser)
            throw new Exception("Error in GetSelf: user not found.");
        
        return Results.Json(authUser, statusCode: 200);
    }
}