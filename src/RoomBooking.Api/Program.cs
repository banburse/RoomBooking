using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RoomBooking.Api.Data;
using RoomBooking.Api.Endpoints;
using RoomBooking.Api.Http;
using RoomBooking.Api.Validation;
using RoomBooking.Domain;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(TimeProvider.System);

builder.Services.Configure<BookingPolicyOptions>(builder.Configuration.GetSection("Office"));
builder.Services.AddSingleton<IBookingPolicy>(sp => new BookingPolicy(
    sp.GetRequiredService<TimeProvider>(),
    sp.GetRequiredService<IOptions<BookingPolicyOptions>>().Value));
builder.Services.AddSingleton<RequestValidator>();

// Connection string czytany leniwie, żeby testy mogły go nadpisać przez UseSetting.
builder.Services.AddDbContext<RoomBookingDbContext>((sp, options) =>
    options.UseSqlite(sp.GetRequiredService<IConfiguration>().GetConnectionString("RoomBooking")));

builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// Błędy wiązania (zły JSON, brak body) zawsze jako wyjątek -> ProblemExceptionHandler -> 400 ProblemDetails.
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ProblemExceptionHandler>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

await DatabaseInitializer.InitializeAsync(app.Services);

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapResourceEndpoints();
app.MapBookingEndpoints();

// Fallback SPA: nieznane ścieżki GET (bez rozszerzenia pliku) -> index.html. Ścieżki API dostają 404 ProblemDetails.
app.MapFallback(async (HttpContext context, IWebHostEnvironment env, CancellationToken cancellationToken) =>
{
    var path = context.Request.Path;
    var isApiPath = path.StartsWithSegments("/resources", StringComparison.OrdinalIgnoreCase)
        || path.StartsWithSegments("/bookings", StringComparison.OrdinalIgnoreCase)
        || path.StartsWithSegments("/swagger", StringComparison.OrdinalIgnoreCase);
    var isGet = HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method);
    var index = env.WebRootFileProvider.GetFileInfo("index.html");

    if (isApiPath || !isGet || !index.Exists)
    {
        return Problems.NotFound($"Ścieżka {path} nie istnieje.");
    }

    context.Response.ContentType = "text/html; charset=utf-8";
    await context.Response.SendFileAsync(index, cancellationToken);
    return Results.Empty;
});

await app.RunAsync();

// Dla WebApplicationFactory<Program> w testach integracyjnych.
public partial class Program;
