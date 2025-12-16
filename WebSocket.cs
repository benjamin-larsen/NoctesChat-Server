using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using NoctesChat.ResponseModels;
using NoctesChat.WSRequestModels;

namespace NoctesChat;

public class WSException : Exception {
    public WebSocketCloseStatus StatusCode { get; }

    public WSException(string message, WebSocketCloseStatus statusCode = WebSocketCloseStatus.PolicyViolation) : base(message) {
        StatusCode = statusCode;
    }
}

public class WSSocket {
    private readonly WebSocket _socket;
    private readonly CancellationToken _ct;

    public ulong? _authId;
    private UserToken? _userToken;
    // bool for wether socket was typing in this channel
    private ConcurrentDictionary<ulong, bool> _channels = new();
    
    public WSSocket(WebSocket socket, CancellationToken ct) {
        _socket = socket;
        _ct = ct;
    }

    public Task Close(WebSocketCloseStatus status, string? desc) {
        if (_socket.State is WebSocketState.Closed or WebSocketState.Aborted or WebSocketState.CloseSent) return Task.CompletedTask;
        
        return _socket.CloseAsync(status, desc, _ct);
    }

    public void CloseAndForget(WebSocketCloseStatus status, string desc) {
        Task.Run(
            async () => {
                try {
                    await Close(status, desc);
                }
                catch { }
            }
        );
    }

    public void SendAndForget(byte[] message) {
        Task.Run(
            async () => {
                try {
                    await _socket.SendAsync(
                        message,
                        WebSocketMessageType.Text,
                        true,
                        _ct
                    );
                }
                catch { }
            }
        );
    }

    public void SendCloseAndForget(byte[] message, WebSocketCloseStatus status, string? desc) {
        Task.Run(
            async () => {
                try {
                    await SendAndClose(
                        message, status, desc
                    );
                }
                catch { }
            }
        );
    }

    public async Task SendAndClose(byte[] message, WebSocketCloseStatus status, string? desc) {
        await SendMessage(message);
        await Close(status, desc);
    }
    
    public Task SendMessage(byte[] message) {
        return _socket.SendAsync(
            message,
            WebSocketMessageType.Text,
            true,
            _ct);
    }
    
    public Task SendMessage(object message) {
        var json = JsonSerializer.Serialize(message);

        return SendMessage(Encoding.UTF8.GetBytes(json));
    }
    
    public async Task Run() {
        var buffer = new byte[4096];
        WebSocketReceiveResult recv;

        do {
            recv = await _socket.ReceiveAsync(new ArraySegment<byte>(buffer), _ct);
            
            if (recv.MessageType == WebSocketMessageType.Close) break;

            var msgRaw = new ReadOnlySpan<byte>(buffer, 0, recv.Count);
            WSBaseMessage? baseMsg = null;

            try {
                baseMsg = JsonSerializer.Deserialize<WSBaseMessage>(msgRaw);
            } catch {}
            
            if (baseMsg == null) throw new WSException("Invalid JSON");
            
            baseMsg.ThrowIfInvalid();
            
            // Don't accept messages larger than 4096 bytes
            if (!recv.EndOfMessage) {
                await Close(
                    WebSocketCloseStatus.MessageTooBig,
                    "Frame too large");
                return;
            }

            await ProcessMessage(baseMsg);
        } while (!recv.CloseStatus.HasValue);
        
        await Close(
            recv.CloseStatus!.Value,
            recv.CloseStatusDescription);
    }

    public void Cleanup() {
        if (_authId.HasValue) {
            WSServer.Users.Unsubscribe(_authId.Value, this);
            
            var nextPresence = WSServer.GetPresence(_authId.Value);

            if (nextPresence != "online") {
                foreach (var (channel, _) in _channels) {
                    WSServer.Channels.SendMessageExclUser(channel, new WSPushPresence {
                        User = _authId.Value,
                        Status = nextPresence
                    }, _authId.Value);
                }
            }
        }
        
        if (_userToken.HasValue) {
            WSServer.UserTokens.Unsubscribe(_userToken.Value, this);
        }

        foreach (var (channel, wasTyping) in _channels) {
            WSServer.Channels.Unsubscribe(channel, this);
            
            if (wasTyping && _authId.HasValue) {
                WSServer.Channels.SendMessage(channel, new WSAnnounceStopTyping {
                    Member = _authId.Value,
                    Channel = channel
                });
            }
        }
    }
    
    public void SubscribeToChannel(ulong channelId) {
        _channels.TryAdd(channelId, false);
        WSServer.Channels.Subscribe(channelId, this);
    }
    
    public void UnsubscribeFromChannel(ulong channelId) {
        _channels.TryRemove(channelId, out _);
        WSServer.Channels.Unsubscribe(channelId, this);
    }

    private async Task ProcessLogin(WSLoginMessage msg) {
        if (_authId.HasValue) {
            throw new WSException("Already logged in");
        }
        
        var parsedToken = UTokenService.DecodeToken(msg.Token);
        
        if (!parsedToken.success)
            throw new WSException("Invalid token");
        
        var keyHash = SHA256.HashData(parsedToken.token);

        await using var conn = await Database.GetConnection(_ct);
        await using var txn = await conn.BeginTransactionAsync(_ct);

        List<WSAuthChannel> channels;
        HashSet<ulong> memberIds;

        string prevPresence;

        try {
            var hasToken = await Database.HasUserTokenAtomic(parsedToken.userID, keyHash, conn, txn, _ct);

            if (!hasToken) {
                await SendMessage(
                    new WSAuthError(
                        "You've been logged out. Please log in and try again.",
                        401
                    )
                );
                throw new WSException("You've been logged out. Please log in and try again.");
            }
            
            _authId = parsedToken.userID;
            _userToken = new UserToken(parsedToken.userID, keyHash);
            
            prevPresence = WSServer.GetPresence(_authId.Value);
            
            WSServer.Users.Subscribe(_authId.Value, this);
            WSServer.UserTokens.Subscribe(_userToken.Value, this);
            
            // Initalize Channel List
            channels = new List<WSAuthChannel>();
            memberIds = new HashSet<ulong>();
            
            await using (var cmd = conn.CreateCommand()) {
                cmd.Transaction = txn;
                cmd.CommandText = """
                                  SELECT
                                      cm.channel_id AS id,
                                      cm.last_accessed,
                                      c.name,
                                      c.created_at,
                                      o.id AS owner_id,
                                      o.username AS owner_username,
                                      o.created_at AS owner_created_at
                                  FROM channel_members cm
                                  JOIN channels c ON cm.channel_id = c.id
                                  LEFT JOIN users o ON c.owner = o.id
                                  WHERE cm.user_id = @user_id
                                  FOR SHARE OF cm;
                                  """;

                cmd.Parameters.AddWithValue("@user_id", _authId);
                
                await using var reader = await cmd.ExecuteReaderAsync(_ct);
                
                while (await reader.ReadAsync(_ct)) {
                    channels.Add(WSAuthChannel.FromReader(reader));
                }
            }

            // Map Channel to ID
            var channelMap = channels.ToDictionary(c => c.ID);

            await using (var cmd = conn.CreateCommand()) {
                cmd.Transaction = txn;
                cmd.CommandText = $"""
                                  SELECT
                                      cm.channel_id,
                                      u.id AS member_id,
                                      u.username AS member_username,
                                      u.created_at AS member_created_at
                                  FROM channel_members cm
                                  JOIN users u ON u.id = cm.user_id
                                  WHERE cm.channel_id IN ({
                                      string.Join(',', channels.Select(c => c.ID.ToString()))
                                  });
                                  """;
                // Normally we would use SQL paramaters, but c.ID is a ulong, so it's safe to input directly.
                
                await using var reader = await cmd.ExecuteReaderAsync(_ct);

                while (await reader.ReadAsync(_ct)) {
                    var channelId = reader.GetUInt64(0);
                    if (!channelMap.TryGetValue(channelId, out var channel)) continue;

                    var user = UserResponse.FromReader(reader, 1);
                    
                    channel.Members.Add(user);
                    memberIds.Add(user.ID);
                }
            }
            
            await txn.CommitAsync(_ct);
        }
        catch {
            await txn.RollbackAsync();
            throw;
        }
        
        // Subscribe to channels
        foreach (var channel in channels)
        {
            _channels.TryAdd(channel.ID, false);
            WSServer.Channels.Subscribe(channel.ID, this);

            if (prevPresence != "online") {
                WSServer.Channels.SendMessageExclUser(channel.ID, new WSPushPresence {
                    User = _authId.Value,
                    Status = "online"
                }, _authId.Value);
            }
        }
            
        await SendMessage(new WSAuthAck(_authId.Value, channels));
            
        // Send presences
        foreach (var memberId in memberIds) {
            if (memberId == _authId) continue;
            await SendMessage(new WSPushPresence {
                User = memberId,
                Status = WSServer.GetPresence(memberId)
            });
        }
    }

    private async Task ProcessTyping(WSTyping msg, WSTyping.Variant variant) {
        var respondType = variant == WSTyping.Variant.Start ? "start_typing" : "stop_typing";

        if (!_authId.HasValue) {
            await SendMessage(new WSError("You need to be logged in.", respondType, 401));
            return;
        }

        if (!_channels.TryGetValue(msg.Channel, out var oldTyping)) {
            await SendMessage(new WSError("Unknown Channel", respondType, 404));
            return;
        }

        if (variant == WSTyping.Variant.Start) {
            _channels.TryUpdate(msg.Channel, true, oldTyping);

            WSServer.Channels.SendMessage(msg.Channel, new WSAnnounceStartTyping {
                Member = _authId.Value,
                Channel = msg.Channel
            });
        } else if (variant == WSTyping.Variant.Stop) {
            _channels.TryUpdate(msg.Channel, false, oldTyping);

            WSServer.Channels.SendMessage(msg.Channel, new WSAnnounceStopTyping {
                Member = _authId.Value,
                Channel = msg.Channel
            });
        }
    }

    private async Task ProcessHeartbeat() {
        await SendMessage(new WSHeartbeatAck());
    }

    private Task ProcessMessage(WSBaseMessage msg) {
        switch (msg.Type) {
            case "login": {
                WSLoginMessage? decodedMsg = null;

                try {
                    decodedMsg = msg.Data.Deserialize<WSLoginMessage>();
                }
                catch { }

                if (decodedMsg == null) throw new WSException("Invalid JSON");

                return ProcessLogin(decodedMsg);
            }

            case "heartbeat": {
                return ProcessHeartbeat();
            }
            
            case "stop_typing":
            case "start_typing": {
                WSTyping? decodedMsg = null;

                try {
                    decodedMsg = msg.Data.Deserialize<WSTyping>(new JsonSerializerOptions { NumberHandling = JsonNumberHandling.AllowReadingFromString });
                }
                catch { }

                if (decodedMsg == null) throw new WSException("Invalid JSON");

                return ProcessTyping(decodedMsg, msg.Type == "start_typing" ? WSTyping.Variant.Start : WSTyping.Variant.Stop);
            }

            case "debug": {
                Console.WriteLine(JsonSerializer.Serialize(new {
                    authId = _authId,
                    token = _userToken.HasValue ? UTokenService.EncodeToken(_userToken.Value.userId, _userToken.Value.token) : null,
                    channels = _channels.Select(x=>x.Key).ToArray()
                }, new JsonSerializerOptions { WriteIndented = true }));
                return Task.CompletedTask;
            }
            
            case "gdebug": {
                Console.WriteLine(JsonSerializer.Serialize(new {
                    users = WSServer.Users.Subs.Select(x=> (x.Key, x.Value.sockets.Count)).ToDictionary(),
                    token = WSServer.UserTokens.Subs.Select(x=> (UTokenService.EncodeToken(x.Key.userId, x.Key.token), x.Value.sockets.Count)).ToDictionary(),
                    channels = WSServer.Channels.Subs.Select(x=> (x.Key, x.Value.sockets.Count)).ToDictionary()
                }, new JsonSerializerOptions { WriteIndented = true }));
                return Task.CompletedTask;
            }
            
            default:
                throw new WSException("Unknown message type: " + msg.Type);
        }
    }
}

public static class WSServer {
    public static readonly WSChannelManager<ulong> Users = new();
    public static readonly WSChannelManager<UserToken> UserTokens = new();
    public static readonly WSChannelManager<ulong> Channels = new();

    public static string GetPresence(ulong userId) {
        if (!Users.Subs.TryGetValue(userId, out var socketChannel)) return "offline";
        
        if (socketChannel.sockets.Count == 0) return "offline";

        return "online";
    }

    public static async Task AnnounceChannel(ulong userId, ChannelResponse channel) {
        var channelId = channel.ID;
        
        if (!Users.Subs.TryGetValue(userId, out var socketChannel)) return;

        WSPushChannel msg;

        await using (var conn = await Database.GetConnection()) {
            msg = new WSPushChannel {
                Channel = channel,
                Members = await Database.GetChannelMembers(channelId, conn, null)
            };
        }
        
        var json = JsonSerializer.Serialize(msg);
        var bytes = Encoding.UTF8.GetBytes(json);

        lock (socketChannel._lock) {
            foreach (var socket in socketChannel.sockets) {
                // Fire and Forget
                socket.SubscribeToChannel(channelId);
                socket.SendAndForget(bytes);
            }
        }
        
        foreach (var member in msg.Members) {
            var memberId = member.ID;
            if (memberId == userId) continue;
            
            Users.SendMessage(userId, new WSPushPresence {
                User = memberId,
                Status = GetPresence(memberId)
            });
        }
    }
    
    public static async Task AnnounceChannelBulk(List<ulong> members, ChannelResponse channel) {
        WSPushChannel? msg = null;
        var channelId = channel.ID;
        
        var bytes = Array.Empty<byte>();

        foreach (var userId in members) {
            if (!Users.Subs.TryGetValue(userId, out var socketChannel)) continue;

            if (msg == null) {
                await using (var conn = await Database.GetConnection()) {
                    msg = new WSPushChannel {
                        Channel = channel,
                        Members = await Database.GetChannelMembers(channelId, conn, null)
                    };
                }
                
                var json = JsonSerializer.Serialize(msg);
                bytes = Encoding.UTF8.GetBytes(json);
            }
            
            lock (socketChannel._lock) {
                foreach (var socket in socketChannel.sockets) {
                    // Fire and Forget
                    socket.SubscribeToChannel(channelId);
                    socket.SendAndForget(bytes);
                }
            }
        }
        
        if (msg == null) return;

        foreach (var member in msg.Members) {
            var memberId = member.ID;
            
            Channels.SendMessageExclUser(channelId, new WSPushPresence {
                User = memberId,
                Status = GetPresence(memberId)
            }, memberId);
        }
    }

    public static void DeleteChannel(ulong channelId) {
        if (!Channels.Subs.TryGetValue(channelId, out var channel)) return;
        
        var json = JsonSerializer.Serialize(new WSDeleteChannel {
            Channel = channelId
        });
        var bytes = Encoding.UTF8.GetBytes(json);

        lock (channel._lock) {
            foreach (var socket in channel.sockets) {
                // Fire and Forget
                socket.UnsubscribeFromChannel(channelId);
                socket.SendAndForget(bytes);
            }
        }
    }

    public static void LeaveChannel(ulong userId, ulong channelId) {
        if (!Users.Subs.TryGetValue(userId, out var user)) return;
        
        var json = JsonSerializer.Serialize(new WSDeleteChannel {
            Channel = channelId
        });
        var bytes = Encoding.UTF8.GetBytes(json);

        lock (user._lock) {
            foreach (var socket in user.sockets) {
                // Fire and Forget
                socket.UnsubscribeFromChannel(channelId);
                socket.SendAndForget(bytes);
            }
        }
    }
    
    public static async Task HandleRequest(HttpContext context) {
        if (!context.WebSockets.IsWebSocketRequest)  {
            context.Response.StatusCode = 400;
            context.Response.ContentType = "application/json";
        
            await context.Response.WriteAsJsonAsync(new ErrorResponse("Invalid WebSocket request"));
            return;
        }
    
        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        var wsSocket = new WSSocket(socket, context.RequestAborted);

        try {
            await wsSocket.Run();
        }
        catch (Exception ex) {
            if (ex is JsonException) {
                await wsSocket.Close(
                    WebSocketCloseStatus.PolicyViolation,
                    "Invalid JSON"
                );
                
                return;
            }
            
            if (ex is not WSException and not ObjectDisposedException and not OperationCanceledException and not WebSocketException) {
                Console.WriteLine($"An error occured in WebSocket: {ex}");
            }
            
            if (ex is WSException wsEx) {
                await wsSocket.Close(
                    wsEx.StatusCode,
                    wsEx.Message
                );
                return;
            }

            await wsSocket.Close(
                WebSocketCloseStatus.InternalServerError,
                "Internal Server Error"
            );
        }
        finally {
            wsSocket.Cleanup();
        }
    }
}