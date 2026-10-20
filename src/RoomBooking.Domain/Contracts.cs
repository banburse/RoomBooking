namespace RoomBooking.Domain;

// KONTRAKT (krok 0). Sygnatur w tym pliku nie zmieniamy bez zgody Orchestratora.

public enum ResourceType
{
    Room,
    Desk,
}

/// <summary>Żądanie rezerwacji po walidacji wejścia (wymagane pola, długości). Czasy w UTC.</summary>
public sealed record BookingRequest(
    Guid ResourceId,
    string BookedBy,
    string? Title,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc);

/// <summary>Istniejąca rezerwacja zasobu, przekazywana do sprawdzania kolizji.</summary>
public sealed record BookingSlot(
    Guid Id,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    bool IsCancelled = false);

/// <summary>
/// Wynik sprawdzenia reguł czasowych. Klucze <see cref="Errors"/> to nazwy pól JSON
/// (<c>startUtc</c>, <c>endUtc</c>), wartości to komunikaty po polsku.
/// </summary>
public sealed class BookingValidationResult
{
    private BookingValidationResult(IReadOnlyDictionary<string, string[]> errors) => Errors = errors;

    public IReadOnlyDictionary<string, string[]> Errors { get; }

    public bool IsValid => Errors.Count == 0;

    public static BookingValidationResult Success { get; } = new(new Dictionary<string, string[]>());

    public static BookingValidationResult Failure(IReadOnlyDictionary<string, string[]> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        if (errors.Count == 0)
        {
            throw new ArgumentException("Failure requires at least one error.", nameof(errors));
        }

        return new BookingValidationResult(errors);
    }
}

/// <summary>Konfiguracja reguł biura (sekcja <c>Office</c> w appsettings).</summary>
public sealed class BookingPolicyOptions
{
    public string TimeZoneId { get; set; } = "Europe/Warsaw";
    public TimeOnly OpeningTime { get; set; } = new(8, 0);
    public TimeOnly ClosingTime { get; set; } = new(20, 0);
    public TimeSpan MinDuration { get; set; } = TimeSpan.FromMinutes(15);
    public TimeSpan MaxDuration { get; set; } = TimeSpan.FromHours(8);
    public TimeSpan SlotGranularity { get; set; } = TimeSpan.FromMinutes(15);
}

public interface IBookingPolicy
{
    /// <summary>
    /// Reguły czasowe ze SPEC.md: start &lt; end, 15 min..8 h, wyrównanie do 15 min,
    /// jeden dzień i godziny pracy w strefie biura (z DST), start &gt;= now (TimeProvider).
    /// Nie sprawdza pól tekstowych ani istnienia zasobu.
    /// </summary>
    BookingValidationResult Validate(BookingRequest request);

    /// <summary>
    /// Zwraca Id pierwszej (najwcześniejszej po StartUtc) niezanulowanej rezerwacji, która nachodzi na
    /// półotwarty przedział [startUtc, endUtc), albo null. Back-to-back nie jest kolizją.
    /// </summary>
    Guid? FindConflict(DateTimeOffset startUtc, DateTimeOffset endUtc, IEnumerable<BookingSlot> existing);
}
