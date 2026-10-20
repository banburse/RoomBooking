using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RoomBooking.Api.Data;
using RoomBooking.Api.Http;
using RoomBooking.Api.Validation;
using RoomBooking.Domain;

namespace RoomBooking.Api.Endpoints;

public static class ResourceEndpoints
{
    /// <summary>Kolejność nazw po polsku i bez wielkości liter (SQLite BINARY sortuje źle).</summary>
    private static readonly StringComparer ResourceNameComparer =
        StringComparer.Create(CultureInfo.GetCultureInfo("pl-PL"), ignoreCase: true);

    public static IEndpointRouteBuilder MapResourceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/resources").WithTags("Resources");

        group.MapGet("/", ListResources)
            .WithName("ListResources")
            .Produces<ResourceResponse[]>()
            .ProducesValidationProblem();

        group.MapPost("/", CreateResource)
            .WithName("CreateResource")
            .Produces<ResourceResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        // Stały segment "available" ma pierwszeństwo, a {id:guid} i tak go nie dopasuje.
        group.MapGet("/available", ListAvailableResources)
            .WithName("ListAvailableResources")
            .Produces<ResourceResponse[]>()
            .ProducesValidationProblem();

        group.MapGet("/{id:guid}/bookings", ListResourceBookings)
            .WithName("ListResourceBookings")
            .Produces<BookingResponse[]>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    // 1. GET /resources?type=Room|Desk
    private static async Task<IResult> ListResources(
        string? type,
        RoomBookingDbContext db,
        CancellationToken cancellationToken)
    {
        var filter = RequestValidator.ValidateResourceTypeFilter(type);
        if (!filter.IsValid)
        {
            return Problems.Validation(filter.Errors!);
        }

        var query = db.Resources.AsNoTracking().Where(r => r.IsActive);
        if (filter.Value is { } resourceType)
        {
            query = query.Where(r => r.Type == resourceType);
        }

        var resources = await query
            .OrderBy(r => r.Type == ResourceType.Room ? 0 : 1)
            .ThenBy(r => r.Name)
            .Select(r => new ResourceResponse(r.Id, r.Name, r.Type, r.Capacity, r.IsActive))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(resources);
    }

    // 6. GET /resources/available?start=...&end=...&type=...&minCapacity=...
    private static async Task<IResult> ListAvailableResources(
        string? start,
        string? end,
        string? type,
        string? minCapacity,
        IBookingPolicy policy,
        RoomBookingDbContext db,
        CancellationToken cancellationToken)
    {
        var validated = RequestValidator.ValidateAvailabilityQuery(start, end, type, minCapacity);
        if (!validated.IsValid)
        {
            return Problems.Validation(validated.Errors!);
        }

        var query = validated.Value!;

        // Te same reguły czasowe co przy rezerwacji; pola tekstowe policy nie sprawdza.
        var rules = policy.Validate(new BookingRequest(Guid.Empty, string.Empty, null, query.StartUtc, query.EndUtc));
        if (!rules.IsValid)
        {
            return Problems.Validation(rules.Errors.ToDictionary(
                kv => kv.Key switch { "startUtc" => "start", "endUtc" => "end", _ => kv.Key },
                kv => kv.Value,
                StringComparer.Ordinal));
        }

        var candidatesQuery = db.Resources.AsNoTracking().Where(r => r.IsActive);
        if (query.Type is { } resourceType)
        {
            candidatesQuery = candidatesQuery.Where(r => r.Type == resourceType);
        }

        if (query.MinCapacity is { } capacity)
        {
            candidatesQuery = candidatesQuery.Where(r => r.Capacity >= capacity);
        }

        var candidates = await candidatesQuery.ToListAsync(cancellationToken);
        var candidateIds = candidates.Select(r => r.Id).ToList();

        // Jedno zapytanie dla wszystkich kandydatów (bez N+1). Filtr nakładania w SQL to tylko
        // wstępne zawężenie; o kolizji rozstrzyga IBookingPolicy.FindConflict.
        var slotsByResource = (await db.Bookings.AsNoTracking()
                .Where(b => candidateIds.Contains(b.ResourceId)
                    && b.CancelledAt == null
                    && b.StartUtc < query.EndUtc
                    && b.EndUtc > query.StartUtc)
                .Select(b => new { b.ResourceId, Slot = new BookingSlot(b.Id, b.StartUtc, b.EndUtc, false) })
                .ToListAsync(cancellationToken))
            .ToLookup(x => x.ResourceId, x => x.Slot);

        var available = candidates
            .Where(r => policy.FindConflict(query.StartUtc, query.EndUtc, slotsByResource[r.Id]) is null)
            .OrderBy(r => r.Capacity)
            .ThenBy(r => r.Type == ResourceType.Room ? 0 : 1)
            .ThenBy(r => r.Name, ResourceNameComparer)
            .Select(ResourceResponse.From)
            .ToList();

        return TypedResults.Ok(available);
    }

    // 2. POST /resources
    private static async Task<IResult> CreateResource(
        CreateResourceRequest request,
        RoomBookingDbContext db,
        CancellationToken cancellationToken)
    {
        var validated = RequestValidator.ValidateCreateResource(request);
        if (!validated.IsValid)
        {
            return Problems.Validation(validated.Errors!);
        }

        var input = validated.Value!;
        var normalized = Resource.Normalize(input.Name);

        if (await db.Resources.AsNoTracking().AnyAsync(r => r.NormalizedName == normalized, cancellationToken))
        {
            return DuplicateName(input.Name);
        }

        var resource = new Resource
        {
            Id = Guid.NewGuid(),
            Name = input.Name,
            NormalizedName = normalized,
            Type = input.Type,
            Capacity = input.Capacity,
            IsActive = true,
        };
        db.Resources.Add(resource);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqliteException { SqliteErrorCode: 19 })
        {
            // Wyścig dwóch żądań z tą samą nazwą: unikalny indeks rozstrzyga.
            return DuplicateName(input.Name);
        }

        return TypedResults.Created($"/resources/{resource.Id}", ResourceResponse.From(resource));
    }

    // 3. GET /resources/{id}/bookings?from=...&to=...
    private static async Task<IResult> ListResourceBookings(
        Guid id,
        string? from,
        string? to,
        RequestValidator validator,
        RoomBookingDbContext db,
        CancellationToken cancellationToken)
    {
        var range = validator.ValidateBookingRange(from, to);
        if (!range.IsValid)
        {
            return Problems.Validation(range.Errors!);
        }

        if (!await db.Resources.AsNoTracking().AnyAsync(r => r.Id == id, cancellationToken))
        {
            return Problems.NotFound($"Zasób {id} nie istnieje.");
        }

        var (fromUtc, toUtc) = range.Value!;
        var bookings = await db.Bookings.AsNoTracking()
            .Where(b => b.ResourceId == id
                && b.CancelledAt == null
                && b.StartUtc < toUtc
                && b.EndUtc > fromUtc)
            .OrderBy(b => b.StartUtc)
            .Select(b => new BookingResponse(b.Id, b.ResourceId, b.BookedBy, b.Title, b.StartUtc, b.EndUtc, b.CreatedAt))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(bookings);
    }

    private static IResult DuplicateName(string name) =>
        Problems.Conflict($"Zasób o nazwie '{name}' już istnieje.");
}
