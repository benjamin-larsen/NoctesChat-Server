using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace NoctesChat;

public class WSChannel {
    public readonly object _lock = new();
    public readonly HashSet<WSSocket> sockets = new();

    public void Add(WSSocket socket) {
        sockets.Add(socket);
    }
    
    public bool Remove(WSSocket socket) {
        sockets.Remove(socket);
        
        return sockets.Count == 0;
    }
}

public class WSChannelManager<TKey>
where TKey : notnull {
    public readonly ConcurrentDictionary<TKey, WSChannel> Subs = new();

    public void SendMessage(TKey key, object message) {
        if (!Subs.TryGetValue(key, out var channel)) return;
        
        var json = JsonSerializer.Serialize(message);
        var bytes = Encoding.UTF8.GetBytes(json);

        lock (channel._lock) {
            foreach (var socket in channel.sockets) {
                // Fire and Forget
                socket.SendAndForget(bytes);
            }
        }
    }

    public void SendCloseAndForget(TKey key, object message, WebSocketCloseStatus status, string desc) {
        if (!Subs.TryGetValue(key, out var channel)) return;
        
        var json = JsonSerializer.Serialize(message);
        var bytes = Encoding.UTF8.GetBytes(json);

        lock (channel._lock) {
            foreach (var socket in channel.sockets) {
                // Fire and Forget
                socket.SendCloseAndForget(bytes, status, desc);
            }
        }
    }

    public void Subscribe(TKey key, WSSocket socket) {
        var channel = Subs.GetOrAdd(key, _ => new WSChannel());

        lock (channel._lock) {
            channel.Add(socket);
        }
    }
    
    public void Unsubscribe(TKey key, WSSocket socket) {
        if (Subs.TryGetValue(key, out var channel)) {
            lock (channel._lock) {
                var shouldRemove = channel.Remove(socket);

                if (shouldRemove) {
                    Subs.TryRemove(key, out _);
                }
            }
        }
    }
}