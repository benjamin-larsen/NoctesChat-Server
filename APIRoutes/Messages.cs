using NoctesChat.RequestModels;

namespace NoctesChat.APIRoutes;

public class Messages {
    internal static async Task<IResult> GetList(HttpContext ctx, string _channelId) {
        if (!ulong.TryParse(_channelId, out var channelId)) {
            return Results.Json(new { error = "Invalid channel id." }, statusCode: 400);
        }

        var after = Utils.GetQueryParameter<ulong?>(
            ctx.Request.Query, "after", null, (rawValue) => {
                if (!ulong.TryParse(rawValue, out var parsedValue))
                    throw new APIException($"Invalid message id in 'after'", 400);

                return parsedValue;
            }
        );
        
        var before = Utils.GetQueryParameter<ulong?>(
            ctx.Request.Query, "before", null, (rawValue) => {
                if (!ulong.TryParse(rawValue, out var parsedValue))
                    throw new APIException($"Invalid message id in 'before'", 400);

                return parsedValue;
            }
        );
        
        var includeSelf = Utils.GetQueryParameter<bool>(
            ctx.Request.Query, "include_self", false, (rawValue) => {
                if (!bool.TryParse(rawValue, out var parsedValue))
                    throw new APIException($"Invalid value for 'include_self'", 400);
                
                if (before == null && after == null)
                    throw new APIException("Can't use parameter 'include_self' without 'before' or 'after'", 400);

                return parsedValue;
            }
        );
        
        var limit = Utils.GetQueryParameter<int>(
            ctx.Request.Query, "limit", 50, (rawValue) => {
                if (!int.TryParse(rawValue, out var parsedValue))
                    throw new APIException($"Invalid value for 'limit'", 400);
                
                if (parsedValue < 1)
                    throw new APIException("Limit must be at least 1.", 400);
                
                if (parsedValue > 50)
                    throw new APIException("Limit must not be more than 50.", 400);

                return parsedValue;
            }
        );
        
        if (before != null && after != null)
            return Results.Json(new { error = "Both 'after' and 'before' parameters defined, you can only use one." }, statusCode: 400);
        
        var userId = (ulong)ctx.Items["authId"]!;
        
        await using var conn = await Database.GetConnection();
        if (!await Database.ExistsInChannel(userId, channelId, conn, null))
            return Results.Json(new { error = "Unknown Channel." }, statusCode: 404);
        
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
                          SELECT
                              m.id,
                              m.content,
                              m.`timestamp`,
                              m.edited_timestamp,
                              a.id AS author_id,
                              a.username AS author_username,
                              a.created_at AS author_created_at
                          FROM messages m FORCE INDEX (index_channel_messages)
                          LEFT JOIN users a ON m.author_id = a.id
                          WHERE m.channel_id = @channel_id{(
                              after != null ? $" AND m.id {(includeSelf ? "<=" : "<")} @after_id" :
                              before != null ? $" AND m.id {(includeSelf ? ">=" : ">")} @before_id" : "")}
                          ORDER BY id DESC
                          LIMIT {(limit + 1).ToString()};
                          """;
        
        cmd.Parameters.AddWithValue("@channel_id", channelId);

        if (after != null) {
            cmd.Parameters.AddWithValue("@after_id", after!);
        } else if (before != null) {
            cmd.Parameters.AddWithValue("@before_id", before!);
        }

        var hasMore = false;
        var messageList = new List<object>();
        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync()) {
            if (messageList.Count >= limit) {
                hasMore = true;
                break;
            }

            var hasAuthor = !reader.IsDBNull(4 /* Author ID */);
            
            messageList.Add(new {
                id = reader.GetFieldValue<ulong>(0 /* Message ID */).ToString(),
                channel = channelId.ToString(),
                author = hasAuthor ? new {
                    id = reader.GetFieldValue<ulong>(4 /* Author ID */).ToString(),
                    username = reader.GetFieldValue<string>(5 /* Author Username */),
                    created_at = reader.GetFieldValue<long>(6 /* Author Created At */),
                } : null,
                content = reader.GetFieldValue<string>(1 /* Message Content */),
                timestamp = reader.GetFieldValue<long>(2 /* Message Timestamp */),
                edited = reader.IsDBNull(3 /* Message Edited Timestamp */) ?
                    (long?)null : reader.GetFieldValue<long>(3 /* Message Edited Timestamp */)
            });
        }

        return Results.Json(new { messages = messageList, has_more = hasMore }, statusCode: 200);
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