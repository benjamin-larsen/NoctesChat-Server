using NoctesChat;
using dotenv.net;

Database.Setup();

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment()) {
    DotEnv.Load();
}

app.UseHttpsRedirection();
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