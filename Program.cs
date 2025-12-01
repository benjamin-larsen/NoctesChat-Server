using NoctesChat;
using dotenv.net;

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

app.UseExceptionHandler(exApp => exApp.Run(async context => {
    context.Response.StatusCode = 500;
    context.Response.ContentType = "application/json";

    await context.Response.WriteAsJsonAsync(new {
        error = "Internal Server Error"
    });
}));

app.Run();