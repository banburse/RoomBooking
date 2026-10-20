using RoomBooking.Api.Data;
using RoomBooking.Domain;

namespace RoomBooking.Api.Endpoints;

// Wejście: typy "luźne" (string/int?), żeby cała walidacja (w tym format czasu z offsetem)
// odbywała się w jednym miejscu: RequestValidator.

public sealed record CreateResourceRequest(string? Name, string? Type, int? Capacity);

public sealed record CreateBookingRequest(
    string? ResourceId,
    string? BookedBy,
    string? Title,
    string? StartUtc,
    string? EndUtc);

public sealed record UpdateBookingRequest(string? StartUtc, string? EndUtc, string? Title);

// Wyjście wg CONTRACT.md.

public sealed record ResourceResponse(Guid Id, string Name, ResourceType Type, int Capacity, bool IsActive)
{
    public static ResourceResponse From(Resource r) => new(r.Id, r.Name, r.Type, r.Capacity, r.IsActive);
}

public sealed record BookingResponse(
    Guid Id,
    Guid ResourceId,
    string BookedBy,
    string? Title,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    DateTimeOffset CreatedAt)
{
    public static BookingResponse From(Booking b) =>
        new(b.Id, b.ResourceId, b.BookedBy, b.Title, b.StartUtc, b.EndUtc, b.CreatedAt);
}

/// <summary>GET /bookings?bookedBy=: Booking z nazwą i typem zasobu.</summary>
public sealed record MyBookingResponse(
    Guid Id,
    Guid ResourceId,
    string BookedBy,
    string? Title,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    DateTimeOffset CreatedAt,
    string ResourceName,
    ResourceType ResourceType);
