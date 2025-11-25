using NoctesChat;

using System.Text.Json;

/*var result = new byte[32];
var success = Base64Url.DecodeFromChars("VGVzdCBoZWxsbyB0aGVyZQx", result, out _, out var bytesWritten);

Console.WriteLine($"Bytes: {Convert.ToHexString(result)}\nWritten: {bytesWritten}\nSuccess: {success.ToString()}");*/

var rand = UserToken.GenerateToken();
var key = UserToken.EncodeToken(53454, rand);

Console.WriteLine(JsonSerializer.Serialize(new {
    rand = Convert.ToHexString(rand),
    key = key
}));

var x = UserToken.DecodeToken(key);

Console.WriteLine(JsonSerializer.Serialize(new {
    userId = x.userID,
    token = Convert.ToHexString(x.token),
    success = x.success
}));

Database.Setup();

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

var app = builder.Build();

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