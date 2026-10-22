var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

// Fallback para SPA client-side routing
app.MapFallbackToFile("index.html");

app.Run();
