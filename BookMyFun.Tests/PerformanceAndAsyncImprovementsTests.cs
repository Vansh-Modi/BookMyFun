using System.Net;
using System.Security.Claims;
using BookMyFun.Controllers;
using BookMyFun.Data;
using BookMyFun.Models.Entities;
using BookMyFun.Repositories;
using BookMyFun.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BookMyFun.Tests
{
    public class PerformanceAndAsyncImprovementsTests
    {
        [Fact]
        public async Task PropertiesController_Index_AppliesPaginationCorrectly()
        {
            // Arrange
            var mockPropRepo = new MockPropertyRepositoryWithPaging();
            var mockBookingRepo = new MockBookingRepository();
            var controller = new PropertiesController(mockPropRepo, mockBookingRepo);

            // Act: Request page 2 with page size 5
            var result = await controller.Index(null, null, null, null, null, null, page: 2, pageSize: 5) as ViewResult;

            // Assert
            Assert.NotNull(result);
            var model = Assert.IsAssignableFrom<IEnumerable<Property>>(result.Model);
            Assert.Equal(5, model.Count());
            Assert.Equal(2, controller.ViewBag.CurrentPage);
            Assert.Equal(5, controller.ViewBag.PageSize);
            Assert.Equal(15, controller.ViewBag.TotalCount);
            Assert.Equal(3, controller.ViewBag.TotalPages);
        }

        [Fact]
        public async Task AdminController_ManageBookings_AppliesPaginationCorrectly()
        {
            // Arrange
            var mockBookingRepo = new MockBookingRepositoryWithManyBookings(25);
            var controller = new AdminController(
                propRepo: new MockPropertyRepositoryWithPaging(),
                userRepo: null!,
                bookingRepo: mockBookingRepo,
                subRepo: null!,
                configRepo: null!,
                ownerReqRepo: null!,
                contactRepo: null!,
                siteSettingsService: null!,
                financialRepo: null!,
                env: null!,
                mediaStorage: null!,
                refundEngine: null!,
                partnerLeadService: null!,
                damageClaimService: null!,
                idVerificationService: null!,
                eventPackageRepo: null!);

            // Act: Request page 1 with 10 per page
            var result = await controller.ManageBookings("All", page: 1, pageSize: 10) as ViewResult;

            // Assert
            Assert.NotNull(result);
            var model = Assert.IsAssignableFrom<List<Booking>>(result.Model);
            Assert.Equal(10, model.Count);
            Assert.Equal(1, controller.ViewBag.CurrentPage);
            Assert.Equal(10, controller.ViewBag.PageSize);
            Assert.Equal(25, controller.ViewBag.TotalCount);
            Assert.Equal(3, controller.ViewBag.TotalPages);
        }

        [Fact]
        public async Task EmailQueueService_SupportsBothEmailAndWhatsAppChannels()
        {
            // Arrange
            var queue = new EmailQueueService(NullLogger<EmailQueueService>.Instance);

            // Act
            await queue.EnqueueAsync(new EmailQueueMessage
            {
                Channel = "Email",
                ToEmail = "guest@example.com",
                Subject = "Booking Receipt",
                BodyHtml = "<p>Confirmed</p>"
            });

            await queue.EnqueueAsync(new EmailQueueMessage
            {
                Channel = "WhatsApp",
                ToPhone = "9876543210",
                BodyHtml = "Booking confirmed via WhatsApp",
                MessageType = "WhatsAppNotification"
            });

            // Assert
            Assert.Equal(2, queue.ApproximateQueueCount);

            var first = await queue.DequeueAsync(CancellationToken.None);
            Assert.Equal("Email", first.Channel);
            Assert.Equal("guest@example.com", first.ToEmail);

            var second = await queue.DequeueAsync(CancellationToken.None);
            Assert.Equal("WhatsApp", second.Channel);
            Assert.Equal("9876543210", second.ToPhone);
        }

        [Fact]
        public void Services_AcceptIHttpClientFactoryGracefully()
        {
            // Arrange
            var mockConfigRepo = new MockConfigRepository();
            var config = new ConfigurationBuilder().Build();
            var factory = new MockHttpClientFactory();

            // Act
            var rzp = new RazorpayPaymentService(mockConfigRepo, config, NullLogger<RazorpayPaymentService>.Instance, factory);
            var comms = new CommunicationService(
                mockConfigRepo, 
                config, 
                new EmailQueueService(NullLogger<EmailQueueService>.Instance), 
                NullLogger<CommunicationService>.Instance, 
                factory);

            // Assert
            Assert.NotNull(rzp);
            Assert.NotNull(comms);
        }

        // Mock helpers
        private class MockHttpClientFactory : IHttpClientFactory
        {
            public HttpClient CreateClient(string name) => new HttpClient();
        }

        private class MockConfigRepository : IConfigRepository
        {
            public Task<ApiConfiguration?> GetActiveConfigByProviderAsync(string provider) => Task.FromResult<ApiConfiguration?>(null);
            public Task<List<ApiConfiguration>> GetAllConfigsAsync() => Task.FromResult(new List<ApiConfiguration>());
            public Task<bool> SaveOrUpdateConfigAsync(ApiConfiguration config) => Task.FromResult(true);
            public Task<string?> GetSettingValueAsync(string key) => Task.FromResult<string?>(null);
            public Task<bool> SetSettingValueAsync(string key, string value, bool isEncrypted = false) => Task.FromResult(true);
            public Task<List<SiteSetting>> GetAllSettingsAsync() => Task.FromResult(new List<SiteSetting>());
            public Task LogAuditAsync(int adminUserId, string action, string entityName, int? entityId, string? details) => Task.CompletedTask;
            public Task<List<AuditLog>> GetAuditLogsAsync(int limit = 100) => Task.FromResult(new List<AuditLog>());
        }

        private class MockPropertyRepositoryWithPaging : IPropertyRepository
        {
            public Task<Property?> GetByIdAsync(int propertyId) => Task.FromResult<Property?>(new Property { PropertyID = propertyId });
            public Task<List<Property>> GetAllPropertiesAsync(bool onlyApproved = false) => Task.FromResult(new List<Property>());
            public Task<List<Property>> GetPropertiesByOwnerIdAsync(int ownerId) => Task.FromResult(new List<Property>());
            public Task<List<Property>> SearchPropertiesAsync(string? location, int? propertyTypeId, int? occasionId, decimal? minPrice, decimal? maxPrice, int? guests)
            {
                var list = Enumerable.Range(1, 15).Select(i => new Property { PropertyID = i, Title = $"Prop {i}" }).ToList();
                return Task.FromResult(list);
            }
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
            public Task<List<Occasion>> GetOccasionsAsync() => Task.FromResult(new List<Occasion>());
            public Task<bool> AddOccasionAsync(string occasionName) => Task.FromResult(true);
            public Task<bool> DeleteOccasionAsync(int occasionId) => Task.FromResult(true);
            public Task<bool> AddPropertyTypeAsync(string typeName, string? description) => Task.FromResult(true);
            public Task<bool> DeletePropertyTypeAsync(int propertyTypeId) => Task.FromResult(true);
            public Task<int> GetOrCreatePropertyTypeByNameAsync(string name) => Task.FromResult(1);
            public Task<bool> UpdatePropertyCategoryAsync(int propertyId, int ownerId, int propertyTypeId) => Task.FromResult(true);
            public Task<int> GetOrCreateAmenityByNameAsync(string name) => Task.FromResult(1);
            public Task<int> GetOrCreateOccasionByNameAsync(string name) => Task.FromResult(1);
            public Task<List<PropertyEventCommission>> GetPropertyEventCommissionsAsync(int propertyId) => Task.FromResult(new List<PropertyEventCommission>());
            public Task<decimal?> GetPropertyEventCommissionRateAsync(int propertyId, int packageId) => Task.FromResult<decimal?>(null);
            public Task<bool> SetPropertyEventCommissionsAsync(int propertyId, List<(int packageId, decimal commissionRate)> commissions) => Task.FromResult(true);
        }

        private class MockBookingRepository : IBookingRepository
        {
            public Task<Booking?> GetByIdAsync(int bookingID) => Task.FromResult<Booking?>(null);
            public Task<Booking?> GetBookingByIdAsync(int bookingId) => Task.FromResult<Booking?>(null);
            public Task<Booking?> GetByIdempotencyKeyAsync(string idempotencyKey) => Task.FromResult<Booking?>(null);
            public Task<List<Booking>> GetByClientIdAsync(int clientId) => Task.FromResult(new List<Booking>());
            public Task<List<Booking>> GetByOwnerIdAsync(int ownerId) => Task.FromResult(new List<Booking>());
            public virtual Task<List<Booking>> GetAllBookingsAsync() => Task.FromResult(new List<Booking>());
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

        private class MockBookingRepositoryWithManyBookings : MockBookingRepository
        {
            private readonly int _count;
            public MockBookingRepositoryWithManyBookings(int count) => _count = count;
            public override Task<List<Booking>> GetAllBookingsAsync()
            {
                var list = Enumerable.Range(1, _count).Select(i => new Booking
                {
                    BookingID = i,
                    Status = "Confirmed",
                    CreatedAt = DateTime.UtcNow
                }).ToList();
                return Task.FromResult(list);
            }
        }
    }
}
