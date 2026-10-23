using System.Security.Claims;
using BookMyFun.Controllers;
using BookMyFun.Models.Entities;
using BookMyFun.Models.ViewModels;
using BookMyFun.Repositories;
using BookMyFun.Services;
using BookMyFun.Services.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging;
using Xunit;

namespace BookMyFun.Tests
{
    public class AccessControlAndAuthorizationTests
    {
        private class SimpleTempDataProvider : ITempDataProvider
        {
            public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
            public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
        }

        private class MockLogger<T> : ILogger<T>
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
        }

        private class MockBookingRepo : IBookingRepository
        {
            public readonly Dictionary<int, Booking> Bookings = new();
            public readonly List<CustomerReview> Reviews = new();

            public Task<Booking?> GetByIdAsync(int bookingId)
            {
                Bookings.TryGetValue(bookingId, out var b);
                return Task.FromResult(b);
            }

            public Task<Booking?> GetBookingByIdAsync(int bookingId) => GetByIdAsync(bookingId);
            public Task<List<Booking>> GetByClientIdAsync(int clientId) => Task.FromResult(Bookings.Values.Where(b => b.ClientID == clientId).ToList());
            public Task<List<Booking>> GetByOwnerIdAsync(int ownerId) => Task.FromResult(new List<Booking>());
            public Task<List<Booking>> GetAllBookingsAsync() => Task.FromResult(Bookings.Values.ToList());
            public Task<Booking?> GetByIdempotencyKeyAsync(string idempotencyKey) => Task.FromResult<Booking?>(null);
            public Task<int> CreateBookingAsync(Booking booking)
            {
                booking.BookingID = Bookings.Count + 1;
                Bookings[booking.BookingID] = booking;
                return Task.FromResult(booking.BookingID);
            }
            public Task<bool> UpdateBookingStatusAsync(int bookingId, string status) => Task.FromResult(true);
            public Task<bool> CancelBookingAsync(int bookingId) => Task.FromResult(true);
            public Task<bool> IsSlotAvailableAsync(int propertyId, DateTime checkIn, DateTime checkOut, string bookingType = "FullDay", string? slotType = null) => Task.FromResult(true);
            public Task<int> RecordPaymentAsync(Payment payment) => Task.FromResult(1);
            public Task<List<Payment>> GetPaymentsByBookingIdAsync(int bookingId) => Task.FromResult(new List<Payment>());
            public Task<bool> UpdateSecurityDepositStatusAsync(int bookingId, string status) => Task.FromResult(true);
            public Task<List<CustomerReview>> GetReviewsByPropertyIdAsync(int propertyId) => Task.FromResult(Reviews.Where(r => r.PropertyID == propertyId).ToList());
            public Task<int> AddReviewAsync(CustomerReview review)
            {
                Reviews.Add(review);
                return Task.FromResult(Reviews.Count);
            }
            public Task<bool> UpdateBookingEventCommissionAsync(int bookingId, decimal newEventCommissionRate) => Task.FromResult(true);
        }

        private class MockUserRepo : IUserRepository
        {
            public readonly Dictionary<int, User> Users = new();
            public Task<User?> GetByIdAsync(int userId)
            {
                Users.TryGetValue(userId, out var u);
                return Task.FromResult(u);
            }
            public Task<User?> GetByEmailAsync(string email) => Task.FromResult(Users.Values.FirstOrDefault(u => u.Email == email));
            public Task<User?> GetByAuthProviderKeyAsync(string provider, string providerKey) => Task.FromResult<User?>(null);
            public Task<int> CreateUserAsync(User user)
            {
                user.UserID = Users.Count + 1;
                Users[user.UserID] = user;
                return Task.FromResult(user.UserID);
            }
            public Task<bool> UpdateUserAsync(User user) => Task.FromResult(true);
            public Task<bool> DeleteUserAsync(int userId) => Task.FromResult(true);
            public Task<List<User>> GetUsersByRoleAsync(string role) => Task.FromResult(Users.Values.Where(u => u.Role == role).ToList());
            public Task<List<User>> GetAllUsersAsync() => Task.FromResult(Users.Values.ToList());
            public Task<bool> ResetPasswordAsync(string email, string newPasswordHash) => Task.FromResult(true);
            public Task<bool> UpdateUserRoleAsync(int userId, string role) => Task.FromResult(true);
            public Task<bool> UpdateUserTierAndCommissionAsync(int userId, string subscriptionTier, decimal? customCommissionRate) => Task.FromResult(true);
            public Task<bool> UpdateUserCommissionRateAsync(int userId, decimal? commissionRate) => Task.FromResult(true);
            public Task<bool> LinkAuthProviderAsync(int userId, string provider, string providerKey, string? profilePic = null) => Task.FromResult(true);
        }

        private class MockIdVerificationService : IIdVerificationService
        {
            public Task<int> SubmitVerificationAsync(int clientId, string documentType, string storageKey, string? maskedIdentifier) => Task.FromResult(1);
            public Task<ClientIdVerification?> GetClientVerificationAsync(int clientId, int requestingUserId, string userRole) => Task.FromResult<ClientIdVerification?>(null);
            public Task<List<ClientIdVerification>> GetAllVerificationsAsync(string? status = null) => Task.FromResult(new List<ClientIdVerification>());
            public Task<bool> ReviewVerificationAsync(int verificationId, string action, int staffUserId, string? rejectionReason = null) => Task.FromResult(true);
            public Task<bool> IsClientVerifiedAsync(int clientId) => Task.FromResult(true);
        }

        private class MockPropertyRepo : IPropertyRepository
        {
            public readonly Dictionary<int, Property> Properties = new();
            public readonly Dictionary<int, PropertyMedia> MediaRecords = new();

            public Task<Property?> GetByIdAsync(int propertyId)
            {
                Properties.TryGetValue(propertyId, out var prop);
                return Task.FromResult(prop);
            }

            public Task<PropertyMedia?> GetPropertyMediaByIdAsync(int mediaId)
            {
                MediaRecords.TryGetValue(mediaId, out var m);
                return Task.FromResult(m);
            }

            public Task<bool> DeletePropertyMediaAsync(int mediaId)
            {
                return Task.FromResult(MediaRecords.Remove(mediaId));
            }

            public Task<List<Property>> GetAllPropertiesAsync(bool onlyApproved = false) => Task.FromResult(Properties.Values.ToList());
            public Task<List<Property>> GetPropertiesByOwnerIdAsync(int ownerId) => Task.FromResult(Properties.Values.Where(p => p.OwnerID == ownerId).ToList());
            public Task<List<Property>> SearchPropertiesAsync(string? location, int? propertyTypeId, int? occasionId, decimal? minPrice, decimal? maxPrice, int? guests) => Task.FromResult(new List<Property>());
            public Task<int> CreatePropertyAsync(Property property) => Task.FromResult(1);
            public Task<bool> UpdatePropertyAsync(Property property) => Task.FromResult(true);
            public Task<bool> DeletePropertyAsync(int propertyId) => PermanentlyDeletePropertyAsync(propertyId);
            public Task<bool> PermanentlyDeletePropertyAsync(int propertyId) => Task.FromResult(Properties.Remove(propertyId));
            public Task<bool> DeactivatePropertyAsync(int propertyId, bool isDeactivated = true) => Task.FromResult(true);
            public Task<bool> DeactivatePropertyAsync(int propertyId, int ownerId, bool isDeactivated) => Task.FromResult(true);
            public Task<bool> ApprovePropertyAsync(int propertyId, bool isApproved, string? targetStatus = null) => Task.FromResult(true);
            public Task<bool> ApprovePropertyAsync(int propertyId, bool isApproved) => Task.FromResult(true);
            public Task<bool> PermanentDeletePropertyAsync(int propertyId) => Task.FromResult(true);
            public Task<bool> UpdatePropertyMapUrlAsync(int propertyId, int ownerId, string? mapUrl) => Task.FromResult(true);
            public Task<bool> SetPropertyOccasionsAsync(int propertyId, List<int> occasionIds) => Task.FromResult(true);
            public Task<List<int>> GetPropertyOccasionIdsAsync(int propertyId) => Task.FromResult(new List<int>());
            public Task<int> AddPropertyImageAsync(int propertyId, string imageUrl, bool isPrimary) => Task.FromResult(1);
            public Task<List<PropertyImage>> GetPropertyImagesAsync(int propertyId) => Task.FromResult(new List<PropertyImage>());
            public Task<int> AddPropertyMediaAsync(PropertyMedia media)
            {
                media.MediaID = MediaRecords.Count + 1;
                MediaRecords[media.MediaID] = media;
                return Task.FromResult(media.MediaID);
            }
            public Task<List<PropertyMedia>> GetPropertyMediaAsync(int propertyId) => Task.FromResult(MediaRecords.Values.Where(m => m.PropertyID == propertyId).ToList());
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

        private class MockMediaStorageService : IMediaStorageService
        {
            public Task<(string publicUrl, string publicId)> SaveMediaFileAsync(IFormFile file, string mediaType) =>
                Task.FromResult(("/uploads/sample.jpg", "sample_id"));

            public Task<string> SavePrivateDocumentAsync(IFormFile file, int clientId) =>
                Task.FromResult($"doc_client_{clientId}_test.jpg");

            public Task<bool> ValidateExternalUrlAsync(string url) => Task.FromResult(true);

            public string GenerateExpiringDocumentToken(string documentKey, int requestingUserId, string userRole, int expirationMinutes = 15) =>
                "valid_dummy_token";

            public bool ValidateExpiringDocumentToken(string documentKey, string token, int requestingUserId, string userRole) =>
                token == "valid_dummy_token";

            public string GetPrivateDocumentPhysicalPath(string documentKey) =>
                Path.Combine(Path.GetTempPath(), documentKey);
        }

        private static ControllerContext CreateContext(int userId, string role)
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(ClaimTypes.Role, role)
            };
            var identity = new ClaimsIdentity(claims, "TestAuth");
            var principal = new ClaimsPrincipal(identity);
            var httpContext = new DefaultHttpContext { User = principal };
            return new ControllerContext { HttpContext = httpContext };
        }

        [Fact]
        public async Task ClientController_AddReview_RejectsReviewForAnotherClientsBooking()
        {
            var bookingRepo = new MockBookingRepo();
            var userRepo = new MockUserRepo();
            var idService = new MockIdVerificationService();

            bookingRepo.Bookings[101] = new Booking
            {
                BookingID = 101,
                ClientID = 999, // Owned by another client
                PropertyID = 5,
                TotalAmount = 5000m
            };

            var controller = new ClientController(userRepo, bookingRepo, idService)
            {
                ControllerContext = CreateContext(userId: 42, role: "Client"),
                TempData = new TempDataDictionary(new DefaultHttpContext(), new SimpleTempDataProvider())
            };

            var result = await controller.AddReview(bookingId: 101, propertyId: 5, rating: 5, comment: "Attempted IDOR review");

            Assert.IsType<ForbidResult>(result);
            Assert.Empty(bookingRepo.Reviews);
        }

        [Fact]
        public async Task ClientController_AddReview_SucceedsForOwnedBookingAndMatchingProperty()
        {
            var bookingRepo = new MockBookingRepo();
            var userRepo = new MockUserRepo();
            var idService = new MockIdVerificationService();

            bookingRepo.Bookings[102] = new Booking
            {
                BookingID = 102,
                ClientID = 42, // Matches logged in client
                PropertyID = 7,
                TotalAmount = 5000m
            };

            var controller = new ClientController(userRepo, bookingRepo, idService)
            {
                ControllerContext = CreateContext(userId: 42, role: "Client"),
                TempData = new TempDataDictionary(new DefaultHttpContext(), new SimpleTempDataProvider())
            };

            var result = await controller.AddReview(bookingId: 102, propertyId: 7, rating: 5, comment: "Legitimate great stay");

            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Index", redirect.ActionName);
            Assert.Single(bookingRepo.Reviews);
            Assert.Equal(42, bookingRepo.Reviews[0].ClientID);
            Assert.Equal(102, bookingRepo.Reviews[0].BookingID);
        }

        [Fact]
        public async Task MediaController_DeleteMedia_RejectsHostDeletingAnotherHostsMedia()
        {
            var propRepo = new MockPropertyRepo();
            var mediaStorage = new MockMediaStorageService();
            var idService = new MockIdVerificationService();
            var logger = new MockLogger<MediaController>();

            propRepo.Properties[10] = new Property { PropertyID = 10, OwnerID = 888 }; // Host 888
            propRepo.MediaRecords[55] = new PropertyMedia { MediaID = 55, PropertyID = 10, StorageUrl = "/media/55.jpg" };

            var controller = new MediaController(mediaStorage, propRepo, idService, logger)
            {
                ControllerContext = CreateContext(userId: 777, role: "Owner") // Host 777
            };

            await Assert.ThrowsAsync<ForbiddenException>(async () =>
            {
                await controller.DeleteMedia(55);
            });

            Assert.True(propRepo.MediaRecords.ContainsKey(55)); // Not deleted
        }

        [Fact]
        public void MediaController_ViewPrivateDocument_RejectsClientAccessToAnotherClientsDocument()
        {
            var propRepo = new MockPropertyRepo();
            var mediaStorage = new MockMediaStorageService();
            var idService = new MockIdVerificationService();
            var logger = new MockLogger<MediaController>();

            var controller = new MediaController(mediaStorage, propRepo, idService, logger)
            {
                ControllerContext = CreateContext(userId: 10, role: "Client")
            };

            // Trying to view doc belonging to client 99
            Assert.Throws<ForbiddenException>(() =>
            {
                controller.ViewPrivateDocument("doc_client_99_secret.pdf", "valid_dummy_token");
            });
        }

        [Fact]
        public void MediaController_ViewPrivateDocument_StrictlyForbidsHostFromViewingIdDocuments()
        {
            var propRepo = new MockPropertyRepo();
            var mediaStorage = new MockMediaStorageService();
            var idService = new MockIdVerificationService();
            var logger = new MockLogger<MediaController>();

            var controller = new MediaController(mediaStorage, propRepo, idService, logger)
            {
                ControllerContext = CreateContext(userId: 5, role: "Owner")
            };

            Assert.Throws<ForbiddenException>(() =>
            {
                controller.ViewPrivateDocument("doc_client_10_secret.pdf", "valid_dummy_token");
            });
        }
    }
}
