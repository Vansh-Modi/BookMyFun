using System.Security.Claims;
using BookMyFun.Controllers;
using BookMyFun.Models.Entities;
using BookMyFun.Models.ViewModels;
using BookMyFun.Repositories;
using BookMyFun.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;

namespace BookMyFun.Tests
{
    public class CommissionArchitectureTests
    {
        private class MockUserRepo : IUserRepository
        {
            public readonly Dictionary<int, User> Users = new();

            public Task<User?> GetByIdAsync(int userId)
            {
                Users.TryGetValue(userId, out var user);
                return Task.FromResult(user);
            }

            public Task<User?> GetByEmailAsync(string email)
            {
                var user = Users.Values.FirstOrDefault(u => u.Email.Equals(email, StringComparison.OrdinalIgnoreCase));
                return Task.FromResult(user);
            }

            public Task<User?> GetByAuthProviderKeyAsync(string provider, string providerKey) => Task.FromResult<User?>(null);

            public Task<List<User>> GetUsersByRoleAsync(string role)
            {
                return Task.FromResult(Users.Values.Where(u => u.Role == role).ToList());
            }

            public Task<List<User>> GetAllUsersAsync() => Task.FromResult(Users.Values.ToList());
            public Task<int> CreateUserAsync(User user)
            {
                user.UserID = Users.Count + 1;
                Users[user.UserID] = user;
                return Task.FromResult(user.UserID);
            }

            public Task<bool> UpdateUserAsync(User user)
            {
                Users[user.UserID] = user;
                return Task.FromResult(true);
            }

            public Task<bool> DeleteUserAsync(int userId) => Task.FromResult(Users.Remove(userId));
            public Task<bool> ResetPasswordAsync(string email, string newPasswordHash) => Task.FromResult(true);
            public Task<bool> UpdateUserTierAndCommissionAsync(int userId, string subscriptionTier, decimal? customCommissionRate) => Task.FromResult(true);

            public Task<bool> UpdateUserCommissionRateAsync(int userId, decimal? customCommissionRate)
            {
                if (Users.TryGetValue(userId, out var user))
                {
                    user.CustomCommissionRate = customCommissionRate;
                    return Task.FromResult(true);
                }
                return Task.FromResult(false);
            }

            public Task<bool> LinkAuthProviderAsync(int userId, string provider, string providerKey, string? profilePic = null) => Task.FromResult(true);
        }

        private class MockConfigRepo : IConfigRepository
        {
            public readonly Dictionary<string, string> Settings = new();
            public readonly List<string> Audits = new();

            public Task LogAuditAsync(int performedByUserId, string action, string entityType, int? entityId, string? details)
            {
                Audits.Add($"{performedByUserId}:{action}:{entityType}:{entityId}:{details}");
                return Task.CompletedTask;
            }

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
        }

        private class MockEventPackageRepo : IEventPackageRepository
        {
            public readonly Dictionary<int, EventPackage> Packages = new();

            public Task<List<EventPackage>> GetAllPackagesAsync() => Task.FromResult(Packages.Values.ToList());
            public Task<List<EventPackage>> GetActivePackagesAsync() => Task.FromResult(Packages.Values.Where(p => p.IsActive).ToList());
            public Task<EventPackage?> GetPackageByIdAsync(int packageId)
            {
                Packages.TryGetValue(packageId, out var pkg);
                return Task.FromResult(pkg);
            }
            public Task<int> CreatePackageAsync(EventPackage package)
            {
                package.PackageID = Packages.Count + 1;
                Packages[package.PackageID] = package;
                return Task.FromResult(package.PackageID);
            }
            public Task<bool> UpdatePackageAsync(EventPackage package)
            {
                Packages[package.PackageID] = package;
                return Task.FromResult(true);
            }
            public Task<bool> DeletePackageAsync(int packageId) => Task.FromResult(Packages.Remove(packageId));

            public Task<bool> UpdatePackageCommissionRateAsync(int packageId, decimal? commissionRate)
            {
                if (Packages.TryGetValue(packageId, out var pkg))
                {
                    pkg.CommissionRate = commissionRate;
                    return Task.FromResult(true);
                }
                return Task.FromResult(false);
            }

            public Task<List<Booking>> GetAssignedEventBookingsAsync(int decoratorUserId) => Task.FromResult(new List<Booking>());
            public Task<List<Booking>> GetAllEventBookingsAsync() => Task.FromResult(new List<Booking>());
            public Task<bool> UpdateSetupStatusAsync(int bookingId, string setupStatus, string? setupProofMediaUrls, int? decoratorId) => Task.FromResult(true);
            public Task<bool> AssignDecoratorAsync(int bookingId, int decoratorUserId) => Task.FromResult(true);
            public Task<bool> CancelOrRemoveEventFromBookingAsync(int bookingId) => Task.FromResult(true);
            public Task<List<User>> GetDecoratorsAsync() => Task.FromResult(new List<User>());
            public Task<List<string>> GetAllDistinctPropertyLocationsAsync() => Task.FromResult(new List<string>());
            public Task<string?> GetEventManagerServiceAreasAsync(int eventManagerUserId) => Task.FromResult<string?>(null);
            public Task<bool> UpdateEventManagerServiceAreasAsync(int eventManagerUserId, string serviceAreas) => Task.FromResult(true);
            public Task<bool> IsAreaServicedByAnyEventManagerAsync(string? propertyLocation) => Task.FromResult(true);
        }

        private class MockBookingRepo : IBookingRepository
        {
            public readonly Dictionary<int, Booking> Bookings = new();

            public Task<Booking?> GetByIdAsync(int bookingID)
            {
                Bookings.TryGetValue(bookingID, out var b);
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
            public Task<bool> UpdateSecurityDepositStatusAsync(int bookingId, string status) => Task.FromResult(true);
            public Task<bool> IsSlotAvailableAsync(int propertyId, DateTime checkInDate, DateTime checkOutDate, string bookingType, string? slotType) => Task.FromResult(true);
            public Task<int> RecordPaymentAsync(Payment payment) => Task.FromResult(1);
            public Task<List<Payment>> GetPaymentsByBookingIdAsync(int bookingId) => Task.FromResult(new List<Payment>());
            public Task<List<CustomerReview>> GetReviewsByPropertyIdAsync(int propertyId) => Task.FromResult(new List<CustomerReview>());
            public Task<int> AddReviewAsync(CustomerReview review) => Task.FromResult(1);

            public Task<bool> UpdateBookingEventCommissionAsync(int bookingId, decimal newEventCommissionRate)
            {
                if (Bookings.TryGetValue(bookingId, out var b))
                {
                    b.EventCommissionRateSnapshot = newEventCommissionRate;
                    decimal price = b.EventPackagePriceSnapshot;
                    b.EventPlatformCommissionAmount = Math.Round((price * newEventCommissionRate) / 100m, 2);
                    b.EventManagerPayoutAmount = Math.Max(0, price - b.EventPlatformCommissionAmount);
                    return Task.FromResult(true);
                }
                return Task.FromResult(false);
            }
        }

        private class SimpleTempDataProvider : ITempDataProvider
        {
            private readonly Dictionary<string, object> _data = new();
            public IDictionary<string, object> LoadTempData(HttpContext context) => _data;
            public void SaveTempData(HttpContext context, IDictionary<string, object> values)
            {
                foreach (var kvp in values) _data[kvp.Key] = kvp.Value;
            }
        }

        private static AdminController CreateAdminController(
            MockUserRepo userRepo,
            MockConfigRepo configRepo,
            MockEventPackageRepo eventPkgRepo,
            MockBookingRepo bookingRepo,
            SiteSettingsService siteSettingsService,
            int adminUserId = 1)
        {
            var controller = new AdminController(
                null!, // propRepo
                userRepo,
                bookingRepo,
                null!, // subRepo
                configRepo,
                null!, // ownerReqRepo
                null!, // contactRepo
                siteSettingsService,
                null!, // financialRepo
                null!, // env
                null!, // refundEngine
                null!, // partnerLeadService
                null!, // damageClaimService
                null!, // idVerificationService
                null!, // mediaStorage
                eventPkgRepo
            );

            var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, adminUserId.ToString()),
                new Claim(ClaimTypes.Role, "Admin"),
                new Claim(ClaimTypes.Name, "Platform Admin")
            }, "TestAuth"));

            var httpContext = new DefaultHttpContext { User = user };
            controller.ControllerContext = new ControllerContext
            {
                HttpContext = httpContext,
                RouteData = new RouteData()
            };
            controller.TempData = new TempDataDictionary(httpContext, new SimpleTempDataProvider());
            return controller;
        }

        private static EmployeeController CreateEmployeeController(
            MockUserRepo userRepo,
            MockConfigRepo configRepo,
            MockEventPackageRepo eventPkgRepo,
            MockBookingRepo bookingRepo,
            SiteSettingsService siteSettingsService,
            int empUserId = 2)
        {
            var controller = new EmployeeController(
                null!, // propRepo
                userRepo,
                bookingRepo,
                configRepo,
                null!, // ownerReqRepo
                null!, // contactRepo
                null!, // refundEngine
                null!, // partnerLeadService
                null!, // damageClaimService
                null!, // idVerificationService
                null!, // mediaStorage
                eventPkgRepo,
                siteSettingsService
            );

            var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, empUserId.ToString()),
                new Claim(ClaimTypes.Role, "Employee"),
                new Claim(ClaimTypes.Name, "Operations Employee")
            }, "TestAuth"));

            var httpContext = new DefaultHttpContext { User = user };
            controller.ControllerContext = new ControllerContext
            {
                HttpContext = httpContext,
                RouteData = new RouteData()
            };
            controller.TempData = new TempDataDictionary(httpContext, new SimpleTempDataProvider());
            return controller;
        }

        public async Task CalculateEventCommission_AppliesDefaultGlobalStandingRate_WhenNoOverridesExist()
        {
            var configRepo = new MockConfigRepo();
            var userRepo = new MockUserRepo();
            var eventPkgRepo = new MockEventPackageRepo();

            // Establish global standing default rate in config: 12.0%
            await configRepo.SetSettingValueAsync("EventPlatformCommissionPercent", "12.0");

            var commissionService = new CommissionService(
                configRepo: configRepo,
                userRepo: userRepo,
                propRepo: null,
                eventPackageRepo: eventPkgRepo
            );

            decimal packagePrice = 20000m;
            var (rate, comm, payout) = await commissionService.CalculateEventCommissionAsync(
                eventPackagePrice: packagePrice,
                propertyId: 0,
                packageId: null,
                propertyEventCommissionOverride: null,
                eventManagerUserId: null
            );

            if (rate != 12.0m)
            {
                throw new Exception($"Expected rate 12.0%, but got {rate}%");
            }
            if (comm != 2400.00m)
            {
                throw new Exception($"Expected commission ₹2,400.00, but got ₹{comm}");
            }
            if (payout != 17600.00m)
            {
                throw new Exception($"Expected manager payout ₹17,600.00, but got ₹{payout}");
            }
        }

        public async Task UpdateGlobalEventCommission_AdminAndEmployee_UpdatesGlobalRateAndAudits()
        {
            var configRepo = new MockConfigRepo();
            var userRepo = new MockUserRepo();
            var eventPkgRepo = new MockEventPackageRepo();
            var bookingRepo = new MockBookingRepo();
            var siteSettingsService = new SiteSettingsService(configRepo);

            var adminController = CreateAdminController(userRepo, configRepo, eventPkgRepo, bookingRepo, siteSettingsService, 1);
            var empController = CreateEmployeeController(userRepo, configRepo, eventPkgRepo, bookingRepo, siteSettingsService, 2);

            // 1. Admin updates global event commission rate to 14.5%
            var adminResult = await adminController.UpdateGlobalEventCommission(new UpdateGlobalEventCommissionRequest { CommissionRate = 14.5m }) as JsonResult;
            if (adminResult == null) throw new Exception("Expected JsonResult from Admin UpdateGlobalEventCommission.");

            var currentSetting = await configRepo.GetSettingValueAsync("EventPlatformCommissionPercent");
            if (currentSetting != "14.5")
            {
                throw new Exception($"Expected setting '14.5', but got: '{currentSetting}'");
            }

            var auditAdmin = configRepo.Audits.FirstOrDefault(a => a.Contains("UpdateGlobalEventCommission") && a.Contains("14.5"));
            if (auditAdmin == null) throw new Exception("Expected audit log for Admin UpdateGlobalEventCommission.");

            // 2. Employee updates global event commission rate to 11.0%
            var empResult = await empController.UpdateGlobalEventCommission(new UpdateGlobalEventCommissionRequest { CommissionRate = 11.0m }) as JsonResult;
            if (empResult == null) throw new Exception("Expected JsonResult from Employee UpdateGlobalEventCommission.");

            currentSetting = await configRepo.GetSettingValueAsync("EventPlatformCommissionPercent");
            if (currentSetting != "11")
            {
                throw new Exception($"Expected setting '11', but got: '{currentSetting}'");
            }

            var auditEmp = configRepo.Audits.FirstOrDefault(a => a.Contains("UpdateGlobalEventCommission") && a.Contains("Employee #2"));
            if (auditEmp == null) throw new Exception("Expected audit log for Employee UpdateGlobalEventCommission.");
        }

        public async Task UpdateEventManagerCommission_AdminAndEmployee_AppliesCustomRatePerEventManager()
        {
            var configRepo = new MockConfigRepo();
            var userRepo = new MockUserRepo();
            var eventPkgRepo = new MockEventPackageRepo();
            var bookingRepo = new MockBookingRepo();
            var siteSettingsService = new SiteSettingsService(configRepo);

            // Seed an Event Manager
            userRepo.Users[10] = new User
            {
                UserID = 10,
                FullName = "Royal Decorators Ltd",
                Email = "royal@decorators.com",
                Role = "EventManager",
                IsActive = true,
                CustomCommissionRate = null // Initially inheriting global
            };

            await configRepo.SetSettingValueAsync("EventPlatformCommissionPercent", "10.0");

            var adminController = CreateAdminController(userRepo, configRepo, eventPkgRepo, bookingRepo, siteSettingsService, 1);
            var empController = CreateEmployeeController(userRepo, configRepo, eventPkgRepo, bookingRepo, siteSettingsService, 2);

            // 1. Employee sets custom commission rate of 7.5% for Event Manager #10
            var empResult = await empController.UpdateEventManagerCommission(new UpdateEventManagerCommissionRequest
            {
                UserId = 10,
                CommissionRate = 7.5m
            }) as JsonResult;

            if (empResult == null) throw new Exception("Expected JsonResult from Employee UpdateEventManagerCommission.");

            var mgr = userRepo.Users[10];
            if (mgr.CustomCommissionRate != 7.5m)
            {
                throw new Exception($"Expected CustomCommissionRate 7.5%, but got {mgr.CustomCommissionRate}%");
            }

            // Verify audit log
            var auditLog = configRepo.Audits.FirstOrDefault(a => a.Contains("UpdateEventManagerCommission") && a.Contains("Royal Decorators Ltd"));
            if (auditLog == null) throw new Exception("Expected audit log for UpdateEventManagerCommission.");

            // Verify CommissionService calculates using this manager's custom rate
            var commissionService = new CommissionService(configRepo, userRepo, null, eventPkgRepo);
            var (rate, comm, payout) = await commissionService.CalculateEventCommissionAsync(
                eventPackagePrice: 10000m,
                propertyId: 0,
                packageId: null,
                propertyEventCommissionOverride: null,
                eventManagerUserId: 10
            );

            if (rate != 7.5m)
            {
                throw new Exception($"Expected rate 7.5% for manager #10, but got {rate}%");
            }
            if (comm != 750.00m)
            {
                throw new Exception($"Expected commission ₹750.00, but got ₹{comm}");
            }
            if (payout != 9250.00m)
            {
                throw new Exception($"Expected payout ₹9,250.00, but got ₹{payout}");
            }
        }

        public async Task UpdateEventManagerCommission_ResetToNull_RevertsToGlobalStandingRate()
        {
            var configRepo = new MockConfigRepo();
            var userRepo = new MockUserRepo();
            var eventPkgRepo = new MockEventPackageRepo();
            var bookingRepo = new MockBookingRepo();
            var siteSettingsService = new SiteSettingsService(configRepo);

            userRepo.Users[20] = new User
            {
                UserID = 20,
                FullName = "Elite Floral Stays",
                Email = "elite@floral.com",
                Role = "EventManager",
                IsActive = true,
                CustomCommissionRate = 15.0m // Currently has custom override
            };

            await configRepo.SetSettingValueAsync("EventPlatformCommissionPercent", "10.0");
            var adminController = CreateAdminController(userRepo, configRepo, eventPkgRepo, bookingRepo, siteSettingsService, 1);

            // Admin resets to null (inheriting default)
            var result = await adminController.UpdateEventManagerCommission(new UpdateEventManagerCommissionRequest
            {
                UserId = 20,
                CommissionRate = null
            }) as JsonResult;

            if (result == null) throw new Exception("Expected JsonResult from UpdateEventManagerCommission.");

            var mgr = userRepo.Users[20];
            if (mgr.CustomCommissionRate.HasValue)
            {
                throw new Exception($"Expected CustomCommissionRate to be null, but got {mgr.CustomCommissionRate}");
            }

            // Calculation should revert to standing global rate of 10.0%
            var commissionService = new CommissionService(configRepo, userRepo, null, eventPkgRepo);
            var (rate, comm, payout) = await commissionService.CalculateEventCommissionAsync(
                eventPackagePrice: 10000m,
                propertyId: 0,
                packageId: null,
                propertyEventCommissionOverride: null,
                eventManagerUserId: 20
            );

            if (rate != 10.0m)
            {
                throw new Exception($"Expected reverted rate 10.0%, but got {rate}%");
            }
        }

        public async Task UpdateEventCommission_And_UpdateBookingEventCommission_PerEventOverrides()
        {
            var configRepo = new MockConfigRepo();
            var userRepo = new MockUserRepo();
            var eventPkgRepo = new MockEventPackageRepo();
            var bookingRepo = new MockBookingRepo();
            var siteSettingsService = new SiteSettingsService(configRepo);

            // 1. Seed Package
            eventPkgRepo.Packages[100] = new EventPackage
            {
                PackageID = 100,
                Name = "Glow Birthday Party",
                BasePrice = 15000,
                CommissionRate = null
            };

            // 2. Seed Booking with Event Add-on
            bookingRepo.Bookings[500] = new Booking
            {
                BookingID = 500,
                IsEventBooking = true,
                EventPackagePriceSnapshot = 15000,
                EventCommissionRateSnapshot = 10.0m,
                EventPlatformCommissionAmount = 1500m,
                EventManagerPayoutAmount = 13500m
            };

            var empController = CreateEmployeeController(userRepo, configRepo, eventPkgRepo, bookingRepo, siteSettingsService, 2);

            // A. Update Package Custom Commission Rate via Employee
            var pkgResult = await empController.UpdateEventCommission(new UpdateEventCommissionRequest
            {
                PackageId = 100,
                CommissionRate = 18.0m
            }) as JsonResult;

            if (pkgResult == null) throw new Exception("Expected JsonResult from UpdateEventCommission.");
            if (eventPkgRepo.Packages[100].CommissionRate != 18.0m)
            {
                throw new Exception($"Expected Package commission rate 18.0%, but got {eventPkgRepo.Packages[100].CommissionRate}%");
            }

            // B. Update Specific Booking Event Commission Rate via Employee
            var bookingResult = await empController.UpdateBookingEventCommission(new UpdateBookingEventCommissionRequest
            {
                BookingId = 500,
                CommissionRate = 12.5m
            }) as JsonResult;

            if (bookingResult == null) throw new Exception("Expected JsonResult from UpdateBookingEventCommission.");
            var b = bookingRepo.Bookings[500];
            if (b.EventCommissionRateSnapshot != 12.5m)
            {
                throw new Exception($"Expected Booking commission rate 12.5%, but got {b.EventCommissionRateSnapshot}%");
            }
            if (b.EventPlatformCommissionAmount != 1875.00m)
            {
                throw new Exception($"Expected Platform fee ₹1,875.00, but got ₹{b.EventPlatformCommissionAmount}");
            }
            if (b.EventManagerPayoutAmount != 13125.00m)
            {
                throw new Exception($"Expected Manager payout ₹13,125.00, but got ₹{b.EventManagerPayoutAmount}");
            }
        }

        public void CommissionOverrideEndpoints_RestrictedStrictlyToAdminAndEmployeeRoles()
        {
            var adminType = typeof(AdminController);
            var empType = typeof(EmployeeController);

            var adminAttr = (Microsoft.AspNetCore.Authorization.AuthorizeAttribute?)Attribute.GetCustomAttribute(adminType, typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute));
            if (adminAttr == null || adminAttr.Roles != "Admin")
            {
                throw new Exception("AdminController must enforce [Authorize(Roles = 'Admin')].");
            }

            var empAttr = (Microsoft.AspNetCore.Authorization.AuthorizeAttribute?)Attribute.GetCustomAttribute(empType, typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute));
            if (empAttr == null || !empAttr.Roles!.Contains("Employee") || !empAttr.Roles.Contains("Admin"))
            {
                throw new Exception("EmployeeController must enforce [Authorize(Roles = 'Employee,Admin')].");
            }
        }

        public void EventCommissionsViews_ContainGlobalRateManagerOverridesAndEventPackageTables()
        {
            string adminViewPath = Path.Combine(Directory.GetCurrentDirectory(), "Views", "Admin", "EventCommissions.cshtml");
            if (!File.Exists(adminViewPath))
            {
                adminViewPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "Views", "Admin", "EventCommissions.cshtml");
            }

            string empViewPath = Path.Combine(Directory.GetCurrentDirectory(), "Views", "Employee", "EventCommissions.cshtml");
            if (!File.Exists(empViewPath))
            {
                empViewPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "Views", "Employee", "EventCommissions.cshtml");
            }

            string manageBookingsViewPath = Path.Combine(Directory.GetCurrentDirectory(), "Views", "Admin", "ManageBookings.cshtml");
            if (!File.Exists(manageBookingsViewPath))
            {
                manageBookingsViewPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "Views", "Admin", "ManageBookings.cshtml");
            }

            string adminContent = File.ReadAllText(adminViewPath);
            if (!adminContent.Contains("globalEventCommInput") || !adminContent.Contains("UpdateGlobalEventCommission"))
            {
                throw new Exception("EventCommissions.cshtml must contain global baseline rate input and UpdateGlobalEventCommission action.");
            }

            if (!adminContent.Contains("UpdateEventManagerCommission") || !adminContent.Contains("input_mgr_"))
            {
                throw new Exception("EventCommissions.cshtml must contain per event manager overrides and UpdateEventManagerCommission action.");
            }

            if (!adminContent.Contains("UpdateEventCommission") || !adminContent.Contains("input_pkg_"))
            {
                throw new Exception("EventCommissions.cshtml must contain per event package overrides and UpdateEventCommission action.");
            }

            string empContent = File.ReadAllText(empViewPath);
            if (!empContent.Contains("EventCommissions.cshtml"))
            {
                throw new Exception("Employee/EventCommissions.cshtml must render EventCommissions partial.");
            }

            string bookingsContent = File.ReadAllText(manageBookingsViewPath);
            if (!bookingsContent.Contains("User.IsInRole(\"Employee\")"))
            {
                throw new Exception("ManageBookings.cshtml must permit Employees to adjust event commission on specific bookings.");
            }
        }
    }
}
