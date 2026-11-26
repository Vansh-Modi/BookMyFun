using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using BookMyFun.Controllers;
using BookMyFun.Models.Entities;
using BookMyFun.Models.Validation;
using BookMyFun.Models.ViewModels;
using BookMyFun.Repositories;
using BookMyFun.Services;
using BookMyFun.Services.Checkout;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Xunit;

namespace BookMyFun.Tests
{
    public class BookingDocumentValidationTests
    {
        private class SimpleTempDataProvider : ITempDataProvider
        {
            public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
            public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
        }

        private class DummyFormFile : IFormFile
        {
            private readonly byte[] _bytes;
            public DummyFormFile(byte[] bytes, string fileName, string contentType)
            {
                _bytes = bytes;
                FileName = fileName;
                ContentType = contentType;
                Length = bytes.Length;
            }

            public string ContentType { get; set; }
            public string ContentDisposition => $"form-data; name=\"IdDocumentFile\"; filename=\"{FileName}\"";
            public IHeaderDictionary Headers => new HeaderDictionary();
            public long Length { get; set; }
            public string Name => "IdDocumentFile";
            public string FileName { get; set; }

            public void CopyTo(Stream target) => new MemoryStream(_bytes).CopyTo(target);
            public Task CopyToAsync(Stream target, CancellationToken cancellationToken = default) => new MemoryStream(_bytes).CopyToAsync(target, cancellationToken);
            public Stream OpenReadStream() => new MemoryStream(_bytes);
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
            public Task<bool> IsSlotAvailableAsync(int propertyId, DateTime checkInDate, DateTime checkOutDate, string bookingType, string? slotType) => Task.FromResult(IsAvailableResponse);
            public Task<bool> IsSlotAvailableAsync(int propertyId, DateTime checkInDate, DateTime checkOutDate, string bookingType, string? slotType, int? excludeBookingId) => Task.FromResult(IsAvailableResponse);
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
            public Task<List<User>> GetDecoratorsAsync(bool onlyVerified = false) => Task.FromResult(new List<User>());
            public Task<List<string>> GetAllDistinctPropertyLocationsAsync() => Task.FromResult(new List<string>());
            public Task<string?> GetEventManagerServiceAreasAsync(int eventManagerUserId) => Task.FromResult<string?>(null);
            public Task<bool> UpdateEventManagerServiceAreasAsync(int eventManagerUserId, string serviceAreas) => Task.FromResult(true);
            public Task<bool> IsAreaServicedByAnyEventManagerAsync(string? propertyLocation) => Task.FromResult(false);
            public Task<List<EventPackage>> GetPackagesByUserIdAsync(int userId) => Task.FromResult(new List<EventPackage>());
            public Task<List<EventManagerPortfolio>> GetPortfoliosByUserIdAsync(int userId, bool onlyApproved = true) => Task.FromResult(new List<EventManagerPortfolio>());
            public Task<int> AddPortfolioItemAsync(EventManagerPortfolio item) => Task.FromResult(1);
            public Task<List<EventManagerPortfolio>> GetAllPortfoliosAsync() => Task.FromResult(new List<EventManagerPortfolio>());
            public Task<bool> ApprovePortfolioItemAsync(int portfolioId, bool isApproved) => Task.FromResult(true);
            public Task<bool> DeletePortfolioItemAsync(int portfolioId, int userId) => Task.FromResult(true);
            public Task<List<FlaggedContactAttempt>> GetFlaggedContactAttemptsAsync() => Task.FromResult(new List<FlaggedContactAttempt>());
            public Task<bool> UpdateFlaggedContactAttemptStatusAsync(int flagId, string status) => Task.FromResult(true);
        }

        private class MockMediaStorageService : IMediaStorageService
        {
            public List<string> SavedDocuments = new();
            public Task<(string publicUrl, string publicId)> SaveMediaFileAsync(IFormFile file, string mediaType) => Task.FromResult(("/uploads/file.jpg", "file_123"));
            public Task<string> SavePrivateDocumentAsync(IFormFile file, int clientId)
            {
                string key = $"doc_client_{clientId}_{Guid.NewGuid():N}.pdf";
                SavedDocuments.Add(key);
                return Task.FromResult(key);
            }
            public Task<bool> ValidateExternalUrlAsync(string url) => Task.FromResult(true);
            public string GenerateExpiringDocumentToken(string documentKey, int requestingUserId, string userRole, int expirationMinutes = 15) => "token";
            public bool ValidateExpiringDocumentToken(string documentKey, string token, int requestingUserId, string userRole) => true;
            public string GetPrivateDocumentPhysicalPath(string documentKey) => Path.Combine(Path.GetTempPath(), documentKey);
        }

        private class MockIdVerificationService : IIdVerificationService
        {
            public Task<int> SubmitVerificationAsync(int clientId, string documentType, string storageKey, string? maskedIdentifier) => Task.FromResult(1);
            public Task<ClientIdVerification?> GetClientVerificationAsync(int clientId, int requestingUserId, string userRole) => Task.FromResult<ClientIdVerification?>(null);
            public Task<List<ClientIdVerification>> GetAllVerificationsAsync(string? status = null) => Task.FromResult(new List<ClientIdVerification>());
            public Task<bool> ReviewVerificationAsync(int verificationId, string action, int staffUserId, string? rejectionReason = null) => Task.FromResult(true);
            public Task<bool> IsClientVerifiedAsync(int clientId) => Task.FromResult(false);
        }

        private class MockPaymentGateway : IPaymentGatewayService
        {
            public Task<PaymentOrderResponse> CreateOrderAsync(decimal amount, string receipt, string currency = "INR") =>
                Task.FromResult(new PaymentOrderResponse { OrderId = "order_123", KeyId = "key_123", Currency = currency, IsSimulated = true });
            public Task<bool> VerifyPaymentSignatureAsync(string orderId, string paymentId, string signature) => Task.FromResult(true);
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

        private class MockPricingService : IBookingPricingService
        {
            public decimal CalculatePrice(decimal pricePerNight, string bookingType, string? slotType, int count) => pricePerNight * count;
        }

        private class MockCalendarRangeService : ICalendarRangeService
        {
            public Task<List<string>> GetBlockedDatesForPropertyAsync(int propertyId) => Task.FromResult(new List<string>());
            public CalendarValidationResult ValidateAndCalculate(DateTime checkIn, DateTime checkOut, int minStayNights, int maxStayNights, decimal basePricePerNight, string bookingType = "FullDay", string? slotType = null, int? hourlyDuration = null, decimal eventPackagePrice = 0, IEnumerable<string>? blockedDatesIso = null) =>
                new CalendarValidationResult { IsValid = true, CalculatedPrice = basePricePerNight, Nights = 1 };
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
            return new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
            };
        }

        private static BookingController CreateController(MockPropertyRepo propRepo, MockBookingRepo bookingRepo, MockUserRepo userRepo, MockMediaStorageService mediaStorage, MockIdVerificationService? idVerificationService = null)
        {
            return new BookingController(
                propRepo,
                bookingRepo,
                new MockEventPackageRepo(),
                new MockPricingService(),
                new MockCommissionService(),
                null!,
                null!,
                new MockPaymentGateway(),
                null!,
                null!,
                userRepo,
                idVerificationService ?? new MockIdVerificationService(),
                mediaStorage,
                null,
                null,
                null,
                new MockCalendarRangeService())
            {
                ControllerContext = CreateContextWithUser(10, "Client", "client@example.com"),
                TempData = new TempDataDictionary(new DefaultHttpContext(), new SimpleTempDataProvider())
            };
        }

        // =========================================================================
        // 1. Model / DTO Validation Tests (IValidatableObject & RequiredDocumentFile)
        // =========================================================================

        [Fact]
        public void ModelValidation_WhenDocumentNumberAndPhoneProvided_WithoutFile_FailsValidation()
        {
            var model = new BookingCheckoutViewModel
            {
                GuestFullName = "Vansh Sharma",
                GuestPhone = "9876543210",
                DocumentType = "Aadhaar",
                IdDocumentNumber = "123456789012",
                IdDocumentFile = null
            };

            var context = new ValidationContext(model);
            var results = new List<ValidationResult>();
            bool isValid = Validator.TryValidateObject(model, context, results, validateAllProperties: true);

            Assert.False(isValid);
            Assert.Contains(results, r => r.MemberNames.Contains("IdDocumentFile") && r.ErrorMessage!.Contains("Please upload a copy of your document"));
        }

        [Fact]
        public void ModelValidation_WhenValidPdfProvided_PassesValidation()
        {
            var model = new BookingCheckoutViewModel
            {
                GuestFullName = "Vansh Sharma",
                GuestPhone = "9876543210",
                DocumentType = "Aadhaar",
                IdDocumentNumber = "123456789012",
                IdDocumentFile = new DummyFormFile(new byte[] { 0x25, 0x50, 0x44, 0x46 }, "document.pdf", "application/pdf")
            };

            var context = new ValidationContext(model);
            var results = new List<ValidationResult>();
            bool isValid = Validator.TryValidateObject(model, context, results, validateAllProperties: true);

            Assert.True(isValid);
            Assert.Empty(results);
        }

        [Fact]
        public void ModelValidation_WhenWrongFileTypeProvided_FailsValidation()
        {
            var model = new BookingCheckoutViewModel
            {
                GuestFullName = "Vansh Sharma",
                GuestPhone = "9876543210",
                DocumentType = "Aadhaar",
                IdDocumentNumber = "123456789012",
                IdDocumentFile = new DummyFormFile(new byte[] { 1, 2, 3 }, "payload.exe", "application/x-msdownload")
            };

            var context = new ValidationContext(model);
            var results = new List<ValidationResult>();
            bool isValid = Validator.TryValidateObject(model, context, results, validateAllProperties: true);

            Assert.False(isValid);
            Assert.Contains(results, r => r.MemberNames.Contains("IdDocumentFile") && r.ErrorMessage!.Contains("Only JPG, JPEG, PNG, and PDF files are accepted"));
        }

        [Fact]
        public void ModelValidation_WhenOversizedFileProvided_FailsValidation()
        {
            var model = new BookingCheckoutViewModel
            {
                GuestFullName = "Vansh Sharma",
                GuestPhone = "9876543210",
                DocumentType = "Aadhaar",
                IdDocumentNumber = "123456789012",
                IdDocumentFile = new DummyFormFile(new byte[10], "huge.pdf", "application/pdf")
                {
                    Length = 6 * 1024 * 1024 // 6MB > 5MB
                }
            };

            var context = new ValidationContext(model);
            var results = new List<ValidationResult>();
            bool isValid = Validator.TryValidateObject(model, context, results, validateAllProperties: true);

            Assert.False(isValid);
            Assert.Contains(results, r => r.MemberNames.Contains("IdDocumentFile") && r.ErrorMessage!.Contains("5MB"));
        }

        [Fact]
        public void ModelValidation_WhenExistingDocumentUnchanged_PassesWithoutNewFile()
        {
            var model = new BookingCheckoutViewModel
            {
                GuestFullName = "Vansh Sharma",
                GuestPhone = "9876543210",
                DocumentType = "Aadhaar",
                IdDocumentNumber = "123456789012",
                ExistingDocumentKey = "vault/doc_10.pdf",
                ExistingDocumentNumber = "123456789012",
                IsClientAlreadyVerified = true,
                IdDocumentFile = null
            };

            var context = new ValidationContext(model);
            var results = new List<ValidationResult>();
            bool isValid = Validator.TryValidateObject(model, context, results, validateAllProperties: true);

            Assert.True(isValid);
        }

        [Fact]
        public void ModelValidation_WhenAlreadyVerified_DoesNotRequireNewFile()
        {
            var model = new BookingCheckoutViewModel
            {
                GuestFullName = "Vansh Sharma",
                GuestPhone = "9876543210",
                DocumentType = "Aadhaar",
                IsClientAlreadyVerified = true,
                IdDocumentFile = null
            };

            var context = new ValidationContext(model);
            var results = new List<ValidationResult>();
            bool isValid = Validator.TryValidateObject(model, context, results, validateAllProperties: true);

            Assert.True(isValid);
        }

        // =========================================================================
        // 2. Controller Action Tests (ConfirmBooking)
        // =========================================================================

        [Fact]
        public async Task ConfirmBooking_DocumentNumberAndPhone_WithNoFile_FailsAndReturnsCheckoutView()
        {
            var propRepo = new MockPropertyRepo();
            var bookingRepo = new MockBookingRepo();
            var userRepo = new MockUserRepo();
            var mediaStorage = new MockMediaStorageService();

            propRepo.Properties[1] = new Property { PropertyID = 1, Title = "Hillside Villa", Location = "Lonavala", PricePerNight = 8000 };
            userRepo.Users[10] = new User { UserID = 10, FullName = "Vansh Sharma", Role = "Client" };

            var controller = CreateController(propRepo, bookingRepo, userRepo, mediaStorage);

            var model = new BookingCheckoutViewModel
            {
                PropertyID = 1,
                BookingType = "FullDay",
                CheckInDate = DateTime.Today.AddDays(2),
                CheckOutDate = DateTime.Today.AddDays(4),
                GuestFullName = "Vansh Sharma",
                GuestPhone = "9876543210",
                DocumentType = "Aadhaar",
                IdDocumentNumber = "123456789012",
                IdDocumentFile = null // MISSING FILE
            };

            var result = await controller.ConfirmBooking(model);

            // Assertions: Validation must fail, return to Checkout view, preserve values, save no booking
            Assert.False(controller.ModelState.IsValid);
            Assert.True(controller.ModelState.ContainsKey("IdDocumentFile"));
            Assert.Contains("Please upload a copy of your document", controller.ModelState["IdDocumentFile"]!.Errors[0].ErrorMessage);

            var viewResult = Assert.IsType<ViewResult>(result);
            Assert.Equal("Checkout", viewResult.ViewName);
            var returnedModel = Assert.IsType<BookingCheckoutViewModel>(viewResult.Model);
            Assert.Equal("123456789012", returnedModel.IdDocumentNumber);
            Assert.Equal("9876543210", returnedModel.GuestPhone);
            Assert.Equal("Vansh Sharma", returnedModel.GuestFullName);

            Assert.Empty(bookingRepo.Bookings); // No booking saved
            Assert.Empty(mediaStorage.SavedDocuments); // No file saved
        }

        [Fact]
        public async Task ConfirmBooking_WithValidDocumentFile_SucceedsAndRedirectsToPayment()
        {
            var propRepo = new MockPropertyRepo();
            var bookingRepo = new MockBookingRepo();
            var userRepo = new MockUserRepo();
            var mediaStorage = new MockMediaStorageService();

            propRepo.Properties[1] = new Property { PropertyID = 1, Title = "Hillside Villa", Location = "Lonavala", PricePerNight = 8000 };
            userRepo.Users[10] = new User { UserID = 10, FullName = "Vansh Sharma", Role = "Client" };

            var controller = CreateController(propRepo, bookingRepo, userRepo, mediaStorage);

            var model = new BookingCheckoutViewModel
            {
                PropertyID = 1,
                BookingType = "FullDay",
                CheckInDate = DateTime.Today.AddDays(2),
                CheckOutDate = DateTime.Today.AddDays(4),
                GuestFullName = "Vansh Sharma",
                GuestPhone = "9876543210",
                DocumentType = "Aadhaar",
                IdDocumentNumber = "123456789012",
                IdDocumentFile = new DummyFormFile(new byte[] { 0x25, 0x50, 0x44, 0x46 }, "aadhaar.pdf", "application/pdf")
            };

            var result = await controller.ConfirmBooking(model);

            // Assertions: Valid submission proceeds to payment and records booking & vault doc
            Assert.True(controller.ModelState.IsValid);
            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Payment", redirect.ActionName);

            Assert.Single(bookingRepo.Bookings);
            Assert.Single(mediaStorage.SavedDocuments);
        }

        [Fact]
        public async Task ConfirmBooking_WithWrongFileType_FailsValidationAndDoesNotProceed()
        {
            var propRepo = new MockPropertyRepo();
            var bookingRepo = new MockBookingRepo();
            var userRepo = new MockUserRepo();
            var mediaStorage = new MockMediaStorageService();

            propRepo.Properties[1] = new Property { PropertyID = 1, Title = "Hillside Villa", Location = "Lonavala", PricePerNight = 8000 };
            userRepo.Users[10] = new User { UserID = 10, FullName = "Vansh Sharma", Role = "Client" };

            var controller = CreateController(propRepo, bookingRepo, userRepo, mediaStorage);

            var model = new BookingCheckoutViewModel
            {
                PropertyID = 1,
                BookingType = "FullDay",
                CheckInDate = DateTime.Today.AddDays(2),
                CheckOutDate = DateTime.Today.AddDays(4),
                GuestFullName = "Vansh Sharma",
                GuestPhone = "9876543210",
                DocumentType = "Aadhaar",
                IdDocumentNumber = "123456789012",
                IdDocumentFile = new DummyFormFile(new byte[] { 1, 2, 3 }, "doc.exe", "application/x-msdownload")
            };

            var result = await controller.ConfirmBooking(model);

            Assert.False(controller.ModelState.IsValid);
            Assert.True(controller.ModelState.ContainsKey("IdDocumentFile"));
            Assert.Contains("Only JPG, JPEG, PNG, and PDF files are accepted", controller.ModelState["IdDocumentFile"]!.Errors[0].ErrorMessage);

            var viewResult = Assert.IsType<ViewResult>(result);
            Assert.Equal("Checkout", viewResult.ViewName);
            Assert.Empty(bookingRepo.Bookings);
            Assert.Empty(mediaStorage.SavedDocuments);
        }

        [Fact]
        public async Task ConfirmBooking_WithOversizedFile_FailsValidationAndDoesNotProceed()
        {
            var propRepo = new MockPropertyRepo();
            var bookingRepo = new MockBookingRepo();
            var userRepo = new MockUserRepo();
            var mediaStorage = new MockMediaStorageService();

            propRepo.Properties[1] = new Property { PropertyID = 1, Title = "Hillside Villa", Location = "Lonavala", PricePerNight = 8000 };
            userRepo.Users[10] = new User { UserID = 10, FullName = "Vansh Sharma", Role = "Client" };

            var controller = CreateController(propRepo, bookingRepo, userRepo, mediaStorage);

            var model = new BookingCheckoutViewModel
            {
                PropertyID = 1,
                BookingType = "FullDay",
                CheckInDate = DateTime.Today.AddDays(2),
                CheckOutDate = DateTime.Today.AddDays(4),
                GuestFullName = "Vansh Sharma",
                GuestPhone = "9876543210",
                DocumentType = "Aadhaar",
                IdDocumentNumber = "123456789012",
                IdDocumentFile = new DummyFormFile(new byte[10], "huge.pdf", "application/pdf")
                {
                    Length = 6 * 1024 * 1024 // 6MB
                }
            };

            var result = await controller.ConfirmBooking(model);

            Assert.False(controller.ModelState.IsValid);
            Assert.True(controller.ModelState.ContainsKey("IdDocumentFile"));
            Assert.Contains("5MB", controller.ModelState["IdDocumentFile"]!.Errors[0].ErrorMessage);

            var viewResult = Assert.IsType<ViewResult>(result);
            Assert.Equal("Checkout", viewResult.ViewName);
            Assert.Empty(bookingRepo.Bookings);
            Assert.Empty(mediaStorage.SavedDocuments);
        }

        [Fact]
        public async Task ConfirmBooking_WhenExistingDocumentOnRecord_AndDocumentNumberUnchanged_PassesWithoutNewFile()
        {
            var propRepo = new MockPropertyRepo();
            var bookingRepo = new MockBookingRepo();
            var userRepo = new MockUserRepo();
            var mediaStorage = new MockMediaStorageService();

            propRepo.Properties[1] = new Property { PropertyID = 1, Title = "Hillside Villa", Location = "Lonavala", PricePerNight = 8000 };
            userRepo.Users[10] = new User { UserID = 10, FullName = "Vansh Sharma", Role = "Client" };

            var controller = CreateController(propRepo, bookingRepo, userRepo, mediaStorage);

            var model = new BookingCheckoutViewModel
            {
                PropertyID = 1,
                BookingType = "FullDay",
                CheckInDate = DateTime.Today.AddDays(2),
                CheckOutDate = DateTime.Today.AddDays(4),
                GuestFullName = "Vansh Sharma",
                GuestPhone = "9876543210",
                DocumentType = "Aadhaar",
                IdDocumentNumber = "1234 5678 9012",
                ExistingDocumentKey = "vault/doc_10.pdf",
                ExistingDocumentNumber = "123456789012",
                IsClientAlreadyVerified = true,
                IdDocumentFile = null
            };

            var result = await controller.ConfirmBooking(model);

            Assert.True(controller.ModelState.IsValid);
            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Payment", redirect.ActionName);
            Assert.Single(bookingRepo.Bookings);
        }

        [Fact]
        public async Task ConfirmBooking_WhenExistingDocumentOnRecord_ButDocumentNumberChanged_FailsWithoutNewFile()
        {
            var propRepo = new MockPropertyRepo();
            var bookingRepo = new MockBookingRepo();
            var userRepo = new MockUserRepo();
            var mediaStorage = new MockMediaStorageService();

            propRepo.Properties[1] = new Property { PropertyID = 1, Title = "Hillside Villa", Location = "Lonavala", PricePerNight = 8000 };
            userRepo.Users[10] = new User { UserID = 10, FullName = "Vansh Sharma", Phone = "9876543210", Role = "Client" };

            var controller = CreateController(propRepo, bookingRepo, userRepo, mediaStorage);

            // 1. Checkout GET prefills GuestFullName and GuestPhone from logged-in user's profile
            var getRes = await controller.Checkout(propertyId: 1);
            var getView = Assert.IsType<ViewResult>(getRes);
            var getVm = Assert.IsType<BookingCheckoutViewModel>(getView.Model);
            Assert.Equal("Vansh Sharma", getVm.GuestFullName);
            Assert.Equal("9876543210", getVm.GuestPhone);

            // 2. ConfirmBooking when already verified succeeds without requiring a new file upload
            var model = new BookingCheckoutViewModel
            {
                PropertyID = 1,
                BookingType = "FullDay",
                CheckInDate = DateTime.Today.AddDays(2),
                CheckOutDate = DateTime.Today.AddDays(4),
                GuestFullName = "Vansh Sharma",
                GuestPhone = "9876543210",
                DocumentType = "Aadhaar",
                IsClientAlreadyVerified = true,
                IdDocumentFile = null
            };

            var result = await controller.ConfirmBooking(model);

            Assert.True(controller.ModelState.IsValid);
            Assert.IsType<RedirectToActionResult>(result);
            Assert.Single(bookingRepo.Bookings);
            Assert.Empty(mediaStorage.SavedDocuments);
        }

        [Fact]
        public void CheckoutView_RemovesDocumentNumberField_AndIncludesMaskingGuidance()
        {
            var path = TestPathHelper.GetPath("Views", "Booking", "Checkout.cshtml");
            var html = System.IO.File.ReadAllText(path);

            Assert.DoesNotContain("IdDocumentNumber", html);
            Assert.Contains("Upload a copy with the first 8 digits masked. Only the last 4 digits should be visible.", html);
            Assert.Contains("Driving Licence", html);
            Assert.Contains("Voter ID", html);
            Assert.Contains("PAN", html);
            Assert.Contains("Passport", html);
        }
    }
}
