using System.Text;
using MySqlConnector;
using NoctesChat.RequestModels;

namespace NoctesChat.APIRoutes;

public class Channels {
    internal static async Task<IResult> GetList(HttpContext ctx) {
        var userId = (ulong)ctx.Items["authId"]!;
        var channelList = new List<object>();
        
        var conn = (MySqlConnection)ctx.Items["conn"]!;
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

    internal static async Task<IResult> Create(HttpContext ctx) {
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
        
        var conn = (MySqlConnection)ctx.Items["conn"]!;
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
}