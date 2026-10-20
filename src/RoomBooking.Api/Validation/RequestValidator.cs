using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using RoomBooking.Api.Data;
using RoomBooking.Api.Endpoints;
using RoomBooking.Domain;

namespace RoomBooking.Api.Validation;

/// <summary>Wynik walidacji wejścia: albo wartość, albo słownik błędów (klucze = pola JSON w camelCase).</summary>
public sealed class Validated<T>
{
    private Validated(T? value, Dictionary<string, string[]>? errors)
    {
        Value = value;
        Errors = errors;
    }

    public T? Value { get; }

    public Dictionary<string, string[]>? Errors { get; }

    public bool IsValid => Errors is null;

    public static Validated<T> Ok(T value) => new(value, null);

    public static Validated<T> Fail(Dictionary<string, string[]> errors) => new(default, errors);
}

public sealed record NewResource(string Name, ResourceType Type, int Capacity);

public sealed record BookingRange(DateTimeOffset FromUtc, DateTimeOffset ToUtc);

public sealed record BookingChange(DateTimeOffset StartUtc, DateTimeOffset EndUtc, string? Title);

public sealed record AvailabilityQuery(DateTimeOffset StartUtc, DateTimeOffset EndUtc, ResourceType? Type, int? MinCapacity);

/// <summary>Jedyne miejsce walidacji wejścia API (body i parametry zapytań).</summary>
public sealed partial class RequestValidator
{
    public const int MaxRangeDays = 31;
    public const int MinCapacity = 1;
    public const int MaxCapacity = 50;

    private readonly TimeZoneInfo _officeZone;

    public RequestValidator(IOptions<BookingPolicyOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _officeZone = TimeZoneInfo.FindSystemTimeZoneById(options.Value.TimeZoneId);
    }

    public static Validated<ResourceType?> ValidateResourceTypeFilter(string? type)
    {
        if (type is null)
        {
            return Validated<ResourceType?>.Ok(null);
        }

        return TryParseResourceType(type, out var parsed)
            ? Validated<ResourceType?>.Ok(parsed)
            : Validated<ResourceType?>.Fail(Single("type", Messages.UnknownType));
    }

    public static Validated<NewResource> ValidateCreateResource(CreateResourceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var errors = new ErrorBag();

        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            errors.Add("name", "Nazwa jest wymagana.");
        }
        else if (name.Length > Resource.NameMaxLength)
        {
            errors.Add("name", $"Nazwa może mieć maksymalnie {Resource.NameMaxLength} znaków.");
        }

        ResourceType type = default;
        if (string.IsNullOrWhiteSpace(request.Type))
        {
            errors.Add("type", "Typ zasobu jest wymagany.");
        }
        else if (!TryParseResourceType(request.Type, out type))
        {
            errors.Add("type", Messages.UnknownType);
        }

        if (request.Capacity is null)
        {
            errors.Add("capacity", "Pojemność jest wymagana.");
        }
        else if (request.Capacity is < MinCapacity or > MaxCapacity)
        {
            errors.Add("capacity", $"Pojemność musi być w zakresie {MinCapacity}–{MaxCapacity}.");
        }

        return errors.HasErrors
            ? Validated<NewResource>.Fail(errors.ToDictionary())
            : Validated<NewResource>.Ok(new NewResource(name!, type, request.Capacity!.Value));
    }

    public static Validated<BookingRequest> ValidateCreateBooking(CreateBookingRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var errors = new ErrorBag();

        var resourceId = Guid.Empty;
        if (string.IsNullOrWhiteSpace(request.ResourceId))
        {
            errors.Add("resourceId", "Identyfikator zasobu jest wymagany.");
        }
        else if (!Guid.TryParse(request.ResourceId, out resourceId) || resourceId == Guid.Empty)
        {
            errors.Add("resourceId", "Nieprawidłowy identyfikator zasobu.");
        }

        var bookedBy = ValidateBookedBy(request.BookedBy, errors);
        var title = ValidateTitle(request.Title, errors);
        var (start, end) = ValidateBookingTimes(request.StartUtc, request.EndUtc, errors);

        return errors.HasErrors
            ? Validated<BookingRequest>.Fail(errors.ToDictionary())
            : Validated<BookingRequest>.Ok(new BookingRequest(resourceId, bookedBy!, title, start, end));
    }

    /// <summary>GET /bookings?bookedBy=: wymagane, po Trim() 1–60 znaków. Zwraca imię po Trim().</summary>
    public static Validated<string> ValidateBookedByQuery(string? bookedBy)
    {
        var errors = new ErrorBag();
        var trimmed = ValidateBookedBy(bookedBy, errors);
        return errors.HasErrors
            ? Validated<string>.Fail(errors.ToDictionary())
            : Validated<string>.Ok(trimmed!);
    }

    private static string? ValidateBookedBy(string? value, ErrorBag errors)
    {
        var bookedBy = value?.Trim();
        if (string.IsNullOrEmpty(bookedBy))
        {
            errors.Add("bookedBy", "Imię jest wymagane.");
        }
        else if (bookedBy.Length > Booking.BookedByMaxLength)
        {
            errors.Add("bookedBy", $"Imię może mieć maksymalnie {Booking.BookedByMaxLength} znaków.");
        }

        return bookedBy;
    }

    /// <summary>PUT /bookings/{id}: te same reguły co w POST dla startUtc, endUtc i title.</summary>
    public static Validated<BookingChange> ValidateUpdateBooking(UpdateBookingRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var errors = new ErrorBag();

        var title = ValidateTitle(request.Title, errors);
        var (start, end) = ValidateBookingTimes(request.StartUtc, request.EndUtc, errors);

        return errors.HasErrors
            ? Validated<BookingChange>.Fail(errors.ToDictionary())
            : Validated<BookingChange>.Ok(new BookingChange(start, end, title));
    }

    private static string? ValidateTitle(string? value, ErrorBag errors)
    {
        var title = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (title is { Length: > Booking.TitleMaxLength })
        {
            errors.Add("title", $"Tytuł może mieć maksymalnie {Booking.TitleMaxLength} znaków.");
        }

        return title;
    }

    private static (DateTimeOffset Start, DateTimeOffset End) ValidateBookingTimes(string? start, string? end, ErrorBag errors) =>
        (ParseRequiredInstant(start, "startUtc", "Początek rezerwacji jest wymagany.", errors),
         ParseRequiredInstant(end, "endUtc", "Koniec rezerwacji jest wymagany.", errors));

    public static Validated<AvailabilityQuery> ValidateAvailabilityQuery(
        string? start,
        string? end,
        string? type,
        string? minCapacity)
    {
        var errors = new ErrorBag();

        var startUtc = ParseRequiredInstant(start, "start", "Parametr 'start' jest wymagany.", errors);
        var endUtc = ParseRequiredInstant(end, "end", "Parametr 'end' jest wymagany.", errors);

        ResourceType? resourceType = null;
        if (type is not null)
        {
            if (TryParseResourceType(type, out var parsed))
            {
                resourceType = parsed;
            }
            else
            {
                errors.Add("type", Messages.UnknownType);
            }
        }

        int? capacity = null;
        if (minCapacity is not null)
        {
            if (int.TryParse(minCapacity, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
                && parsed is >= MinCapacity and <= MaxCapacity)
            {
                capacity = parsed;
            }
            else
            {
                errors.Add("minCapacity", $"Parametr 'minCapacity' musi być liczbą całkowitą {MinCapacity}–{MaxCapacity}.");
            }
        }

        return errors.HasErrors
            ? Validated<AvailabilityQuery>.Fail(errors.ToDictionary())
            : Validated<AvailabilityQuery>.Ok(new AvailabilityQuery(startUtc, endUtc, resourceType, capacity));
    }

    public Validated<BookingRange> ValidateBookingRange(string? from, string? to)
    {
        var errors = new ErrorBag();
        var fromUtc = ParseRangeBoundary(from, "from", errors);
        var toUtc = ParseRangeBoundary(to, "to", errors);

        if (!errors.HasErrors)
        {
            if (toUtc <= fromUtc)
            {
                errors.Add("to", "Data 'to' musi być późniejsza niż 'from'.");
            }
            else
            {
                // Długość zakresu liczona w czasie lokalnym biura, żeby 31 dni kalendarzowych
                // obejmujących zmianę czasu (DST) nie było odrzucane przez dodatkową godzinę.
                var localSpan = TimeZoneInfo.ConvertTime(toUtc, _officeZone).DateTime
                    - TimeZoneInfo.ConvertTime(fromUtc, _officeZone).DateTime;
                if (localSpan > TimeSpan.FromDays(MaxRangeDays))
                {
                    errors.Add("to", $"Zakres nie może przekraczać {MaxRangeDays} dni.");
                }
            }
        }

        return errors.HasErrors
            ? Validated<BookingRange>.Fail(errors.ToDictionary())
            : Validated<BookingRange>.Ok(new BookingRange(fromUtc, toUtc));
    }

    private DateTimeOffset ParseRangeBoundary(string? value, string key, ErrorBag errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(key, $"Parametr '{key}' jest wymagany.");
            return default;
        }

        if (DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return OfficeMidnightUtc(date);
        }

        if (TryParseInstant(value, out var instant))
        {
            return instant;
        }

        errors.Add(key, $"Parametr '{key}' musi mieć format yyyy-MM-dd albo ISO 8601 z przesunięciem strefy.");
        return default;
    }

    private DateTimeOffset OfficeMidnightUtc(DateOnly date)
    {
        var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        while (_officeZone.IsInvalidTime(local))
        {
            local = local.AddMinutes(30);
        }

        return new DateTimeOffset(local, _officeZone.GetUtcOffset(local)).ToUniversalTime();
    }

    private static DateTimeOffset ParseRequiredInstant(string? value, string key, string requiredMessage, ErrorBag errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(key, requiredMessage);
            return default;
        }

        if (!TryParseInstant(value, out var instant))
        {
            errors.Add(key, Messages.InvalidInstant);
            return default;
        }

        return instant;
    }

    /// <summary>ISO 8601 z wymaganym offsetem (Z lub ±hh:mm); wynik znormalizowany do UTC.</summary>
    private static bool TryParseInstant(string value, out DateTimeOffset instant)
    {
        instant = default;
        if (!IsoWithOffset().IsMatch(value))
        {
            return false;
        }

        if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            return false;
        }

        instant = parsed.ToUniversalTime();
        return true;
    }

    private static bool TryParseResourceType(string value, out ResourceType type)
    {
        foreach (var candidate in Enum.GetValues<ResourceType>())
        {
            if (string.Equals(candidate.ToString(), value.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                type = candidate;
                return true;
            }
        }

        type = default;
        return false;
    }

    private static Dictionary<string, string[]> Single(string key, string message) => new() { [key] = [message] };

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(:\d{2}(\.\d{1,7})?)?(Z|z|[+-]\d{2}:\d{2})$", RegexOptions.CultureInvariant)]
    private static partial Regex IsoWithOffset();

    private static class Messages
    {
        public const string UnknownType = "Nieznany typ zasobu. Dozwolone wartości: Room, Desk.";

        public const string InvalidInstant =
            "Nieprawidłowy format czasu. Wymagany ISO 8601 z przesunięciem strefy, np. 2026-10-21T08:00:00Z.";
    }

    private sealed class ErrorBag
    {
        private readonly Dictionary<string, List<string>> _errors = new(StringComparer.Ordinal);

        public bool HasErrors => _errors.Count > 0;

        public void Add(string key, string message)
        {
            if (!_errors.TryGetValue(key, out var list))
            {
                list = [];
                _errors[key] = list;
            }

            list.Add(message);
        }

        public Dictionary<string, string[]> ToDictionary() =>
            _errors.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray(), StringComparer.Ordinal);
    }
}
