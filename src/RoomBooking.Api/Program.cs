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

var app = builder.Build();

await DatabaseInitializer.InitializeAsync(app.Services);

app.UseExceptionHandler();

app.MapResourceEndpoints();
app.MapBookingEndpoints();

await app.RunAsync();

// Dla WebApplicationFactory<Program> w testach integracyjnych.
public partial class Program;
