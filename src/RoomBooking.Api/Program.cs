using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RoomBooking.Api.Data;
using RoomBooking.Domain;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(TimeProvider.System);

builder.Services.Configure<BookingPolicyOptions>(builder.Configuration.GetSection("Office"));
builder.Services.AddSingleton<IBookingPolicy>(sp => new BookingPolicy(
    sp.GetRequiredService<TimeProvider>(),
    sp.GetRequiredService<IOptions<BookingPolicyOptions>>().Value));

// Connection string czytany leniwie, żeby testy mogły go nadpisać przez UseSetting.
builder.Services.AddDbContext<RoomBookingDbContext>((sp, options) =>
    options.UseSqlite(sp.GetRequiredService<IConfiguration>().GetConnectionString("RoomBooking")));

var app = builder.Build();

await DatabaseInitializer.InitializeAsync(app.Services);

await app.RunAsync();

// Dla WebApplicationFactory<Program> w testach integracyjnych.
public partial class Program;
