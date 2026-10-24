using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BookMyFun.Controllers;
using BookMyFun.Models.Entities;
using BookMyFun.Repositories;
using BookMyFun.Services;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace BookMyFun.Tests
{
    public class CalendarRangePickerTests
    {
        private class MockBookingRepo : IBookingRepository
        {
            public List<Booking> Bookings { get; } = new();

            public Task<List<Booking>> GetBookingsByPropertyIdAsync(int propertyId)
            {
                return Task.FromResult(Bookings.Where(b => b.PropertyID == propertyId).ToList());
            }

            public Task<List<Booking>> GetAllBookingsAsync() => Task.FromResult(Bookings.ToList());

            public Task<bool> IsSlotAvailableAsync(int propertyId, DateTime checkInDate, DateTime checkOutDate, string bookingType, string? slotType)
            {
                return Task.FromResult(true);
            }

            public Task<Booking?> GetByIdAsync(int bookingID) => Task.FromResult(Bookings.FirstOrDefault(b => b.BookingID == bookingID));
            public Task<Booking?> GetBookingByIdAsync(int bookingId) => GetByIdAsync(bookingId);
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
        }

        private class MockPropertyRepo : IPropertyRepository
        {
            public Dictionary<int, Property> Properties { get; } = new();

            public Task<Property?> GetByIdAsync(int propertyId)
            {
                Properties.TryGetValue(propertyId, out var p);
                return Task.FromResult(p);
            }

            public Task<List<Property>> GetAllPropertiesAsync(bool onlyApproved = false) => Task.FromResult(Properties.Values.ToList());
            public Task<List<Property>> GetPropertiesByOwnerIdAsync(int ownerId) => Task.FromResult(new List<Property>());
            public Task<List<Property>> SearchPropertiesAsync(string? location, int? propertyTypeId, int? occasionId, decimal? minPrice, decimal? maxPrice, int? guests) => Task.FromResult(new List<Property>());
            public Task<int> CreatePropertyAsync(Property property) => Task.FromResult(1);
            public Task<bool> UpdatePropertyAsync(Property property) => Task.FromResult(true);
            public Task<bool> DeletePropertyAsync(int propertyId) => Task.FromResult(true);
            public Task<bool> PermanentlyDeletePropertyAsync(int propertyId) => Task.FromResult(true);
            public Task<bool> DeactivatePropertyAsync(int propertyId, bool deactivate = true) => Task.FromResult(true);
            public Task<bool> ApprovePropertyAsync(int propertyId, bool isApproved, string? targetStatus = null) => Task.FromResult(true);
            public Task<List<PropertyType>> GetPropertyTypesAsync() => Task.FromResult(new List<PropertyType>());
            public Task<List<Occasion>> GetOccasionsAsync() => Task.FromResult(new List<Occasion>());
            public Task<List<AmenityMaster>> GetAmenitiesMasterAsync() => Task.FromResult(new List<AmenityMaster>());
            public Task<bool> UpdatePropertyMapUrlAsync(int propertyId, int ownerId, string? mapUrl) => Task.FromResult(true);
            public Task<int> GetOrCreatePropertyTypeByNameAsync(string name) => Task.FromResult(1);
            public Task<bool> UpdatePropertyCategoryAsync(int propertyId, int ownerId, int propertyTypeId) => Task.FromResult(true);
            public Task<PropertyEventCommission?> GetEventCommissionByPropertyIdAsync(int propertyId) => Task.FromResult<PropertyEventCommission?>(null);
            public Task<bool> SetPropertyEventCommissionAsync(int propertyId, decimal? commissionRate) => Task.FromResult(true);
            public Task<List<PropertyEventCommission>> GetAllPropertyEventCommissionsAsync() => Task.FromResult(new List<PropertyEventCommission>());
            public Task<bool> ResetPropertyEventCommissionAsync(int propertyId) => Task.FromResult(true);
            public Task<bool> SetPropertyOccasionsAsync(int propertyId, List<int> occasionIds) => Task.FromResult(true);
            public Task<List<int>> GetPropertyOccasionIdsAsync(int propertyId) => Task.FromResult(new List<int>());
            public Task<int> AddPropertyImageAsync(int propertyId, string imageUrl, bool isPrimary) => Task.FromResult(1);
            public Task<List<PropertyImage>> GetPropertyImagesAsync(int propertyId) => Task.FromResult(new List<PropertyImage>());
            public Task<int> AddPropertyMediaAsync(PropertyMedia media) => Task.FromResult(1);
            public Task<List<PropertyMedia>> GetPropertyMediaAsync(int propertyId) => Task.FromResult(new List<PropertyMedia>());
            public Task<bool> DeletePropertyMediaAsync(int mediaId) => Task.FromResult(true);
            public Task<List<AmenityMaster>> GetAllAmenitiesAsync() => Task.FromResult(new List<AmenityMaster>());
            public Task<List<int>> GetPropertyAmenityIdsAsync(int propertyId) => Task.FromResult(new List<int>());
            public Task<List<AmenityMaster>> GetPropertyAmenitiesAsync(int propertyId) => Task.FromResult(new List<AmenityMaster>());
            public Task<bool> SetPropertyAmenitiesAsync(int propertyId, List<int> amenityIds) => Task.FromResult(true);
            public Task<int> GetOrCreateAmenityByNameAsync(string name) => Task.FromResult(1);
            public Task<int> GetOrCreateOccasionByNameAsync(string name) => Task.FromResult(1);
            public Task<List<PropertyEventCommission>> GetPropertyEventCommissionsAsync(int propertyId) => Task.FromResult(new List<PropertyEventCommission>());
            public Task<decimal?> GetPropertyEventCommissionRateAsync(int propertyId, int packageId) => Task.FromResult<decimal?>(null);
            public Task<bool> SetPropertyEventCommissionsAsync(int propertyId, List<(int packageId, decimal commissionRate)> commissions) => Task.FromResult(true);
        }

        [Fact]
        public void RangeSelection_ComputesCorrectNights()
        {
            var service = new CalendarRangeService(new MockBookingRepo());

            // 1. 3 nights (Nov 10 to Nov 13)
            var res1 = service.ValidateAndCalculate(
                new DateTime(2026, 11, 10), new DateTime(2026, 11, 13), 1, 30, 5000);
            Assert.True(res1.IsValid);
            Assert.Equal(3, res1.Nights);

            // 2. 1 night (Nov 10 to Nov 11)
            var res2 = service.ValidateAndCalculate(
                new DateTime(2026, 11, 10), new DateTime(2026, 11, 11), 1, 30, 5000);
            Assert.True(res2.IsValid);
            Assert.Equal(1, res2.Nights);

            // 3. 7 nights (Nov 10 to Nov 17)
            var res3 = service.ValidateAndCalculate(
                new DateTime(2026, 11, 10), new DateTime(2026, 11, 17), 1, 30, 5000);
            Assert.True(res3.IsValid);
            Assert.Equal(7, res3.Nights);
        }

        [Fact]
        public void MinimumStay_Enforced()
        {
            var service = new CalendarRangeService(new MockBookingRepo());

            int minStay = 3;
            int maxStay = 14;

            // Attempt to book 2 nights (below min stay of 3)
            var resFail = service.ValidateAndCalculate(
                new DateTime(2026, 11, 10), new DateTime(2026, 11, 12), minStay, maxStay, 5000);

            Assert.False(resFail.IsValid);
            Assert.Equal(2, resFail.Nights);
            Assert.Contains("Minimum stay for this property is 3 nights.", resFail.ErrorMessage);

            // Book exact min stay (3 nights)
            var resSuccess = service.ValidateAndCalculate(
                new DateTime(2026, 11, 10), new DateTime(2026, 11, 13), minStay, maxStay, 5000);

            Assert.True(resSuccess.IsValid);
            Assert.Equal(3, resSuccess.Nights);
            Assert.Null(resSuccess.ErrorMessage);
        }

        [Fact]
        public void MaximumStay_Enforced()
        {
            var service = new CalendarRangeService(new MockBookingRepo());

            int minStay = 1;
            int maxStay = 5;

            // Attempt to book 6 nights (above max stay of 5)
            var resFail = service.ValidateAndCalculate(
                new DateTime(2026, 11, 10), new DateTime(2026, 11, 16), minStay, maxStay, 5000);

            Assert.False(resFail.IsValid);
            Assert.Equal(6, resFail.Nights);
            Assert.Contains("Maximum stay for this property is 5 nights.", resFail.ErrorMessage);

            // Book exact max stay (5 nights)
            var resSuccess = service.ValidateAndCalculate(
                new DateTime(2026, 11, 10), new DateTime(2026, 11, 15), minStay, maxStay, 5000);

            Assert.True(resSuccess.IsValid);
            Assert.Equal(5, resSuccess.Nights);
            Assert.Null(resSuccess.ErrorMessage);
        }

        [Fact]
        public void BlockedDates_CannotBeIncludedInRange()
        {
            var service = new CalendarRangeService(new MockBookingRepo());

            var blockedDates = new List<string> { "2026-11-12" };

            // Range spanning Nov 10 to Nov 14 (contains Nov 12)
            var resFail = service.ValidateAndCalculate(
                new DateTime(2026, 11, 10), new DateTime(2026, 11, 14), 1, 30, 5000,
                blockedDatesIso: blockedDates);

            Assert.False(resFail.IsValid);
            Assert.Contains("Selected range includes an unavailable date", resFail.ErrorMessage);

            // Range starting after the blocked date (Nov 13 to Nov 15)
            var resSuccess = service.ValidateAndCalculate(
                new DateTime(2026, 11, 13), new DateTime(2026, 11, 15), 1, 30, 5000,
                blockedDatesIso: blockedDates);

            Assert.True(resSuccess.IsValid);
            Assert.Null(resSuccess.ErrorMessage);
        }

        [Fact]
        public void Price_UpdatesLiveWithDurationAndAddons()
        {
            var service = new CalendarRangeService(new MockBookingRepo());
            decimal baseRate = 4000m;

            // 1. FullDay 3 nights: 4000 * 3 = 12000
            var res1 = service.ValidateAndCalculate(
                new DateTime(2026, 11, 10), new DateTime(2026, 11, 13), 1, 30, baseRate, bookingType: "FullDay");
            Assert.Equal(12000m, res1.CalculatedPrice);

            // 2. FullDay 3 nights with 2500 Event Decor package: 12000 + 2500 = 14500
            var res2 = service.ValidateAndCalculate(
                new DateTime(2026, 11, 10), new DateTime(2026, 11, 13), 1, 30, baseRate,
                bookingType: "FullDay", eventPackagePrice: 2500m);
            Assert.Equal(14500m, res2.CalculatedPrice);

            // 3. HalfDay: Math.Round(4000 * 0.55) = 2200
            var res3 = service.ValidateAndCalculate(
                new DateTime(2026, 11, 10), new DateTime(2026, 11, 10), 1, 30, baseRate, bookingType: "HalfDay");
            Assert.Equal(2200m, res3.CalculatedPrice);

            // 4. Hourly 6 hours: Math.Round((4000 / 10) * 6) = 2400
            var res4 = service.ValidateAndCalculate(
                new DateTime(2026, 11, 10), new DateTime(2026, 11, 10), 1, 30, baseRate,
                bookingType: "Hourly", hourlyDuration: 6);
            Assert.Equal(2400m, res4.CalculatedPrice);
        }

        [Fact]
        public async Task GetBlockedDates_ExpandsDates_AndIgnoresCancelledBookings()
        {
            var repo = new MockBookingRepo();
            int propId = 1;

            // Confirmed booking: Nov 10 to Nov 12
            repo.Bookings.Add(new Booking
            {
                BookingID = 1,
                PropertyID = propId,
                CheckInDate = new DateTime(2026, 11, 10),
                CheckOutDate = new DateTime(2026, 11, 12),
                Status = "Confirmed"
            });

            // Cancelled booking: Nov 20 to Nov 22 (should be ignored)
            repo.Bookings.Add(new Booking
            {
                BookingID = 2,
                PropertyID = propId,
                CheckInDate = new DateTime(2026, 11, 20),
                CheckOutDate = new DateTime(2026, 11, 22),
                Status = "Cancelled"
            });

            var service = new CalendarRangeService(repo);
            var blocked = await service.GetBlockedDatesForPropertyAsync(propId);

            Assert.Contains("2026-11-10", blocked);
            Assert.Contains("2026-11-11", blocked);
            Assert.Contains("2026-11-12", blocked);

            // Cancelled booking dates must not be blocked
            Assert.DoesNotContain("2026-11-20", blocked);
            Assert.DoesNotContain("2026-11-21", blocked);
            Assert.DoesNotContain("2026-11-22", blocked);
        }

        [Fact]
        public async Task Endpoint_GetBookedDates_ValidatesInputAndReturnsList()
        {
            var propRepo = new MockPropertyRepo();
            var bookingRepo = new MockBookingRepo();
            var calService = new CalendarRangeService(bookingRepo);

            bookingRepo.Bookings.Add(new Booking
            {
                BookingID = 1,
                PropertyID = 5,
                CheckInDate = new DateTime(2026, 12, 1),
                CheckOutDate = new DateTime(2026, 12, 2),
                Status = "Confirmed"
            });

            var controller = new PropertiesController(propRepo, bookingRepo, null, calService);

            // 1. Invalid ID <= 0 returns BadRequest
            var resBad = await controller.GetBookedDates(0);
            Assert.IsType<BadRequestObjectResult>(resBad);

            // 2. Valid ID returns 200 OK with list of blocked dates
            var resOk = await controller.GetBookedDates(5);
            var ok = Assert.IsType<OkObjectResult>(resOk);
            var dates = Assert.IsAssignableFrom<List<string>>(ok.Value);
            Assert.Contains("2026-12-01", dates);
            Assert.Contains("2026-12-02", dates);
        }
    }
}
