using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BookMyFun.Controllers;
using BookMyFun.Models.Entities;
using BookMyFun.Repositories;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Xunit;

namespace BookMyFun.Tests
{
    public class HomeSearchAvailabilityTests
    {
        private class MockPropRepo : IPropertyRepository
        {
            public List<Property> Properties { get; } = new();
            public Task<Property?> GetByIdAsync(int propertyId) => Task.FromResult(Properties.FirstOrDefault(p => p.PropertyID == propertyId));
            public Task<List<Property>> GetAllPropertiesAsync(bool onlyApproved = false) => Task.FromResult(Properties.ToList());
            public Task<List<Property>> GetPropertiesByOwnerIdAsync(int ownerId) => Task.FromResult(Properties.Where(p => p.OwnerID == ownerId).ToList());
            public Task<List<Property>> SearchPropertiesAsync(string? location, int? propertyTypeId, int? occasionId, decimal? minPrice, decimal? maxPrice, int? guests)
                => Task.FromResult(Properties.ToList());
            public Task<int> CreatePropertyAsync(Property property) => Task.FromResult(1);
            public Task<bool> UpdatePropertyAsync(Property property) => Task.FromResult(true);
            public Task<bool> UpdatePropertyMapUrlAsync(int propertyId, int ownerId, string? mapUrl) => Task.FromResult(true);
            public Task<bool> DeletePropertyAsync(int propertyId) => Task.FromResult(true);
            public Task<bool> PermanentlyDeletePropertyAsync(int propertyId) => Task.FromResult(true);
            public Task<bool> DeactivatePropertyAsync(int propertyId, bool deactivate = true) => Task.FromResult(true);
            public Task<bool> ApprovePropertyAsync(int propertyId, bool isApproved, string? targetStatus = null) => Task.FromResult(true);
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
            public Task<List<PropertyType>> GetPropertyTypesAsync() => Task.FromResult(new List<PropertyType>());
            public Task<int> GetOrCreatePropertyTypeByNameAsync(string name) => Task.FromResult(1);
            public Task<bool> UpdatePropertyCategoryAsync(int propertyId, int ownerId, int propertyTypeId) => Task.FromResult(true);
            public Task<List<Occasion>> GetOccasionsAsync() => Task.FromResult(new List<Occasion>());
            public Task<int> GetOrCreateAmenityByNameAsync(string name) => Task.FromResult(1);
            public Task<int> GetOrCreateOccasionByNameAsync(string name) => Task.FromResult(1);
            public Task<List<PropertyEventCommission>> GetPropertyEventCommissionsAsync(int propertyId) => Task.FromResult(new List<PropertyEventCommission>());
            public Task<decimal?> GetPropertyEventCommissionRateAsync(int propertyId, int packageId) => Task.FromResult<decimal?>(null);
            public Task<bool> SetPropertyEventCommissionsAsync(int propertyId, List<(int packageId, decimal commissionRate)> commissions) => Task.FromResult(true);
        }

        private class MockBookRepo : IBookingRepository
        {
            public List<Booking> Bookings { get; } = new();
            public Task<List<Booking>> GetBookingsByPropertyIdAsync(int propertyId) => Task.FromResult(Bookings.Where(b => b.PropertyID == propertyId).ToList());
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
            public Task<bool> IsSlotAvailableAsync(int propertyId, DateTime checkInDate, DateTime checkOutDate, string bookingType, string? slotType) => Task.FromResult(true);
            public Task<int> RecordPaymentAsync(Payment payment) => Task.FromResult(1);
            public Task<List<Payment>> GetPaymentsByBookingIdAsync(int bookingId) => Task.FromResult(new List<Payment>());
            public Task<List<CustomerReview>> GetReviewsByPropertyIdAsync(int propertyId) => Task.FromResult(new List<CustomerReview>());
            public Task<int> AddReviewAsync(CustomerReview review) => Task.FromResult(1);
            public Task<bool> UpdateBookingEventCommissionAsync(int bookingId, decimal newEventCommissionRate) => Task.FromResult(true);
        }

        private class NullTempDataProvider : ITempDataProvider
        {
            public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
            public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
        }

        [Fact]
        public void HomeIndexView_ReplacesDestinationWithCheckInAndCheckOut()
        {
            var path = TestPathHelper.GetPath("Views", "Home", "Index.cshtml");
            var html = File.ReadAllText(path);

            Assert.DoesNotContain("name=\"location\"", html);
            Assert.Contains("name=\"checkIn\"", html);
            Assert.Contains("name=\"checkOut\"", html);
            Assert.Contains("name=\"typeId\"", html);
            Assert.Contains("name=\"occasionId\"", html);
        }

        [Fact]
        public async Task PropertiesIndex_FiltersResultsByDateAvailabilityRule()
        {
            var propRepo = new MockPropRepo();
            propRepo.Properties.Add(new Property { PropertyID = 1, Title = "Available Villa", ListingStatus = "Live" });
            propRepo.Properties.Add(new Property { PropertyID = 2, Title = "Booked Villa", ListingStatus = "Live" });
            propRepo.Properties.Add(new Property { PropertyID = 3, Title = "Checkout-Day Villa", ListingStatus = "Live" });

            var bookRepo = new MockBookRepo();
            // Property 2 is booked 26/11 - 28/11 (overlaps 26/11 - 28/11)
            bookRepo.Bookings.Add(new Booking
            {
                BookingID = 1,
                PropertyID = 2,
                CheckInDate = new DateTime(2026, 11, 26),
                CheckOutDate = new DateTime(2026, 11, 28),
                BookingType = "FullDay",
                Status = "Confirmed"
            });
            // Property 3 checks out 26/11 at 10:00 (available for 26/11 11:00 check-in)
            bookRepo.Bookings.Add(new Booking
            {
                BookingID = 2,
                PropertyID = 3,
                CheckInDate = new DateTime(2026, 11, 24),
                CheckOutDate = new DateTime(2026, 11, 26),
                BookingType = "FullDay",
                Status = "Confirmed"
            });

            var controller = new PropertiesController(propRepo, bookRepo);
            controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
            controller.TempData = new TempDataDictionary(controller.HttpContext, new NullTempDataProvider());

            var result = await controller.Index(
                location: null, typeId: null, occasionId: null, minPrice: null, maxPrice: null, guests: null,
                checkIn: new DateTime(2026, 11, 26), checkOut: new DateTime(2026, 11, 28));

            var viewResult = Assert.IsType<ViewResult>(result);
            var model = Assert.IsAssignableFrom<List<Property>>(viewResult.Model);
            Assert.Contains(model, p => p.PropertyID == 1);
            Assert.DoesNotContain(model, p => p.PropertyID == 2);
            Assert.Contains(model, p => p.PropertyID == 3);
        }
    }
}
