using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;
using System.IO.Hashing;

namespace NoctesChat;

public struct UserToken
{
    public UserToken(ulong userId, byte[] token)
    {
        this.userId = userId;
        this.token = token;
    }

    public ulong userId;
    public byte[] token;

    public override bool Equals(object? obj) {
        if (obj is not UserToken other) return false;
        
        if (this.userId != other.userId) return false;
        if (this.token.Length != other.token.Length) return false;
        
        return this.token.SequenceEqual(other.token);
    }

    public override int GetHashCode() {
        Span<byte> bytes = stackalloc byte[8 + 32];
        
        BitConverter.GetBytes(userId).CopyTo(bytes);
        token.CopyTo(bytes.Slice(8));
        
        return (int)XxHash32.HashToUInt32(bytes);
    }
}

public static class UTokenService {
    public static (ulong userID, byte[] token, bool success) DecodeToken(string key) {
        var segments = key.Split(':');

        if (segments.Length != 2) return (
            userID: 0,
            token: [],
            success: false
        );

        var idBytes = new byte[Base64Url.GetMaxDecodedLength(segments[0].Length)];
        
        var resultStatus = Base64Url.DecodeFromChars(segments[0], idBytes, out _, out _);
        
        if (resultStatus != OperationStatus.Done || !UInt64.TryParse(idBytes, out var userID)) return (
            userID: 0,
            token: [],
            success: false
        );
        
        var tokenBytes = new byte[32];
        
        resultStatus = Base64Url.DecodeFromChars(segments[1], tokenBytes, out _, out var bytesWritten);
        if (resultStatus != OperationStatus.Done || bytesWritten != 32) return (
            userID: 0,
            token: [],
            success: false
        );

        return (
            userID,
            token: tokenBytes,
            success: true
        );
    }

    public static string EncodeToken(ulong userID, byte[] token) {
        if (token.Length != 32) throw new ArgumentException("Token must be 32 bytes.");

        return $"{Base64Url.EncodeToString(Encoding.ASCII.GetBytes(userID.ToString()))}:{Base64Url.EncodeToString(token)}";
    }
    
    public static byte[] GenerateToken() {
        return RandomNumberGenerator.GetBytes(32);
    }
}