// Szkielet (krok 0). Implementuje Dev-A (dev-api) wg CONTRACT.md.
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(TimeProvider.System);

var app = builder.Build();

app.Run();

// Dla WebApplicationFactory<Program> w testach integracyjnych.
public partial class Program;
