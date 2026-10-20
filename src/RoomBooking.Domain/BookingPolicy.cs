namespace RoomBooking.Domain;

/// <summary>
/// Reguły czasowe rezerwacji i wykrywanie kolizji. Czyste obliczenia, bez EF.
/// Czas bieżący wyłącznie z <see cref="TimeProvider"/>; godziny pracy liczone w strefie biura (z DST).
/// </summary>
public sealed class BookingPolicy(TimeProvider timeProvider, BookingPolicyOptions options) : IBookingPolicy
{
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    private readonly BookingPolicyOptions _options = options ?? throw new ArgumentNullException(nameof(options));

    public BookingValidationResult Validate(BookingRequest request) =>
        throw new NotImplementedException($"{nameof(BookingPolicy)} for {_options.TimeZoneId} at {_timeProvider.GetUtcNow()}");

    public Guid? FindConflict(DateTimeOffset startUtc, DateTimeOffset endUtc, IEnumerable<BookingSlot> existing)
    {
        ArgumentNullException.ThrowIfNull(existing);

        // Przedziały półotwarte [start, end): nachodzą na siebie, gdy a.start < b.end && a.end > b.start.
        var conflict = existing
            .Where(b => !b.IsCancelled && b.StartUtc < endUtc && b.EndUtc > startUtc)
            .OrderBy(b => b.StartUtc)
            .ThenBy(b => b.Id)
            .FirstOrDefault();

        return conflict?.Id;
    }
}
