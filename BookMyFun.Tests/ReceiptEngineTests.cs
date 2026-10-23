using System.Security.Claims;
using BookMyFun.Controllers;
using BookMyFun.Models.Entities;
using BookMyFun.Models.ViewModels;
using BookMyFun.Repositories;
using BookMyFun.Services.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;

namespace BookMyFun.Tests
{
    public class ReceiptEngineTests
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

        // Test 76: BookingReceiptViewModel calculations
        public void BookingReceiptViewModel_FinancialCalculations_CorrectlyDerivesStayGrossAndHostNetPayout()
        {
            var vm = new BookingReceiptViewModel
            {
                Booking = new Booking
                {
                    BookingID = 101,
                    TotalAmount = 45000.00m,
                    EventPackagePriceSnapshot = 10000.00m,
                    CommissionRateSnapshot = 10.00m,
                    PlatformCommissionAmount = 3500.00m,
                    OwnerPayoutAmount = 31500.00m,
                    SecurityDepositAmount = 5000.00m,
                    CreatedAt = new DateTime(2026, 10, 23, 10, 30, 0)
                }
            };

            if (vm.PropertyStayGross != 35000.00m)
                throw new Exception($"PropertyStayGross expected 35000.00 but was {vm.PropertyStayGross}");

            if (vm.PlatformCommission != 3500.00m)
                throw new Exception($"PlatformCommission expected 3500.00 but was {vm.PlatformCommission}");

            if (vm.HostNetPayout != 31500.00m)
                throw new Exception($"HostNetPayout expected 31500.00 but was {vm.HostNetPayout}");

            if (vm.EventPackagePrice != 10000.00m)
                throw new Exception($"EventPackagePrice expected 10000.00 but was {vm.EventPackagePrice}");

            if (vm.SecurityDeposit != 5000.00m)
                throw new Exception($"SecurityDeposit expected 5000.00 but was {vm.SecurityDeposit}");

            if (vm.ReceiptNumber != "BMF-REC-000101")
                throw new Exception($"ReceiptNumber expected BMF-REC-000101 but was {vm.ReceiptNumber}");

            if (vm.HostReceiptNumber != "BMF-HST-000101")
                throw new Exception($"HostReceiptNumber expected BMF-HST-000101 but was {vm.HostReceiptNumber}");

            if (vm.TaxInvoiceNumber != "INV-202610-00101")
                throw new Exception($"TaxInvoiceNumber expected INV-202610-00101 but was {vm.TaxInvoiceNumber}");
        }

        // Test 77: BookingController.Receipt returns ViewResult with Guest and Stay details
        public async Task BookingController_Receipt_ReturnsViewModelWithGuestAndStayDetails()
        {
            var bookingRepo = new MockBookingRepo();
            var propRepo = new MockPropertyRepo();
            var userRepo = new MockUserRepo();

            var host = new User { UserID = 10, FullName = "Rajesh Sharma", Email = "rajesh@host.com", Phone = "9820011223", Role = "Owner" };
            var guest = new User { UserID = 20, FullName = "Amit Verma", Email = "amit@traveler.com", Phone = "9899001122", Role = "Client" };
            userRepo.Users[10] = host;
            userRepo.Users[20] = guest;

            var prop = new Property
            {
                PropertyID = 5,
                OwnerID = 10,
                Title = "Whispering Pines Villa",
                Location = "Lonavala, Maharashtra",
                FormattedAddress = "Plot 42, Gold Valley Sector 3, Lonavala",
                MapUrl = "https://maps.google.com/?q=Whispering+Pines+Lonavala"
            };
            propRepo.Properties[5] = prop;

            var booking = new Booking
            {
                BookingID = 101,
                PropertyID = 5,
                ClientID = 20,
                TotalAmount = 25000.00m,
                CheckInDate = DateTime.Today.AddDays(7),
                CheckOutDate = DateTime.Today.AddDays(9),
                Status = "Confirmed"
            };
            bookingRepo.Bookings[101] = booking;

            var payment = new Payment { PaymentID = 1, BookingID = 101, GatewayTransactionID = "RAZORPAY_pay_test101", Amount = 25000.00m, PaymentStatus = "Success" };
            bookingRepo.Payments[101] = new List<Payment> { payment };

            var controller = new BookingController(
                propRepo, bookingRepo, null!, null!,
                null!, null!, null!,
                null!, null!, null!,
                userRepo, null!, null!)
            {
                ControllerContext = CreateContextWithUser(20, "Client", "amit@traveler.com"),
                TempData = new TempDataDictionary(new DefaultHttpContext(), new SimpleTempDataProvider())
            };

            var result = await controller.Receipt(101);
            if (result is not ViewResult vr)
                throw new Exception($"Expected ViewResult but was {result.GetType().Name}");

            if (vr.ViewName != "Receipt")
                throw new Exception($"Expected ViewName 'Receipt' but was '{vr.ViewName}'");

            if (vr.Model is not BookingReceiptViewModel vm)
                throw new Exception($"Expected BookingReceiptViewModel model but was {vr.Model?.GetType().Name}");

            if (vm.Booking.BookingID != 101)
                throw new Exception($"Expected BookingID 101 but was {vm.Booking.BookingID}");

            if (vm.Property?.Title != "Whispering Pines Villa")
                throw new Exception($"Expected PropertyTitle 'Whispering Pines Villa' but was '{vm.Property?.Title}'");

            if (vm.Client?.FullName != "Amit Verma")
                throw new Exception($"Expected ClientName 'Amit Verma' but was '{vm.Client?.FullName}'");

            if (vm.Host?.FullName != "Rajesh Sharma")
                throw new Exception($"Expected HostName 'Rajesh Sharma' but was '{vm.Host?.FullName}'");

            if (vm.PrimaryPayment?.GatewayTransactionID != "RAZORPAY_pay_test101")
                throw new Exception($"Expected GatewayTransactionID 'RAZORPAY_pay_test101' but was '{vm.PrimaryPayment?.GatewayTransactionID}'");
        }

        // Test 78: BookingController.Receipt IDOR check rejects unauthorized clients
        public async Task BookingController_Receipt_IDOR_ClientRole_ForbiddenForOtherGuests()
        {
            var bookingRepo = new MockBookingRepo();
            var propRepo = new MockPropertyRepo();
            var userRepo = new MockUserRepo();

            userRepo.Users[10] = new User { UserID = 10, FullName = "Owner", Role = "Owner" };
            userRepo.Users[20] = new User { UserID = 20, FullName = "Legit Guest", Role = "Client" };
            userRepo.Users[99] = new User { UserID = 99, FullName = "Unauthorized Guest", Role = "Client" };

            propRepo.Properties[5] = new Property { PropertyID = 5, OwnerID = 10, Title = "Private Villa" };
            bookingRepo.Bookings[101] = new Booking { BookingID = 101, PropertyID = 5, ClientID = 20, Status = "Confirmed" };

            var controller = new BookingController(
                propRepo, bookingRepo, null!, null!,
                null!, null!, null!,
                null!, null!, null!,
                userRepo, null!, null!)
            {
                ControllerContext = CreateContextWithUser(99, "Client", "other@traveler.com"),
                TempData = new TempDataDictionary(new DefaultHttpContext(), new SimpleTempDataProvider())
            };

            var result = await controller.Receipt(101);
            if (result is not ForbidResult)
                throw new Exception($"Expected ForbidResult for unauthorized guest access but got {result.GetType().Name}");
        }

        // Test 79: OwnerController.BookingReceipt returns ViewModel with Host Settlement details
        public async Task OwnerController_BookingReceipt_ReturnsViewModelWithHostSettlementDetails()
        {
            var bookingRepo = new MockBookingRepo();
            var propRepo = new MockPropertyRepo();
            var userRepo = new MockUserRepo();

            var host = new User { UserID = 55, FullName = "Karan Johar", Email = "karan@host.com", Role = "Owner" };
            var guest = new User { UserID = 66, FullName = "Simran Kaur", Email = "simran@guest.com", Role = "Client" };
            userRepo.Users[55] = host;
            userRepo.Users[66] = guest;

            var prop = new Property
            {
                PropertyID = 12,
                OwnerID = 55,
                Title = "Heritage Country Estate",
                Location = "Karjat, Maharashtra"
            };
            propRepo.Properties[12] = prop;

            var booking = new Booking
            {
                BookingID = 202,
                PropertyID = 12,
                ClientID = 66,
                TotalAmount = 50000.00m,
                EventPackagePriceSnapshot = 0.00m,
                CommissionRateSnapshot = 10.00m,
                PlatformCommissionAmount = 5000.00m,
                OwnerPayoutAmount = 45000.00m,
                Status = "Confirmed"
            };
            bookingRepo.Bookings[202] = booking;

            var controller = new OwnerController(
                propRepo, userRepo, bookingRepo, null!,
                null!, null!, null!,
                NullLogger<OwnerController>.Instance)
            {
                ControllerContext = CreateContextWithUser(55, "Owner", "karan@host.com"),
                TempData = new TempDataDictionary(new DefaultHttpContext(), new SimpleTempDataProvider())
            };

            var result = await controller.BookingReceipt(202);
            if (result is not ViewResult vr)
                throw new Exception($"Expected ViewResult but was {result.GetType().Name}");

            if (vr.Model is not BookingReceiptViewModel vm)
                throw new Exception($"Expected BookingReceiptViewModel but was {vr.Model?.GetType().Name}");

            if (!vm.IsOwnerView)
                throw new Exception("Expected IsOwnerView to be true for Host Settlement receipt");

            if (vm.HostNetPayout != 45000.00m)
                throw new Exception($"Expected HostNetPayout 45000.00 but was {vm.HostNetPayout}");

            if (vm.PlatformCommission != 5000.00m)
                throw new Exception($"Expected PlatformCommission 5000.00 but was {vm.PlatformCommission}");
        }

        // Test 80: OwnerController.BookingReceipt IDOR check throws ForbiddenException for other hosts
        public async Task OwnerController_BookingReceipt_IDOR_OwnerRole_ThrowsForbiddenExceptionForOtherHosts()
        {
            var bookingRepo = new MockBookingRepo();
            var propRepo = new MockPropertyRepo();
            var userRepo = new MockUserRepo();

            userRepo.Users[55] = new User { UserID = 55, FullName = "Legit Host", Role = "Owner" };
            userRepo.Users[99] = new User { UserID = 99, FullName = "Unauthorized Host", Role = "Owner" };

            propRepo.Properties[12] = new Property { PropertyID = 12, OwnerID = 55, Title = "Heritage Country Estate" };
            bookingRepo.Bookings[202] = new Booking { BookingID = 202, PropertyID = 12, ClientID = 66, Status = "Confirmed" };

            var controller = new OwnerController(
                propRepo, userRepo, bookingRepo, null!,
                null!, null!, null!,
                NullLogger<OwnerController>.Instance)
            {
                ControllerContext = CreateContextWithUser(99, "Owner", "unauth@host.com"),
                TempData = new TempDataDictionary(new DefaultHttpContext(), new SimpleTempDataProvider())
            };

            bool caughtForbidden = false;
            try
            {
                await controller.BookingReceipt(202);
            }
            catch (ForbiddenException)
            {
                caughtForbidden = true;
            }

            if (!caughtForbidden)
                throw new Exception("Expected ForbiddenException when host accesses receipt for property they do not own, but no exception was thrown");
        }

        // Test 81: Views contain print rules, receipt card classes, and direct navigation links
        public void Views_ReceiptsAndDashboards_DeclarePrintRulesAndDirectActionLinks()
        {
            string receiptViewPath = TestPathHelper.GetPath("Views", "Booking", "Receipt.cshtml");
            if (!File.Exists(receiptViewPath))
                throw new Exception($"Receipt view not found at: {receiptViewPath}");

            string receiptViewContent = File.ReadAllText(receiptViewPath);
            if (!receiptViewContent.Contains("receipt-card") || !receiptViewContent.Contains("TaxInvoiceNumber") || !receiptViewContent.Contains("window.print()"))
                throw new Exception("Views/Booking/Receipt.cshtml missing receipt-card, TaxInvoiceNumber, or window.print()");

            string confirmationViewPath = TestPathHelper.GetPath("Views", "Booking", "Confirmation.cshtml");
            string confirmationContent = File.ReadAllText(confirmationViewPath);
            if (!confirmationContent.Contains("receipt-card") || !confirmationContent.Contains("BookingReceiptViewModel") || !confirmationContent.Contains("Full Tax Receipt"))
                throw new Exception("Views/Booking/Confirmation.cshtml missing receipt-card, BookingReceiptViewModel, or Full Tax Receipt link");

            string hostReceiptViewPath = TestPathHelper.GetPath("Views", "Owner", "BookingReceipt.cshtml");
            if (!File.Exists(hostReceiptViewPath))
                throw new Exception($"Host receipt view not found at: {hostReceiptViewPath}");

            string hostReceiptContent = File.ReadAllText(hostReceiptViewPath);
            if (!hostReceiptContent.Contains("receipt-header-host") || !hostReceiptContent.Contains("Host Financial Settlement Statement") || !hostReceiptContent.Contains("HostNetPayout"))
                throw new Exception("Views/Owner/BookingReceipt.cshtml missing receipt-header-host or Host Financial Settlement Statement");

            string clientIndexPath = TestPathHelper.GetPath("Views", "Client", "Index.cshtml");
            string clientIndexContent = File.ReadAllText(clientIndexPath);
            if (!clientIndexContent.Contains("asp-action=\"Receipt\""))
                throw new Exception("Views/Client/Index.cshtml missing direct link to Receipt action");

            string ownerIndexPath = TestPathHelper.GetPath("Views", "Owner", "Index.cshtml");
            string ownerIndexContent = File.ReadAllText(ownerIndexPath);
            if (!ownerIndexContent.Contains("asp-action=\"BookingReceipt\""))
                throw new Exception("Views/Owner/Index.cshtml missing direct link to BookingReceipt action");

            string siteCssPath = TestPathHelper.GetPath("wwwroot", "css", "site.css");
            string siteCssContent = File.ReadAllText(siteCssPath);
            if (!siteCssContent.Contains("@media print") || !siteCssContent.Contains(".receipt-card") || !siteCssContent.Contains(".receipt-header-host"))
                throw new Exception("wwwroot/css/site.css missing @media print or receipt styling classes");
        }
    }
}
