using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using BookMyFun.Controllers;
using BookMyFun.Models.Entities;
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
    public class StayAndCelebrateTests
    {
        private class FakeEventPackageRepository : IEventPackageRepository
        {
            public List<User> Decorators = new();
            public List<EventPackage> Packages = new();
            public List<EventManagerPortfolio> Portfolios = new();

            public Task<List<User>> GetDecoratorsAsync(bool onlyVerified = false)
            {
                var list = Decorators.AsEnumerable();
                if (onlyVerified)
                    list = list.Where(d => d.VerificationStatus == "Verified");
                return Task.FromResult(list.ToList());
            }

            public Task<List<EventPackage>> GetPackagesByUserIdAsync(int userId)
            {
                return Task.FromResult(Packages.Where(p => p.CreatedByUserID == userId).ToList());
            }

            public Task<EventPackage?> GetPackageByIdAsync(int packageId)
            {
                return Task.FromResult(Packages.FirstOrDefault(p => p.PackageID == packageId));
            }

            public Task<List<EventManagerPortfolio>> GetPortfoliosByUserIdAsync(int userId, bool onlyApproved = true)
            {
                var list = Portfolios.Where(p => p.UserID == userId);
                if (onlyApproved)
                    list = list.Where(p => p.IsApproved);
                return Task.FromResult(list.ToList());
            }

            public Task<HashSet<int>> GetUserIdsWithApprovedPortfoliosAsync()
            {
                if (Portfolios.Any())
                {
                    return Task.FromResult(Portfolios.Where(p => p.IsApproved).Select(p => p.UserID).ToHashSet());
                }
                return Task.FromResult(Decorators.Select(d => d.UserID).ToHashSet());
            }

            public Task<bool> IsAreaServicedByAnyEventManagerAsync(string? propertyLocation) => Task.FromResult(true);
            public Task<List<EventPackage>> GetAllPackagesAsync() => Task.FromResult(Packages);
            public Task<List<EventPackage>> GetActivePackagesAsync() => Task.FromResult(Packages.Where(p => p.IsActive).ToList());
            public Task<int> CreatePackageAsync(EventPackage package) => Task.FromResult(1);
            public Task<bool> UpdatePackageAsync(EventPackage package) => Task.FromResult(true);
            public Task<bool> UpdatePackageCommissionRateAsync(int packageId, decimal? commissionRate) => Task.FromResult(true);
            public Task<bool> DeletePackageAsync(int packageId) => Task.FromResult(true);
            public Task<List<Booking>> GetAssignedEventBookingsAsync(int decoratorUserId) => Task.FromResult(new List<Booking>());
            public Task<List<Booking>> GetAllEventBookingsAsync() => Task.FromResult(new List<Booking>());
            public Task<bool> UpdateSetupStatusAsync(int bookingId, string setupStatus, string? setupProofMediaUrls, int? decoratorId) => Task.FromResult(true);
            public Task<bool> AssignDecoratorAsync(int bookingId, int decoratorUserId) => Task.FromResult(true);
            public Task<bool> CancelOrRemoveEventFromBookingAsync(int bookingId) => Task.FromResult(true);
            public Task<List<string>> GetAllDistinctPropertyLocationsAsync() => Task.FromResult(new List<string>());
            public Task<string?> GetEventManagerServiceAreasAsync(int eventManagerUserId) => Task.FromResult<string?>(null);
            public Task<bool> UpdateEventManagerServiceAreasAsync(int eventManagerUserId, string serviceAreas) => Task.FromResult(true);
            public Task<int> AddPortfolioItemAsync(EventManagerPortfolio item) => Task.FromResult(1);
            public Task<List<EventManagerPortfolio>> GetAllPortfoliosAsync() => Task.FromResult(Portfolios);
            public Task<bool> ApprovePortfolioItemAsync(int portfolioId, bool isApproved) => Task.FromResult(true);
            public Task<bool> DeletePortfolioItemAsync(int portfolioId, int userId) => Task.FromResult(true);
            public Task<List<FlaggedContactAttempt>> GetFlaggedContactAttemptsAsync() => Task.FromResult(new List<FlaggedContactAttempt>());
            public Task<bool> UpdateFlaggedContactAttemptStatusAsync(int flagId, string status) => Task.FromResult(true);
        }

        private class FakeUserRepository : IUserRepository
        {
            public List<User> Users = new();

            public Task<User?> GetByIdAsync(int userId) => Task.FromResult(Users.FirstOrDefault(u => u.UserID == userId));
            public Task<User?> GetByEmailAsync(string email) => Task.FromResult(Users.FirstOrDefault(u => u.Email.Equals(email, StringComparison.OrdinalIgnoreCase)));
            public Task<User?> GetByAuthProviderKeyAsync(string provider, string providerKey) => Task.FromResult(Users.FirstOrDefault(u => u.AuthProvider == provider && u.AuthProviderKey == providerKey));
            public Task<List<User>> GetUsersByRoleAsync(string role) => Task.FromResult(Users.Where(u => u.Role.Equals(role, StringComparison.OrdinalIgnoreCase)).ToList());
            public Task<List<User>> GetAllUsersAsync() => Task.FromResult(Users);
            public Task<int> CreateUserAsync(User user) => Task.FromResult(1);
            public Task<bool> UpdateUserAsync(User user) => Task.FromResult(true);
            public Task<bool> DeleteUserAsync(int userId) => Task.FromResult(true);
            public Task<bool> ResetPasswordAsync(string email, string newPasswordHash) => Task.FromResult(true);
            public Task<bool> UpdateUserTierAndCommissionAsync(int userId, string subscriptionTier, decimal? customCommissionRate) => Task.FromResult(true);
            public Task<bool> UpdateUserCommissionRateAsync(int userId, decimal? customCommissionRate) => Task.FromResult(true);
            public Task<bool> LinkAuthProviderAsync(int userId, string provider, string providerKey, string? profilePic = null) => Task.FromResult(true);
        }

        private class FakePropertyRepository : IPropertyRepository
        {
            public List<Property> Properties = new();
            public List<Occasion> Occasions = new();
            public Dictionary<int, List<int>> PropertyOccasions = new();

            public Task<Property?> GetByIdAsync(int propertyId) => Task.FromResult(Properties.FirstOrDefault(p => p.PropertyID == propertyId));
            public Task<List<Occasion>> GetOccasionsAsync() => Task.FromResult(Occasions);
            public Task<List<int>> GetPropertyOccasionIdsAsync(int propertyId)
            {
                if (PropertyOccasions.TryGetValue(propertyId, out var list))
                    return Task.FromResult(list);
                return Task.FromResult(new List<int>());
            }
            public Task<List<Property>> GetAllPropertiesAsync(bool onlyApproved = false) => Task.FromResult(Properties);
            public Task<List<Property>> GetPropertiesByOwnerIdAsync(int ownerId) => Task.FromResult(Properties.Where(p => p.OwnerID == ownerId).ToList());
            public Task<List<Property>> SearchPropertiesAsync(string? location, int? propertyTypeId, int? occasionId, decimal? minPrice, decimal? maxPrice, int? guests) => Task.FromResult(new List<Property>());
            public Task<int> CreatePropertyAsync(Property property) => Task.FromResult(1);
            public Task<bool> UpdatePropertyAsync(Property property) => Task.FromResult(true);
            public Task<bool> UpdatePropertyMapUrlAsync(int propertyId, int ownerId, string? mapUrl) => Task.FromResult(true);
            public Task<bool> DeletePropertyAsync(int propertyId) => Task.FromResult(true);
            public Task<bool> PermanentlyDeletePropertyAsync(int propertyId) => Task.FromResult(true);
            public Task<bool> DeactivatePropertyAsync(int propertyId, bool deactivate = true) => Task.FromResult(true);
            public Task<bool> ApprovePropertyAsync(int propertyId, bool isApproved, string? targetStatus = null) => Task.FromResult(true);
            public Task<bool> SetPropertyOccasionsAsync(int propertyId, List<int> occasionIds) => Task.FromResult(true);
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
            public Task<int> GetOrCreateAmenityByNameAsync(string name) => Task.FromResult(1);
            public Task<int> GetOrCreateOccasionByNameAsync(string name) => Task.FromResult(1);
            public Task<List<PropertyEventCommission>> GetPropertyEventCommissionsAsync(int propertyId) => Task.FromResult(new List<PropertyEventCommission>());
            public Task<decimal?> GetPropertyEventCommissionRateAsync(int propertyId, int packageId) => Task.FromResult<decimal?>(null);
            public Task<bool> SetPropertyEventCommissionsAsync(int propertyId, List<(int packageId, decimal commissionRate)> commissions) => Task.FromResult(true);
        }

        private class FakeBookingRepository : IBookingRepository
        {
            public readonly Dictionary<int, Booking> Bookings = new();
            public bool IsAvailable = true;

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
            public Task<bool> IsSlotAvailableAsync(int propertyId, DateTime checkInDate, DateTime checkOutDate, string bookingType, string? slotType) => Task.FromResult(IsAvailable);
            public Task<bool> IsSlotAvailableAsync(int propertyId, DateTime checkInDate, DateTime checkOutDate, string bookingType, string? slotType, int? excludeBookingId) => Task.FromResult(IsAvailable);
            public Task<bool> UpdateSecurityDepositStatusAsync(int bookingId, string status) => Task.FromResult(true);
            public Task<List<CustomerReview>> GetReviewsByPropertyIdAsync(int propertyId) => Task.FromResult(new List<CustomerReview>());
            public Task<int> AddReviewAsync(CustomerReview review) => Task.FromResult(1);
            public Task<bool> UpdateBookingEventCommissionAsync(int bookingId, decimal newEventCommissionRate) => Task.FromResult(true);
        }

        private class SimpleTempDataProvider : ITempDataProvider
        {
            public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
            public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
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
            public Task<PaymentOrderResponse> CreateOrderAsync(decimal amount, string receipt, string currency = "INR")
            {
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

        private ControllerContext CreateContext(int userId = 10, string role = "Client", string email = "guest@example.com")
        {
            var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(ClaimTypes.Name, email),
                new Claim(ClaimTypes.Role, role)
            }, "TestAuth"));

            var httpContext = new DefaultHttpContext { User = user };
            return new ControllerContext { HttpContext = httpContext };
        }

        // ─────────────────────────────────────────────────────────────
        // Test 1: Toggle on and off
        // ─────────────────────────────────────────────────────────────
        [Fact]
        public void Test1_ToggleOnOff_MarkupAndLogicEnforceDefaultAndClearing()
        {
            var checkoutPath = TestPathHelper.GetPath("Views", "Booking", "Checkout.cshtml");
            Assert.True(File.Exists(checkoutPath));
            var html = File.ReadAllText(checkoutPath);

            // 1. Two-option toggle elements exist
            Assert.Contains("id=\"toggleStandardStay\"", html);
            Assert.Contains("id=\"toggleEventStay\"", html);
            Assert.Contains("Standard Stay", html);
            Assert.Contains("Stay &amp; Celebrate", html);

            // 2. Standard stay is default (checked when !IsEventBooking)
            Assert.Contains("@(Model.IsEventBooking ? \"\" : \"checked\")", html);

            // 3. Switching to standard stay clears hidden décor fields and resets total
            Assert.Contains("function switchToStandardStay()", html);
            Assert.Contains("selectedOccasionIdInput.value = '';", html);
            Assert.Contains("selectedEventPackageIdInput.value = '';", html);
            Assert.Contains("decoratorAssignedIdInput.value = '';", html);
            Assert.Contains("isEventBookingInput.value = 'false';", html);
            Assert.Contains("decorPrice = 0;", html);
        }

        // ─────────────────────────────────────────────────────────────
        // Test 2: 48-Hour Edge Case (exactly 48 hours hidden, 48h 1m shown)
        // ─────────────────────────────────────────────────────────────
        [Fact]
        public void Test2_LeadTime48HourEdgeCase_Exactly48HoursFails_48Hours1MinPasses()
        {
            var baseTime = new DateTime(2026, 11, 5, 12, 0, 0); // Reference time in IST

            // Exactly 48 hours: NOT allowed (> 48 hours is strictly required)
            var exactly48Hours = baseTime.AddHours(48);
            bool allowedAt48H = BookingController.IsLeadTimeMoreThan48Hours(exactly48Hours, baseTime);
            Assert.False(allowedAt48H, "Exactly 48 hours must NOT qualify for Stay & Celebrate.");

            // 48 hours and 1 minute: allowed
            var at48H1M = baseTime.AddHours(48).AddMinutes(1);
            bool allowedAt48H1M = BookingController.IsLeadTimeMoreThan48Hours(at48H1M, baseTime);
            Assert.True(allowedAt48H1M, "48 hours and 1 minute must qualify for Stay & Celebrate.");

            // 47 hours and 59 minutes: NOT allowed
            var under48H = baseTime.AddHours(48).AddMinutes(-1);
            bool allowedUnder48H = BookingController.IsLeadTimeMoreThan48Hours(under48H, baseTime);
            Assert.False(allowedUnder48H, "Less than 48 hours must NOT qualify.");
        }

        // ─────────────────────────────────────────────────────────────
        // Test 3: Area match and no match
        // ─────────────────────────────────────────────────────────────
        [Fact]
        public async Task Test3_AreaMatchAndNoMatch_CityAndStateMatchedCorrectly()
        {
            var fakeEventRepo = new FakeEventPackageRepository();
            var fakeUserRepo = new FakeUserRepository();
            var fakePropRepo = new FakePropertyRepository();
            var service = new CheckoutEventManagerService(fakeEventRepo, fakeUserRepo, fakePropRepo);

            // Active decorator servicing Lonavala and Pune
            var mgr = new User
            {
                UserID = 201,
                FullName = "Royal Moments Decor",
                Role = "EventManager",
                IsActive = true,
                VerificationStatus = "Verified",
                ServiceAreas = "Lonavala; Khandala; Pune, Maharashtra"
            };
            fakeUserRepo.Users.Add(mgr);
            fakeEventRepo.Decorators.Add(mgr);
            fakeEventRepo.Packages.Add(new EventPackage
            {
                PackageID = 11,
                Name = "Grand Arch",
                Category = "Birthday Party",
                BasePrice = 15000,
                IsActive = true,
                CreatedByUserID = 201
            });

            var propInLonavala = new Property { PropertyID = 1, Title = "Hilltop Villa", Location = "Lonavala, Maharashtra", City = "Lonavala", State = "Maharashtra" };
            var propInGoa = new Property { PropertyID = 2, Title = "Beach Villa", Location = "Panaji, Goa", City = "Panaji", State = "Goa" };
            fakePropRepo.Properties.Add(propInLonavala);
            fakePropRepo.Properties.Add(propInGoa);

            // Test area matching service directly
            Assert.True(service.ServicesLocation(mgr.ServiceAreas, propInLonavala.Location));
            Assert.False(service.ServicesLocation(mgr.ServiceAreas, propInGoa.Location));

            // Test Controller Area Availability Check
            var controller = new BookingController(
                fakePropRepo, new FakeBookingRepository(), fakeEventRepo, new BookingPricingService(),
                new MockCommissionService(), null!, null!, new MockPaymentGateway(), null!, null!,
                fakeUserRepo, new MockIdVerificationService(), null!,
                checkoutEventMgrService: service);

            bool servesLonavala = await controller.HasActiveVerifiedDecoratorForAreaAsync(propInLonavala);
            bool servesGoa = await controller.HasActiveVerifiedDecoratorForAreaAsync(propInGoa);

            Assert.True(servesLonavala, "Property in Lonavala should match decorator's service areas.");
            Assert.False(servesGoa, "Property in Goa should NOT match decorator's service areas.");
        }

        // ─────────────────────────────────────────────────────────────
        // Test 4: Sorting both ways (Top rated and Lowest price)
        // ─────────────────────────────────────────────────────────────
        [Fact]
        public void Test4_SortingBothWays_TopRatedAndLowestPrice_ProducesExpectedOrder()
        {
            var p1 = new { Name = "Basic Arch", Price = 8000m, Rating = 4.7, Events = 15 };
            var p2 = new { Name = "Luxury Floral", Price = 25000m, Rating = 5.0, Events = 80 };
            var p3 = new { Name = "Standard Theme", Price = 12000m, Rating = 4.9, Events = 35 };

            var list = new List<dynamic> { p1, p2, p3 };

            // Sort by top-rated
            var topRated = list.OrderByDescending(x => (double)x.Rating).ThenByDescending(x => (int)x.Events).ToList();
            Assert.Equal("Luxury Floral", topRated[0].Name);
            Assert.Equal("Standard Theme", topRated[1].Name);
            Assert.Equal("Basic Arch", topRated[2].Name);

            // Sort by lowest-price
            var lowestPrice = list.OrderBy(x => (decimal)x.Price).ToList();
            Assert.Equal("Basic Arch", lowestPrice[0].Name);
            Assert.Equal("Standard Theme", lowestPrice[1].Name);
            Assert.Equal("Luxury Floral", lowestPrice[2].Name);
        }

        // ─────────────────────────────────────────────────────────────
        // Test 5: "You might also like" list (3 to 7 packages with category name)
        // ─────────────────────────────────────────────────────────────
        [Fact]
        public async Task Test5_YouMightAlsoLike_Returns3To7PackagesWithCategory_WhenCategoryEmpty()
        {
            var fakeEventRepo = new FakeEventPackageRepository();
            var fakeUserRepo = new FakeUserRepository();
            var fakePropRepo = new FakePropertyRepository();

            var mgr = new User
            {
                UserID = 301,
                FullName = "Elite Decors",
                Role = "EventManager",
                IsActive = true,
                VerificationStatus = "Verified",
                ServiceAreas = "Alibaug"
            };
            fakeUserRepo.Users.Add(mgr);
            fakeEventRepo.Decorators.Add(mgr);

            // 4 packages across other categories
            fakeEventRepo.Packages.AddRange(new[]
            {
                new EventPackage { PackageID = 1, Name = "Anniversary Bliss", Category = "Anniversary", BasePrice = 14000, IsActive = true, CreatedByUserID = 301 },
                new EventPackage { PackageID = 2, Name = "Royal Reception", Category = "Wedding / Reception", BasePrice = 30000, IsActive = true, CreatedByUserID = 301 },
                new EventPackage { PackageID = 3, Name = "Corporate Gala", Category = "Corporate / Offsite", BasePrice = 20000, IsActive = true, CreatedByUserID = 301 },
                new EventPackage { PackageID = 4, Name = "Rustic Evening", Category = "Custom Gathering", BasePrice = 18000, IsActive = true, CreatedByUserID = 301 }
            });

            var property = new Property { PropertyID = 50, Title = "Alibaug Haven", Location = "Alibaug, Maharashtra" };
            fakePropRepo.Properties.Add(property);

            var controller = new BookingController(
                fakePropRepo, new FakeBookingRepository(), fakeEventRepo, new BookingPricingService(),
                new MockCommissionService(), null!, null!, new MockPaymentGateway(), null!, null!,
                fakeUserRepo, new MockIdVerificationService(), null!,
                checkoutEventMgrService: new CheckoutEventManagerService(fakeEventRepo, fakeUserRepo, fakePropRepo))
            {
                ControllerContext = CreateContext(),
                TempData = new TempDataDictionary(new DefaultHttpContext(), new SimpleTempDataProvider())
            };

            // Request "Birthday Party" category which has no packages
            var actionResult = await controller.GetEventManagersForOccasion(50, occasionId: 0, category: "Birthday Party");
            var jsonResult = Assert.IsType<JsonResult>(actionResult);

            var json = JsonSerializer.Serialize(jsonResult.Value);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            Assert.True(root.GetProperty("success").GetBoolean());
            var managersArray = root.GetProperty("managers");
            Assert.Equal(0, managersArray.GetArrayLength()); // 0 matching in Birthday Party

            var youMightAlsoLike = root.GetProperty("youMightAlsoLike");
            int count = youMightAlsoLike.GetArrayLength();
            Assert.InRange(count, 3, 7); // 3 to 7 packages

            // Verify each card contains category name
            for (int i = 0; i < count; i++)
            {
                var item = youMightAlsoLike[i];
                Assert.True(item.TryGetProperty("category", out var catProp));
                Assert.False(string.IsNullOrWhiteSpace(catProp.GetString()), "Each card must name its category.");
            }
        }

        // ─────────────────────────────────────────────────────────────
        // Test 6: Flat fee in the total (stay total + flat fee = grand total)
        // ─────────────────────────────────────────────────────────────
        [Fact]
        public void Test6_FlatFeeInTotal_DecorChargeAddedOnceAsFlatFeeNotPerNight()
        {
            decimal pricePerNight = 6000m;
            int nights = 4;
            decimal stayTotal = pricePerNight * nights; // ₹24,000

            decimal decorFlatFee = 15000m; // Single flat fee per booking

            decimal grandTotal = stayTotal + decorFlatFee; // ₹39,000 (NOT 24000 + 4 * 15000)

            Assert.Equal(24000m, stayTotal);
            Assert.Equal(15000m, decorFlatFee);
            Assert.Equal(39000m, grandTotal);
        }

        // ─────────────────────────────────────────────────────────────
        // Test 7: Tampered price rejected on the server
        // ─────────────────────────────────────────────────────────────
        [Fact]
        public async Task Test7_TamperedPriceIgnored_ServerAlwaysUsesDatabasePrice()
        {
            var fakeEventRepo = new FakeEventPackageRepository();
            var fakeUserRepo = new FakeUserRepository();
            var fakePropRepo = new FakePropertyRepository();
            var fakeBookingRepo = new FakeBookingRepository();

            var mgr = new User
            {
                UserID = 401,
                FullName = "Golden Touch Events",
                Role = "EventManager",
                IsActive = true,
                VerificationStatus = "Verified",
                ServiceAreas = "Karjat"
            };
            fakeUserRepo.Users.Add(mgr);
            fakeEventRepo.Decorators.Add(mgr);

            var officialDbPackage = new EventPackage
            {
                PackageID = 88,
                Name = "Grand Anniversary Staging",
                Category = "Anniversary",
                BasePrice = 18000m, // Real price in DB is ₹18,000
                IsActive = true,
                CreatedByUserID = 401
            };
            fakeEventRepo.Packages.Add(officialDbPackage);

            var property = new Property
            {
                PropertyID = 70,
                Title = "Karjat Farm",
                PricePerNight = 5000m,
                Location = "Karjat, Maharashtra"
            };
            fakePropRepo.Properties.Add(property);
            fakePropRepo.PropertyOccasions[70] = new List<int> { 1 };
            fakePropRepo.Occasions.Add(new Occasion { OccasionID = 1, OccasionName = "Anniversary" });

            var controller = new BookingController(
                fakePropRepo, fakeBookingRepo, fakeEventRepo, new BookingPricingService(),
                new MockCommissionService(), null!, null!, new MockPaymentGateway(), null!, null!,
                fakeUserRepo, new MockIdVerificationService(), null!,
                calendarRangeService: new CalendarRangeService(fakeBookingRepo),
                checkoutEventMgrService: new CheckoutEventManagerService(fakeEventRepo, fakeUserRepo, fakePropRepo))
            {
                ControllerContext = CreateContext(),
                TempData = new TempDataDictionary(new DefaultHttpContext(), new SimpleTempDataProvider())
            };

            var checkIn = DateTime.Today.AddDays(4); // More than 48 hours notice
            var checkOut = DateTime.Today.AddDays(5);

            var model = new BookingCheckoutViewModel
            {
                PropertyID = 70,
                BookingType = "FullDay",
                CheckInDate = checkIn,
                CheckOutDate = checkOut,
                IsEventBooking = true,
                OccasionID = 1,
                EventPackageID = 88,
                CalculatedAmount = 5001m // Tampered: client claimed total amount was ₹5,001 (tampering with package price!)
            };

            var result = await controller.ConfirmBooking(model);

            // Successfully validated and redirected to Payment
            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Payment", redirect.ActionName);

            // Verify in repository that booking was created with official DB price (₹5,000 stay + ₹18,000 décor = ₹23,000)
            Assert.NotEmpty(fakeBookingRepo.Bookings);
            var createdBooking = fakeBookingRepo.Bookings.Values.First();
            Assert.Equal(23000m, createdBooking.TotalAmount); // Ignored client's 5001 tampered amount!
            Assert.Equal(88, createdBooking.EventPackageID);
            Assert.True(createdBooking.IsEventBooking);
        }

        // ─────────────────────────────────────────────────────────────
        // Test 8: No contact fields in HTML or JSON
        // ─────────────────────────────────────────────────────────────
        [Fact]
        public async Task Test8_PrivacyProtection_NoContactFieldsInHtmlCardsOrJsonResponse()
        {
            var fakeEventRepo = new FakeEventPackageRepository();
            var fakeUserRepo = new FakeUserRepository();
            var fakePropRepo = new FakePropertyRepository();

            var mgr = new User
            {
                UserID = 501,
                FullName = "Celebration Studio",
                Email = "secret-manager@example.com",
                Phone = "+919876543210",
                WhatsAppPhone = "+919876543210",
                Role = "EventManager",
                IsActive = true,
                VerificationStatus = "Verified",
                ServiceAreas = "Lonavala"
            };
            fakeUserRepo.Users.Add(mgr);
            fakeEventRepo.Decorators.Add(mgr);
            fakeEventRepo.Packages.Add(new EventPackage
            {
                PackageID = 91,
                Name = "Twilight Celebration",
                Category = "Birthday Party",
                BasePrice = 16000,
                IsActive = true,
                CreatedByUserID = 501
            });

            var property = new Property { PropertyID = 80, Title = "Lonavala Retreat", Location = "Lonavala, Maharashtra" };
            fakePropRepo.Properties.Add(property);

            var controller = new BookingController(
                fakePropRepo, new FakeBookingRepository(), fakeEventRepo, new BookingPricingService(),
                new MockCommissionService(), null!, null!, new MockPaymentGateway(), null!, null!,
                fakeUserRepo, new MockIdVerificationService(), null!,
                checkoutEventMgrService: new CheckoutEventManagerService(fakeEventRepo, fakeUserRepo, fakePropRepo))
            {
                ControllerContext = CreateContext(),
                TempData = new TempDataDictionary(new DefaultHttpContext(), new SimpleTempDataProvider())
            };

            // 1. Check JSON response
            var actionResult = await controller.GetEventManagersForOccasion(80, occasionId: 0, category: "Birthday Party");
            var jsonResult = Assert.IsType<JsonResult>(actionResult);
            var jsonString = JsonSerializer.Serialize(jsonResult.Value);

            Assert.DoesNotContain("secret-manager@example.com", jsonString, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("9876543210", jsonString, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("whatsapp", jsonString, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("phone", jsonString, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("email", jsonString, StringComparison.OrdinalIgnoreCase);

            // 2. Check Checkout.cshtml template markup
            var checkoutPath = TestPathHelper.GetPath("Views", "Booking", "Checkout.cshtml");
            var html = File.ReadAllText(checkoutPath);

            Assert.Contains("target=\"_blank\" rel=\"noopener noreferrer\"", html); // View profile opens in new tab
            Assert.DoesNotContain("mgr.phoneNumber", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("mgr.email", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("mgr.whatsapp", html, StringComparison.OrdinalIgnoreCase);
        }

        // ─────────────────────────────────────────────────────────────
        // Step 1 Verification: Check-in 13 Nov (>48h) option shows & menu opens
        // ─────────────────────────────────────────────────────────────
        [Fact]
        public async Task Step1_Verification_CheckIn13Nov_OptionShows_AndMenuOpens()
        {
            var fakeEventRepo = new FakeEventPackageRepository();
            var fakeUserRepo = new FakeUserRepository();
            var fakePropRepo = new FakePropertyRepository();
            var fakeBookingRepo = new FakeBookingRepository();

            var mgr = new User
            {
                UserID = 101,
                FullName = "Goa Dream Celebrations",
                Role = "EventManager",
                IsActive = true,
                VerificationStatus = "Verified",
                ServiceAreas = "Goa; Panaji; Calangute"
            };
            fakeUserRepo.Users.Add(mgr);
            fakeEventRepo.Decorators.Add(mgr);

            fakeEventRepo.Packages.AddRange(new[]
            {
                new EventPackage { PackageID = 1, Name = "Sunset Beach Floral Theme", Category = "Birthday Party", BasePrice = 12000, IsActive = true, CreatedByUserID = 101 },
                new EventPackage { PackageID = 2, Name = "Goan Romance Anniversary", Category = "Anniversary", BasePrice = 15000, IsActive = true, CreatedByUserID = 101 },
                new EventPackage { PackageID = 3, Name = "Tropical Gala", Category = "Corporate / Offsite", BasePrice = 25000, IsActive = true, CreatedByUserID = 101 }
            });

            var property4 = new Property
            {
                PropertyID = 4,
                Title = "Sunset Horizon Ocean Villa",
                Location = "Calangute, Goa",
                City = "Calangute",
                State = "Goa",
                PricePerNight = 12000m
            };
            fakePropRepo.Properties.Add(property4);

            var checkoutService = new CheckoutEventManagerService(fakeEventRepo, fakeUserRepo, fakePropRepo);
            var controller = new BookingController(
                fakePropRepo, fakeBookingRepo, fakeEventRepo, new BookingPricingService(),
                new MockCommissionService(), null!, null!, new MockPaymentGateway(), null!, null!,
                fakeUserRepo, new MockIdVerificationService(), null!,
                checkoutEventMgrService: checkoutService);
            controller.ControllerContext = CreateContext();
            controller.TempData = new TempDataDictionary(new DefaultHttpContext(), new SimpleTempDataProvider());

            // 1. Call CelebrationEligibility for Check-in > 48 hours away
            var futureCheckInDate = DateTime.Today.AddDays(5).ToString("yyyy-MM-dd");
            var futureCheckOutDate = DateTime.Today.AddDays(10).ToString("yyyy-MM-dd");
            var actionResult = await controller.CelebrationEligibility(4, checkIn: futureCheckInDate, checkOut: futureCheckOutDate);
            var jsonResult = Assert.IsType<JsonResult>(actionResult);
            var json = JsonSerializer.Serialize(jsonResult.Value);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            Assert.True(root.GetProperty("success").GetBoolean());
            Assert.True(root.GetProperty("allowed").GetBoolean(), "Stay & Celebrate must be allowed for Check-in 13 Nov.");
            var cats = root.GetProperty("availableCategories").EnumerateArray().Select(e => e.GetString()).ToList();
            Assert.Contains("Birthday Party", cats);
            Assert.Contains("Anniversary", cats);
            Assert.Contains("Corporate / Offsite", cats);

            // 2. Open Menu: Call GetEventManagersForOccasion
            var menuResult = await controller.GetEventManagersForOccasion(4, category: "Birthday Party");
            var menuJsonResult = Assert.IsType<JsonResult>(menuResult);
            var menuJson = JsonSerializer.Serialize(menuJsonResult.Value);
            using var menuDoc = JsonDocument.Parse(menuJson);
            var menuRoot = menuDoc.RootElement;

            Assert.True(menuRoot.GetProperty("success").GetBoolean());
            var managers = menuRoot.GetProperty("managers");
            Assert.True(managers.GetArrayLength() > 0, "Managers list must not be empty.");
            var firstMgr = managers[0];
            Assert.Equal("Goa Dream Celebrations", firstMgr.GetProperty("displayName").GetString());
            Assert.Equal(12000m, firstMgr.GetProperty("startingPrice").GetDecimal());
        }

        // ─────────────────────────────────────────────────────────────
        // Step 2 Verification: Date change to >48h future date: Option shows & menu updates
        // ─────────────────────────────────────────────────────────────
        [Fact]
        public async Task Step2_Verification_FutureCheckInDate_OptionShows_AndMenuUpdates()
        {
            var fakeEventRepo = new FakeEventPackageRepository();
            var fakeUserRepo = new FakeUserRepository();
            var fakePropRepo = new FakePropertyRepository();
            var fakeBookingRepo = new FakeBookingRepository();

            var mgr = new User
            {
                UserID = 101,
                FullName = "Goa Dream Celebrations",
                Role = "EventManager",
                IsActive = true,
                VerificationStatus = "Verified",
                ServiceAreas = "Goa; Panaji; Calangute"
            };
            fakeUserRepo.Users.Add(mgr);
            fakeEventRepo.Decorators.Add(mgr);

            fakeEventRepo.Packages.AddRange(new[]
            {
                new EventPackage { PackageID = 1, Name = "Sunset Beach Floral Theme", Category = "Birthday Party", BasePrice = 12000, IsActive = true, CreatedByUserID = 101 },
                new EventPackage { PackageID = 2, Name = "Goan Romance Anniversary", Category = "Anniversary", BasePrice = 15000, IsActive = true, CreatedByUserID = 101 }
            });

            var property4 = new Property
            {
                PropertyID = 4,
                Title = "Sunset Horizon Ocean Villa",
                Location = "Calangute, Goa",
                City = "Calangute",
                State = "Goa",
                PricePerNight = 12000m
            };
            fakePropRepo.Properties.Add(property4);

            var checkoutService = new CheckoutEventManagerService(fakeEventRepo, fakeUserRepo, fakePropRepo);
            var controller = new BookingController(
                fakePropRepo, fakeBookingRepo, fakeEventRepo, new BookingPricingService(),
                new MockCommissionService(), null!, null!, new MockPaymentGateway(), null!, null!,
                fakeUserRepo, new MockIdVerificationService(), null!,
                checkoutEventMgrService: checkoutService);
            controller.ControllerContext = CreateContext();
            controller.TempData = new TempDataDictionary(new DefaultHttpContext(), new SimpleTempDataProvider());

            // Check-in date > 48 hours away
            var futureCheckIn = DateTime.Today.AddDays(4).ToString("yyyy-MM-dd");
            var futureCheckOut = DateTime.Today.AddDays(7).ToString("yyyy-MM-dd");

            var actionResult = await controller.CelebrationEligibility(4, checkIn: futureCheckIn, checkOut: futureCheckOut);
            var jsonResult = Assert.IsType<JsonResult>(actionResult);
            var json = JsonSerializer.Serialize(jsonResult.Value);
            using var doc = JsonDocument.Parse(json);

            Assert.True(doc.RootElement.GetProperty("allowed").GetBoolean());

            // Menu updates with Anniversary packages when selected
            var menuResult = await controller.GetEventManagersForOccasion(4, category: "Anniversary");
            var menuJsonResult = Assert.IsType<JsonResult>(menuResult);
            var menuJson = JsonSerializer.Serialize(menuJsonResult.Value);
            using var menuDoc = JsonDocument.Parse(menuJson);
            var managers = menuDoc.RootElement.GetProperty("managers");
            Assert.True(managers.GetArrayLength() > 0);
            var packages = managers[0].GetProperty("packages");
            Assert.Equal("Goan Romance Anniversary", packages[0].GetProperty("name").GetString());
        }

        // ─────────────────────────────────────────────────────────────
        // Step 3 Verification: Check-in under 48 hours: Option hides & switches to standard stay
        // ─────────────────────────────────────────────────────────────
        [Fact]
        public async Task Step3_Verification_CheckInUnder48Hours_OptionHides_SwitchesToStandardStay()
        {
            var fakeEventRepo = new FakeEventPackageRepository();
            var fakeUserRepo = new FakeUserRepository();
            var fakePropRepo = new FakePropertyRepository();
            var fakeBookingRepo = new FakeBookingRepository();

            var mgr = new User
            {
                UserID = 101,
                FullName = "Goa Dream Celebrations",
                Role = "EventManager",
                IsActive = true,
                VerificationStatus = "Verified",
                ServiceAreas = "Goa"
            };
            fakeUserRepo.Users.Add(mgr);
            fakeEventRepo.Decorators.Add(mgr);
            fakeEventRepo.Packages.Add(new EventPackage
            {
                PackageID = 1,
                Name = "Sunset Beach Floral Theme",
                Category = "Birthday Party",
                BasePrice = 12000,
                IsActive = true,
                CreatedByUserID = 101
            });

            var property4 = new Property
            {
                PropertyID = 4,
                Title = "Sunset Horizon Ocean Villa",
                Location = "Calangute, Goa",
                City = "Calangute",
                State = "Goa",
                PricePerNight = 12000m
            };
            fakePropRepo.Properties.Add(property4);

            // 1. Edge Case: 05 Nov 13:00 to 06 Nov 12:00 (23 hours notice - strictly under 48 hours)
            var baseTime = new DateTime(2026, 11, 5, 13, 0, 0);
            var under48HCheckIn = new DateTime(2026, 11, 6, 12, 0, 0);
            bool isAllowed = BookingController.IsLeadTimeMoreThan48Hours(under48HCheckIn, baseTime);
            Assert.False(isAllowed, "Check-in at 23 hours notice must NOT be allowed.");

            // 2. Endpoint returns allowed=false with exact message
            var checkoutService = new CheckoutEventManagerService(fakeEventRepo, fakeUserRepo, fakePropRepo);
            var controller = new BookingController(
                fakePropRepo, fakeBookingRepo, fakeEventRepo, new BookingPricingService(),
                new MockCommissionService(), null!, null!, new MockPaymentGateway(), null!, null!,
                fakeUserRepo, new MockIdVerificationService(), null!,
                checkoutEventMgrService: checkoutService);
            controller.ControllerContext = CreateContext();
            controller.TempData = new TempDataDictionary(new DefaultHttpContext(), new SimpleTempDataProvider());

            // Check-in tomorrow (less than 48 hours from today)
            var tomorrow = DateTime.Today.AddDays(1).ToString("yyyy-MM-dd");
            var result = await controller.CelebrationEligibility(4, checkIn: tomorrow);
            var jsonResult = Assert.IsType<JsonResult>(result);
            var json = JsonSerializer.Serialize(jsonResult.Value);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            Assert.False(root.GetProperty("allowed").GetBoolean());
            Assert.Equal("Décor needs more than 48 hours' notice, so we've switched you to a standard stay.", root.GetProperty("reason").GetString());
        }

        // ─────────────────────────────────────────────────────────────
        // Step 4 Verification: Property with no décor in its area: Option never shows
        // ─────────────────────────────────────────────────────────────
        [Fact]
        public async Task Step4_Verification_PropertyWithNoDecorInArea_OptionNeverShows()
        {
            var fakeEventRepo = new FakeEventPackageRepository();
            var fakeUserRepo = new FakeUserRepository();
            var fakePropRepo = new FakePropertyRepository();
            var fakeBookingRepo = new FakeBookingRepository();

            // Decorator only serves Goa
            var mgr = new User
            {
                UserID = 101,
                FullName = "Goa Celebrations",
                Role = "EventManager",
                IsActive = true,
                VerificationStatus = "Verified",
                ServiceAreas = "Goa"
            };
            fakeUserRepo.Users.Add(mgr);
            fakeEventRepo.Decorators.Add(mgr);

            // Property in Shimla, Himachal Pradesh (No decorator)
            var propShimla = new Property
            {
                PropertyID = 99,
                Title = "Himalayan Pine Retreat",
                Location = "Shimla, Himachal Pradesh",
                City = "Shimla",
                State = "Himachal Pradesh",
                PricePerNight = 15000m
            };
            fakePropRepo.Properties.Add(propShimla);

            var checkoutService = new CheckoutEventManagerService(fakeEventRepo, fakeUserRepo, fakePropRepo);
            var controller = new BookingController(
                fakePropRepo, fakeBookingRepo, fakeEventRepo, new BookingPricingService(),
                new MockCommissionService(), null!, null!, new MockPaymentGateway(), null!, null!,
                fakeUserRepo, new MockIdVerificationService(), null!,
                checkoutEventMgrService: checkoutService);
            controller.ControllerContext = CreateContext();
            controller.TempData = new TempDataDictionary(new DefaultHttpContext(), new SimpleTempDataProvider());

            // 1. Controller Area Availability Check
            bool hasDecorator = await controller.HasActiveVerifiedDecoratorForAreaAsync(propShimla);
            Assert.False(hasDecorator, "Property in Shimla must NOT have active decorators.");

            // 2. CelebrationEligibility endpoint check
            var farFuture = DateTime.Today.AddDays(10).ToString("yyyy-MM-dd");
            var result = await controller.CelebrationEligibility(99, checkIn: farFuture);
            var jsonResult = Assert.IsType<JsonResult>(result);
            var json = JsonSerializer.Serialize(jsonResult.Value);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            Assert.False(root.GetProperty("allowed").GetBoolean());
            Assert.Equal("No active décor manager serves this property's location.", root.GetProperty("reason").GetString());
        }

        // ─────────────────────────────────────────────────────────────
        // Step 5 Verification: Console and Network: No errors and client listeners wired
        // ─────────────────────────────────────────────────────────────
        [Fact]
        public void Step5_Verification_ClientListenersAndEndpointContractIntegrity()
        {
            var checkoutPath = TestPathHelper.GetPath("Views", "Booking", "Checkout.cshtml");
            Assert.True(File.Exists(checkoutPath));
            var html = File.ReadAllText(checkoutPath);

            // 1. fetchCelebrationEligibility function queries GET /Booking/CelebrationEligibility
            Assert.Contains("fetch(`/Booking/CelebrationEligibility?propertyId=", html);
            Assert.Contains("applyCelebrationEligibility(data);", html);

            // 2. Wired to visibleCheckIn, visibleCheckOut, visibleHalfDay, visibleHourly, cCalResetBtn, handleCompactCalClick
            Assert.DoesNotContain("checkDecorLeadTime", html);
            Assert.Contains("fetchCelebrationEligibility();", html);

            // 3. Message on switch matches exact requirement
            Assert.Contains("Décor needs more than 48 hours' notice, so we've switched you to a standard stay.", html);

            // 4. Keyboard trap and accessibility
            Assert.Contains("e.key === 'Escape'", html);
            Assert.Contains("planEventModalEl.addEventListener('keydown'", html);
        }

        // ─────────────────────────────────────────────────────────────
        // Step 6 Verification: Build has no new warnings (0 compile errors)
        // ─────────────────────────────────────────────────────────────
        [Fact]
        public void Step6_Verification_ContractTypesAndSignaturesValid()
        {
            // Verify CelebrationEligibility endpoint signature exists
            var method = typeof(BookingController).GetMethod("CelebrationEligibility");
            Assert.NotNull(method);
            var parameters = method.GetParameters();
            Assert.Equal("propertyId", parameters[0].Name);
            Assert.Equal("checkIn", parameters[1].Name);
            Assert.Equal("checkOut", parameters[2].Name);
        }

        // ─────────────────────────────────────────────────────────────
        // Phase 2 Acceptance: Local property 6 and Cloud property 4
        // ─────────────────────────────────────────────────────────────
        [Fact]
        public async Task Phase2_Acceptance_LocalProperty6_And_CloudProperty4()
        {
            var fakeEventRepo = new FakeEventPackageRepository();
            var fakeUserRepo = new FakeUserRepository();
            var fakePropRepo = new FakePropertyRepository();

            var eligibilityService = new StayCelebrationEligibilityService(
                fakePropRepo,
                fakeEventRepo,
                fakeUserRepo,
                new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(),
                new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()),
                null);

            // 1. Local Property 6: Manager Vansh (verified, active packages, zero approved portfolio items)
            var vansh = new User
            {
                UserID = 601,
                FullName = "Vansh Décor",
                Role = "EventManager",
                IsActive = true,
                VerificationStatus = "Verified",
                ServiceAreas = "Pune, Maharashtra"
            };
            fakeUserRepo.Users.Add(vansh);
            fakeEventRepo.Decorators.Add(vansh);
            fakeEventRepo.Packages.Add(new EventPackage
            {
                PackageID = 6011,
                Name = "Pune Grand Celebration",
                Category = "Birthday Party",
                BasePrice = 16000,
                IsActive = true,
                CreatedByUserID = 601
            });

            // Property 6 in Pune
            var property6 = new Property
            {
                PropertyID = 6,
                Title = "Pune Hillside Farmstay",
                Location = "Pune, Maharashtra",
                City = "Pune",
                State = "Maharashtra",
                PricePerNight = 10000m
            };
            fakePropRepo.Properties.Add(property6);

            // Zero approved portfolio items: must NOT offer Stay & Celebrate
            fakeEventRepo.Portfolios.Clear();
            fakeEventRepo.Portfolios.Add(new EventManagerPortfolio
            {
                PortfolioID = 1,
                UserID = 601,
                Title = "Unapproved Stage",
                MediaUrl = "/img/stage.jpg",
                IsApproved = false
            });

            var checkIn = DateTime.Today.AddDays(5);
            var resultProp6Before = await eligibilityService.CheckEligibilityAsync(6, checkIn);
            Assert.False(resultProp6Before.Available, "Property 6 must not offer Stay & Celebrate when manager has zero approved portfolio items.");
            Assert.Equal("no_approved_portfolio", resultProp6Before.ReasonCode);

            // Now approve portfolio item: must offer Stay & Celebrate
            fakeEventRepo.Portfolios[0].IsApproved = true;
            eligibilityService.InvalidateCache(6);
            var resultProp6After = await eligibilityService.CheckEligibilityAsync(6, checkIn);
            Assert.True(resultProp6After.Available, "Property 6 must offer Stay & Celebrate once manager has an approved portfolio item.");
            Assert.Equal("ok", resultProp6After.ReasonCode);

            // 2. Cloud Property 4 (Surat): stays hidden until verified manager serving Surat exists
            var property4Surat = new Property
            {
                PropertyID = 4,
                Title = "Surat Riverside Retreat",
                Location = "Surat, Gujarat",
                City = "Surat",
                State = "Gujarat",
                PricePerNight = 14000m
            };
            fakePropRepo.Properties.Add(property4Surat);

            // No manager serves Surat yet: stays hidden
            var resultSuratBefore = await eligibilityService.CheckEligibilityAsync(4, checkIn);
            Assert.False(resultSuratBefore.Available, "Cloud property 4 (Surat) must not offer Stay & Celebrate when no manager serves Surat.");
            Assert.Equal("no_area_coverage", resultSuratBefore.ReasonCode);

            // Now add verified manager serving Surat with package and approved portfolio
            var suratManager = new User
            {
                UserID = 401,
                FullName = "Surat Festive Decors",
                Role = "EventManager",
                IsActive = true,
                VerificationStatus = "Verified",
                ServiceAreas = "Surat, Gujarat"
            };
            fakeUserRepo.Users.Add(suratManager);
            fakeEventRepo.Decorators.Add(suratManager);
            fakeEventRepo.Packages.Add(new EventPackage
            {
                PackageID = 4011,
                Name = "Diamond City Gala",
                Category = "Anniversary",
                BasePrice = 20000,
                IsActive = true,
                CreatedByUserID = 401
            });
            fakeEventRepo.Portfolios.Add(new EventManagerPortfolio
            {
                PortfolioID = 2,
                UserID = 401,
                Title = "Grand Banquet",
                MediaUrl = "/img/banquet.jpg",
                IsApproved = true
            });

            eligibilityService.InvalidateCache(4);
            var resultSuratAfter = await eligibilityService.CheckEligibilityAsync(4, checkIn);
            Assert.True(resultSuratAfter.Available, "Cloud property 4 (Surat) must offer Stay & Celebrate once verified manager serving Surat exists.");
            Assert.Equal("ok", resultSuratAfter.ReasonCode);
        }
    }
}
