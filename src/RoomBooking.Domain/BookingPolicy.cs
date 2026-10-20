namespace RoomBooking.Domain;

// Szkielet: implementuje Dev-B (dev-domain). Konstruktor jest częścią kontraktu (DI w Api).
public sealed class BookingPolicy(TimeProvider timeProvider, BookingPolicyOptions options) : IBookingPolicy
{
    private readonly TimeProvider _timeProvider = timeProvider;
    private readonly BookingPolicyOptions _options = options;

    public BookingValidationResult Validate(BookingRequest request) =>
        throw new NotImplementedException($"{nameof(BookingPolicy)} for {_options.TimeZoneId} at {_timeProvider.GetUtcNow()}");

    public Guid? FindConflict(DateTimeOffset startUtc, DateTimeOffset endUtc, IEnumerable<BookingSlot> existing) =>
        throw new NotImplementedException();
}
