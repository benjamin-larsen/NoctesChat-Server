using System.Text;
using MySqlConnector;
using NoctesChat.RequestModels;
using NoctesChat.ResponseModels;
using NoctesChat.WSRequestModels;

namespace NoctesChat.APIRoutes;

public static class Channels {
    internal static async Task<IResult> GetList(HttpContext ctx) {
        var userId = (ulong)ctx.Items["authId"]!;
        var channelList = new List<ChannelResponse>();
        
        var ct = ctx.RequestAborted;
        
        await using var conn = await Database.GetConnection(ct);
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
        
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct)) {
            channelList.Add(ChannelResponse.FromReader(reader));
        }
        
        return Results.Json(new {
            channels = channelList,
        }, statusCode: 200);
    }

    internal static async Task<IResult> GetSingle(HttpContext ctx, string _channelId) {
        if (!ulong.TryParse(_channelId, out var channelId)) {
            return Results.Json(new ErrorResponse("Invalid channel id."), statusCode: 400);
        }
        
        var userId = (ulong)ctx.Items["authId"]!;
        
        var ct = ctx.RequestAborted;
        
        await using var conn = await Database.GetConnection(ct);
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
                          WHERE cm.user_id = @user_id AND cm.channel_id = @channel_id;
                          """;
        
        cmd.Parameters.AddWithValue("@user_id", userId);
        cmd.Parameters.AddWithValue("@channel_id", channelId);
        
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        if (!await reader.ReadAsync(ct))
            return Results.Json(new ErrorResponse("Unknown Channel"), statusCode: 404);
        
        return Results.Json(ChannelResponse.FromReader(reader), statusCode: 200);
    }

    internal static async Task<IResult> Create(HttpContext ctx) {
        var userId = (ulong)ctx.Items["authId"]!;
        
        var ct = ctx.RequestAborted;

        CreateChannelBody? reqBody = null;

        try {
            reqBody = await ctx.Request.ReadFromJsonAsync<CreateChannelBody>(ct);
        } catch {}
        
        if (reqBody == null)
            return Results.Json(new ErrorResponse("Invalid JSON"), statusCode: 400);

        var result = CreateChannelValidator.Instance.Validate(reqBody);
        
        if (!result.IsValid)
            return Results.Json(new ErrorResponse(result.Errors[0].ErrorMessage), statusCode: 400);
        
        if (reqBody.Members.Contains(userId))
            return Results.Json(new ErrorResponse("You are already implicitly added in this channel."), statusCode: 400);

        var channelId = await Database.ChannelIDGenerator.Generate(ct);
        var creationTime = Utils.GetTime();
        UserResponse? user;
        
        await using var conn = await Database.GetConnection(ct);
        await using var txn = await conn.BeginTransactionAsync(ct);

        try {
            await using (var cmd = conn.CreateCommand()) {
                cmd.Transaction = txn;
                cmd.CommandText = "SELECT id, username, created_at FROM users WHERE id = @id;";

                cmd.Parameters.AddWithValue("@id", userId);
            
                await using var reader = await cmd.ExecuteReaderAsync(ct);

                if (!await reader.ReadAsync(ct)) throw new Exception("Failed to get user");

                user = UserResponse.FromReader(reader);
            }

            await using (var cmd = conn.CreateCommand()) {
                cmd.Transaction = txn;
                cmd.CommandText = """
                                  INSERT INTO channels (id, owner, name, member_count, created_at)
                                  VALUES(@id, @owner, @name, 0, @created_at);
                                  """;

                cmd.Parameters.AddWithValue("@id", channelId);
                cmd.Parameters.AddWithValue("@owner", userId);
                cmd.Parameters.AddWithValue("@name", reqBody.Name);
                cmd.Parameters.AddWithValue("@created_at", creationTime);

                var rowsInserted = await cmd.ExecuteNonQueryAsync(ct);

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

                var rowsInserted = await cmd.ExecuteNonQueryAsync(ct);

                if (rowsInserted != (reqBody.Members.Length + 1)) throw new Exception("Failed to insert channel members");
            }

            await txn.CommitAsync(ct);
        }
        catch (MySqlException ex) when (ex.ErrorCode == MySqlErrorCode.NoReferencedRow2) {
            await txn.RollbackAsync();
            
            if (ex.Message.Contains("FOREIGN KEY (`user_id`)"))
                return Results.Json(new ErrorResponse("One or more members doesn't exist."), statusCode: 400);

            throw;
        }
        catch {
            await txn.RollbackAsync();
            throw;
        }

        var channel = new ChannelResponse {
            ID = channelId,
            Name = reqBody.Name,
            Owner = user,
            MemberCount = (uint)reqBody.Members.Length + 1,
            CreatedAt = creationTime,
            LastAccessed = creationTime
        };

        var postChannel = new WSPushChannel {
            Channel = channel,
        };
        
        WSServer.AnnounceChannel(userId, postChannel);

        foreach (var member in reqBody.Members) {
            WSServer.AnnounceChannel(member, postChannel);
        }

        return Results.Json(channel, statusCode: 200);
    }

    internal static async Task<IResult> Update(HttpContext ctx, string _channelId) {
        if (!ulong.TryParse(_channelId, out var channelId)) {
            return Results.Json(new ErrorResponse("Invalid channel id."), statusCode: 400);
        }
        
        var userId = (ulong)ctx.Items["authId"]!;
        
        var ct = ctx.RequestAborted;
        
        UpdateChannelBody? reqBody = null;

        try {
            reqBody = await ctx.Request.ReadFromJsonAsync<UpdateChannelBody>(ct);
        } catch {}
        
        if (reqBody == null)
            return Results.Json(new ErrorResponse("Invalid JSON"), statusCode: 400);

        var result = UpdateChannelValidator.Instance.Validate(reqBody);
        
        if (!result.IsValid)
            return Results.Json(new ErrorResponse(result.Errors[0].ErrorMessage), statusCode: 400);
        
        var updatesOwner = reqBody.Owner != null;
        var updatesName = reqBody.Name != null;

        if (updatesOwner && reqBody.Owner == userId)
            return Results.Json(new ErrorResponse("You are already owner"), statusCode: 400);

        ChannelResponse channel;
        var doesUpdate = false;
        
        await using var conn = await Database.GetConnection(ct);
        await using var txn = await conn.BeginTransactionAsync(ct);
        
        try {
            await using (var cmd = conn.CreateCommand()) {
                cmd.Transaction = txn;
                cmd.CommandText = """
                                  SELECT
                                      cm.channel_id AS id,
                                      cm.last_accessed,
                                      c.name,
                                      c.member_count,
                                      c.created_at,
                                      c.owner
                                  FROM channel_members cm
                                  JOIN channels c ON cm.channel_id = c.id
                                  WHERE cm.user_id = @user_id AND cm.channel_id = @channel_id
                                  FOR UPDATE OF c;
                                  """;
                
                cmd.Parameters.AddWithValue("@user_id", userId);
                cmd.Parameters.AddWithValue("@channel_id", channelId);
                
                await using var reader = await cmd.ExecuteReaderAsync(ct);
                
                if (!await reader.ReadAsync(ct))
                    throw new APIException("Unknown Channel", 404);

                if (reader.GetFieldValue<ulong>(5 /* Owner ID */) != userId)
                    throw new APIException("Insufficient Permissions", 403);
                
                channel = ChannelResponse.FromReader(reader, false);
                
                if (updatesOwner && reqBody.Owner != userId) doesUpdate = true;
                if (updatesName && reqBody.Name != channel.Name) doesUpdate = true;
                
                if (updatesName) {
                    channel.Name = reqBody.Name!;
                }
            }
            
            if (updatesOwner && !await Database.ExistsInChannel(reqBody.Owner!.Value, channelId, conn, txn, ct, true)) {
                await txn.RollbackAsync();
                return Results.Json(new ErrorResponse("New Owner must be a Channel Member"), statusCode: 403);
            }
            
            await using (var cmd = conn.CreateCommand()) {
                cmd.Transaction = txn;
                cmd.CommandText = "SELECT id, username, created_at FROM users WHERE id = @user_id;";
                
                cmd.Parameters.AddWithValue("@user_id", updatesOwner ? reqBody.Owner : userId);
                
                await using var reader = await cmd.ExecuteReaderAsync(ct);

                if (!await reader.ReadAsync(ct)) throw new Exception("Failed to fetch new/current channel owner");

                channel.Owner = UserResponse.FromReader(reader);
            }

            if (!doesUpdate) {
                await txn.RollbackAsync();
                return Results.Json(channel, statusCode: 200);
            }

            var updateList = new List<string>();

            if (updatesName) {
                updateList.Add("name = @name");
            }
            
            if (updatesOwner) {
                updateList.Add("owner = @owner_id");
            }
            
            if (updateList.Count == 0) throw new Exception("Channel Update: list is zero");

            await using (var cmd = conn.CreateCommand()) {
                cmd.Transaction = txn;
                cmd.CommandText = $"""
                                   UPDATE channels
                                   SET {string.Join(", ", updateList)}
                                   WHERE id = @channel_id;
                                   """;
                
                cmd.Parameters.AddWithValue("@channel_id", channelId);
                if (updatesName) cmd.Parameters.AddWithValue("@name", reqBody.Name);
                if (updatesOwner) cmd.Parameters.AddWithValue("@owner_id", reqBody.Owner);
            
                await cmd.ExecuteNonQueryAsync(ct);
            }
            
            await txn.CommitAsync(ct);
        }
        catch {
            await txn.RollbackAsync();
            throw;
        }
        
        WSServer.Channels.SendMessage(channelId, new WSUpdateChannel {
            Channel = channel
        });
        
        return Results.Json(channel, statusCode: 200);
    }

    internal static async Task<IResult> Delete(HttpContext ctx, string _channelId) {
        if (!ulong.TryParse(_channelId, out var channelId)) {
            return Results.Json(new ErrorResponse("Invalid channel id."), statusCode: 400);
        }
        
        var userId = (ulong)ctx.Items["authId"]!;
        
        var ct = ctx.RequestAborted;
        
        await using var conn = await Database.GetConnection(ct);
        await using var txn = await conn.BeginTransactionAsync(ct);

        try {
            if (!await Database.IsChannelOwner(userId, channelId, conn, txn, ct)) {
                await txn.RollbackAsync();
                return Results.Json(new ErrorResponse("Insufficient Permissions"), statusCode: 403);
            }
            
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = txn;
            cmd.CommandText = "DELETE FROM channels WHERE id = @channel_id;";
            
            cmd.Parameters.AddWithValue("@channel_id", channelId);
            
            var rowsDeleted = await cmd.ExecuteNonQueryAsync(ct);

            if (rowsDeleted != 1) throw new Exception("Failed to delete channel");
            
            await txn.CommitAsync(ct);
        }
        catch {
            await txn.RollbackAsync();
            throw;
        }
        
        WSServer.DeleteChannel(channelId);
        
        return Results.Json(new { ok = true }, statusCode: 200);
    }

    internal static async Task<IResult> AddMember(HttpContext ctx, string _channelId, string _memberId) {
        if (!ulong.TryParse(_channelId, out var channelId)) {
            return Results.Json(new ErrorResponse("Invalid channel id."), statusCode: 400);
        }
        
        if (!ulong.TryParse(_memberId, out var memberId)) {
            return Results.Json(new ErrorResponse("Invalid user id."), statusCode: 400);
        }
        
        var userId = (ulong)ctx.Items["authId"]!;
        
        var ct = ctx.RequestAborted;

        ChannelResponse channel;
        
        await using var conn = await Database.GetConnection(ct);
        await using var txn = await conn.BeginTransactionAsync(ct);

        try {
            await using (var cmd = conn.CreateCommand()) {
                cmd.Transaction = txn;
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
                                  WHERE cm.user_id = @user_id AND cm.channel_id = @channel_id
                                  FOR UPDATE OF c;
                                  """;

                cmd.Parameters.AddWithValue("@user_id", userId);
                cmd.Parameters.AddWithValue("@channel_id", channelId);

                await using var reader = await cmd.ExecuteReaderAsync(ct);

                if (!await reader.ReadAsync(ct))
                    throw new APIException("Unknown Channel", 404);

                if (reader.GetFieldValue<ulong>(5 /* Owner ID */) != userId)
                    throw new APIException("Insufficient Permissions", 403);

                channel = ChannelResponse.FromReader(reader);
                channel.LastAccessed = Utils.GetTime();
                channel.MemberCount++;
            }

            await using (var cmd = conn.CreateCommand()) {
                cmd.Transaction = txn;
                cmd.CommandText = """
                                  INSERT INTO channel_members (user_id, channel_id, last_accessed)
                                  VALUES (@member_id, @channel_id, @last_accessed);
                                  """;

                cmd.Parameters.AddWithValue("@member_id", memberId);
                cmd.Parameters.AddWithValue("@channel_id", channelId);
                cmd.Parameters.AddWithValue("@last_accessed", channel.LastAccessed);

                var rowsAdded = await cmd.ExecuteNonQueryAsync(ct);

                if (rowsAdded != 1) throw new Exception("Failed to add member");
            }

            await txn.CommitAsync(ct);
        }
        catch (MySqlException ex) when (ex.ErrorCode == MySqlErrorCode.DuplicateKeyEntry) {
            await txn.RollbackAsync();
            
            var index = Utils.DecodeDuplicateKeyError(ex.Message);
            
            if (index == "PRIMARY")
                return Results.Json(new ErrorResponse("User is already a member of this channel"), statusCode: 400);
            
            throw;
        }
        catch (MySqlException ex) when (ex.ErrorCode == MySqlErrorCode.NoReferencedRow2) {
            await txn.RollbackAsync();
            
            if (ex.Message.Contains("FOREIGN KEY (`user_id`)"))
                return Results.Json(new ErrorResponse("User doesn't exist."), statusCode: 404);

            throw;
        }
        catch {
            await txn.RollbackAsync();
            throw;
        }
        
        WSServer.AnnounceChannel(memberId, new WSPushChannel {
            Channel = channel
        });

        return Results.Json(new { ok = true });
    }
    
    internal static async Task<IResult> RemoveMember(HttpContext ctx, string _channelId, string _memberId) {
        if (!ulong.TryParse(_channelId, out var channelId)) {
            return Results.Json(new ErrorResponse("Invalid channel id."), statusCode: 400);
        }
        
        if (!ulong.TryParse(_memberId, out var memberId)) {
            return Results.Json(new ErrorResponse("Invalid user id."), statusCode: 400);
        }
        
        var userId = (ulong)ctx.Items["authId"]!;
        
        if (userId == memberId)
            return Results.Json(new ErrorResponse("You can't remove yourself."), statusCode: 400);
        
        var ct = ctx.RequestAborted;
        
        await using var conn = await Database.GetConnection(ct);
        await using var txn = await conn.BeginTransactionAsync(ct);

        try {
            if (!await Database.IsChannelOwner(userId, channelId, conn, txn, ct)) {
                await txn.RollbackAsync();
                return Results.Json(new ErrorResponse("Insufficient Permissions"), statusCode: 403);
            }

            await using (var cmd = conn.CreateCommand()) {
                cmd.Transaction = txn;
                cmd.CommandText = """
                                  DELETE FROM channel_members
                                  WHERE user_id = @member_id AND channel_id = @channel_id;
                                  """;

                cmd.Parameters.AddWithValue("@member_id", memberId);
                cmd.Parameters.AddWithValue("@channel_id", channelId);

                var rowsRemoved = await cmd.ExecuteNonQueryAsync(ct);

                if (rowsRemoved != 1) {
                    await txn.RollbackAsync();
                    return Results.Json(new ErrorResponse("User is not a member of this channel."), statusCode: 400);
                }
            }

            await txn.CommitAsync(ct);
        }
        catch {
            await txn.RollbackAsync();
            throw;
        }
        
        WSServer.LeaveChannel(memberId, channelId);

        return Results.Json(new { ok = true });
    }
}