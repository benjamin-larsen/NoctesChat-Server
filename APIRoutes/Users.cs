using System.Text.RegularExpressions;
using MySqlConnector;

namespace NoctesChat.APIRoutes;

public static class Users {
    internal static async Task<IResult> Get(CancellationToken ct, string _id) {
        if (!ulong.TryParse(_id, out var id)) {
            return Results.Json(new { error = "Invalid user id." }, statusCode: 400);
        }

        await using var conn = await Database.GetConnection(ct);
        var result = await Database.GetUserById(id, false, conn, ct);

        if (result == null)
            return Results.Json(new { error = "User doesn't exist." }, statusCode: 404);

        return Results.Json(
            new {
                id = result.ID.ToString(),
                username = result.Username,
                created_at = result.CreatedAt
            }, statusCode: 200);
    }

    internal static async Task<IResult> GetByUsername(CancellationToken ct, string username) {
        if (!Regex.IsMatch(username, "^[a-z0-9_]{3,20}$"))
            return Results.Json(new { error = "Invalid username." }, statusCode: 400);

        await using var conn = await Database.GetConnection(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, username, created_at FROM users WHERE username = @username;";
        cmd.Parameters.AddWithValue("@username", username);
        
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        if (!await reader.ReadAsync(ct))
            return Results.Json(new { error = "User doesn't exist." }, statusCode: 404);
        
        return Results.Json(
            new {
                id = reader.GetFieldValue<ulong>(0 /* User ID */).ToString(),
                username = reader.GetFieldValue<string>(1 /* Username */),
                created_at = reader.GetFieldValue<long>(2 /* Created At */)
            }, statusCode: 200);
    }

    internal static async Task<IResult> GetSelf(HttpContext ctx) {
        var ct = ctx.RequestAborted;
        
        var conn = (MySqlConnection)ctx.Items["conn"]!;
        var userId = (ulong)ctx.Items["authId"]!;
        var user = (await Database.GetUserById(userId, true, conn, ct))!;
        
        return Results.Json(
            new {
                id = user.ID.ToString(),
                username = user.Username,
                email = user.Email,
                email_verified = user.EmailVerified,
                created_at = user.CreatedAt
            }, statusCode: 200);
    }
}