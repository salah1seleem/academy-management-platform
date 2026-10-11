using Academy.Infrastructure.Facilities;
namespace Academy.UnitTests;
public sealed class BookingCapacityTests
{
    private static BookingCapacity.Slot Slot(int start, int end, int seats, bool exclusive = false) => new(new(start, 0), new(end, 0), seats, exclusive);
    [Fact] public void Adjacent_reservations_do_not_overlap() => Assert.True(BookingCapacity.Fits(8, false, Slot(10, 11, 8, true), [Slot(9, 10, 8, true)]));
    [Fact] public void Shared_capacity_counts_concurrent_seats_not_whole_day_totals() => Assert.True(BookingCapacity.Fits(8, true, Slot(9, 12, 2), [Slot(9, 10, 6), Slot(10, 11, 6), Slot(11, 12, 6)]));
    [Fact] public void Peak_overlap_cannot_overfill() => Assert.False(BookingCapacity.Fits(8, true, Slot(9, 12, 2), [Slot(9, 11, 4), Slot(10, 12, 4)]));
    [Theory] [InlineData(true, false)] [InlineData(false, true)]
    public void Either_exclusive_booking_blocks_overlap(bool first, bool second) => Assert.False(BookingCapacity.Fits(8, true, Slot(10, 11, 1, first), [Slot(9, 12, 1, second)]));
    [Fact] public void Shared_flag_is_explicit_not_implied_by_free_seats() => Assert.False(BookingCapacity.Fits(100, false, Slot(10, 11, 1), [Slot(9, 12, 1)]));
    [Theory] [InlineData(0)] [InlineData(9)] public void Invalid_seat_counts_cannot_fit(int seats) => Assert.False(BookingCapacity.Fits(8, true, Slot(10, 11, seats), []));
}
