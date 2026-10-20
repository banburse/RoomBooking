using System.Net;
using System.Net.Http.Json;
using static RoomBooking.Tests.Integration.ApiClientV11Extensions;
using static RoomBooking.Tests.Integration.ResourcesEndpointsTests;

namespace RoomBooking.Tests.Integration;

/// <summary>6. <c>GET /resources/available?start=…&amp;end=…&amp;type=…&amp;minCapacity=…</c> (v1.1).</summary>
[Trait("Category", "Integration")]
public class AvailableResourcesTests
{
    // "Teraz" w fabryce: 2026-10-20 06:00Z. Środa 2026-10-21 10:00-11:00 lokalnie (CEST) = 08:00Z-09:00Z.
    private static readonly DateTimeOffset Start = Utc(2026, 10, 21, 8);
    private static readonly DateTimeOffset End = Utc(2026, 10, 21, 9);

    private static readonly string[] SeedByCapacity = ["D1", "D2", "D3", "D4", "Beta", "Alfa", "Gamma"];

    // ---------- happy path, sortowanie ----------

    [Fact]
    public async Task NoBookings_ReturnsAllActiveResources_SortedByCapacity()
    {
        await using var factory = new RoomBookingApiFactory();
        var client = factory.CreateClient();

        var available = await client.GetAvailableAsync(Start, End);

        Assert.Equal(SeedByCapacity, available.Select(r => r.Name));
        Assert.All(available, r => Assert.True(r.IsActive));
        Assert.Equal(1, available.Single(r => r.Name == "D1").Capacity);
        Assert.Equal("Room", available.Single(r => r.Name == "Alfa").Type);
    }

    [Fact]
    public async Task Sorting_IsCapacityThenTypeRoomBeforeDeskThenName()
    {
        await using var factory = new RoomBookingApiFactory();
        var client = factory.CreateClient();
        // Sala o pojemności 1 (remis z biurkami -> Room przed Desk) i sala 4 (remis z Beta -> po nazwie).
        await CreateResourceAsync(client, "Kappa", "Room", 1);
        await CreateResourceAsync(client, "Delta", "Room", 4);
        await CreateResourceAsync(client, "Atlas", "Room", 4);

        var available = await client.GetAvailableAsync(Start, End);

        Assert.Equal(["Kappa", "D1", "D2", "D3", "D4", "Atlas", "Beta", "Delta", "Alfa", "Gamma"], available.Select(r => r.Name));
    }

    [Fact]
    public async Task AcceptsLocalOffsetParameters()
    {
        await using var factory = new RoomBookingApiFactory();
        var client = factory.CreateClient();
        var alfa = await client.GetResourceIdAsync("Alfa");
        await client.CreateBookingAsync(alfa, Start, End);

        // 10:00+02:00 == 08:00Z
        var cest = TimeSpan.FromHours(2);
        var available = await client.GetAvailableAsync(Start.ToOffset(cest), End.ToOffset(cest));

        Assert.DoesNotContain(available, r => r.Name == "Alfa");
        Assert.Equal(6, available.Count);
    }

    // ---------- kolizje ----------

    [Theory]
    [InlineData(0, 60)]    // identyczny przedział
    [InlineData(-30, 30)]  // nachodzenie z lewej
    [InlineData(30, 90)]   // nachodzenie z prawej
    [InlineData(15, 45)]   // zawarte w rezerwacji
    [InlineData(-60, 120)] // zawiera rezerwację
    public async Task ResourceWithOverlappingBooking_IsNotAvailable(int startOffsetMinutes, int endOffsetMinutes)
    {
        await using var factory = new RoomBookingApiFactory();
        var client = factory.CreateClient();
        var alfa = await client.GetResourceIdAsync("Alfa");
        await client.CreateBookingAsync(alfa, Start, End);

        var available = await client.GetAvailableAsync(Start.AddMinutes(startOffsetMinutes), Start.AddMinutes(endOffsetMinutes));

        Assert.DoesNotContain(available, r => r.Id == alfa);
        Assert.Equal(SeedByCapacity.Where(n => n != "Alfa"), available.Select(r => r.Name));
    }

    [Fact]
    public async Task ResourceWithBackToBackBookings_IsAvailable()
    {
        await using var factory = new RoomBookingApiFactory();
        var client = factory.CreateClient();
        var alfa = await client.GetResourceIdAsync("Alfa");
        await client.CreateBookingAsync(alfa, Start.AddHours(-1), Start); // kończy się, gdy szukany się zaczyna
        await client.CreateBookingAsync(alfa, End, End.AddHours(1));      // zaczyna się, gdy szukany się kończy

        var available = await client.GetAvailableAsync(Start, End);

        Assert.Contains(available, r => r.Id == alfa);
    }

    [Fact]
    public async Task ResourceWithCancelledBooking_IsAvailable()
    {
        await using var factory = new RoomBookingApiFactory();
        var client = factory.CreateClient();
        var alfa = await client.GetResourceIdAsync("Alfa");
        var booking = await client.CreateBookingAsync(alfa, Start, End);
        Assert.DoesNotContain(await client.GetAvailableAsync(Start, End), r => r.Id == alfa);

        using var cancel = await client.DeleteAsync($"/bookings/{booking.Id}");
        Assert.Equal(HttpStatusCode.NoContent, cancel.StatusCode);

        Assert.Contains(await client.GetAvailableAsync(Start, End), r => r.Id == alfa);
    }

    [Fact]
    public async Task InactiveResource_IsNotReturned()
    {
        await using var factory = new RoomBookingApiFactory();
        var client = factory.CreateClient();
        var beta = await client.GetResourceIdAsync("Beta");
        await factory.SetResourceActiveAsync(beta, isActive: false);

        var available = await client.GetAvailableAsync(Start, End);

        Assert.DoesNotContain(available, r => r.Id == beta);
        Assert.Equal(6, available.Count);
    }

    [Fact]
    public async Task FoundResource_CanBeBookedImmediately()
    {
        await using var factory = new RoomBookingApiFactory();
        var client = factory.CreateClient();

        var first = (await client.GetAvailableAsync(Start, End, type: "Room", minCapacity: 5))[0];
        Assert.Equal("Alfa", first.Name);

        await client.CreateBookingAsync(first.Id, Start, End);
        var next = await client.GetAvailableAsync(Start, End, type: "Room", minCapacity: 5);
        Assert.Equal(["Gamma"], next.Select(r => r.Name));
    }

    // ---------- filtry type / minCapacity ----------

    [Theory]
    [InlineData("Room", new[] { "Beta", "Alfa", "Gamma" })]
    [InlineData("room", new[] { "Beta", "Alfa", "Gamma" })]
    [InlineData("DESK", new[] { "D1", "D2", "D3", "D4" })]
    public async Task TypeFilter_IsCaseInsensitive(string type, string[] expected)
    {
        await using var factory = new RoomBookingApiFactory();
        var client = factory.CreateClient();

        var available = await client.GetAvailableAsync(Start, End, type: type);

        Assert.Equal(expected, available.Select(r => r.Name));
    }

    [Theory]
    [InlineData(1, new[] { "D1", "D2", "D3", "D4", "Beta", "Alfa", "Gamma" })]
    [InlineData(4, new[] { "Beta", "Alfa", "Gamma" })] // capacity >= minCapacity (granica włącznie)
    [InlineData(5, new[] { "Alfa", "Gamma" })]
    [InlineData(12, new[] { "Gamma" })]
    [InlineData(13, new string[0])]
    [InlineData(50, new string[0])]
    public async Task MinCapacityFilter(int minCapacity, string[] expected)
    {
        await using var factory = new RoomBookingApiFactory();
        var client = factory.CreateClient();

        var available = await client.GetAvailableAsync(Start, End, minCapacity: minCapacity);

        Assert.Equal(expected, available.Select(r => r.Name));
    }

    [Fact]
    public async Task TypeAndMinCapacityCombined_NothingMatches_ReturnsEmptyArray()
    {
        await using var factory = new RoomBookingApiFactory();
        var client = factory.CreateClient();

        var available = await client.GetAvailableAsync(Start, End, type: "Desk", minCapacity: 2);

        Assert.Empty(available);
    }

    // ---------- błędy formatu parametrów ----------

    [Theory]
    [InlineData("end=2026-10-21T09%3A00%3A00Z", "start")]                                    // brak start
    [InlineData("start=2026-10-21T08%3A00%3A00Z", "end")]                                    // brak end
    [InlineData("start=jutro&end=2026-10-21T09%3A00%3A00Z", "start")]                         // zły format
    [InlineData("start=2026-10-21T08%3A00%3A00Z&end=2026-10-21T09%3A00%3A00", "end")]         // end bez offsetu
    [InlineData("start=2026-10-21T08%3A00%3A00&end=2026-10-21T09%3A00%3A00Z", "start")]       // start bez offsetu
    [InlineData("start=2026-10-21&end=2026-10-21T09%3A00%3A00Z", "start")]                    // sama data niedozwolona
    public async Task MissingOrMalformedTime_Returns400UnderParameterName(string query, string expectedKey)
    {
        await using var factory = new RoomBookingApiFactory();
        var client = factory.CreateClient();

        using var response = await client.GetAvailableRawAsync(query);

        var problem = await response.ReadProblemAsync(HttpStatusCode.BadRequest);
        problem.AssertHasError(expectedKey);
    }

    [Fact]
    public async Task NoParametersAtAll_Returns400_NotRoutedAsResourceId()
    {
        await using var factory = new RoomBookingApiFactory();
        var client = factory.CreateClient();

        using var response = await client.GetAsync("/resources/available");

        var problem = await response.ReadProblemAsync(HttpStatusCode.BadRequest);
        problem.AssertHasError("start");
        problem.AssertHasError("end");
    }

    [Fact]
    public async Task UnknownType_Returns400UnderType()
    {
        await using var factory = new RoomBookingApiFactory();
        var client = factory.CreateClient();

        using var response = await client.GetAvailableRawAsync(AvailableQuery(Start, End, type: "Chair"));

        var problem = await response.ReadProblemAsync(HttpStatusCode.BadRequest);
        problem.AssertHasError("type");
    }

    [Theory]
    [InlineData("0")]
    [InlineData("51")]
    [InlineData("-1")]
    [InlineData("abc")]
    [InlineData("2.5")]
    public async Task InvalidMinCapacity_Returns400UnderMinCapacity(string minCapacity)
    {
        await using var factory = new RoomBookingApiFactory();
        var client = factory.CreateClient();

        using var response = await client.GetAvailableRawAsync(AvailableQuery(Start, End, minCapacity: minCapacity));

        var problem = await response.ReadProblemAsync(HttpStatusCode.BadRequest);
        problem.AssertHasError("minCapacity");
    }

    // ---------- reguły czasowe (policy) z mapowaniem kluczy startUtc->start, endUtc->end ----------

    [Fact]
    public async Task BeforeOpening_0745_0815_Returns400UnderStart_Mapped()
    {
        await using var factory = new RoomBookingApiFactory();
        var client = factory.CreateClient();

        // 07:45-08:15 lokalnie (CEST) = 05:45Z-06:15Z
        using var response = await client.GetAvailableRawAsync(AvailableQuery(Utc(2026, 10, 21, 5, 45), Utc(2026, 10, 21, 6, 15)));

        var problem = await response.ReadProblemAsync(HttpStatusCode.BadRequest);
        problem.AssertHasError("start");
        problem.AssertNoError("startUtc");
        problem.AssertNoError("endUtc");
    }

    [Fact]
    public async Task AfterClosing_1945_2015_Returns400UnderEnd_Mapped()
    {
        await using var factory = new RoomBookingApiFactory();
        var client = factory.CreateClient();

        // 19:45-20:15 lokalnie (CEST) = 17:45Z-18:15Z
        using var response = await client.GetAvailableRawAsync(AvailableQuery(Utc(2026, 10, 21, 17, 45), Utc(2026, 10, 21, 18, 15)));

        var problem = await response.ReadProblemAsync(HttpStatusCode.BadRequest);
        problem.AssertHasError("end");
        problem.AssertNoError("endUtc");
    }

    [Theory]
    [InlineData(10, 0, 10, 10, "end")]   // za krótko (i niewyrównane)
    [InlineData(10, 0, 9, 0, "end")]     // end < start
    [InlineData(10, 5, 11, 0, "start")]  // start niewyrównany
    [InlineData(8, 0, 16, 15, "end")]    // > 8 h
    public async Task PolicyViolations_Return400UnderMappedKeys(int sh, int sm, int eh, int em, string expectedKey)
    {
        await using var factory = new RoomBookingApiFactory();
        var client = factory.CreateClient();
        var cest = TimeSpan.FromHours(2);
        var start = new DateTimeOffset(2026, 10, 21, sh, sm, 0, cest);
        var end = new DateTimeOffset(2026, 10, 21, eh, em, 0, cest);

        using var response = await client.GetAvailableRawAsync(AvailableQuery(start, end));

        var problem = await response.ReadProblemAsync(HttpStatusCode.BadRequest);
        problem.AssertHasError(expectedKey);
        problem.AssertNoError("startUtc");
        problem.AssertNoError("endUtc");
    }

    [Fact]
    public async Task StartInThePast_Returns400UnderStart()
    {
        await using var factory = new RoomBookingApiFactory();
        var client = factory.CreateClient();

        // 2026-10-19 = dzień przed "teraz"
        using var response = await client.GetAvailableRawAsync(AvailableQuery(Utc(2026, 10, 19, 8), Utc(2026, 10, 19, 9)));

        var problem = await response.ReadProblemAsync(HttpStatusCode.BadRequest);
        problem.AssertHasError("start");
    }

    [Fact]
    public async Task OnDstChangeDay_UsesLocalWorkingHours()
    {
        await using var factory = new RoomBookingApiFactory();
        var client = factory.CreateClient();

        // 2026-10-25 po cofnięciu zegara: 08:00 lokalnie = 07:00Z -> OK; 06:00Z = 07:00 lokalnie -> 400.
        Assert.Equal(7, (await client.GetAvailableAsync(Utc(2026, 10, 25, 7), Utc(2026, 10, 25, 8))).Count);
        using var tooEarly = await client.GetAvailableRawAsync(AvailableQuery(Utc(2026, 10, 25, 6), Utc(2026, 10, 25, 7)));
        (await tooEarly.ReadProblemAsync(HttpStatusCode.BadRequest)).AssertHasError("start");
    }

    private static async Task CreateResourceAsync(HttpClient client, string name, string type, int capacity)
    {
        using var response = await client.PostAsJsonAsync("/resources", new { name, type, capacity });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }
}
