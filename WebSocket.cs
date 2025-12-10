using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
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

    private ulong? _authId;
    private UserToken? _userToken;
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
        }
        
        if (_userToken.HasValue) {
            WSServer.UserTokens.Unsubscribe(_userToken.Value, this);
        }

        foreach (var (channel, _) in _channels) {
            WSServer.Channels.Unsubscribe(channel, this);
        }
    }
    
    public void SubscribeToChannel(ulong channelId) {
        _channels.TryAdd(channelId, true);
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
            
            await using (var cmd = conn.CreateCommand()) {
                cmd.Transaction = txn;
                cmd.CommandText = """
                                  SELECT
                                      channel_id
                                  FROM channel_members
                                  WHERE user_id = @user_id
                                  FOR SHARE;
                                  """;

                cmd.Parameters.AddWithValue("@user_id", parsedToken.userID);
                
                await using var reader = await cmd.ExecuteReaderAsync(_ct);
                
                while (await reader.ReadAsync(_ct)) {
                    var channelId = reader.GetUInt64(0);
                    SubscribeToChannel(channelId);
                }
            }
            
            _authId = parsedToken.userID;
            _userToken = new UserToken(parsedToken.userID, keyHash);
            
            WSServer.Users.Subscribe(_authId.Value, this);
            WSServer.UserTokens.Subscribe(_userToken.Value, this);
            
            // Don't release lock until we've subscribed to topic, to avoid race-condition
            await txn.CommitAsync(_ct);
        }
        catch {
            await txn.RollbackAsync();
            throw;
        }

        await SendMessage(new WSAuthAck(_authId.Value));
    }

    private Task ProcessMessage(WSBaseMessage msg) {
        switch (msg.Type) {
            case "login":
                WSLoginMessage? decodedMsg = null;
                
                try {
                    decodedMsg = msg.Data.Deserialize<WSLoginMessage>();
                } catch {}
                
                if (decodedMsg == null) throw new WSException("Invalid JSON");

                return ProcessLogin(decodedMsg);

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

    public static void AnnounceChannel(ulong userId, WSPushChannel msg) {
        var channelId = msg.Channel.ID;
        
        if (!Users.Subs.TryGetValue(userId, out var channel)) return;
        
        var json = JsonSerializer.Serialize(msg);
        var bytes = Encoding.UTF8.GetBytes(json);

        lock (channel._lock) {
            foreach (var socket in channel.sockets) {
                // Fire and Forget
                socket.SubscribeToChannel(channelId);
                socket.SendAndForget(bytes);
            }
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