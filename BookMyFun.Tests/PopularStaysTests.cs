using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BookMyFun.Data;
using BookMyFun.Models.Entities;
using BookMyFun.Repositories;
using BookMyFun.Services;
using Xunit;

namespace BookMyFun.Tests
{
    public class PopularStaysTests
    {
        private class TestConfigRepo : IConfigRepository
        {
            public readonly Dictionary<string, string> Settings = new();

            public Task<ApiConfiguration?> GetActiveConfigByProviderAsync(string provider) => Task.FromResult<ApiConfiguration?>(null);
            public Task<List<ApiConfiguration>> GetAllConfigsAsync() => Task.FromResult(new List<ApiConfiguration>());
            public Task<bool> SaveOrUpdateConfigAsync(ApiConfiguration config) => Task.FromResult(true);

            public Task<string?> GetSettingValueAsync(string key)
            {
                Settings.TryGetValue(key, out var val);
                return Task.FromResult<string?>(val);
            }

            public Task<bool> SetSettingValueAsync(string key, string value, bool isEncrypted = false)
            {
                Settings[key] = value;
                return Task.FromResult(true);
            }

            public Task<List<SiteSetting>> GetAllSettingsAsync()
            {
                var list = Settings.Select(kvp => new SiteSetting { SettingKey = kvp.Key, SettingValue = kvp.Value }).ToList();
                return Task.FromResult(list);
            }

            public Task LogAuditAsync(int performedByUserId, string action, string entityType, int? entityId, string? details) => Task.CompletedTask;
        }

        private class MockBookingRepo : IBookingRepository
        {
            public List<Booking> Bookings { get; set; } = new();
            public int GetAllBookingsCallCount { get; private set; }

            public Task<List<Booking>> GetAllBookingsAsync()
            {
                GetAllBookingsCallCount++;
                return Task.FromResult(Bookings.ToList());
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
            public Task<bool> UpdateOwnerPayoutAsync(int bookingId, decimal newOwnerPayout) => Task.FromResult(true);
            public Task<bool> UpdateSecurityDepositStatusAsync(int bookingId, string status) => Task.FromResult(true);
            public Task<bool> IsSlotAvailableAsync(int propertyId, DateTime checkInDate, DateTime checkOutDate, string bookingType, string? slotType) => Task.FromResult(true);
            public Task<int> RecordPaymentAsync(Payment payment) => Task.FromResult(1);
            public Task<List<Payment>> GetPaymentsByBookingIdAsync(int bookingId) => Task.FromResult(new List<Payment>());
            public Task<List<CustomerReview>> GetReviewsByPropertyIdAsync(int propertyId) => Task.FromResult(new List<CustomerReview>());
            public Task<int> AddReviewAsync(CustomerReview review) => Task.FromResult(1);
            public Task<bool> UpdateBookingEventCommissionAsync(int bookingId, decimal newEventCommissionRate) => Task.FromResult(true);
            public Task<List<Booking>> GetBookingsByPropertyIdAsync(int propertyId) => Task.FromResult(Bookings.Where(b => b.PropertyID == propertyId).ToList());
        }

        private class MockPropRepo : IPropertyRepository
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
        public async Task GetPopularStaysFallbackAsync_ReturnsDefaults_WhenNotConfigured()
        {
            var configRepo = new TestConfigRepo();
            var service = new SiteSettingsService(configRepo);

            var result = await service.GetPopularStaysFallbackAsync();

            Assert.NotNull(result);
            Assert.Equal(5, result.Count);
            Assert.Contains(result, x => x.Location == "Lonavala");
            Assert.Contains(result, x => x.Location == "Alibaug");
            Assert.Contains(result, x => x.Location == "Goa");
        }

        [Fact]
        public async Task SavePopularStaysFallbackAsync_PersistsAndReadsBack()
        {
            var configRepo = new TestConfigRepo();
            var service = new SiteSettingsService(configRepo);

            var customList = new List<PopularStayItem>
            {
                new PopularStayItem { PropertyID = 101, Title = "Sunset Luxury Haven", Location = "Khandala" },
                new PopularStayItem { PropertyID = 102, Title = "Sea Breeze Villa", Location = "Kashid" },
                new PopularStayItem { PropertyID = 0, Title = "Heritage Palace", Location = "Udaipur" },
                new PopularStayItem { PropertyID = 0, Title = "Forest Retreat", Location = "Coorg" },
                new PopularStayItem { PropertyID = 0, Title = "Alpine Cottage", Location = "Manali" }
            };

            await service.SavePopularStaysFallbackAsync(customList);
            var result = await service.GetPopularStaysFallbackAsync();

            Assert.Equal(5, result.Count);
            Assert.Equal("Sunset Luxury Haven", result[0].Title);
            Assert.Equal(101, result[0].PropertyID);
            Assert.Equal("Kashid", result[1].Location);
            Assert.Equal("Udaipur", result[2].Location);
        }

        [Fact]
        public async Task GetPopularStaysAsync_AlwaysReturns5ItemsPaddedFromFallback()
        {
            var configRepo = new TestConfigRepo();
            var service = new SiteSettingsService(configRepo);

            // Invalidate any residual cache before testing
            SiteSettingsService.InvalidatePopularStaysCacheStatic();
            var stays = await service.GetPopularStaysAsync();

            Assert.NotNull(stays);
            Assert.Equal(5, stays.Count);
        }

        [Fact]
        public async Task SavePopularStaysFallbackAsync_EvictsCacheImmediately()
        {
            var configRepo = new TestConfigRepo();
            var service = new SiteSettingsService(configRepo);

            SiteSettingsService.InvalidatePopularStaysCacheStatic();

            // Populate initial cache
            var initial = await service.GetPopularStaysAsync();
            Assert.Equal(5, initial.Count);

            // Update fallback
            var customList = new List<PopularStayItem>
            {
                new PopularStayItem { PropertyID = 50, Title = "Custom Villa 50", Location = "Nashik" },
                new PopularStayItem { PropertyID = 51, Title = "Custom Villa 51", Location = "Panchgani" },
                new PopularStayItem { PropertyID = 52, Title = "Custom Villa 52", Location = "Igatpuri" },
                new PopularStayItem { PropertyID = 53, Title = "Custom Villa 53", Location = "Wayanad" },
                new PopularStayItem { PropertyID = 54, Title = "Custom Villa 54", Location = "Munnar" }
            };
            await service.SavePopularStaysFallbackAsync(customList);

            // Next call should read new fallback because cache was evicted
            var updated = await service.GetPopularStaysAsync();
            Assert.Equal(5, updated.Count);
            Assert.Equal("Custom Villa 50", updated[0].Title);
            Assert.Equal("Nashik", updated[0].Location);
        }

        [Fact]
        public async Task Query_ReturnsTop5ByBookingCount()
        {
            var configRepo = new TestConfigRepo();
            var bookingRepo = new MockBookingRepo();
            var propRepo = new MockPropRepo();

            for (int i = 1; i <= 6; i++)
            {
                propRepo.Properties[i] = new Property
                {
                    PropertyID = i,
                    Title = $"Property {i}",
                    Location = $"Location {i}",
                    IsApproved = true
                };
            }

            // Create bookings in last 30 days
            // Property 1: 5 bookings
            // Property 2: 4 bookings
            // Property 3: 3 bookings
            // Property 4: 2 bookings
            // Property 5: 1 booking
            // Property 6: 0 bookings
            int bookingId = 1;
            void AddBookings(int propId, int count)
            {
                for (int j = 0; j < count; j++)
                {
                    bookingRepo.Bookings.Add(new Booking
                    {
                        BookingID = bookingId++,
                        PropertyID = propId,
                        Status = "Confirmed",
                        CreatedAt = DateTime.UtcNow.AddDays(-5)
                    });
                }
            }

            AddBookings(1, 5);
            AddBookings(2, 4);
            AddBookings(3, 3);
            AddBookings(4, 2);
            AddBookings(5, 1);

            SiteSettingsService.InvalidatePopularStaysCacheStatic();
            var service = new SiteSettingsService(configRepo, db: null, bookingRepo: bookingRepo, propRepo: propRepo);

            var result = await service.GetPopularStaysAsync();

            Assert.Equal(5, result.Count);
            Assert.Equal(1, result[0].PropertyID);
            Assert.Equal(2, result[1].PropertyID);
            Assert.Equal(3, result[2].PropertyID);
            Assert.Equal(4, result[3].PropertyID);
            Assert.Equal(5, result[4].PropertyID);
        }

        [Fact]
        public async Task Fallback_FillsRemainingSlotsUpTo5()
        {
            var configRepo = new TestConfigRepo();
            var bookingRepo = new MockBookingRepo();
            var propRepo = new MockPropRepo();

            propRepo.Properties[10] = new Property { PropertyID = 10, Title = "Only Booked 1", Location = "Khandala", IsApproved = true };
            propRepo.Properties[20] = new Property { PropertyID = 20, Title = "Only Booked 2", Location = "Matheran", IsApproved = true };

            bookingRepo.Bookings.Add(new Booking { BookingID = 1, PropertyID = 10, Status = "Confirmed", CreatedAt = DateTime.UtcNow.AddDays(-2) });
            bookingRepo.Bookings.Add(new Booking { BookingID = 2, PropertyID = 20, Status = "Confirmed", CreatedAt = DateTime.UtcNow.AddDays(-3) });

            SiteSettingsService.InvalidatePopularStaysCacheStatic();
            var service = new SiteSettingsService(configRepo, db: null, bookingRepo: bookingRepo, propRepo: propRepo);

            var result = await service.GetPopularStaysAsync();

            Assert.Equal(5, result.Count);
            Assert.Equal(10, result[0].PropertyID);
            Assert.Equal(20, result[1].PropertyID);
            // Remaining 3 come from default fallbacks (Lonavala, Alibaug, Goa)
            Assert.Equal("Lonavala", result[2].Location);
            Assert.Equal("Alibaug", result[3].Location);
            Assert.Equal("Goa", result[4].Location);
        }

        [Fact]
        public async Task Cache_ServesSubsequentRequestsWithin15Minutes()
        {
            var configRepo = new TestConfigRepo();
            var bookingRepo = new MockBookingRepo();
            var propRepo = new MockPropRepo();

            SiteSettingsService.InvalidatePopularStaysCacheStatic();
            var service = new SiteSettingsService(configRepo, db: null, bookingRepo: bookingRepo, propRepo: propRepo);

            var firstCall = await service.GetPopularStaysAsync();
            Assert.Equal(1, bookingRepo.GetAllBookingsCallCount);

            var secondCall = await service.GetPopularStaysAsync();
            Assert.Equal(1, bookingRepo.GetAllBookingsCallCount); // Served from cache, count didn't increase
        }

        [Fact]
        public async Task CacheInvalidation_OnConfirmedBooking_CausesFreshQuery()
        {
            var configRepo = new TestConfigRepo();
            var bookingRepo = new MockBookingRepo();
            var propRepo = new MockPropRepo();

            SiteSettingsService.InvalidatePopularStaysCacheStatic();
            var service = new SiteSettingsService(configRepo, db: null, bookingRepo: bookingRepo, propRepo: propRepo);

            var firstCall = await service.GetPopularStaysAsync();
            Assert.Equal(1, bookingRepo.GetAllBookingsCallCount);

            // Invalidate cache (as happens when a booking is confirmed)
            SiteSettingsService.InvalidatePopularStaysCacheStatic();

            var secondCall = await service.GetPopularStaysAsync();
            Assert.Equal(2, bookingRepo.GetAllBookingsCallCount); // Fresh query made
        }
    }
}
