using NoctesChat.RequestModels;

namespace NoctesChat.APIRoutes;

public class Messages {
    internal static async Task<IResult> Get(HttpContext ctx, string _channelId) {
        if (!ulong.TryParse(_channelId, out var channelId)) {
            return Results.Json(new { error = "Invalid channel id." }, statusCode: 400);
        }

        return Results.Json(new { error = "Not Implemented" }, statusCode: 501);
    }
    
    internal static async Task<IResult> Post(HttpContext ctx, string _channelId) {
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
}