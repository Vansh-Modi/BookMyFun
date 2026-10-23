using System.Security.Claims;
using BookMyFun.Controllers;
using BookMyFun.Models.Entities;
using BookMyFun.Models.ViewModels;
using BookMyFun.Repositories;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;

namespace BookMyFun.Tests
{
    public class DedicatedConfirmationPageTests
    {
        private class SimpleTempDataProvider : ITempDataProvider
        {
            public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
            public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
        }

        private class MockBookingRepo : IBookingRepository
        {
            public readonly Dictionary<int, Booking> Bookings = new();
            public readonly Dictionary<int, List<Payment>> Payments = new();

            public Task<Booking?> GetByIdAsync(int bookingId)
            {
                Bookings.TryGetValue(bookingId, out var b);
                return Task.FromResult(b);
            }

            public Task<Booking?> GetBookingByIdAsync(int bookingId) => GetByIdAsync(bookingId);

            public Task<List<Payment>> GetPaymentsByBookingIdAsync(int bookingId)
            {
                if (Payments.TryGetValue(bookingId, out var p)) return Task.FromResult(p);
                return Task.FromResult(new List<Payment>());
            }

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
            public Task<bool> IsSlotAvailableAsync(int propertyId, DateTime checkInDate, DateTime checkOutDate, string bookingType, string? slotType) => Task.FromResult(true);
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
            public Task<int> CreatePropertyAsync(Property property)
            {
                property.PropertyID = Properties.Count + 1;
                Properties[property.PropertyID] = property;
                return Task.FromResult(property.PropertyID);
            }
            public Task<bool> UpdatePropertyAsync(Property property) => Task.FromResult(true);
            public Task<bool> DeletePropertyAsync(int propertyId) => PermanentlyDeletePropertyAsync(propertyId);
            public Task<bool> PermanentlyDeletePropertyAsync(int propertyId) => Task.FromResult(Properties.Remove(propertyId));
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

        private ControllerContext CreateContextWithUser(int userId, string role, string email = "user@bookmyfun.com")
        {
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, userId.ToString()),
                new(ClaimTypes.Name, email),
                new(ClaimTypes.Email, email),
                new(ClaimTypes.Role, role)
            };
            var identity = new ClaimsIdentity(claims, "TestAuth");
            var principal = new ClaimsPrincipal(identity);

            var httpContext = new DefaultHttpContext { User = principal };
            return new ControllerContext { HttpContext = httpContext, RouteData = new RouteData() };
        }

        // Test 82: BookingController.Confirmation returns dedicated ViewModel with metadata and receipt links
        public async Task BookingController_Confirmation_ReturnsDedicatedViewModelWithMetadataAndReceiptLinks()
        {
            var bookingRepo = new MockBookingRepo();
            var propRepo = new MockPropertyRepo();
            var userRepo = new MockUserRepo();

            var host = new User { UserID = 10, FullName = "Vikram Singhania", Email = "vikram@host.com", Phone = "9822001122", Role = "Owner" };
            var guest = new User { UserID = 25, FullName = "Pooja Hegde", Email = "pooja@traveler.com", Phone = "9811223344", Role = "Client" };
            userRepo.Users[10] = host;
            userRepo.Users[25] = guest;

            var prop = new Property
            {
                PropertyID = 8,
                OwnerID = 10,
                Title = "The Glass Manor",
                Location = "Mahabaleshwar, Maharashtra",
                FormattedAddress = "Panchgani Road, Mahabaleshwar",
                MapUrl = "https://maps.google.com/?q=The+Glass+Manor+Mahabaleshwar",
                MaxGuests = 12
            };
            propRepo.Properties[8] = prop;

            var booking = new Booking
            {
                BookingID = 305,
                PropertyID = 8,
                ClientID = 25,
                TotalAmount = 48000.00m,
                EventPackagePriceSnapshot = 8000.00m,
                CheckInDate = DateTime.Today.AddDays(14),
                CheckOutDate = DateTime.Today.AddDays(16),
                BookingType = "FullDay",
                Status = "Confirmed"
            };
            bookingRepo.Bookings[305] = booking;

            var payment = new Payment { PaymentID = 12, BookingID = 305, GatewayTransactionID = "RAZORPAY_pay_glass305", Amount = 48000.00m, PaymentStatus = "Success" };
            bookingRepo.Payments[305] = new List<Payment> { payment };

            var controller = new BookingController(
                propRepo, bookingRepo, null!, null!,
                null!, null!, null!,
                null!, null!, null!,
                userRepo, null!, null!)
            {
                ControllerContext = CreateContextWithUser(25, "Client", "pooja@traveler.com"),
                TempData = new TempDataDictionary(new DefaultHttpContext(), new SimpleTempDataProvider())
            };

            var result = await controller.Confirmation(305);
            if (result is not ViewResult vr)
                throw new Exception($"Expected ViewResult but was {result.GetType().Name}");

            if (vr.Model is not BookingReceiptViewModel vm)
                throw new Exception($"Expected BookingReceiptViewModel model but was {vr.Model?.GetType().Name}");

            if (vm.Booking.BookingID != 305)
                throw new Exception($"Expected BookingID 305 but was {vm.Booking.BookingID}");

            if (vm.Property?.Title != "The Glass Manor")
                throw new Exception($"Expected Property Title 'The Glass Manor' but was '{vm.Property?.Title}'");

            if (vm.PropertyStayGross != 40000.00m)
                throw new Exception($"Expected PropertyStayGross 40000.00 but was {vm.PropertyStayGross}");

            if (vm.EventPackagePrice != 8000.00m)
                throw new Exception($"Expected EventPackagePrice 8000.00 but was {vm.EventPackagePrice}");

            if (vm.TotalPaid != 48000.00m)
                throw new Exception($"Expected TotalPaid 48000.00 but was {vm.TotalPaid}");

            if (vm.PrimaryPayment?.GatewayTransactionID != "RAZORPAY_pay_glass305")
                throw new Exception($"Expected PrimaryPayment transaction ID 'RAZORPAY_pay_glass305' but was '{vm.PrimaryPayment?.GatewayTransactionID}'");
        }

        // Test 83: BookingController.Confirmation IDOR check rejects unauthorized clients
        public async Task BookingController_Confirmation_IDOR_ClientRole_ForbiddenForOtherGuests()
        {
            var bookingRepo = new MockBookingRepo();
            var propRepo = new MockPropertyRepo();
            var userRepo = new MockUserRepo();

            userRepo.Users[10] = new User { UserID = 10, FullName = "Owner", Role = "Owner" };
            userRepo.Users[25] = new User { UserID = 25, FullName = "Legit Guest", Role = "Client" };
            userRepo.Users[88] = new User { UserID = 88, FullName = "Another Guest", Role = "Client" };

            propRepo.Properties[8] = new Property { PropertyID = 8, OwnerID = 10, Title = "The Glass Manor" };
            bookingRepo.Bookings[305] = new Booking { BookingID = 305, PropertyID = 8, ClientID = 25, Status = "Confirmed" };

            var controller = new BookingController(
                propRepo, bookingRepo, null!, null!,
                null!, null!, null!,
                null!, null!, null!,
                userRepo, null!, null!)
            {
                ControllerContext = CreateContextWithUser(88, "Client", "another@traveler.com"),
                TempData = new TempDataDictionary(new DefaultHttpContext(), new SimpleTempDataProvider())
            };

            var result = await controller.Confirmation(305);
            if (result is not ForbidResult)
                throw new Exception($"Expected ForbidResult for unauthorized guest access to confirmation but got {result.GetType().Name}");
        }

        // Test 84: BookingController.Confirmation allows Admin and Employee roles to inspect any booking
        public async Task BookingController_Confirmation_AdminAndEmployee_AllowedToInspectAnyBooking()
        {
            var bookingRepo = new MockBookingRepo();
            var propRepo = new MockPropertyRepo();
            var userRepo = new MockUserRepo();

            userRepo.Users[10] = new User { UserID = 10, FullName = "Owner", Role = "Owner" };
            userRepo.Users[25] = new User { UserID = 25, FullName = "Guest", Role = "Client" };
            userRepo.Users[1] = new User { UserID = 1, FullName = "Operations Admin", Role = "Admin" };

            propRepo.Properties[8] = new Property { PropertyID = 8, OwnerID = 10, Title = "The Glass Manor" };
            bookingRepo.Bookings[305] = new Booking { BookingID = 305, PropertyID = 8, ClientID = 25, Status = "Confirmed" };

            var controller = new BookingController(
                propRepo, bookingRepo, null!, null!,
                null!, null!, null!,
                null!, null!, null!,
                userRepo, null!, null!)
            {
                ControllerContext = CreateContextWithUser(1, "Admin", "admin@bookmyfun.com"),
                TempData = new TempDataDictionary(new DefaultHttpContext(), new SimpleTempDataProvider())
            };

            var result = await controller.Confirmation(305);
            if (result is not ViewResult vr || vr.Model is not BookingReceiptViewModel)
                throw new Exception("Expected ViewResult with BookingReceiptViewModel when Admin inspects booking confirmation");
        }

        // Test 85: Views/Booking/Confirmation.cshtml contains Next Steps roadmap with all four steps
        public void ConfirmationView_ContainsNextStepsRoadmapAndAllFourPhases()
        {
            string viewPath = TestPathHelper.GetPath("Views", "Booking", "Confirmation.cshtml");
            if (!File.Exists(viewPath))
                throw new Exception($"Confirmation view not found at: {viewPath}");

            string content = File.ReadAllText(viewPath);

            if (!content.Contains("next-steps-grid") || !content.Contains("next-step-card"))
                throw new Exception("Views/Booking/Confirmation.cshtml missing next-steps-grid or next-step-card CSS classes");

            if (!content.Contains("1. Confirmation Dispatched"))
                throw new Exception("Views/Booking/Confirmation.cshtml missing Step 1 '1. Confirmation Dispatched'");

            if (!content.Contains("2. Carry Government IDs"))
                throw new Exception("Views/Booking/Confirmation.cshtml missing Step 2 '2. Carry Government IDs'");

            if (!content.Contains("3. Map Your Route"))
                throw new Exception("Views/Booking/Confirmation.cshtml missing Step 3 '3. Map Your Route'");

            if (!content.Contains("4. Gate Pass &amp; Check-In"))
                throw new Exception("Views/Booking/Confirmation.cshtml missing Step 4 '4. Gate Pass &amp; Check-In'");
        }

        // Test 86: Views/Booking/Confirmation.cshtml contains dedicated receipt download hub and action buttons
        public void ConfirmationView_ContainsDedicatedReceiptDownloadHubAndActionButtons()
        {
            string viewPath = TestPathHelper.GetPath("Views", "Booking", "Confirmation.cshtml");
            string content = File.ReadAllText(viewPath);

            if (!content.Contains("receipt-download-hub") || !content.Contains("id=\"receiptDownloadCard\""))
                throw new Exception("Views/Booking/Confirmation.cshtml missing receipt-download-hub or receiptDownloadCard element");

            if (!content.Contains("asp-action=\"Receipt\""))
                throw new Exception("Views/Booking/Confirmation.cshtml missing direct download link to Receipt action");

            if (!content.Contains("window.print()"))
                throw new Exception("Views/Booking/Confirmation.cshtml missing window.print() action for printing entry pass");

            if (!content.Contains("copyReservationSummary()") || !content.Contains("id=\"btnShareReservation\""))
                throw new Exception("Views/Booking/Confirmation.cshtml missing copyReservationSummary() or btnShareReservation button");

            if (!content.Contains("confirm-hero-banner") || !content.Contains("confirm-pulse-icon"))
                throw new Exception("Views/Booking/Confirmation.cshtml missing confirm-hero-banner or confirm-pulse-icon");
        }

        // Test 87: wwwroot/css/site.css contains dedicated confirmation page and roadmap styling
        public void SiteCss_ContainsDedicatedConfirmationPageAndRoadmapStyling()
        {
            string cssPath = TestPathHelper.GetPath("wwwroot", "css", "site.css");
            if (!File.Exists(cssPath))
                throw new Exception($"site.css not found at: {cssPath}");

            string content = File.ReadAllText(cssPath);

            if (!content.Contains(".confirm-hero-banner"))
                throw new Exception("wwwroot/css/site.css missing .confirm-hero-banner class");

            if (!content.Contains(".confirm-pulse-icon"))
                throw new Exception("wwwroot/css/site.css missing .confirm-pulse-icon class");

            if (!content.Contains(".next-steps-grid") || !content.Contains(".next-step-card"))
                throw new Exception("wwwroot/css/site.css missing .next-steps-grid or .next-step-card classes");

            if (!content.Contains(".receipt-download-hub"))
                throw new Exception("wwwroot/css/site.css missing .receipt-download-hub class");
        }
    }
}
