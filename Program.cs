using NoctesChat;
using dotenv.net;
using Microsoft.AspNetCore.Diagnostics;
using NoctesChat.ResponseModels;

DotEnv.Load();
Database.Setup();

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options =>
{
    options.AddPolicy(name: "localhost-cors",
        policy  => {
            policy.WithOrigins("http://localhost:5173").AllowAnyMethod().AllowAnyHeader();
        });
});

builder.Services.AddOpenApi();

var app = builder.Build();

if (builder.Environment.IsDevelopment()) {
    app.UseCors("localhost-cors");
}

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

            await context.Response.WriteAsJsonAsync(new ErrorResponse(ex.Message));
            return;
        }
        
        context.Response.StatusCode = 500;

        await context.Response.WriteAsJsonAsync(new ErrorResponse("Internal Server Error"));
    }
});

app.Run();