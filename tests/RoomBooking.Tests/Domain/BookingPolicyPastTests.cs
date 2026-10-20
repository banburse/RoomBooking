using Microsoft.Extensions.Time.Testing;
using RoomBooking.Domain;
using static RoomBooking.Tests.Domain.PolicyTestHelpers;

namespace RoomBooking.Tests.Domain;

/// <summary>Validate: start &gt;= now, gdzie now pochodzi z TimeProvider.</summary>
public class BookingPolicyPastTests
{
    [Fact]
    public void StartInThePast_IsRejected_OnStartUtc()
    {
        var result = Validate(Oct21(10), Oct21(11), CreatePolicy(now: Oct21(10, 1)));
        AssertError(result, "startUtc");
        Assert.Contains("Nie można rezerwować w przeszłości.", result.Errors["startUtc"]);
    }

    [Fact]
    public void StartEqualToNow_IsValid()
    {
        AssertValid(Validate(Oct21(10), Oct21(11), CreatePolicy(now: Oct21(10))));
    }

    [Fact]
    public void InProgressSlot_IsRejected()
    {
        // trwająca już rezerwacja (start < now < end) też jest w przeszłości
        AssertError(Validate(Oct21(10), Oct21(12), CreatePolicy(now: Oct21(11))), "startUtc");
    }

    [Fact]
    public void PastCheck_FollowsTimeProvider()
    {
        var time = new FakeTimeProvider(Oct21(9));
        var policy = new BookingPolicy(time, new BookingPolicyOptions());

        AssertValid(policy.Validate(Request(Oct21(10), Oct21(11))));
        time.Advance(TimeSpan.FromHours(2));
        AssertError(policy.Validate(Request(Oct21(10), Oct21(11))), "startUtc");
    }

    [Fact]
    public void MultipleViolations_AreAllReported()
    {
        var result = Validate(Oct21(7, 10), Oct21(7, 20), CreatePolicy(now: Oct21(12)));

        Assert.True(result.Errors["startUtc"].Length >= 3); // przeszłość, wyrównanie, godziny
        Assert.True(result.Errors["endUtc"].Length >= 3);   // minimalny czas, wyrównanie, godziny
        Assert.Equal(["endUtc", "startUtc"], result.Errors.Keys.Order());
    }
}
