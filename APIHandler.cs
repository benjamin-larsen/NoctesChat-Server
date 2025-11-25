using System.Security.Cryptography;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;

namespace NoctesChat;

public class APIHandler {
    private static async Task<IResult> GetChannels(HttpContext ctx) {
        var auth = (User)ctx.Items["auth"]!;

        var filter = Builders<ChannelMembership>.Filter.Eq(m => m.UserID, auth.ID);
        var channels = await Database.ChannelMembers.Find(filter).SortByDescending(m => m.LastAccessed).ToListAsync();
        
        return Results.Json(new {
            channels = channels.Select(channel => new {
                id = channel.ChannelID.ToString(),
                last_accessed = channel.LastAccessed,
            })
        }, statusCode: 404);
    }

    private static async Task<IResult> GetUser(string _id) {
        if (!UInt64.TryParse(_id, out ulong id)) {
            return Results.Json(new { error = "Invalid user id." }, statusCode: 400);
        }

        User? result = await Database.FindUserByID(id);

        if (result == null) {
            return Results.Json(new { error = "User doesn't exist." }, statusCode: 404);
        }

        return Results.Json(new { id = result.ID.ToString(), username = result.Username, }, statusCode: 200);
    }

    private static IResult GetSelfUser(HttpContext ctx) {
        var user = (User)ctx.Items["auth"]!;

        return Results.Json(
            new {
                id = user.ID.ToString(),
                username = user.Username,
                email = user.Email,
                email_verified = user.EmailVerified,
            },
            statusCode: 200
            );
    }

    private static IResult NotFound() {
        return Results.Json(new { error = "API Endpoint: Not Found." }, statusCode: 404);
    }

    private static async ValueTask<object?> AuthMiddleware(
        EndpointFilterInvocationContext context, EndpointFilterDelegate next) {
        var headers = context.HttpContext.Request.Headers;

        if (!headers.TryGetValue("Authorization", out var key))
            return Results.Json(new { error = "You need to be logged in." }, statusCode: 401);

        var parsedToken = UserToken.DecodeToken(key);
        
        if (!parsedToken.success)
            return Results.Json(new { error = "Invalid token." }, statusCode: 400);
        
        var keyHash = SHA256.HashData(parsedToken.token);
        
        var filter = Builders<User>.Filter.Eq("id", parsedToken.userID) & Builders<User>.Filter.Eq("tokens.key", keyHash);
        var DBResult = await Database.Users.Find(filter).FirstOrDefaultAsync();
        
        if (DBResult == null)
            return Results.Json(new { error = "You've been logged out. Please log in and try again." }, statusCode: 401);
        
        context.HttpContext.Items["auth"] = DBResult;
        
        return await next(context);
    }

    public static void Use(WebApplication app) {
        var apiRouter = app.MapGroup("/api");

        apiRouter.MapGet("/users/{_id}", GetUser);
        apiRouter.MapGet("/users/@me", GetSelfUser).AddEndpointFilter(AuthMiddleware);
        apiRouter.MapGet("/channels", (Delegate)GetChannels).AddEndpointFilter(AuthMiddleware);

        apiRouter.MapFallback(NotFound);
    }
}