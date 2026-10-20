using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace RoomBooking.Tests.Integration;

/// <summary>
/// Host API wg "Kontraktu hostingu" z CONTRACT.md: osobny plik SQLite w katalogu tymczasowym na każdą fabrykę
/// oraz <see cref="FakeTimeProvider"/> podmieniony w DI.
/// </summary>
public sealed class RoomBookingApiFactory : WebApplicationFactory<Program>
{
    /// <summary>Domyślne "teraz": wtorek 2026-10-20 08:00 czasu Warszawy (06:00Z, CEST).</summary>
    public static readonly DateTimeOffset DefaultNow = new(2026, 10, 20, 6, 0, 0, TimeSpan.Zero);

    public static readonly JsonSerializerOptions Json = CreateJsonOptions();

    private readonly string _directory;

    public RoomBookingApiFactory(DateTimeOffset? now = null)
    {
        Time = new FakeTimeProvider(now ?? DefaultNow);
        _directory = Path.Combine(Path.GetTempPath(), "RoomBookingTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        DatabasePath = Path.Combine(_directory, "roombooking.db");
    }

    public FakeTimeProvider Time { get; }

    public string DatabasePath { get; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:RoomBooking", $"Data Source={DatabasePath}");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Time);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            Cleanup();
        }
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        Cleanup();
    }

    private void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch (IOException)
        {
            // Plik może być jeszcze chwilowo zablokowany; to katalog tymczasowy, nie przerywamy testu.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}

public sealed record ResourceDto(Guid Id, string Name, string Type, int Capacity, bool IsActive);

public sealed record BookingDto(
    Guid Id,
    Guid ResourceId,
    string BookedBy,
    string? Title,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    DateTimeOffset CreatedAt);

public static class ApiClientExtensions
{
    public static async Task<IReadOnlyList<ResourceDto>> GetResourcesAsync(this HttpClient client, string? type = null)
    {
        var url = type is null ? "/resources" : $"/resources?type={type}";
        return (await client.GetFromJsonAsync<List<ResourceDto>>(url, RoomBookingApiFactory.Json))!;
    }

    public static async Task<Guid> GetResourceIdAsync(this HttpClient client, string name)
    {
        var resources = await client.GetResourcesAsync();
        return resources.Single(r => r.Name == name).Id;
    }

    public static Task<HttpResponseMessage> PostBookingAsync(
        this HttpClient client, Guid resourceId, DateTimeOffset start, DateTimeOffset end,
        string bookedBy = "Jan", string? title = "Daily") =>
        client.PostAsJsonAsync("/bookings", new
        {
            resourceId,
            bookedBy,
            title,
            startUtc = start.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            endUtc = end.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
        });

    public static async Task<BookingDto> CreateBookingAsync(
        this HttpClient client, Guid resourceId, DateTimeOffset start, DateTimeOffset end, string bookedBy = "Jan")
    {
        using var response = await client.PostBookingAsync(resourceId, start, end, bookedBy);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == System.Net.HttpStatusCode.Created, $"Oczekiwano 201, jest {(int)response.StatusCode}: {body}");
        return JsonSerializer.Deserialize<BookingDto>(body, RoomBookingApiFactory.Json)!;
    }

    public static async Task<IReadOnlyList<BookingDto>> GetBookingsAsync(this HttpClient client, Guid resourceId, string from, string to)
    {
        using var response = await client.GetAsync(
            $"/resources/{resourceId}/bookings?from={Uri.EscapeDataString(from)}&to={Uri.EscapeDataString(to)}");
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"Oczekiwano 200, jest {(int)response.StatusCode}: {body}");
        return JsonSerializer.Deserialize<List<BookingDto>>(body, RoomBookingApiFactory.Json)!;
    }

    /// <summary>Sprawdza, że odpowiedź to ProblemDetails z danym statusem, i zwraca jej JSON.</summary>
    public static async Task<JsonElement> ReadProblemAsync(this HttpResponseMessage response, System.Net.HttpStatusCode expected)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected, $"Oczekiwano {(int)expected}, jest {(int)response.StatusCode}: {body}");
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement.Clone();
        Assert.Equal((int)expected, root.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("title").GetString()));
        return root;
    }

    public static void AssertHasError(this JsonElement problem, string key)
    {
        Assert.True(problem.TryGetProperty("errors", out var errors), $"Brak 'errors' w: {problem}");
        Assert.True(errors.TryGetProperty(key, out var messages), $"Brak errors.{key} w: {errors}");
        Assert.True(messages.GetArrayLength() > 0);
    }
}
