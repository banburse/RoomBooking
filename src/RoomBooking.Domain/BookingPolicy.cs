using System.Globalization;

namespace RoomBooking.Domain;

/// <summary>
/// Reguły czasowe rezerwacji i wykrywanie kolizji. Czyste obliczenia, bez EF.
/// Czas bieżący wyłącznie z <see cref="TimeProvider"/>; godziny pracy liczone w strefie biura (z DST).
/// </summary>
public sealed class BookingPolicy(TimeProvider timeProvider, BookingPolicyOptions options) : IBookingPolicy
{
    private const string StartKey = "startUtc";
    private const string EndKey = "endUtc";

    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    private readonly BookingPolicyOptions _options = options ?? throw new ArgumentNullException(nameof(options));

    // Na Windows .NET 10 korzysta z ICU, więc identyfikatory IANA (np. "Europe/Warsaw") działają.
    private readonly TimeZoneInfo _zone = TimeZoneInfo.FindSystemTimeZoneById(options.TimeZoneId);

    public BookingValidationResult Validate(BookingRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        void Add(string key, string message)
        {
            if (!errors.TryGetValue(key, out var list))
            {
                errors[key] = list = [];
            }

            list.Add(message);
        }

        var start = request.StartUtc.ToUniversalTime();
        var end = request.EndUtc.ToUniversalTime();
        var startLocal = TimeZoneInfo.ConvertTime(start, _zone);
        var endLocal = TimeZoneInfo.ConvertTime(end, _zone);

        if (start >= end)
        {
            Add(EndKey, "Koniec rezerwacji musi być późniejszy niż początek.");
        }
        else
        {
            var duration = end - start;
            if (duration < _options.MinDuration)
            {
                Add(EndKey, $"Rezerwacja musi trwać co najmniej {FormatDuration(_options.MinDuration)}.");
            }
            else if (duration > _options.MaxDuration)
            {
                Add(EndKey, $"Rezerwacja może trwać maksymalnie {FormatDuration(_options.MaxDuration)}.");
            }
        }

        var granularity = FormatDuration(_options.SlotGranularity);
        if (!IsAligned(startLocal))
        {
            Add(StartKey, $"Początek rezerwacji musi być wyrównany do {granularity} (np. 08:00, 08:15).");
        }

        if (!IsAligned(endLocal))
        {
            Add(EndKey, $"Koniec rezerwacji musi być wyrównany do {granularity} (np. 09:00, 09:15).");
        }

        var hours = $"{Format(_options.OpeningTime)}–{Format(_options.ClosingTime)} (strefa {_options.TimeZoneId})";
        var startTime = TimeOnly.FromDateTime(startLocal.DateTime);
        if (startTime < _options.OpeningTime || startTime >= _options.ClosingTime)
        {
            Add(StartKey, $"Początek rezerwacji musi mieścić się w godzinach pracy {hours}.");
        }

        if (startLocal.Date != endLocal.Date)
        {
            Add(EndKey, $"Rezerwacja musi zaczynać się i kończyć tego samego dnia (strefa {_options.TimeZoneId}).");
        }
        else
        {
            var endTime = TimeOnly.FromDateTime(endLocal.DateTime);
            if (endTime > _options.ClosingTime || endTime <= _options.OpeningTime)
            {
                Add(EndKey, $"Koniec rezerwacji musi mieścić się w godzinach pracy {hours}.");
            }
        }

        return errors.Count == 0
            ? BookingValidationResult.Success
            : BookingValidationResult.Failure(errors.ToDictionary(e => e.Key, e => e.Value.ToArray(), StringComparer.Ordinal));
    }

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

    private bool IsAligned(DateTimeOffset local)
    {
        var granularityTicks = _options.SlotGranularity.Ticks;
        return granularityTicks <= 0 || local.TimeOfDay.Ticks % granularityTicks == 0;
    }

    private static string Format(TimeOnly time) => time.ToString("HH:mm", CultureInfo.InvariantCulture);

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration >= TimeSpan.FromHours(1) && duration.Ticks % TimeSpan.TicksPerHour == 0)
        {
            return $"{(long)duration.TotalHours} godz.";
        }

        return $"{(long)duration.TotalMinutes} minut";
    }
}
