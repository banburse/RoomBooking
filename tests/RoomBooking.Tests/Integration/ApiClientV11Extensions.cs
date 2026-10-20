using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace RoomBooking.Tests.Integration;

/// <summary>Element odpowiedzi <c>GET /bookings?bookedBy=…</c> (v1.1): Booking + nazwa i typ zasobu.</summary>
public sealed record MyBookingDto(
    Guid Id,
    Guid ResourceId,
    string BookedBy,
    string? Title,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    DateTimeOffset CreatedAt,
    string ResourceName,
    string ResourceType);

/// <summary>Helpery dla endpointów v1.1 (6, 7, 8) wg CONTRACT.md.</summary>
public static class ApiClientV11Extensions
{
    public static string Iso(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);

    // ---------- 6. GET /resources/available ----------

    /// <summary>Wysyła zapytanie z surowymi (już zakodowanymi) parametrami, np. <c>start=…&amp;end=…</c>.</summary>
    public static Task<HttpResponseMessage> GetAvailableRawAsync(this HttpClient client, string query) =>
        client.GetAsync($"/resources/available?{query}");

    public static string AvailableQuery(DateTimeOffset start, DateTimeOffset end, string? type = null, string? minCapacity = null)
    {
        var query = $"start={Uri.EscapeDataString(Iso(start))}&end={Uri.EscapeDataString(Iso(end))}";
        if (type is not null)
        {
            query += $"&type={Uri.EscapeDataString(type)}";
        }

        if (minCapacity is not null)
        {
            query += $"&minCapacity={Uri.EscapeDataString(minCapacity)}";
        }

        return query;
    }

    public static async Task<IReadOnlyList<ResourceDto>> GetAvailableAsync(
        this HttpClient client, DateTimeOffset start, DateTimeOffset end, string? type = null, int? minCapacity = null)
    {
        using var response = await client.GetAvailableRawAsync(
            AvailableQuery(start, end, type, minCapacity?.ToString(CultureInfo.InvariantCulture)));
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"Oczekiwano 200, jest {(int)response.StatusCode}: {body}");
        return JsonSerializer.Deserialize<List<ResourceDto>>(body, RoomBookingApiFactory.Json)!;
    }

    // ---------- 7. PUT /bookings/{id} ----------

    public static Task<HttpResponseMessage> PutBookingAsync(
        this HttpClient client, Guid id, DateTimeOffset start, DateTimeOffset end, string? title = "Daily") =>
        client.PutAsJsonAsync($"/bookings/{id}", new { startUtc = Iso(start), endUtc = Iso(end), title });

    /// <summary>PUT z dowolnym (np. niepoprawnym) body.</summary>
    public static Task<HttpResponseMessage> PutBookingRawAsync(this HttpClient client, Guid id, object body) =>
        client.PutAsJsonAsync($"/bookings/{id}", body);

    public static async Task<BookingDto> ReadBookingAsync(this HttpResponseMessage response, HttpStatusCode expected)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected, $"Oczekiwano {(int)expected}, jest {(int)response.StatusCode}: {body}");
        return JsonSerializer.Deserialize<BookingDto>(body, RoomBookingApiFactory.Json)!;
    }

    // ---------- 8. GET /bookings?bookedBy= ----------

    public static Task<HttpResponseMessage> GetMyBookingsRawAsync(this HttpClient client, string? bookedBy) =>
        client.GetAsync(bookedBy is null ? "/bookings" : $"/bookings?bookedBy={Uri.EscapeDataString(bookedBy)}");

    public static async Task<IReadOnlyList<MyBookingDto>> GetMyBookingsAsync(this HttpClient client, string bookedBy)
    {
        using var response = await client.GetMyBookingsRawAsync(bookedBy);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"Oczekiwano 200, jest {(int)response.StatusCode}: {body}");
        return JsonSerializer.Deserialize<List<MyBookingDto>>(body, RoomBookingApiFactory.Json)!;
    }

    // ---------- wspólne ----------

    /// <summary>Sprawdza, że w <c>errors</c> nie ma danego klucza (np. niezmapowanego <c>startUtc</c>).</summary>
    public static void AssertNoError(this JsonElement problem, string key)
    {
        if (problem.TryGetProperty("errors", out var errors))
        {
            Assert.False(errors.TryGetProperty(key, out _), $"Nieoczekiwany klucz errors.{key} w: {errors}");
        }
    }

    /// <summary>Żadne dwie rezerwacje z listy nie nachodzą na siebie ([start, end) półotwarte).</summary>
    public static void AssertNoOverlaps(IReadOnlyList<BookingDto> bookings)
    {
        for (var i = 0; i < bookings.Count; i++)
        {
            for (var j = i + 1; j < bookings.Count; j++)
            {
                var a = bookings[i];
                var b = bookings[j];
                Assert.False(a.StartUtc < b.EndUtc && b.StartUtc < a.EndUtc,
                    $"Nakładające się rezerwacje w bazie: {a.Id} [{a.StartUtc:O}, {a.EndUtc:O}) i {b.Id} [{b.StartUtc:O}, {b.EndUtc:O})");
            }
        }
    }
}
