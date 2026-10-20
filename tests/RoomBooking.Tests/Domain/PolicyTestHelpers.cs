using Microsoft.Extensions.Time.Testing;
using RoomBooking.Domain;

namespace RoomBooking.Tests.Domain;

internal static class PolicyTestHelpers
{
    // Czas "teraz" daleko przed testowanymi terminami, żeby reguła przeszłości nie przeszkadzała.
    public static readonly DateTimeOffset DefaultNow = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static readonly TimeSpan Cest = TimeSpan.FromHours(2); // czas letni w Warszawie
    public static readonly TimeSpan Cet = TimeSpan.FromHours(1);  // czas zimowy w Warszawie

    public static BookingPolicy CreatePolicy(DateTimeOffset? now = null, BookingPolicyOptions? options = null) =>
        new(new FakeTimeProvider(now ?? DefaultNow), options ?? new BookingPolicyOptions());

    public static BookingRequest Request(DateTimeOffset start, DateTimeOffset end) =>
        new(Guid.NewGuid(), "Jan", "Daily", start, end);

    public static BookingValidationResult Validate(DateTimeOffset start, DateTimeOffset end, BookingPolicy? policy = null) =>
        (policy ?? CreatePolicy()).Validate(Request(start, end));

    public static DateTimeOffset Utc(int year, int month, int day, int hour, int minute = 0) =>
        new(year, month, day, hour, minute, 0, TimeSpan.Zero);

    /// <summary>Czas lokalny biura podany jawnie z offsetem (niezależna wyrocznia testu).</summary>
    public static DateTimeOffset Local(int year, int month, int day, int hour, int minute, TimeSpan offset) =>
        new(year, month, day, hour, minute, 0, offset);

    /// <summary>2026-10-21 (środa) – CEST, +02:00.</summary>
    public static DateTimeOffset Oct21(int hour, int minute = 0) => Local(2026, 10, 21, hour, minute, Cest);

    public static void AssertValid(BookingValidationResult result) =>
        Assert.True(result.IsValid, "Oczekiwano poprawnego wyniku, błędy: " +
            string.Join("; ", result.Errors.Select(e => $"{e.Key}: {string.Join(" | ", e.Value)}")));

    public static void AssertError(BookingValidationResult result, string key)
    {
        Assert.False(result.IsValid);
        Assert.True(result.Errors.ContainsKey(key),
            $"Brak błędu pod kluczem '{key}', są: {string.Join(", ", result.Errors.Keys)}");
        Assert.All(result.Errors[key], m => Assert.False(string.IsNullOrWhiteSpace(m)));
    }
}
