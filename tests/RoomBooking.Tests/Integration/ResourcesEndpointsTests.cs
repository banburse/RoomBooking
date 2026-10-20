using System.Net;
using System.Net.Http.Json;

namespace RoomBooking.Tests.Integration;

[Trait("Category", "Integration")]
public class ResourcesEndpointsTests
{
    // ---------- 1. GET /resources ----------

    [Fact]
    public async Task GetResources_ReturnsSeed_SortedRoomsThenDesksByName()
    {
        await using var factory = new RoomBookingApiFactory();
        var client = factory.CreateClient();

        var resources = await client.GetResourcesAsync();

        Assert.Equal(["Alfa", "Beta", "Gamma", "D1", "D2", "D3", "D4"], resources.Select(r => r.Name));
        Assert.All(resources, r => Assert.True(r.IsActive));
        Assert.Equal(8, resources.Single(r => r.Name == "Alfa").Capacity);
        Assert.Equal(4, resources.Single(r => r.Name == "Beta").Capacity);
        Assert.Equal(12, resources.Single(r => r.Name == "Gamma").Capacity);
        Assert.All(resources.Where(r => r.Name.StartsWith('D')), r =>
        {
            Assert.Equal("Desk", r.Type);
            Assert.Equal(1, r.Capacity);
        });
    }

    [Theory]
    [InlineData("Desk", 4)]
    [InlineData("desk", 4)]
    [InlineData("ROOM", 3)]
    public async Task GetResources_FilterByType_IsCaseInsensitive(string type, int expectedCount)
    {
        await using var factory = new RoomBookingApiFactory();
        var client = factory.CreateClient();

        var resources = await client.GetResourcesAsync(type);

        Assert.Equal(expectedCount, resources.Count);
        Assert.All(resources, r => Assert.Equal(type, r.Type, ignoreCase: true));
    }

    [Fact]
    public async Task GetResources_UnknownType_Returns400WithTypeError()
    {
        await using var factory = new RoomBookingApiFactory();
        var client = factory.CreateClient();

        using var response = await client.GetAsync("/resources?type=Chair");

        var problem = await response.ReadProblemAsync(HttpStatusCode.BadRequest);
        problem.AssertHasError("type");
    }

    // ---------- 2. POST /resources ----------

    [Fact]
    public async Task PostResource_Returns201WithLocation_AndAppearsInList()
    {
        await using var factory = new RoomBookingApiFactory();
        var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/resources", new { name = "  Delta  ", type = "Room", capacity = 6 });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<ResourceDto>(RoomBookingApiFactory.Json))!;
        Assert.Equal("Delta", created.Name);
        Assert.Equal("Room", created.Type);
        Assert.Equal(6, created.Capacity);
        Assert.True(created.IsActive);
        Assert.NotEqual(Guid.Empty, created.Id);
        Assert.NotNull(response.Headers.Location);
        Assert.EndsWith($"/resources/{created.Id}", response.Headers.Location!.OriginalString, StringComparison.OrdinalIgnoreCase);

        var rooms = await client.GetResourcesAsync("Room");
        Assert.Contains(rooms, r => r.Id == created.Id);
    }

    [Theory]
    [InlineData("alfa")]
    [InlineData(" ALFA ")]
    public async Task PostResource_DuplicateName_Returns409(string name)
    {
        await using var factory = new RoomBookingApiFactory();
        var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/resources", new { name, type = "Room", capacity = 4 });

        await response.ReadProblemAsync(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task PostResource_InvalidCapacity_Returns400WithCapacityError()
    {
        await using var factory = new RoomBookingApiFactory();
        var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/resources", new { name = "Omega", type = "Desk", capacity = 0 });

        var problem = await response.ReadProblemAsync(HttpStatusCode.BadRequest);
        problem.AssertHasError("capacity");
    }

    [Fact]
    public async Task PostResource_EmptyName_Returns400WithNameError()
    {
        await using var factory = new RoomBookingApiFactory();
        var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/resources", new { name = "   ", type = "Room", capacity = 5 });

        var problem = await response.ReadProblemAsync(HttpStatusCode.BadRequest);
        problem.AssertHasError("name");
    }

    [Fact]
    public async Task PostResource_TooLargeCapacity_Returns400()
    {
        await using var factory = new RoomBookingApiFactory();
        var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/resources", new { name = "Omega", type = "Room", capacity = 51 });

        var problem = await response.ReadProblemAsync(HttpStatusCode.BadRequest);
        problem.AssertHasError("capacity");
    }

    // ---------- 3. GET /resources/{id}/bookings ----------

    [Fact]
    public async Task GetBookings_ReturnsActiveBookingsSortedByStart()
    {
        await using var factory = new RoomBookingApiFactory();
        var client = factory.CreateClient();
        var alfa = await client.GetResourceIdAsync("Alfa");

        // Środa 2026-10-21 (CEST, +02:00), tworzone w odwrotnej kolejności.
        var later = await client.CreateBookingAsync(alfa, Utc(2026, 10, 21, 12), Utc(2026, 10, 21, 13));
        var earlier = await client.CreateBookingAsync(alfa, Utc(2026, 10, 21, 8), Utc(2026, 10, 21, 9));

        var bookings = await client.GetBookingsAsync(alfa, "2026-10-19", "2026-10-26");

        Assert.Equal([earlier.Id, later.Id], bookings.Select(b => b.Id));
        Assert.All(bookings, b => Assert.Equal(alfa, b.ResourceId));
        Assert.Equal(Utc(2026, 10, 21, 8), earlier.StartUtc);
        Assert.Equal(TimeSpan.Zero, bookings[0].StartUtc.Offset); // odpowiedzi w UTC
    }

    [Fact]
    public async Task GetBookings_WeekBoundary_DateParametersUseOfficeMidnight()
    {
        await using var factory = new RoomBookingApiFactory();
        var client = factory.CreateClient();
        var alfa = await client.GetResourceIdAsync("Alfa");

        // Piątek 23.10 18:00-20:00 lokalnie (CEST) = 16:00Z-18:00Z
        var friday = await client.CreateBookingAsync(alfa, Utc(2026, 10, 23, 16), Utc(2026, 10, 23, 18));
        // Niedziela 25.10 (dzień zmiany czasu, już CET) 08:00-09:00 lokalnie = 07:00Z-08:00Z
        var sunday = await client.CreateBookingAsync(alfa, Utc(2026, 10, 25, 7), Utc(2026, 10, 25, 8));
        // Poniedziałek 26.10 08:00-09:00 lokalnie (CET) = 07:00Z-08:00Z
        var monday = await client.CreateBookingAsync(alfa, Utc(2026, 10, 26, 7), Utc(2026, 10, 26, 8));

        var thisWeek = await client.GetBookingsAsync(alfa, "2026-10-19", "2026-10-26");
        Assert.Equal([friday.Id, sunday.Id], thisWeek.Select(b => b.Id));

        var nextWeek = await client.GetBookingsAsync(alfa, "2026-10-26", "2026-11-02");
        Assert.Equal([monday.Id], nextWeek.Select(b => b.Id));
    }

    [Fact]
    public async Task GetBookings_WeekBoundary_IsoParametersAreHalfOpen()
    {
        await using var factory = new RoomBookingApiFactory();
        var client = factory.CreateClient();
        var alfa = await client.GetResourceIdAsync("Alfa");

        var friday = await client.CreateBookingAsync(alfa, Utc(2026, 10, 23, 16), Utc(2026, 10, 23, 18));
        var monday = await client.CreateBookingAsync(alfa, Utc(2026, 10, 26, 7), Utc(2026, 10, 26, 8));

        // to == start poniedziałkowej rezerwacji -> nie nachodzi ([from, to) półotwarte)
        var upToMondayStart = await client.GetBookingsAsync(alfa, "2026-10-19T00:00:00+02:00", "2026-10-26T07:00:00Z");
        Assert.Equal([friday.Id], upToMondayStart.Select(b => b.Id));

        // from == koniec piątkowej rezerwacji -> nie nachodzi
        var fromFridayEnd = await client.GetBookingsAsync(alfa, "2026-10-23T18:00:00Z", "2026-10-27T00:00:00+01:00");
        Assert.Equal([monday.Id], fromFridayEnd.Select(b => b.Id));

        // zakres przecinający rezerwację (nachodzenie częściowe) -> zwracana
        var overlapping = await client.GetBookingsAsync(alfa, "2026-10-23T17:00:00Z", "2026-10-23T17:15:00Z");
        Assert.Equal([friday.Id], overlapping.Select(b => b.Id));
    }

    [Fact]
    public async Task GetBookings_UnknownResource_Returns404()
    {
        await using var factory = new RoomBookingApiFactory();
        var client = factory.CreateClient();

        using var response = await client.GetAsync($"/resources/{Guid.NewGuid()}/bookings?from=2026-10-19&to=2026-10-26");

        await response.ReadProblemAsync(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("from=2026-10-01&to=2026-11-02", null)]          // > 31 dni
    [InlineData("from=2026-10-26&to=2026-10-19", null)]          // to < from
    [InlineData("from=2026-10-19&to=2026-10-19", null)]          // to == from
    [InlineData("to=2026-10-26", "from")]                        // brak from
    [InlineData("from=2026-10-19", "to")]                        // brak to
    [InlineData("from=nie-data&to=2026-10-26", "from")]          // zły format
    [InlineData("from=2026-10-19T00:00:00&to=2026-10-26", "from")] // ISO bez offsetu
    public async Task GetBookings_InvalidRange_Returns400(string query, string? expectedKey)
    {
        await using var factory = new RoomBookingApiFactory();
        var client = factory.CreateClient();
        var alfa = await client.GetResourceIdAsync("Alfa");

        using var response = await client.GetAsync($"/resources/{alfa}/bookings?{query}");

        var problem = await response.ReadProblemAsync(HttpStatusCode.BadRequest);
        Assert.True(problem.TryGetProperty("errors", out var errors), problem.ToString());
        if (expectedKey is null)
        {
            Assert.True(errors.TryGetProperty("from", out _) || errors.TryGetProperty("to", out _), errors.ToString());
        }
        else
        {
            problem.AssertHasError(expectedKey);
        }
    }

    [Fact]
    public async Task GetBookings_Exactly31Days_IsAllowed()
    {
        // Zakres bez zmiany czasu (oba końce CET), żeby nie zależeć od interpretacji '31 dni' przy DST.
        await using var factory = new RoomBookingApiFactory();
        var client = factory.CreateClient();
        var alfa = await client.GetResourceIdAsync("Alfa");

        var bookings = await client.GetBookingsAsync(alfa, "2026-11-01", "2026-12-02");

        Assert.Empty(bookings);
    }

    internal static DateTimeOffset Utc(int year, int month, int day, int hour, int minute = 0) =>
        new(year, month, day, hour, minute, 0, TimeSpan.Zero);
}
