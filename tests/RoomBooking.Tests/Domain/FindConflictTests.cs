using Microsoft.Extensions.Time.Testing;
using RoomBooking.Domain;

namespace RoomBooking.Tests.Domain;

public class FindConflictTests
{
    private static readonly DateTimeOffset Day = new(2026, 10, 21, 0, 0, 0, TimeSpan.Zero);

    private readonly BookingPolicy _policy = new(new FakeTimeProvider(Day.AddDays(-1)), new BookingPolicyOptions());

    private static DateTimeOffset At(int hour, int minute = 0) => Day.AddHours(hour).AddMinutes(minute);

    private static BookingSlot Slot(int startHour, int endHour, bool cancelled = false, Guid? id = null) =>
        new(id ?? Guid.NewGuid(), At(startHour), At(endHour), cancelled);

    [Fact]
    public void NoExistingBookings_ReturnsNull()
    {
        Assert.Null(_policy.FindConflict(At(10), At(11), []));
    }

    [Fact]
    public void BackToBack_AfterExisting_IsNotAConflict()
    {
        // istniejąca 10-11, nowa 11-12
        Assert.Null(_policy.FindConflict(At(11), At(12), [Slot(10, 11)]));
    }

    [Fact]
    public void BackToBack_BeforeExisting_IsNotAConflict()
    {
        // istniejąca 11-12, nowa 10-11
        Assert.Null(_policy.FindConflict(At(10), At(11), [Slot(11, 12)]));
    }

    [Fact]
    public void BackToBack_BetweenTwoExisting_IsNotAConflict()
    {
        Assert.Null(_policy.FindConflict(At(10), At(11), [Slot(9, 10), Slot(11, 12)]));
    }

    [Fact]
    public void IdenticalInterval_IsAConflict()
    {
        var existing = Slot(10, 11);
        Assert.Equal(existing.Id, _policy.FindConflict(At(10), At(11), [existing]));
    }

    [Fact]
    public void NewContainedInExisting_IsAConflict()
    {
        var existing = Slot(9, 12);
        Assert.Equal(existing.Id, _policy.FindConflict(At(10), At(11), [existing]));
    }

    [Fact]
    public void NewContainsExisting_IsAConflict()
    {
        var existing = Slot(10, 11);
        Assert.Equal(existing.Id, _policy.FindConflict(At(9), At(12), [existing]));
    }

    [Fact]
    public void PartialOverlapFromLeft_IsAConflict()
    {
        // nowa 9:30-10:30 nachodzi na początek istniejącej 10-11
        var existing = Slot(10, 11);
        Assert.Equal(existing.Id, _policy.FindConflict(At(9, 30), At(10, 30), [existing]));
    }

    [Fact]
    public void PartialOverlapFromRight_IsAConflict()
    {
        // nowa 10:30-11:30 nachodzi na koniec istniejącej 10-11
        var existing = Slot(10, 11);
        Assert.Equal(existing.Id, _policy.FindConflict(At(10, 30), At(11, 30), [existing]));
    }

    [Fact]
    public void OverlapByOneTick_IsAConflict()
    {
        var existing = Slot(10, 11);
        Assert.Equal(existing.Id, _policy.FindConflict(At(11).AddTicks(-1), At(12), [existing]));
    }

    [Fact]
    public void CancelledBooking_DoesNotBlock()
    {
        Assert.Null(_policy.FindConflict(At(10), At(11), [Slot(10, 11, cancelled: true)]));
    }

    [Fact]
    public void CancelledBookingIsSkipped_ActiveOneIsReturned()
    {
        var cancelled = Slot(9, 11, cancelled: true);
        var active = Slot(10, 12);
        Assert.Equal(active.Id, _policy.FindConflict(At(10), At(11), [cancelled, active]));
    }

    [Fact]
    public void MultipleConflicts_ReturnsEarliestByStart_RegardlessOfInputOrder()
    {
        var late = Slot(14, 15);
        var early = Slot(9, 11);
        var middle = Slot(12, 13);
        var unrelated = Slot(8, 9);

        Assert.Equal(early.Id, _policy.FindConflict(At(10), At(16), [late, middle, unrelated, early]));
    }

    [Fact]
    public void ComparesInstants_NotLocalOffsets()
    {
        // Istniejąca zapisana z offsetem +02:00 (10:00+02 == 08:00Z), nowa w UTC 08:30Z-09:30Z.
        var existing = new BookingSlot(Guid.NewGuid(),
            new DateTimeOffset(2026, 10, 21, 10, 0, 0, TimeSpan.FromHours(2)),
            new DateTimeOffset(2026, 10, 21, 11, 0, 0, TimeSpan.FromHours(2)));

        Assert.Equal(existing.Id, _policy.FindConflict(At(8, 30), At(9, 30), [existing]));
        Assert.Null(_policy.FindConflict(At(9), At(10), [existing]));
    }

    [Fact]
    public void NullExisting_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => _policy.FindConflict(At(10), At(11), null!));
    }
}
