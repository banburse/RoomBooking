using Microsoft.EntityFrameworkCore;
using RoomBooking.Api.Data;
using RoomBooking.Api.Http;
using RoomBooking.Api.Validation;
using RoomBooking.Domain;

namespace RoomBooking.Api.Endpoints;

public static class BookingEndpoints
{
    public static IEndpointRouteBuilder MapBookingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/bookings").WithTags("Bookings");

        group.MapPost("/", CreateBooking)
            .WithName("CreateBooking")
            .Produces<BookingResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPut("/{id:guid}", UpdateBooking)
            .WithName("UpdateBooking")
            .Produces<BookingResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapDelete("/{id:guid}", CancelBooking)
            .WithName("CancelBooking")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }

    // 4. POST /bookings. Kolejność sprawdzeń wg CONTRACT.md: walidacja -> zasób -> reguły czasowe -> kolizja.
    private static async Task<IResult> CreateBooking(
        CreateBookingRequest request,
        IBookingPolicy policy,
        TimeProvider timeProvider,
        RoomBookingDbContext db,
        CancellationToken cancellationToken)
    {
        var validated = RequestValidator.ValidateCreateBooking(request);
        if (!validated.IsValid)
        {
            return Problems.Validation(validated.Errors!);
        }

        var input = validated.Value!;

        var resourceActive = await db.Resources.AsNoTracking()
            .AnyAsync(r => r.Id == input.ResourceId && r.IsActive, cancellationToken);
        if (!resourceActive)
        {
            return Problems.NotFound($"Zasób {input.ResourceId} nie istnieje.");
        }

        var rules = policy.Validate(input);
        if (!rules.IsValid)
        {
            return Problems.Validation(rules.Errors);
        }

        // SQLite: BeginTransaction = BEGIN IMMEDIATE, czyli blokada zapisu od początku transakcji.
        // Odczyt kolizji i zapis są więc atomowe względem równoległych żądań: drugie czeka na commit
        // pierwszego i widzi już jego rezerwację.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var overlapping = await db.Bookings.AsNoTracking()
            .Where(b => b.ResourceId == input.ResourceId
                && b.CancelledAt == null
                && b.StartUtc < input.EndUtc
                && b.EndUtc > input.StartUtc)
            .OrderBy(b => b.StartUtc)
            .Select(b => new BookingSlot(b.Id, b.StartUtc, b.EndUtc, false))
            .ToListAsync(cancellationToken);

        var conflictId = policy.FindConflict(input.StartUtc, input.EndUtc, overlapping);
        if (conflictId is { } conflicting)
        {
            return Problems.BookingCollision(conflicting);
        }

        var booking = new Booking
        {
            Id = Guid.NewGuid(),
            ResourceId = input.ResourceId,
            BookedBy = input.BookedBy,
            BookedByNormalized = Booking.NormalizeBookedBy(input.BookedBy),
            Title = input.Title,
            StartUtc = input.StartUtc,
            EndUtc = input.EndUtc,
            CreatedAt = timeProvider.GetUtcNow(),
        };
        db.Bookings.Add(booking);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return TypedResults.Created($"/bookings/{booking.Id}", BookingResponse.From(booking));
    }

    // 7. PUT /bookings/{id}. Kolejność wg CONTRACT.md: walidacja -> istnienie (404) -> rozpoczęta (409)
    // -> reguły czasowe (400) -> w transakcji: ponownie 404/409 i kolizja z innymi rezerwacjami.
    private static async Task<IResult> UpdateBooking(
        Guid id,
        UpdateBookingRequest request,
        IBookingPolicy policy,
        TimeProvider timeProvider,
        RoomBookingDbContext db,
        CancellationToken cancellationToken)
    {
        var validated = RequestValidator.ValidateUpdateBooking(request);
        if (!validated.IsValid)
        {
            return Problems.Validation(validated.Errors!);
        }

        var change = validated.Value!;

        var (current, problem) = await LoadEditableAsync(db.Bookings.AsNoTracking(), id, timeProvider, cancellationToken);
        if (problem is not null)
        {
            return problem;
        }

        var rules = policy.Validate(
            new BookingRequest(current!.ResourceId, current.BookedBy, change.Title, change.StartUtc, change.EndUtc));
        if (!rules.IsValid)
        {
            return Problems.Validation(rules.Errors);
        }

        // BEGIN IMMEDIATE: ponowne sprawdzenie, kolizja i zapis atomowo względem równoległych POST/PUT/DELETE.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var (booking, recheck) = await LoadEditableAsync(db.Bookings, id, timeProvider, cancellationToken);
        if (recheck is not null)
        {
            return recheck;
        }

        // Edytowana rezerwacja wyłączona z listy: przesunięcie na własny stary termin nie jest kolizją.
        var others = await db.Bookings.AsNoTracking()
            .Where(b => b.ResourceId == booking!.ResourceId
                && b.Id != id
                && b.CancelledAt == null
                && b.StartUtc < change.EndUtc
                && b.EndUtc > change.StartUtc)
            .OrderBy(b => b.StartUtc)
            .Select(b => new BookingSlot(b.Id, b.StartUtc, b.EndUtc, false))
            .ToListAsync(cancellationToken);

        var conflictId = policy.FindConflict(change.StartUtc, change.EndUtc, others);
        if (conflictId is { } conflicting)
        {
            return Problems.BookingCollision(conflicting);
        }

        booking!.StartUtc = change.StartUtc;
        booking.EndUtc = change.EndUtc;
        booking.Title = change.Title;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return TypedResults.Ok(BookingResponse.From(booking));
    }

    /// <summary>Sprawdzenia 2–3 z PUT: istnieje, nieanulowana, zasób aktywny (404); jeszcze się nie rozpoczęła (409).</summary>
    private static async Task<(Booking? Booking, IResult? Problem)> LoadEditableAsync(
        IQueryable<Booking> bookings,
        Guid id,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var booking = await bookings
            .Where(b => b.Id == id && b.CancelledAt == null && b.Resource!.IsActive)
            .FirstOrDefaultAsync(cancellationToken);
        if (booking is null)
        {
            return (null, Problems.NotFound($"Rezerwacja {id} nie istnieje."));
        }

        if (booking.StartUtc <= timeProvider.GetUtcNow())
        {
            return (null, Problems.Conflict("Nie można edytować rezerwacji, która już się rozpoczęła."));
        }

        return (booking, null);
    }

    // 5. DELETE /bookings/{id}
    private static async Task<IResult> CancelBooking(
        Guid id,
        TimeProvider timeProvider,
        RoomBookingDbContext db,
        CancellationToken cancellationToken)
    {
        var booking = await db.Bookings.FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (booking is null || booking.CancelledAt is not null)
        {
            return Problems.NotFound($"Rezerwacja {id} nie istnieje.");
        }

        var now = timeProvider.GetUtcNow();
        if (booking.EndUtc <= now)
        {
            return Problems.Conflict("Nie można anulować rezerwacji, która już się zakończyła.");
        }

        booking.CancelledAt = now;
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.NoContent();
    }
}
