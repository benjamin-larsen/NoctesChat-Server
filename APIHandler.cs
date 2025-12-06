using NoctesChat.APIRoutes;
using NoctesChat.ResponseModels;

namespace NoctesChat;

public class APIException : Exception {
    public int StatusCode { get; }

    public APIException(string message, int statusCode) : base(message) {
        StatusCode = statusCode;
    }
}

public class APIHandler {
    private static IResult NotFound() {
        return Results.Json(new ErrorResponse("API Endpoint: Not Found."), statusCode: 404);
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
        apiRouter.MapGet("/channels/{_channelId}", Channels.GetSingle).AddEndpointFilter(Auth.Middleware);
        apiRouter.MapGet("/channels", (Delegate)Channels.GetList).AddEndpointFilter(Auth.Middleware);
        apiRouter.MapPost("/channels", (Delegate)Channels.Create).AddEndpointFilter(Auth.Middleware);
        
        apiRouter.MapPatch("/channels/{_channelId}", Channels.Update).AddEndpointFilter(Auth.Middleware);
        // Messages
        apiRouter.MapPost("/channels/{_channelId}/messages", Messages.Post).AddEndpointFilter(Auth.Middleware);
        apiRouter.MapGet("/channels/{_channelId}/messages", Messages.GetList).AddEndpointFilter(Auth.Middleware);

        apiRouter.MapFallback(NotFound);
    }
}