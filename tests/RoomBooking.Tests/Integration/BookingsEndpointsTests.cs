using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static RoomBooking.Tests.Integration.ResourcesEndpointsTests;

namespace RoomBooking.Tests.Integration;

[Trait("Category", "Integration")]
public class BookingsEndpointsTests
{
    // "Teraz" w fabryce: 2026-10-20 06:00Z. Środa 2026-10-21 10:00-11:00 lokalnie (CEST) = 08:00Z-09:00Z.
    private static readonly DateTimeOffset Start = Utc(2026, 10, 21, 8);
    private static readonly DateTimeOffset End = Utc(2026, 10, 21, 9);

    // ---------- 4. POST /bookings ----------

    [Fact]
    public async Task PostBooking_Returns201WithLocationAndBody()
    {
        await using var factory = new RoomBookingApiFactory();
        var client = factory.CreateClient();
        var alfa = await client.GetResourceIdAsync("Alfa");

        using var response = await client.PostBookingAsync(alfa, Start, End, bookedBy: "  Jan  ", title: "Daily");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var booking = (await response.Content.ReadFromJsonAsync<BookingDto>(RoomBookingApiFactory.Json))!;
        Assert.NotEqual(Guid.Empty, booking.Id);
        Assert.Equal(alfa, booking.ResourceId);
        Assert.Equal("Jan", booking.BookedBy);
        Assert.Equal("Daily", booking.Title);
        Assert.Equal(Start, booking.StartUtc);
        Assert.Equal(End, booking.EndUtc);
        Assert.Equal(TimeSpan.Zero, booking.StartUtc.Offset);
        Assert.Equal(factory.Time.GetUtcNow(), booking.CreatedAt); // CreatedAt z TimeProvider
        Assert.EndsWith($"/bookings/{booking.Id}", response.Headers.Location!.OriginalString, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PostBooking_WithLocalOffset_IsNormalizedToUtc_AndWhitespaceTitleBecomesNull()
    {
        await using var factory = new RoomBookingApiFactory();
        var client = factory.CreateClient();
        var alfa = await client.GetResourceIdAsync("Alfa");

        var localStart = new DateTimeOffset(2026, 10, 21, 10, 0, 0, TimeSpan.FromHours(2));
        using var response = await client.PostBookingAsync(alfa, localStart, localStart.AddHours(1), title: "   ");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var booking = (await response.Content.ReadFromJsonAsync<BookingDto>(RoomBookingApiFactory.Json))!;
        Assert.Equal(Start, booking.StartUtc);
        Assert.Equal(TimeSpan.Zero, booking.StartUtc.Offset);
        Assert.Null(booking.Title);
    }

    [Fact]
    public async Task PostBooking_Collision_Returns409WithConflictingBookingId()
    {
        await using var factory = new RoomBookingApiFactory();
        var client = factory.CreateClient();
        var alfa = await client.GetResourceIdAsync("Alfa");
        var existing = await client.CreateBookingAsync(alfa, Start, End);

        // częściowe nakładanie 10:30-11:30 lokalnie
        using var response = await client.PostBookingAsync(alfa, Start.AddMinutes(30), End.AddMinutes(30), bookedBy: "Anna");

        var problem = await response.ReadProblemAsync(HttpStatusCode.Conflict);
        Assert.Equal(existing.Id, problem.GetProperty("conflictingBookingId").GetGuid());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("detail").GetString()));
    }

    [Fact]
    public async Task PostBooking_BackToBack_IsAllowed()
    {
        await using var factory = new RoomBookingApiFactory();
        var client = factory.CreateClient();
        var alfa = await client.GetResourceIdAsync("Alfa");
        await client.CreateBookingAsync(alfa, Start, End);

        await client.CreateBookingAsync(alfa, End, End.AddHours(1));
        await client.CreateBookingAsync(alfa, Start.AddHours(-1), Start);
    }

    [Fact]
    public async Task PostBooking_SameSlotOnDifferentResource_IsAllowed()
    {
        await using var factory = new RoomBookingApiFactory();
        var client = factory.CreateClient();
        await client.CreateBookingAsync(await client.GetResourceIdAsync("Alfa"), Start, End);

        await client.CreateBookingAsync(await client.GetResourceIdAsync("Beta"), Start, End);
    }

    [Fact]
    public async Task PostBooking_BeforeOpening_0745_0815_Returns400OnStartUtc()
    {
        await using var factory = new RoomBookingApiFactory();
        var client = factory.CreateClient();
        var alfa = await client.GetResourceIdAsync("Alfa");

        // 07:45-08:15 lokalnie (CEST) = 05:45Z-06:15Z
        using var response = await client.PostBookingAsync(alfa, Utc(2026, 10, 21, 5, 45), Utc(2026, 10, 21, 6, 15));

        var problem = await response.ReadProblemAsync(HttpStatusCode.BadRequest);
        problem.AssertHasError("startUtc");
    }

    [Fact]
    public async Task PostBooking_AfterClosing_1945_2015_Returns400OnEndUtc()
    {
        await using var factory = new RoomBookingApiFactory();
        var client = factory.CreateClient();
        var alfa = await client.GetResourceIdAsync("Alfa");

        // 19:45-20:15 lokalnie (CEST) = 17:45Z-18:15Z
        using var response = await client.PostBookingAsync(alfa, Utc(2026, 10, 21, 17, 45), Utc(2026, 10, 21, 18, 15));

        var problem = await response.ReadProblemAsync(HttpStatusCode.BadRequest);
        problem.AssertHasError("endUtc");
    }

    [Fact]
    public async Task PostBooking_OnDstChangeDay_UsesLocalWorkingHours()
    {
        await using var factory = new RoomBookingApiFactory();
        var client = factory.CreateClient();
        var alfa = await client.GetResourceIdAsync("Alfa");

        // 2026-10-25: po cofnięciu zegara +01:00, więc 08:00 lokalnie = 07:00Z (a 06:00Z = 07:00 -> odrzucone)
        await client.CreateBookingAsync(alfa, Utc(2026, 10, 25, 7), Utc(2026, 10, 25, 8));
        using var tooEarly = await client.PostBookingAsync(alfa, Utc(2026, 10, 25, 6), Utc(2026, 10, 25, 7));
        (await tooEarly.ReadProblemAsync(HttpStatusCode.BadRequest)).AssertHasError("startUtc");
    }

    [Fact]
    public async Task PostBooking_InThePast_Returns400OnStartUtc()
    {
        await using var factory = new RoomBookingApiFactory();
        var client = factory.CreateClient();
        var alfa = await client.GetResourceIdAsync("Alfa");

        // 2026-10-19 = dzień przed "teraz"
        using var response = await client.PostBookingAsync(alfa, Utc(2026, 10, 19, 8), Utc(2026, 10, 19, 9));

        var problem = await response.ReadProblemAsync(HttpStatusCode.BadRequest);
        problem.AssertHasError("startUtc");
    }

    [Fact]
    public async Task PostBooking_MissingBookedBy_Returns400()
    {
        await using var factory = new RoomBookingApiFactory();
        var client = factory.CreateClient();
        var alfa = await client.GetResourceIdAsync("Alfa");

        using var response = await client.PostBookingAsync(alfa, Start, End, bookedBy: "   ");

        var problem = await response.ReadProblemAsync(HttpStatusCode.BadRequest);
        problem.AssertHasError("bookedBy");
    }

    [Fact]
    public async Task PostBooking_TimeWithoutOffset_Returns400UnderBothTimeKeys()
    {
        await using var factory = new RoomBookingApiFactory();
        var client = factory.CreateClient();
        var alfa = await client.GetResourceIdAsync("Alfa");

        using var response = await client.PostAsJsonAsync("/bookings", new
        {
            resourceId = alfa,
            bookedBy = "Jan",
            title = (string?)null,
            startUtc = "2026-10-21T08:00:00",
            endUtc = "2026-10-21T09:00:00",
        });

        var problem = await response.ReadProblemAsync(HttpStatusCode.BadRequest);
        problem.AssertHasError("startUtc");
        problem.AssertHasError("endUtc");
    }

    // CONTRACT.md: brak, zły format lub brak offsetu w czasie -> błąd pod kluczem tego pola.
    [Theory]
    [InlineData("2026-10-21T08:00:00", "2026-10-21T09:00:00Z", "startUtc", "endUtc")] // start bez offsetu
    [InlineData("2026-10-21T08:00:00Z", "2026-10-21T09:00:00", "endUtc", "startUtc")] // end bez offsetu
    [InlineData("jutro rano", "2026-10-21T09:00:00Z", "startUtc", "endUtc")]          // zły format
    [InlineData("2026-10-21T08:00:00Z", "", "endUtc", "startUtc")]                    // pusty
    [InlineData(null, "2026-10-21T09:00:00Z", "startUtc", "endUtc")]                  // brak
    public async Task PostBooking_InvalidTimeField_Returns400UnderThatFieldOnly(
        string? startUtc, string endUtc, string expectedKey, string otherKey)
    {
        await using var factory = new RoomBookingApiFactory();
        var client = factory.CreateClient();
        var alfa = await client.GetResourceIdAsync("Alfa");

        using var response = await client.PostAsJsonAsync("/bookings", new
        {
            resourceId = alfa,
            bookedBy = "Jan",
            title = (string?)null,
            startUtc,
            endUtc,
        });

        var problem = await response.ReadProblemAsync(HttpStatusCode.BadRequest);
        problem.AssertHasError(expectedKey);
        Assert.False(problem.GetProperty("errors").TryGetProperty(otherKey, out _),
            $"Poprawne pole '{otherKey}' nie powinno mieć błędu: {problem}");
    }

    [Fact]
    public async Task PostBooking_InactiveResource_Returns404_ButItsBookingsStayReadable()
    {
        await using var factory = new RoomBookingApiFactory();
        var client = factory.CreateClient();
        var beta = await client.GetResourceIdAsync("Beta");
        var booking = await client.CreateBookingAsync(beta, Start, End);

        // v1 nie ma endpointu do dezaktywacji: ustawiamy flagę bezpośrednio w pliku SQLite fabryki.
        await factory.SetResourceActiveAsync(beta, isActive: false);

        using var response = await client.PostBookingAsync(beta, End, End.AddHours(1));
        await response.ReadProblemAsync(HttpStatusCode.NotFound);

        Assert.DoesNotContain(await client.GetResourcesAsync(), r => r.Id == beta);
        var bookings = await client.GetBookingsAsync(beta, "2026-10-19", "2026-10-26");
        Assert.Equal([booking.Id], bookings.Select(b => b.Id));
    }

    [Fact]
    public async Task PostBooking_UnknownResource_Returns404()
    {
        await using var factory = new RoomBookingApiFactory();
        var client = factory.CreateClient();

        using var response = await client.PostBookingAsync(Guid.NewGuid(), Start, End);

        await response.ReadProblemAsync(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PostBooking_ConcurrentRequestsForSameSlot_ExactlyOneSucceeds()
    {
        await using var factory = new RoomBookingApiFactory();
        var client = factory.CreateClient();
        var alfa = await client.GetResourceIdAsync("Alfa");

        const int parallel = 8;
        using var gate = new SemaphoreSlim(0);
        var tasks = Enumerable.Range(0, parallel).Select(async i =>
        {
            await gate.WaitAsync();
            return await client.PostBookingAsync(alfa, Start, End, bookedBy: $"Osoba {i}");
        }).ToList();
        gate.Release(parallel);
        var responses = await Task.WhenAll(tasks);

        try
        {
            var statuses = responses.Select(r => r.StatusCode).ToList();
            Assert.Equal(1, statuses.Count(s => s == HttpStatusCode.Created));
            Assert.Equal(parallel - 1, statuses.Count(s => s == HttpStatusCode.Conflict));

            var winner = (await responses.Single(r => r.StatusCode == HttpStatusCode.Created)
                .Content.ReadFromJsonAsync<BookingDto>(RoomBookingApiFactory.Json))!;
            foreach (var loser in responses.Where(r => r.StatusCode == HttpStatusCode.Conflict))
            {
                var problem = await loser.ReadProblemAsync(HttpStatusCode.Conflict);
                Assert.Equal(winner.Id, problem.GetProperty("conflictingBookingId").GetGuid());
            }

            var stored = await client.GetBookingsAsync(alfa, "2026-10-19", "2026-10-26");
            Assert.Equal([winner.Id], stored.Select(b => b.Id));
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    // ---------- 5. DELETE /bookings/{id} ----------

    [Fact]
    public async Task DeleteBooking_Returns204_HidesBookingAndFreesSlot()
    {
        await using var factory = new RoomBookingApiFactory();
        var client = factory.CreateClient();
        var alfa = await client.GetResourceIdAsync("Alfa");
        var booking = await client.CreateBookingAsync(alfa, Start, End);

        using var response = await client.DeleteAsync($"/bookings/{booking.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
        Assert.Empty(await client.GetBookingsAsync(alfa, "2026-10-19", "2026-10-26"));

        // anulowana rezerwacja zwalnia slot
        var rebooked = await client.CreateBookingAsync(alfa, Start, End, bookedBy: "Anna");
        Assert.NotEqual(booking.Id, rebooked.Id);
    }

    [Fact]
    public async Task DeleteBooking_InProgress_IsAllowed()
    {
        await using var factory = new RoomBookingApiFactory();
        var client = factory.CreateClient();
        var alfa = await client.GetResourceIdAsync("Alfa");
        var booking = await client.CreateBookingAsync(alfa, Start, End);

        factory.Time.SetUtcNow(Start.AddMinutes(30));
        using var response = await client.DeleteAsync($"/bookings/{booking.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task DeleteBooking_Unknown_Returns404()
    {
        await using var factory = new RoomBookingApiFactory();
        var client = factory.CreateClient();

        using var response = await client.DeleteAsync($"/bookings/{Guid.NewGuid()}");

        await response.ReadProblemAsync(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteBooking_AlreadyCancelled_Returns404()
    {
        await using var factory = new RoomBookingApiFactory();
        var client = factory.CreateClient();
        var alfa = await client.GetResourceIdAsync("Alfa");
        var booking = await client.CreateBookingAsync(alfa, Start, End);

        using var first = await client.DeleteAsync($"/bookings/{booking.Id}");
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        using var second = await client.DeleteAsync($"/bookings/{booking.Id}");

        await second.ReadProblemAsync(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData(0)]   // endUtc == now
    [InlineData(60)]  // po zakończeniu
    public async Task DeleteBooking_AlreadyEnded_Returns409(int minutesAfterEnd)
    {
        await using var factory = new RoomBookingApiFactory();
        var client = factory.CreateClient();
        var alfa = await client.GetResourceIdAsync("Alfa");
        var booking = await client.CreateBookingAsync(alfa, Start, End);

        factory.Time.SetUtcNow(End.AddMinutes(minutesAfterEnd));
        using var response = await client.DeleteAsync($"/bookings/{booking.Id}");

        await response.ReadProblemAsync(HttpStatusCode.Conflict);
        Assert.Single(await client.GetBookingsAsync(alfa, "2026-10-19", "2026-10-26"));
    }

    [Fact]
    public async Task ErrorsAreProblemDetailsInPolish()
    {
        await using var factory = new RoomBookingApiFactory();
        var client = factory.CreateClient();

        using var response = await client.DeleteAsync($"/bookings/{Guid.NewGuid()}");

        var problem = await response.ReadProblemAsync(HttpStatusCode.NotFound);
        Assert.Equal("Nie znaleziono.", problem.GetProperty("title").GetString());
        Assert.Equal(JsonValueKind.String, problem.GetProperty("type").ValueKind);
    }
}
