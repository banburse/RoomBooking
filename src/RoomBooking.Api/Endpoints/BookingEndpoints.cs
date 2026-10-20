using Microsoft.EntityFrameworkCore;
using RoomBooking.Api.Data;
using RoomBooking.Api.Http;

namespace RoomBooking.Api.Endpoints;

public static class BookingEndpoints
{
    public static IEndpointRouteBuilder MapBookingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/bookings").WithTags("Bookings");

        group.MapDelete("/{id:guid}", CancelBooking)
            .WithName("CancelBooking")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
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
