using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using BookMyFun.Controllers;
using BookMyFun.Models.Entities;
using BookMyFun.Repositories;
using BookMyFun.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace BookMyFun.Tests
{
    public class AvailabilitySuggestionTests
    {
        private class TestBookingRepository : IBookingRepository
        {
            public List<Booking> Bookings { get; } = new();

            public Task<List<Booking>> GetBookingsByPropertyIdAsync(int propertyId)
            {
                return Task.FromResult(Bookings.Where(b => b.PropertyID == propertyId).ToList());
            }

            public Task<List<Booking>> GetAllBookingsAsync()
            {
                return Task.FromResult(Bookings.ToList());
            }

            public Task<bool> IsSlotAvailableAsync(int propertyId, DateTime checkInDate, DateTime checkOutDate, string bookingType, string? slotType)
            {
                bool hasConflict = Bookings.Any(b =>
                    b.PropertyID == propertyId &&
                    !string.Equals(b.Status, "Cancelled", StringComparison.OrdinalIgnoreCase) &&
                    (string.Equals(b.Status, "Confirmed", StringComparison.OrdinalIgnoreCase) ||
                     (string.Equals(b.Status, "Pending", StringComparison.OrdinalIgnoreCase) && (b.CreatedAt == default || b.CreatedAt >= DateTime.UtcNow.AddMinutes(-15)))) &&
                    (b.CheckInDate.Date <= checkOutDate.Date && b.CheckOutDate.Date >= checkInDate.Date));

                return Task.FromResult(!hasConflict);
            }

            public Task<Booking?> GetByIdAsync(int bookingID) => Task.FromResult(Bookings.FirstOrDefault(b => b.BookingID == bookingID));
            public Task<Booking?> GetBookingByIdAsync(int bookingId) => GetByIdAsync(bookingId);
            public Task<Booking?> GetByIdempotencyKeyAsync(string idempotencyKey) => Task.FromResult<Booking?>(null);
            public Task<List<Booking>> GetByClientIdAsync(int clientId) => Task.FromResult(Bookings.Where(b => b.ClientID == clientId).ToList());
            public Task<List<Booking>> GetByOwnerIdAsync(int ownerId) => Task.FromResult(new List<Booking>());
            public Task<int> CreateBookingAsync(Booking booking)
            {
                booking.BookingID = Bookings.Count + 1;
                Bookings.Add(booking);
                return Task.FromResult(booking.BookingID);
            }
            public Task<bool> UpdateBookingStatusAsync(int bookingId, string status) => Task.FromResult(true);
            public Task<bool> CancelBookingAsync(int bookingId) => Task.FromResult(true);
            public Task<bool> UpdateSecurityDepositStatusAsync(int bookingId, string status) => Task.FromResult(true);
            public Task<int> RecordPaymentAsync(Payment payment) => Task.FromResult(1);
            public Task<List<Payment>> GetPaymentsByBookingIdAsync(int bookingId) => Task.FromResult(new List<Payment>());
            public Task<List<CustomerReview>> GetReviewsByPropertyIdAsync(int propertyId) => Task.FromResult(new List<CustomerReview>());
            public Task<int> AddReviewAsync(CustomerReview review) => Task.FromResult(1);
            public Task<bool> UpdateBookingEventCommissionAsync(int bookingId, decimal newEventCommissionRate) => Task.FromResult(true);
        }

        private class TestPropertyRepository : IPropertyRepository
        {
            public readonly Dictionary<int, Property> Properties = new();

            public Task<Property?> GetByIdAsync(int propertyId)
            {
                Properties.TryGetValue(propertyId, out var p);
                return Task.FromResult(p);
            }

            public Task<List<Property>> GetAllPropertiesAsync(bool onlyApproved = false) => Task.FromResult(Properties.Values.ToList());
            public Task<List<Property>> GetPropertiesByOwnerIdAsync(int ownerId) => Task.FromResult(Properties.Values.Where(p => p.OwnerID == ownerId).ToList());
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
        public async Task Suggestion_FindsNearestFreeWindow_ForwardAndBackward()
        {
            var repo = new TestBookingRepository();
            var service = new AvailabilitySuggestionService(repo);

            int propId = 1;
            var requestedIn = new DateTime(2026, 11, 10);
            var requestedOut = new DateTime(2026, 11, 12); // 2 nights
            var baseToday = new DateTime(2026, 11, 1);

            // Block requested dates (Nov 10 - Nov 12) with a confirmed booking
            repo.Bookings.Add(new Booking
            {
                BookingID = 1,
                PropertyID = propId,
                CheckInDate = requestedIn,
                CheckOutDate = requestedOut,
                BookingType = "FullDay",
                Status = "Confirmed"
            });

            // Also block Nov 9
            repo.Bookings.Add(new Booking
            {
                BookingID = 2,
                PropertyID = propId,
                CheckInDate = new DateTime(2026, 11, 9),
                CheckOutDate = new DateTime(2026, 11, 10),
                BookingType = "FullDay",
                Status = "Confirmed"
            });

            var result = await service.GetNearestSuggestionsAsync(
                propId, requestedIn, requestedOut, "FullDay", null, baseToday);

            Assert.False(result.IsRequestedAvailable);
            Assert.NotEmpty(result.Suggestions);
            Assert.True(result.Suggestions.Count <= 3);

            // Backward and forward should both be evaluated
            // Nov 10 + 1 day = Nov 11 (still blocked by Nov 10-12 booking)
            // Nov 10 + 2 days = Nov 12 to Nov 14 (free!) -> distance 2
            // Nov 10 - 3 days = Nov 7 to Nov 9 (free!) -> distance 3
            // Nov 10 + 3 days = Nov 13 to Nov 15 (free!) -> distance 3

            var top = result.Suggestions.First();
            Assert.Equal(new DateTime(2026, 11, 13), top.CheckInDate);
            Assert.Equal(new DateTime(2026, 11, 15), top.CheckOutDate);
            Assert.Equal(3, top.DistanceDays);
            Assert.Equal(2, result.Nights);
            Assert.Contains("Nearest available dates:", result.Message);
        }

        [Fact]
        public async Task Suggestion_Respects30DayLimit_AndReturnsEmptyResultWhenBlocked()
        {
            var repo = new TestBookingRepository();
            var service = new AvailabilitySuggestionService(repo);

            int propId = 1;
            var requestedIn = new DateTime(2026, 11, 15);
            var requestedOut = new DateTime(2026, 11, 17); // 2 nights
            var baseToday = new DateTime(2026, 11, 1);

            // Block all dates within 30 days before and after (from Oct 15 to Dec 20)
            repo.Bookings.Add(new Booking
            {
                BookingID = 1,
                PropertyID = propId,
                CheckInDate = new DateTime(2026, 10, 15),
                CheckOutDate = new DateTime(2026, 12, 20),
                BookingType = "FullDay",
                Status = "Confirmed"
            });

            var result = await service.GetNearestSuggestionsAsync(
                propId, requestedIn, requestedOut, "FullDay", null, baseToday);

            Assert.False(result.IsRequestedAvailable);
            Assert.Empty(result.Suggestions);
            Assert.Equal("No nearby dates available. Try a different range.", result.Message);
        }

        [Fact]
        public async Task Suggestion_ExcludesConflictingPendingAndConfirmedBookings()
        {
            var repo = new TestBookingRepository();
            var service = new AvailabilitySuggestionService(repo);

            int propId = 1;
            var requestedIn = new DateTime(2026, 11, 10);
            var requestedOut = new DateTime(2026, 11, 12);
            var baseToday = new DateTime(2026, 11, 1);

            // Pending booking created 2 minutes ago (active conflict)
            repo.Bookings.Add(new Booking
            {
                BookingID = 1,
                PropertyID = propId,
                CheckInDate = requestedIn,
                CheckOutDate = requestedOut,
                BookingType = "FullDay",
                Status = "Pending",
                CreatedAt = DateTime.UtcNow.AddMinutes(-2)
            });

            // Confirmed booking on Nov 12 - 14
            repo.Bookings.Add(new Booking
            {
                BookingID = 2,
                PropertyID = propId,
                CheckInDate = new DateTime(2026, 11, 12),
                CheckOutDate = new DateTime(2026, 11, 14),
                BookingType = "FullDay",
                Status = "Confirmed"
            });

            var result = await service.GetNearestSuggestionsAsync(
                propId, requestedIn, requestedOut, "FullDay", null, baseToday);

            Assert.False(result.IsRequestedAvailable);

            // Neither the pending window (Nov 10-12) nor the confirmed window (Nov 12-14) can be suggested
            foreach (var s in result.Suggestions)
            {
                Assert.False(s.CheckInDate <= new DateTime(2026, 11, 12) && s.CheckOutDate >= new DateTime(2026, 11, 10));
                Assert.False(s.CheckInDate <= new DateTime(2026, 11, 14) && s.CheckOutDate >= new DateTime(2026, 11, 12));
            }
        }

        [Fact]
        public async Task Suggestion_IgnoresCancelledBookings()
        {
            var repo = new TestBookingRepository();
            var service = new AvailabilitySuggestionService(repo);

            int propId = 1;
            var requestedIn = new DateTime(2026, 11, 10);
            var requestedOut = new DateTime(2026, 11, 12);
            var baseToday = new DateTime(2026, 11, 1);

            // Cancelled booking on requested dates
            repo.Bookings.Add(new Booking
            {
                BookingID = 1,
                PropertyID = propId,
                CheckInDate = requestedIn,
                CheckOutDate = requestedOut,
                BookingType = "FullDay",
                Status = "Cancelled"
            });

            var result = await service.GetNearestSuggestionsAsync(
                propId, requestedIn, requestedOut, "FullDay", null, baseToday);

            Assert.True(result.IsRequestedAvailable);
            Assert.Equal("Requested dates are available.", result.Message);
        }

        [Fact]
        public async Task Suggestion_HandlesMonthBoundariesAndLeapYears()
        {
            var repo = new TestBookingRepository();
            var service = new AvailabilitySuggestionService(repo);

            int propId = 1;
            // 2028 is a leap year (Feb 29 exists)
            var leapIn = new DateTime(2028, 2, 28);
            var leapOut = new DateTime(2028, 3, 2); // 3 nights: Feb 28, 29, Mar 1 -> CheckOut Mar 2
            var baseToday = new DateTime(2028, 2, 1);

            // Block requested leap year window
            repo.Bookings.Add(new Booking
            {
                BookingID = 1,
                PropertyID = propId,
                CheckInDate = leapIn,
                CheckOutDate = leapOut,
                BookingType = "FullDay",
                Status = "Confirmed"
            });

            var result = await service.GetNearestSuggestionsAsync(
                propId, leapIn, leapOut, "FullDay", null, baseToday);

            Assert.False(result.IsRequestedAvailable);
            Assert.NotEmpty(result.Suggestions);
            Assert.Equal(3, result.Nights);

            // Closest backward: Feb 27 to Mar 1 (distance 1 day backward) is still blocked by Feb 28-Mar 2
            // Feb 25 to Feb 28 (distance 3 days backward): check-in Feb 25, out Feb 28
            // Closest forward: Feb 28 + 3 days = Mar 2 to Mar 5 (distance 3 days forward)
            var top = result.Suggestions.First();
            Assert.Equal(4, top.DistanceDays);
            Assert.True(top.CheckInDate == new DateTime(2028, 2, 24) || top.CheckInDate == new DateTime(2028, 3, 3));
        }

        [Fact]
        public async Task Endpoint_InputValidation_ReturnsExpectedStatus()
        {
            var propRepo = new TestPropertyRepository();
            var bookingRepo = new TestBookingRepository();
            var service = new AvailabilitySuggestionService(bookingRepo);

            propRepo.Properties[1] = new Property { PropertyID = 1, Title = "Hilltop Villa" };

            var controller = new PropertiesController(propRepo, bookingRepo, service)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext()
                }
            };

            // 1. Invalid ID <= 0
            var res1 = await controller.GetAvailabilitySuggestions(0, DateTime.Today, DateTime.Today.AddDays(1));
            Assert.IsType<BadRequestObjectResult>(res1);

            // 2. Property not found
            var res2 = await controller.GetAvailabilitySuggestions(999, DateTime.Today, DateTime.Today.AddDays(1));
            Assert.IsType<NotFoundObjectResult>(res2);

            // 3. Missing dates
            var res3 = await controller.GetAvailabilitySuggestions(1, null, DateTime.Today.AddDays(1));
            Assert.IsType<BadRequestObjectResult>(res3);

            // 4. CheckOut <= CheckIn
            var res4 = await controller.GetAvailabilitySuggestions(1, DateTime.Today.AddDays(2), DateTime.Today.AddDays(1));
            Assert.IsType<BadRequestObjectResult>(res4);

            // 5. Valid call returns 200 OK
            var res5 = await controller.GetAvailabilitySuggestions(1, DateTime.Today.AddDays(1), DateTime.Today.AddDays(3));
            var ok = Assert.IsType<OkObjectResult>(res5);
            var result = Assert.IsType<AvailabilitySuggestionResult>(ok.Value);
            Assert.True(result.IsRequestedAvailable);
        }

        [Fact]
        public async Task Endpoint_RateLimiting_RejectsExceedingClientsWith429()
        {
            PropertiesController.ResetRateLimits();

            var propRepo = new TestPropertyRepository();
            var bookingRepo = new TestBookingRepository();
            var service = new AvailabilitySuggestionService(bookingRepo);

            propRepo.Properties[1] = new Property { PropertyID = 1, Title = "Hilltop Villa" };

            var httpContext = new DefaultHttpContext();
            httpContext.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("192.168.1.100");

            var controller = new PropertiesController(propRepo, bookingRepo, service)
            {
                ControllerContext = new ControllerContext { HttpContext = httpContext }
            };

            IActionResult? lastResult = null;

            // Make 61 rapid requests
            for (int i = 0; i < 61; i++)
            {
                lastResult = await controller.GetAvailabilitySuggestions(1, DateTime.Today.AddDays(1), DateTime.Today.AddDays(2));
            }

            // 61st request must trigger 429 Too Many Requests
            var statusResult = Assert.IsType<ObjectResult>(lastResult);
            Assert.Equal(StatusCodes.Status429TooManyRequests, statusResult.StatusCode);

            PropertiesController.ResetRateLimits();
        }
    }
}
