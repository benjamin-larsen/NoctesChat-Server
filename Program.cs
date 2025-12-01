using NoctesChat;
using dotenv.net;
using Microsoft.AspNetCore.Diagnostics;

DotEnv.Load();
Database.Setup();

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

var app = builder.Build();

app.MapStaticAssets();

APIHandler.Use(app);

app.MapFallback(() =>
{
    return Results.File("./index.html", "text/html");
});

app.UseExceptionHandler(new ExceptionHandlerOptions {
    SuppressDiagnosticsCallback = context => context.Exception is APIException,
    ExceptionHandler = async context => {
        context.Response.ContentType = "application/json";

        var exceptionHandlerPathFeature =
            context.Features.Get<IExceptionHandlerPathFeature>();
        
        if (exceptionHandlerPathFeature?.Error is APIException ex) {
            context.Response.StatusCode = ex.StatusCode;

            await context.Response.WriteAsJsonAsync(new {
                error = ex.Message
            });
            return;
        }
        
        context.Response.StatusCode = 500;

        await context.Response.WriteAsJsonAsync(new {
            error = "Internal Server Error"
        });
    }
});

app.Run();