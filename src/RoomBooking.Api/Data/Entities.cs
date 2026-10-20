using RoomBooking.Domain;

namespace RoomBooking.Api.Data;

public sealed class Resource
{
    public const int NameMaxLength = 80;

    public Guid Id { get; set; }

    public required string Name { get; set; }

    /// <summary>Nazwa po Trim() i ToUpperInvariant(); unikalny indeks daje unikalność bez wielkości liter.</summary>
    public required string NormalizedName { get; set; }

    public ResourceType Type { get; set; }

    public int Capacity { get; set; }

    public bool IsActive { get; set; } = true;

    public static string Normalize(string name) => name.Trim().ToUpperInvariant();
}

public sealed class Booking
{
    public const int BookedByMaxLength = 60;
    public const int TitleMaxLength = 100;

    public Guid Id { get; set; }

    public Guid ResourceId { get; set; }

    public Resource? Resource { get; set; }

    public required string BookedBy { get; set; }

    /// <summary>
    /// Imię po Trim() i ToUpperInvariant() (także polskie znaki); indeks dla GET /bookings?bookedBy=.
    /// Pusty string = wiersz sprzed migracji, uzupełniany przy starcie (DatabaseInitializer).
    /// </summary>
    public required string BookedByNormalized { get; set; }

    public string? Title { get; set; }

    public DateTimeOffset StartUtc { get; set; }

    public DateTimeOffset EndUtc { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? CancelledAt { get; set; }

    public static string NormalizeBookedBy(string bookedBy) => bookedBy.Trim().ToUpperInvariant();
}
