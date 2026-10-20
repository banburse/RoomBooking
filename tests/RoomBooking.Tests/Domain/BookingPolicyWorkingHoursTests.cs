using RoomBooking.Domain;
using static RoomBooking.Tests.Domain.PolicyTestHelpers;

namespace RoomBooking.Tests.Domain;

/// <summary>Validate: godziny pracy 08:00-20:00 i jeden dzień w strefie biura (Europe/Warsaw), w tym DST.</summary>
public class BookingPolicyWorkingHoursTests
{
    [Fact]
    public void IanaTimeZoneId_IsResolvable()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw");
        Assert.Equal(Cest, zone.GetUtcOffset(Utc(2026, 7, 1, 12)));
        Assert.Equal(Cet, zone.GetUtcOffset(Utc(2026, 1, 1, 12)));
    }

    // ---------- godziny pracy ----------

    [Fact]
    public void OpeningAndClosing_AreInclusiveForStartAndEnd()
    {
        AssertValid(Validate(Oct21(8), Oct21(9)));
        AssertValid(Validate(Oct21(19), Oct21(20)));
        AssertValid(Validate(Oct21(12), Oct21(20))); // 8 h, koniec dokładnie 20:00
    }

    [Fact]
    public void BeforeOpening_0745_0815_IsRejected_OnStartUtc()
    {
        var result = Validate(Oct21(7, 45), Oct21(8, 15));
        AssertError(result, "startUtc");
        Assert.False(result.Errors.ContainsKey("endUtc"));
        Assert.Contains(result.Errors["startUtc"], m => m.Contains("08:00–20:00", StringComparison.Ordinal));
    }

    [Fact]
    public void AfterClosing_1945_2015_IsRejected_OnEndUtc()
    {
        var result = Validate(Oct21(19, 45), Oct21(20, 15));
        AssertError(result, "endUtc");
        Assert.False(result.Errors.ContainsKey("startUtc"));
    }

    [Fact]
    public void EntirelyAfterClosing_IsRejected()
    {
        var result = Validate(Oct21(20), Oct21(20, 30));
        AssertError(result, "startUtc");
        AssertError(result, "endUtc");
    }

    [Fact]
    public void EntirelyBeforeOpening_IsRejected()
    {
        var result = Validate(Oct21(6), Oct21(8));
        AssertError(result, "startUtc");
        AssertError(result, "endUtc");
    }

    [Fact]
    public void CustomWorkingHours_AreRespected()
    {
        var policy = CreatePolicy(options: new BookingPolicyOptions
        {
            OpeningTime = new TimeOnly(9, 0),
            ClosingTime = new TimeOnly(17, 0),
        });

        AssertValid(Validate(Oct21(9), Oct21(17), policy));
        AssertError(Validate(Oct21(8, 45), Oct21(10), policy), "startUtc");
        AssertError(Validate(Oct21(16), Oct21(17, 15), policy), "endUtc");
    }

    // ---------- jeden dzień w strefie biura ----------

    [Fact]
    public void CrossingMidnight_IsRejected_OnEndUtc()
    {
        var result = Validate(Oct21(19), Local(2026, 10, 22, 1, 0, Cest));
        AssertError(result, "endUtc");
        Assert.Contains(result.Errors["endUtc"], m => m.Contains("tego samego dnia", StringComparison.Ordinal));
    }

    [Fact]
    public void SameUtcDate_ButDifferentOfficeDate_IsRejected()
    {
        // Ten sam dzień w UTC, ale różne dni w strefie biura -> liczy się strefa biura.
        // Godziny pracy prawie całodobowe, żeby izolować regułę "jednego dnia".
        var policy = CreatePolicy(options: new BookingPolicyOptions
        {
            TimeZoneId = "America/New_York",
            OpeningTime = new TimeOnly(0, 0),
            ClosingTime = new TimeOnly(23, 45),
        });

        // 2026-10-21 03:00Z = 2026-10-20 23:00 EDT; 05:00Z = 2026-10-21 01:00 EDT
        var result = Validate(Utc(2026, 10, 21, 3), Utc(2026, 10, 21, 5), policy);
        AssertError(result, "endUtc");
        Assert.Contains(result.Errors["endUtc"], m => m.Contains("tego samego dnia", StringComparison.Ordinal));
    }

    // ---------- DST: ostatnia niedziela marca (2026-03-29) ----------

    [Fact]
    public void Dst_March_DayOfChange_OpeningIsLocal0800_Which_Is_0600Utc()
    {
        // Po przestawieniu zegara (02:00 -> 03:00) obowiązuje +02:00, więc 08:00 lokalnie = 06:00Z.
        AssertValid(Validate(Utc(2026, 3, 29, 6), Utc(2026, 3, 29, 7)));
    }

    [Fact]
    public void Dst_March_DayOfChange_0745_0815Local_IsRejected()
    {
        // 05:45Z-06:15Z = 07:45-08:15 lokalnie (CEST)
        AssertError(Validate(Utc(2026, 3, 29, 5, 45), Utc(2026, 3, 29, 6, 15)), "startUtc");
    }

    [Fact]
    public void Dst_March_DayOfChange_ClosingIsLocal2000_Which_Is_1800Utc()
    {
        AssertValid(Validate(Utc(2026, 3, 29, 12), Utc(2026, 3, 29, 18)));
        AssertError(Validate(Utc(2026, 3, 29, 17, 45), Utc(2026, 3, 29, 18, 15)), "endUtc");
    }

    [Fact]
    public void Dst_March_DayBeforeChange_UsesWinterOffset()
    {
        // 2026-03-28 jeszcze CET (+01:00): 06:00Z = 07:00 lokalnie -> odrzucone; 07:00Z = 08:00 -> OK.
        AssertError(Validate(Utc(2026, 3, 28, 6), Utc(2026, 3, 28, 7)), "startUtc");
        AssertValid(Validate(Utc(2026, 3, 28, 7), Utc(2026, 3, 28, 8)));
        AssertValid(Validate(Utc(2026, 3, 28, 18), Utc(2026, 3, 28, 19)));
    }

    [Fact]
    public void Dst_March_BookingsWithLocalOffsets()
    {
        AssertValid(Validate(Local(2026, 3, 29, 8, 0, Cest), Local(2026, 3, 29, 16, 0, Cest)));
        AssertValid(Validate(Local(2026, 3, 29, 12, 0, Cest), Local(2026, 3, 29, 20, 0, Cest)));
    }

    // ---------- DST: ostatnia niedziela października (2026-10-25) ----------

    [Fact]
    public void Dst_October_DayOfChange_OpeningIsLocal0800_Which_Is_0700Utc()
    {
        // Po cofnięciu zegara (03:00 -> 02:00) obowiązuje +01:00, więc 08:00 lokalnie = 07:00Z.
        AssertValid(Validate(Utc(2026, 10, 25, 7), Utc(2026, 10, 25, 8)));
        AssertError(Validate(Utc(2026, 10, 25, 6), Utc(2026, 10, 25, 7)), "startUtc");
    }

    [Fact]
    public void Dst_October_DayOfChange_0745_0815Local_IsRejected()
    {
        AssertError(Validate(Local(2026, 10, 25, 7, 45, Cet), Local(2026, 10, 25, 8, 15, Cet)), "startUtc");
    }

    [Fact]
    public void Dst_October_DayOfChange_ClosingIsLocal2000_Which_Is_1900Utc()
    {
        AssertValid(Validate(Utc(2026, 10, 25, 11), Utc(2026, 10, 25, 19)));
        AssertError(Validate(Local(2026, 10, 25, 19, 45, Cet), Local(2026, 10, 25, 20, 15, Cet)), "endUtc");
    }

    [Fact]
    public void Dst_October_DayBeforeChange_UsesSummerOffset()
    {
        // 2026-10-24 jeszcze CEST (+02:00): 06:00Z = 08:00 lokalnie -> OK; 18:15Z = 20:15 -> odrzucone.
        AssertValid(Validate(Utc(2026, 10, 24, 6), Utc(2026, 10, 24, 7)));
        AssertError(Validate(Utc(2026, 10, 24, 17), Utc(2026, 10, 24, 18, 15)), "endUtc");
    }
}
