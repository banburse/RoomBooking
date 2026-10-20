using RoomBooking.Domain;
using static RoomBooking.Tests.Domain.PolicyTestHelpers;

namespace RoomBooking.Tests.Domain;

/// <summary>Validate: start &lt; end, czas trwania, wyrównanie do slotu, argumenty.</summary>
public class BookingPolicyValidateTests
{
    // ---------- Happy path ----------

    [Fact]
    public void TypicalBooking_IsValid()
    {
        var result = Validate(Oct21(10), Oct21(11));
        AssertValid(result);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void AcceptsRequestGivenInUtc()
    {
        // 08:00Z-09:00Z == 10:00-11:00 w Warszawie (CEST)
        AssertValid(Validate(Utc(2026, 10, 21, 8), Utc(2026, 10, 21, 9)));
    }

    // ---------- start < end, czas trwania ----------

    [Fact]
    public void StartEqualsEnd_IsRejected_OnEndUtc()
    {
        AssertError(Validate(Oct21(10), Oct21(10)), "endUtc");
    }

    [Fact]
    public void StartAfterEnd_IsRejected_OnEndUtc()
    {
        AssertError(Validate(Oct21(11), Oct21(10)), "endUtc");
    }

    [Fact]
    public void Exactly15Minutes_IsValid()
    {
        AssertValid(Validate(Oct21(10), Oct21(10, 15)));
    }

    [Fact]
    public void ShorterThan15Minutes_IsRejected_WithPolishMessage()
    {
        var result = Validate(Oct21(10), Oct21(10, 10));
        AssertError(result, "endUtc");
        Assert.Contains("Rezerwacja musi trwać co najmniej 15 minut.", result.Errors["endUtc"]);
    }

    [Fact]
    public void Exactly8Hours_IsValid()
    {
        AssertValid(Validate(Oct21(9), Oct21(17)));
    }

    [Fact]
    public void LongerThan8Hours_IsRejected_OnEndUtc()
    {
        var result = Validate(Oct21(9), Oct21(17, 15));
        AssertError(result, "endUtc");
        Assert.Contains(result.Errors["endUtc"], m => m.Contains("maksymalnie 8 godz.", StringComparison.Ordinal));
        Assert.False(result.Errors.ContainsKey("startUtc"));
    }

    // ---------- wyrównanie do 15 min ----------

    [Theory]
    [InlineData(10, 0, true)]
    [InlineData(10, 15, true)]
    [InlineData(10, 30, true)]
    [InlineData(10, 45, true)]
    [InlineData(10, 5, false)]
    [InlineData(10, 10, false)]
    [InlineData(10, 50, false)]
    public void StartAlignment(int hour, int minute, bool valid)
    {
        var result = Validate(Oct21(hour, minute), Oct21(12));
        if (valid)
        {
            AssertValid(result);
        }
        else
        {
            AssertError(result, "startUtc");
            Assert.Contains(result.Errors["startUtc"], m => m.Contains("wyrównany", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void EndNotAligned_IsRejected_OnEndUtc()
    {
        var result = Validate(Oct21(10), Oct21(11, 20));
        AssertError(result, "endUtc");
        Assert.False(result.Errors.ContainsKey("startUtc"));
    }

    [Fact]
    public void StartWithSeconds_IsRejected()
    {
        AssertError(Validate(Oct21(10).AddSeconds(1), Oct21(11)), "startUtc");
    }

    [Fact]
    public void StartWithMilliseconds_IsRejected()
    {
        AssertError(Validate(Oct21(10).AddMilliseconds(1), Oct21(11)), "startUtc");
    }

    [Fact]
    public void EndWithSeconds_IsRejected()
    {
        AssertError(Validate(Oct21(10), Oct21(11).AddSeconds(30)), "endUtc");
    }

    [Fact]
    public void EndWithMilliseconds_IsRejected()
    {
        AssertError(Validate(Oct21(10), Oct21(11).AddMilliseconds(500)), "endUtc");
    }

    [Fact]
    public void Alignment_IsCheckedInOfficeTimeZone()
    {
        // Strefa z offsetem +05:30: 08:00 lokalnie = 02:30Z (wyrównane lokalnie, mimo że w UTC to :30).
        var policy = CreatePolicy(options: new BookingPolicyOptions { TimeZoneId = "Asia/Kolkata" });
        var ist = TimeSpan.FromHours(5.5);

        AssertValid(Validate(Local(2026, 10, 21, 8, 0, ist), Local(2026, 10, 21, 9, 0, ist), policy));
        AssertValid(Validate(Utc(2026, 10, 21, 2, 30), Utc(2026, 10, 21, 3, 30), policy));
    }

    [Fact]
    public void CustomGranularity_IsRespected()
    {
        var policy = CreatePolicy(options: new BookingPolicyOptions { SlotGranularity = TimeSpan.FromMinutes(30) });

        AssertValid(Validate(Oct21(10, 30), Oct21(11), policy));
        var result = Validate(Oct21(10, 15), Oct21(11), policy);
        AssertError(result, "startUtc");
        Assert.Contains(result.Errors["startUtc"], m => m.Contains("30 minut", StringComparison.Ordinal));
    }

    // ---------- argumenty ----------

    [Fact]
    public void NullRequest_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => CreatePolicy().Validate(null!));
    }

    [Fact]
    public void Constructor_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => new BookingPolicy(null!, new BookingPolicyOptions()));
        Assert.Throws<ArgumentNullException>(() => new BookingPolicy(TimeProvider.System, null!));
    }

    [Fact]
    public void Constructor_UnknownTimeZone_Throws()
    {
        Assert.Throws<TimeZoneNotFoundException>(() =>
            new BookingPolicy(TimeProvider.System, new BookingPolicyOptions { TimeZoneId = "Mars/Olympus_Mons" }));
    }

    // ---------- BookingValidationResult (kontrakt) ----------

    [Fact]
    public void ValidationResult_Failure_RequiresErrors()
    {
        Assert.Throws<ArgumentNullException>(() => BookingValidationResult.Failure(null!));
        Assert.Throws<ArgumentException>(() => BookingValidationResult.Failure(new Dictionary<string, string[]>()));
        var failure = BookingValidationResult.Failure(new Dictionary<string, string[]> { ["startUtc"] = ["x"] });
        Assert.False(failure.IsValid);
        Assert.True(BookingValidationResult.Success.IsValid);
    }
}
