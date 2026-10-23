using System.Security.Claims;
using BookMyFun.Controllers;
using BookMyFun.Models.Entities;
using BookMyFun.Models.ViewModels;
using BookMyFun.Repositories;
using BookMyFun.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Xunit;

namespace BookMyFun.Tests
{
    public class BookingAvailabilityAndErrorHandlingTests
    {
        private class SimpleTempDataProvider : ITempDataProvider
        {
            public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
            public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
        }

        private class MockBookingRepo : IBookingRepository
        {
            public readonly Dictionary<int, Booking> Bookings = new();
            public bool IsAvailableResponse = true;

            public Task<Booking?> GetByIdAsync(int bookingId)
            {
                Bookings.TryGetValue(bookingId, out var b);
                return Task.FromResult(b);
            }

            public Task<Booking?> GetBookingByIdAsync(int bookingId) => GetByIdAsync(bookingId);
            public Task<Booking?> GetByIdempotencyKeyAsync(string idempotencyKey) => Task.FromResult<Booking?>(null);
            public Task<List<Booking>> GetByClientIdAsync(int clientId) => Task.FromResult(Bookings.Values.Where(b => b.ClientID == clientId).ToList());
            public Task<List<Booking>> GetByOwnerIdAsync(int ownerId) => Task.FromResult(new List<Booking>());
            public Task<List<Booking>> GetAllBookingsAsync() => Task.FromResult(Bookings.Values.ToList());

            public Task<int> CreateBookingAsync(Booking booking)
            {
                booking.BookingID = Bookings.Count + 1;
                Bookings[booking.BookingID] = booking;
                return Task.FromResult(booking.BookingID);
            }

            public Task<bool> UpdateBookingStatusAsync(int bookingId, string status) => Task.FromResult(true);
            public Task<bool> CancelBookingAsync(int bookingId) => Task.FromResult(true);
            public Task<int> RecordPaymentAsync(Payment payment) => Task.FromResult(1);
            public Task<List<Payment>> GetPaymentsByBookingIdAsync(int bookingId) => Task.FromResult(new List<Payment>());

            public Task<bool> IsSlotAvailableAsync(int propertyId, DateTime checkInDate, DateTime checkOutDate, string bookingType, string? slotType)
                => Task.FromResult(IsAvailableResponse);

            public Task<bool> IsSlotAvailableAsync(int propertyId, DateTime checkInDate, DateTime checkOutDate, string bookingType, string? slotType, int? excludeBookingId)
                => Task.FromResult(IsAvailableResponse);

            public Task<bool> UpdateSecurityDepositStatusAsync(int bookingId, string status) => Task.FromResult(true);
            public Task<List<CustomerReview>> GetReviewsByPropertyIdAsync(int propertyId) => Task.FromResult(new List<CustomerReview>());
            public Task<int> AddReviewAsync(CustomerReview review) => Task.FromResult(1);
            public Task<bool> UpdateBookingEventCommissionAsync(int bookingId, decimal newEventCommissionRate) => Task.FromResult(true);
        }

        private class MockPropertyRepo : IPropertyRepository
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

        private class MockUserRepo : IUserRepository
        {
            public readonly Dictionary<int, User> Users = new();
            public Task<User?> GetByIdAsync(int userId)
            {
                Users.TryGetValue(userId, out var u);
                return Task.FromResult(u);
            }
            public Task<User?> GetByEmailAsync(string email) => Task.FromResult(Users.Values.FirstOrDefault(u => u.Email.Equals(email, StringComparison.OrdinalIgnoreCase)));
            public Task<User?> GetByAuthProviderKeyAsync(string provider, string providerKey) => Task.FromResult<User?>(null);
            public Task<int> CreateUserAsync(User user) => Task.FromResult(1);
            public Task<bool> UpdateUserAsync(User user) => Task.FromResult(true);
            public Task<bool> DeleteUserAsync(int userId) => Task.FromResult(true);
            public Task<List<User>> GetUsersByRoleAsync(string role) => Task.FromResult(Users.Values.Where(u => u.Role == role).ToList());
            public Task<List<User>> GetAllUsersAsync() => Task.FromResult(Users.Values.ToList());
            public Task<bool> ResetPasswordAsync(string email, string newPasswordHash) => Task.FromResult(true);
            public Task<bool> UpdateUserTierAndCommissionAsync(int userId, string subscriptionTier, decimal? customCommissionRate) => Task.FromResult(true);
            public Task<bool> UpdateUserCommissionRateAsync(int userId, decimal? commissionRate) => Task.FromResult(true);
            public Task<bool> LinkAuthProviderAsync(int userId, string provider, string providerKey, string? profilePic = null) => Task.FromResult(true);
        }

        private class MockEventPackageRepo : IEventPackageRepository
        {
            public Task<List<EventPackage>> GetAllPackagesAsync() => Task.FromResult(new List<EventPackage>());
            public Task<List<EventPackage>> GetActivePackagesAsync() => Task.FromResult(new List<EventPackage>());
            public Task<EventPackage?> GetPackageByIdAsync(int packageId) => Task.FromResult<EventPackage?>(null);
            public Task<int> CreatePackageAsync(EventPackage package) => Task.FromResult(1);
            public Task<bool> UpdatePackageAsync(EventPackage package) => Task.FromResult(true);
            public Task<bool> UpdatePackageCommissionRateAsync(int packageId, decimal? commissionRate) => Task.FromResult(true);
            public Task<bool> DeletePackageAsync(int packageId) => Task.FromResult(true);
            public Task<List<Booking>> GetAssignedEventBookingsAsync(int decoratorUserId) => Task.FromResult(new List<Booking>());
            public Task<List<Booking>> GetAllEventBookingsAsync() => Task.FromResult(new List<Booking>());
            public Task<bool> UpdateSetupStatusAsync(int bookingId, string setupStatus, string? setupProofMediaUrls, int? decoratorId) => Task.FromResult(true);
            public Task<bool> AssignDecoratorAsync(int bookingId, int decoratorUserId) => Task.FromResult(true);
            public Task<bool> CancelOrRemoveEventFromBookingAsync(int bookingId) => Task.FromResult(true);
            public Task<List<User>> GetDecoratorsAsync() => Task.FromResult(new List<User>());
            public Task<List<string>> GetAllDistinctPropertyLocationsAsync() => Task.FromResult(new List<string>());
            public Task<string?> GetEventManagerServiceAreasAsync(int eventManagerUserId) => Task.FromResult<string?>(null);
            public Task<bool> UpdateEventManagerServiceAreasAsync(int eventManagerUserId, string serviceAreas) => Task.FromResult(true);
            public Task<bool> IsAreaServicedByAnyEventManagerAsync(string? propertyLocation) => Task.FromResult(false);
        }

        private class MockPricingService : IBookingPricingService
        {
            public decimal CalculatePrice(decimal pricePerNight, string bookingType, string? slotType, int count) => pricePerNight * count;
        }

        private class MockCommissionService : ICommissionService
        {
            public Task<(decimal commissionAmount, decimal ownerPayout)> CalculateCommissionAsync(decimal totalBookingAmount)
                => Task.FromResult((totalBookingAmount * 0.1m, totalBookingAmount * 0.9m));

            public Task<(decimal rateSnapshot, decimal commissionAmount, decimal ownerPayout)> CalculateDynamicCommissionAsync(decimal totalBookingAmount, decimal? propertyCommissionRate, int? ownerId = null)
                => Task.FromResult((10m, totalBookingAmount * 0.1m, totalBookingAmount * 0.9m));

            public Task<(decimal rateSnapshot, decimal commissionAmount, decimal managerPayout)> CalculateEventCommissionAsync(decimal eventPackagePrice, int propertyId, int? packageId = null, decimal? propertyEventCommissionOverride = null, int? eventManagerId = null)
                => Task.FromResult((10m, eventPackagePrice * 0.1m, eventPackagePrice * 0.9m));
        }

        private class MockPaymentGateway : IPaymentGatewayService
        {
            public bool ShouldThrowOnCreateOrder = false;

            public Task<PaymentOrderResponse> CreateOrderAsync(decimal amount, string receipt, string currency = "INR")
            {
                if (ShouldThrowOnCreateOrder)
                {
                    throw new System.Net.Http.HttpRequestException("Payment gateway unreachable");
                }
                return Task.FromResult(new PaymentOrderResponse
                {
                    OrderId = "order_test_123",
                    KeyId = "rzp_test_key",
                    Currency = currency,
                    IsSimulated = true
                });
            }

            public Task<bool> VerifyPaymentSignatureAsync(string orderId, string paymentId, string signature)
                => Task.FromResult(!string.IsNullOrWhiteSpace(signature) && signature != "invalid");
        }

        private class MockIdVerificationService : IIdVerificationService
        {
            public Task<int> SubmitVerificationAsync(int clientId, string documentType, string storageKey, string? maskedIdentifier) => Task.FromResult(1);
            public Task<ClientIdVerification?> GetClientVerificationAsync(int clientId, int requestingUserId, string userRole) => Task.FromResult<ClientIdVerification?>(null);
            public Task<List<ClientIdVerification>> GetAllVerificationsAsync(string? status = null) => Task.FromResult(new List<ClientIdVerification>());
            public Task<bool> ReviewVerificationAsync(int verificationId, string action, int staffUserId, string? rejectionReason = null) => Task.FromResult(true);
            public Task<bool> IsClientVerifiedAsync(int clientId) => Task.FromResult(false);
        }

        private static ControllerContext CreateContextWithUser(int userId, string role, string email)
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(ClaimTypes.Role, role),
                new Claim(ClaimTypes.Email, email),
                new Claim(ClaimTypes.Name, "Test User")
            };
            var identity = new ClaimsIdentity(claims, "TestAuth");
            var user = new ClaimsPrincipal(identity);
            return new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = user }
            };
        }

        [Fact]
        public async Task ConfirmBooking_WhenSlotUnavailable_ShowsFriendlyMessage_AndReturnsCheckoutView()
        {
            var propRepo = new MockPropertyRepo();
            var bookingRepo = new MockBookingRepo();
            var userRepo = new MockUserRepo();

            propRepo.Properties[1] = new Property { PropertyID = 1, Title = "Serene Farm", PricePerNight = 5000, Location = "Surat" };
            userRepo.Users[10] = new User { UserID = 10, FullName = "Guest", Role = "Client" };

            // Simulate slot unavailable (e.g. booked already)
            bookingRepo.IsAvailableResponse = false;

            var controller = new BookingController(
                propRepo, bookingRepo, new MockEventPackageRepo(), new MockPricingService(),
                new MockCommissionService(), null!, null!,
                new MockPaymentGateway(), null!, null!,
                userRepo, new MockIdVerificationService(), null!)
            {
                ControllerContext = CreateContextWithUser(10, "Client", "guest@example.com"),
                TempData = new TempDataDictionary(new DefaultHttpContext(), new SimpleTempDataProvider())
            };

            var model = new BookingCheckoutViewModel
            {
                PropertyID = 1,
                BookingType = "FullDay",
                CheckInDate = DateTime.Today.AddDays(2),
                CheckOutDate = DateTime.Today.AddDays(4)
            };

            var result = await controller.ConfirmBooking(model);

            var viewResult = Assert.IsType<ViewResult>(result);
            Assert.Equal("Checkout", viewResult.ViewName);
            Assert.False(controller.ModelState.IsValid);
            Assert.Equal("These dates are no longer available. Please choose different dates.", controller.TempData["Error"]);
            Assert.Empty(bookingRepo.Bookings); // Verified: no booking created
        }

        [Fact]
        public async Task ConfirmBooking_WhenCheckInDateIsInPast_RejectsWithValidationMessage()
        {
            var propRepo = new MockPropertyRepo();
            var bookingRepo = new MockBookingRepo();
            var userRepo = new MockUserRepo();

            propRepo.Properties[1] = new Property { PropertyID = 1, Title = "Serene Farm", PricePerNight = 5000, Location = "Surat" };
            userRepo.Users[10] = new User { UserID = 10, FullName = "Guest", Role = "Client" };

            var controller = new BookingController(
                propRepo, bookingRepo, new MockEventPackageRepo(), new MockPricingService(),
                new MockCommissionService(), null!, null!,
                new MockPaymentGateway(), null!, null!,
                userRepo, new MockIdVerificationService(), null!)
            {
                ControllerContext = CreateContextWithUser(10, "Client", "guest@example.com"),
                TempData = new TempDataDictionary(new DefaultHttpContext(), new SimpleTempDataProvider())
            };

            var model = new BookingCheckoutViewModel
            {
                PropertyID = 1,
                BookingType = "FullDay",
                CheckInDate = DateTime.Today.AddDays(-2), // Past date
                CheckOutDate = DateTime.Today.AddDays(1)
            };

            var result = await controller.ConfirmBooking(model);

            var viewResult = Assert.IsType<ViewResult>(result);
            Assert.Equal("Checkout", viewResult.ViewName);
            Assert.True(controller.ModelState.ContainsKey("CheckInDate"));
            Assert.Empty(bookingRepo.Bookings);
        }

        [Fact]
        public async Task ConfirmBooking_WhenCheckOutBeforeCheckIn_RejectsWithValidationMessage()
        {
            var propRepo = new MockPropertyRepo();
            var bookingRepo = new MockBookingRepo();
            var userRepo = new MockUserRepo();

            propRepo.Properties[1] = new Property { PropertyID = 1, Title = "Serene Farm", PricePerNight = 5000, Location = "Surat" };
            userRepo.Users[10] = new User { UserID = 10, FullName = "Guest", Role = "Client" };

            var controller = new BookingController(
                propRepo, bookingRepo, new MockEventPackageRepo(), new MockPricingService(),
                new MockCommissionService(), null!, null!,
                new MockPaymentGateway(), null!, null!,
                userRepo, new MockIdVerificationService(), null!)
            {
                ControllerContext = CreateContextWithUser(10, "Client", "guest@example.com"),
                TempData = new TempDataDictionary(new DefaultHttpContext(), new SimpleTempDataProvider())
            };

            var model = new BookingCheckoutViewModel
            {
                PropertyID = 1,
                BookingType = "FullDay",
                CheckInDate = DateTime.Today.AddDays(5),
                CheckOutDate = DateTime.Today.AddDays(3) // Earlier than check-in
            };

            var result = await controller.ConfirmBooking(model);

            var viewResult = Assert.IsType<ViewResult>(result);
            Assert.Equal("Checkout", viewResult.ViewName);
            Assert.True(controller.ModelState.ContainsKey("CheckOutDate"));
            Assert.Empty(bookingRepo.Bookings);
        }

        [Fact]
        public async Task ConfirmBooking_WhenValidAndAvailable_RedirectsToPayment()
        {
            var propRepo = new MockPropertyRepo();
            var bookingRepo = new MockBookingRepo();
            var userRepo = new MockUserRepo();

            propRepo.Properties[1] = new Property { PropertyID = 1, Title = "Serene Farm", PricePerNight = 5000, Location = "Surat" };
            userRepo.Users[10] = new User { UserID = 10, FullName = "Guest", Role = "Client" };

            var controller = new BookingController(
                propRepo, bookingRepo, new MockEventPackageRepo(), new MockPricingService(),
                new MockCommissionService(), null!, null!,
                new MockPaymentGateway(), null!, null!,
                userRepo, new MockIdVerificationService(), null!)
            {
                ControllerContext = CreateContextWithUser(10, "Client", "guest@example.com"),
                TempData = new TempDataDictionary(new DefaultHttpContext(), new SimpleTempDataProvider())
            };

            var model = new BookingCheckoutViewModel
            {
                PropertyID = 1,
                BookingType = "FullDay",
                CheckInDate = DateTime.Today.AddDays(2),
                CheckOutDate = DateTime.Today.AddDays(4)
            };

            var result = await controller.ConfirmBooking(model);

            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Payment", redirect.ActionName);
            Assert.Single(bookingRepo.Bookings);
        }

        [Fact]
        public async Task Payment_WhenSlotBecomesUnavailable_HaltsAndRedirectsToCheckoutWithFriendlyMessage()
        {
            var propRepo = new MockPropertyRepo();
            var bookingRepo = new MockBookingRepo();
            var userRepo = new MockUserRepo();

            propRepo.Properties[1] = new Property { PropertyID = 1, Title = "Serene Farm", PricePerNight = 5000, Location = "Surat" };
            userRepo.Users[10] = new User { UserID = 10, FullName = "Guest", Role = "Client" };

            var booking = new Booking
            {
                BookingID = 101,
                PropertyID = 1,
                ClientID = 10,
                BookingType = "FullDay",
                CheckInDate = DateTime.Today.AddDays(2),
                CheckOutDate = DateTime.Today.AddDays(4),
                TotalAmount = 10000,
                Status = "Pending"
            };
            bookingRepo.Bookings[101] = booking;

            // Slot was booked by someone else right before this user opened Payment!
            bookingRepo.IsAvailableResponse = false;

            var controller = new BookingController(
                propRepo, bookingRepo, new MockEventPackageRepo(), new MockPricingService(),
                new MockCommissionService(), null!, null!,
                new MockPaymentGateway(), null!, null!,
                userRepo, new MockIdVerificationService(), null!)
            {
                ControllerContext = CreateContextWithUser(10, "Client", "guest@example.com"),
                TempData = new TempDataDictionary(new DefaultHttpContext(), new SimpleTempDataProvider())
            };

            var result = await controller.Payment(101);

            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Checkout", redirect.ActionName);
            Assert.Equal("These dates are no longer available. Please choose different dates.", controller.TempData["Error"]);
        }

        [Fact]
        public async Task Payment_WhenPaymentSetupFails_CatchesGracefullyAndShowsFriendlyMessage()
        {
            var propRepo = new MockPropertyRepo();
            var bookingRepo = new MockBookingRepo();
            var userRepo = new MockUserRepo();
            var paymentService = new MockPaymentGateway();

            propRepo.Properties[1] = new Property { PropertyID = 1, Title = "Serene Farm", PricePerNight = 5000, Location = "Surat" };
            userRepo.Users[10] = new User { UserID = 10, FullName = "Guest", Role = "Client" };

            var booking = new Booking
            {
                BookingID = 101,
                PropertyID = 1,
                ClientID = 10,
                BookingType = "FullDay",
                CheckInDate = DateTime.Today.AddDays(2),
                CheckOutDate = DateTime.Today.AddDays(4),
                TotalAmount = 10000,
                Status = "Pending"
            };
            bookingRepo.Bookings[101] = booking;

            // Simulate payment gateway failure
            paymentService.ShouldThrowOnCreateOrder = true;

            var controller = new BookingController(
                propRepo, bookingRepo, new MockEventPackageRepo(), new MockPricingService(),
                new MockCommissionService(), null!, null!,
                paymentService, null!, null!,
                userRepo, new MockIdVerificationService(), null!)
            {
                ControllerContext = CreateContextWithUser(10, "Client", "guest@example.com"),
                TempData = new TempDataDictionary(new DefaultHttpContext(), new SimpleTempDataProvider())
            };

            var result = await controller.Payment(101);

            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Checkout", redirect.ActionName);
            Assert.Equal("We were unable to set up the payment gateway at this moment. Please try again shortly or contact our 24/7 support team.", controller.TempData["Error"]);
        }

        [Fact]
        public async Task VerifyPayment_WhenSignatureInvalid_RedirectsToPaymentWithFriendlyMessage()
        {
            var propRepo = new MockPropertyRepo();
            var bookingRepo = new MockBookingRepo();
            var userRepo = new MockUserRepo();

            propRepo.Properties[1] = new Property { PropertyID = 1, Title = "Serene Farm", PricePerNight = 5000, Location = "Surat" };
            userRepo.Users[10] = new User { UserID = 10, FullName = "Guest", Role = "Client" };

            var booking = new Booking
            {
                BookingID = 101,
                PropertyID = 1,
                ClientID = 10,
                Status = "Pending"
            };
            bookingRepo.Bookings[101] = booking;

            var controller = new BookingController(
                propRepo, bookingRepo, new MockEventPackageRepo(), new MockPricingService(),
                new MockCommissionService(), null!, null!,
                new MockPaymentGateway(), null!, null!,
                userRepo, new MockIdVerificationService(), null!)
            {
                ControllerContext = CreateContextWithUser(10, "Client", "guest@example.com"),
                TempData = new TempDataDictionary(new DefaultHttpContext(), new SimpleTempDataProvider())
            };

            var result = await controller.VerifyPayment(101, "order_123", "pay_123", "invalid");

            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Payment", redirect.ActionName);
            Assert.Equal("Payment signature verification failed. Please try again or reach our 24/7 helpdesk.", controller.TempData["Error"]);
        }
    }
}
