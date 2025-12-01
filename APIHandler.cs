using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MySqlConnector;
using NoctesChat.APIRoutes;
using NoctesChat.RequestModels;

namespace NoctesChat;

public class APIHandler {
    private static IResult NotFound() {
        return Results.Json(new { error = "API Endpoint: Not Found." }, statusCode: 404);
    }

    public static void Use(WebApplication app) {
        var apiRouter = app.MapGroup("/api");

        // Authentication
        apiRouter.MapPost("/auth/logout", (Delegate)Auth.Logout).AddEndpointFilter(Auth.Middleware);
        apiRouter.MapPost("/auth/login", (Delegate)Auth.Login);
        apiRouter.MapPost("/auth/register", (Delegate)Auth.Register);

        // Users
        apiRouter.MapGet("/usernames/{username}", Users.GetByUsername);
        apiRouter.MapGet("/users/{_id}", Users.Get);
        apiRouter.MapGet("/users/@me", (Delegate)Users.GetSelf).AddEndpointFilter(Auth.Middleware);
        
        // Channels
        apiRouter.MapGet("/channels", (Delegate)Channels.GetList).AddEndpointFilter(Auth.Middleware);
        apiRouter.MapPost("/channels", (Delegate)Channels.Create).AddEndpointFilter(Auth.Middleware);
        
        // Messages
        apiRouter.MapPost("/channels/{_channelId}/messages", Messages.Post).AddEndpointFilter(Auth.Middleware);
        apiRouter.MapGet("/channels/{_channelId}/messages", Messages.Get).AddEndpointFilter(Auth.Middleware);

        apiRouter.MapFallback(NotFound);
    }
}