using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BookMyFun.Models.Configuration;
using BookMyFun.Models.Entities;
using BookMyFun.Repositories;
using BookMyFun.Services;
using Microsoft.Extensions.Options;
using Xunit;

namespace BookMyFun.Tests
{
    public class BookingTimingAndOverlapTests
    {
        private class InMemoryBookingRepo : IBookingRepository
        {
            public List<Booking> Bookings { get; } = new();

            public Task<List<Booking>> GetBookingsByPropertyIdAsync(int propertyId)
                => Task.FromResult(Bookings.Where(b => b.PropertyID == propertyId).ToList());

            public Task<List<Booking>> GetAllBookingsAsync() => Task.FromResult(Bookings.ToList());
            public Task<Booking?> GetByIdAsync(int bookingID) => Task.FromResult<Booking?>(null);
            public Task<Booking?> GetBookingByIdAsync(int bookingId) => Task.FromResult<Booking?>(null);
            public Task<Booking?> GetByIdempotencyKeyAsync(string idempotencyKey) => Task.FromResult<Booking?>(null);
            public Task<List<Booking>> GetByClientIdAsync(int clientId) => Task.FromResult(new List<Booking>());
            public Task<List<Booking>> GetByOwnerIdAsync(int ownerId) => Task.FromResult(new List<Booking>());
            public Task<int> CreateBookingAsync(Booking booking) => Task.FromResult(1);
            public Task<bool> UpdateBookingStatusAsync(int bookingId, string status) => Task.FromResult(true);
            public Task<bool> CancelBookingAsync(int bookingId) => Task.FromResult(true);
            public Task<bool> UpdateSecurityDepositStatusAsync(int bookingId, string status) => Task.FromResult(true);
            public Task<int> RecordPaymentAsync(Payment payment) => Task.FromResult(1);
            public Task<List<Payment>> GetPaymentsByBookingIdAsync(int bookingId) => Task.FromResult(new List<Payment>());
            public Task<List<CustomerReview>> GetReviewsByPropertyIdAsync(int propertyId) => Task.FromResult(new List<CustomerReview>());
            public Task<int> AddReviewAsync(CustomerReview review) => Task.FromResult(1);
            public Task<bool> UpdateBookingEventCommissionAsync(int bookingId, decimal newEventCommissionRate) => Task.FromResult(true);

            public Task<bool> IsSlotAvailableAsync(int propertyId, DateTime checkInDate, DateTime checkOutDate, string bookingType, string? slotType)
                => IsSlotAvailableAsync(propertyId, checkInDate, checkOutDate, bookingType, slotType, null, null, null);

            public Task<bool> IsSlotAvailableAsync(int propertyId, DateTime checkInDate, DateTime checkOutDate, string bookingType, string? slotType, int? excludeBookingId, TimeSpan? hourlyCheckInTime, int? hourlyDuration)
            {
                var timing = new BookingTimingOptions();
                if (string.Equals(bookingType, "Hourly", StringComparison.OrdinalIgnoreCase) &&
                    !timing.IsHourlyStartAllowed(hourlyCheckInTime ?? timing.CheckInTimeSpan))
                {
                    return Task.FromResult(false);
                }

                var (reqStart, reqEnd) = timing.GetBookingWindow(checkInDate, checkOutDate, bookingType, slotType, hourlyCheckInTime, hourlyDuration);
                foreach (var b in Bookings.Where(x => x.PropertyID == propertyId && x.Status == "Confirmed"))
                {
                    var (exStart, exEnd) = timing.GetBookingWindow(b.CheckInDate, b.CheckOutDate, b.BookingType ?? "FullDay", b.SlotType, b.SlotStartTime);
                    if (BookingTimingOptions.Overlaps(reqStart, reqEnd, exStart, exEnd))
                        return Task.FromResult(false);
                }
                return Task.FromResult(true);
            }
        }

        private static InMemoryBookingRepo CreateRepoWithNov26Checkout()
        {
            var repo = new InMemoryBookingRepo();
            repo.Bookings.Add(new Booking
            {
                BookingID = 1,
                PropertyID = 10,
                CheckInDate = new DateTime(2026, 11, 24),
                CheckOutDate = new DateTime(2026, 11, 26), // Checks out 26/11 at 10:00
                BookingType = "FullDay",
                Status = "Confirmed"
            });
            return repo;
        }

        [Fact]
        public async Task FullDayCheckIn_OnCheckoutDate_26Nov_1100_IsAllowed()
        {
            var repo = CreateRepoWithNov26Checkout();
            var svc = new AvailabilitySuggestionService(repo, Options.Create(new BookingTimingOptions()));

            var result = await svc.GetNearestSuggestionsAsync(
                10, new DateTime(2026, 11, 26), new DateTime(2026, 11, 28), "FullDay", null, new DateTime(2026, 11, 20));

            Assert.True(result.IsRequestedAvailable);
        }

        [Fact]
        public async Task HalfDayAfternoon_OnCheckoutDate_26Nov_IsAllowed()
        {
            var repo = CreateRepoWithNov26Checkout();
            var svc = new AvailabilitySuggestionService(repo, Options.Create(new BookingTimingOptions()));

            var result = await svc.GetNearestSuggestionsAsync(
                10, new DateTime(2026, 11, 26), new DateTime(2026, 11, 26), "HalfDay", "AfternoonSlot", new DateTime(2026, 11, 20));

            Assert.True(result.IsRequestedAvailable);
        }

        [Fact]
        public async Task HalfDayMorning_OnCheckoutDate_26Nov_IsBlocked()
        {
            var repo = CreateRepoWithNov26Checkout();
            var svc = new AvailabilitySuggestionService(repo, Options.Create(new BookingTimingOptions()));

            // MorningSlot starts at 08:00, before 10:00 checkout -> must be blocked
            var result = await svc.GetNearestSuggestionsAsync(
                10, new DateTime(2026, 11, 26), new DateTime(2026, 11, 26), "HalfDay", "MorningSlot", new DateTime(2026, 11, 20));

            Assert.False(result.IsRequestedAvailable);
        }

        [Fact]
        public void HourlyStart_Before1100_At1030_IsRejected()
        {
            var timing = new BookingTimingOptions();
            Assert.False(timing.IsHourlyStartAllowed(new TimeSpan(10, 30, 0)));
        }

        [Fact]
        public void HourlyStart_At1100_IsAllowed()
        {
            var timing = new BookingTimingOptions();
            Assert.True(timing.IsHourlyStartAllowed(new TimeSpan(11, 0, 0)));
        }

        [Fact]
        public async Task Calendar_DoesNotBlockCheckoutDate_26Nov_ForNewFullDayStay()
        {
            var repo = CreateRepoWithNov26Checkout();
            var cal = new CalendarRangeService(repo);

            var blocked = await cal.GetBlockedDatesForPropertyAsync(10);
            Assert.Contains("2026-11-24", blocked);
            Assert.Contains("2026-11-25", blocked);
            Assert.DoesNotContain("2026-11-26", blocked);

            var val = cal.ValidateAndCalculate(
                new DateTime(2026, 11, 26), new DateTime(2026, 11, 28), 1, 30, 5000m, "FullDay", blockedDatesIso: blocked);
            Assert.True(val.IsValid);
        }
    }
}
