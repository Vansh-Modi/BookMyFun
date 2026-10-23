using System.Security.Claims;
using System.Text;
using BookMyFun.Models.Entities;
using BookMyFun.Models.ViewModels;
using BookMyFun.Repositories;
using BookMyFun.Services;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookMyFun.Controllers
{
    [Authorize(Roles = "Admin")]
    public class AdminController : Controller
    {
        private readonly IPropertyRepository _propRepo;
        private readonly IUserRepository _userRepo;
        private readonly IBookingRepository _bookingRepo;
        private readonly ISubscriptionRepository _subRepo;
        private readonly IConfigRepository _configRepo;
        private readonly IOwnerRequestRepository _ownerReqRepo;
        private readonly IContactRepository _contactRepo;
        private readonly ISiteSettingsService _siteSettingsService;
        private readonly IFinancialRepository _financialRepo;
        private readonly IWebHostEnvironment _env;
        private readonly IRefundEngineService _refundEngine;
        private readonly IPartnerLeadService _partnerLeadService;
        private readonly IDamageClaimService _damageClaimService;
        private readonly IIdVerificationService _idVerificationService;
        private readonly IMediaStorageService _mediaStorage;
        private readonly IEventPackageRepository _eventPackageRepo;

        public AdminController(
            IPropertyRepository propRepo,
            IUserRepository userRepo,
            IBookingRepository bookingRepo,
            ISubscriptionRepository subRepo,
            IConfigRepository configRepo,
            IOwnerRequestRepository ownerReqRepo,
            IContactRepository contactRepo,
            ISiteSettingsService siteSettingsService,
            IFinancialRepository financialRepo,
            IWebHostEnvironment env,
            IRefundEngineService refundEngine,
            IPartnerLeadService partnerLeadService,
            IDamageClaimService damageClaimService,
            IIdVerificationService idVerificationService,
            IMediaStorageService mediaStorage,
            IEventPackageRepository eventPackageRepo)
        {
            _propRepo = propRepo;
            _userRepo = userRepo;
            _bookingRepo = bookingRepo;
            _subRepo = subRepo;
            _configRepo = configRepo;
            _ownerReqRepo = ownerReqRepo;
            _contactRepo = contactRepo;
            _siteSettingsService = siteSettingsService;
            _financialRepo = financialRepo;
            _env = env;
            _refundEngine = refundEngine;
            _partnerLeadService = partnerLeadService;
            _damageClaimService = damageClaimService;
            _idVerificationService = idVerificationService;
            _mediaStorage = mediaStorage;
            _eventPackageRepo = eventPackageRepo;
        }

        public async Task<IActionResult> Index()
        {
            var properties = await _propRepo.GetAllPropertiesAsync();
            var owners = await _userRepo.GetUsersByRoleAsync("Owner");
            var clients = await _userRepo.GetUsersByRoleAsync("Client");
            var employees = await _userRepo.GetUsersByRoleAsync("Employee");
            var bookings = await _bookingRepo.GetAllBookingsAsync();

            decimal totalRevenue = bookings.Where(b => b.Status == "Confirmed").Sum(b => b.TotalAmount);
            decimal platformEarnings = Math.Round(totalRevenue * 0.10m, 2);

            var vm = new AdminDashboardViewModel
            {
                TotalProperties = properties.Count,
                PendingApprovals = properties.Count(p => !p.IsApproved),
                TotalOwners = owners.Count,
                TotalClients = clients.Count,
                TotalEmployees = employees.Count,
                TotalBookings = bookings.Count,
                TotalRevenue = totalRevenue,
                PlatformEarnings = platformEarnings,
                RecentProperties = properties.Take(5).ToList(),
                RecentBookings = bookings.Take(5).ToList()
            };

            return View(vm);
        }

        // --- Full CRUD for Owners on their behalf ---
        public async Task<IActionResult> ManageOwners()
        {
            var owners = await _userRepo.GetUsersByRoleAsync("Owner");
            return View(owners);
        }

        [HttpGet]
        public IActionResult CreateOwner()
        {
            return View(new User { Role = "Owner" });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateOwner(User user, string password)
        {
            user.Role = "Owner";
            user.PasswordHash = AccountController.HashPassword(string.IsNullOrWhiteSpace(password) ? "Owner@123" : password);
            var id = await _userRepo.CreateUserAsync(user);

            var currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            await _configRepo.LogAuditAsync(currentUserId, "CreateOwner", "User", id, $"Created owner {user.FullName} ({user.Email})");

            return RedirectToAction(nameof(ManageOwners));
        }

        [HttpGet]
        public async Task<IActionResult> EditOwner(int id)
        {
            var user = await _userRepo.GetByIdAsync(id);
            if (user == null) return NotFound();
            return View(user);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditOwner(User user)
        {
            user.Role = "Owner";
            await _userRepo.UpdateUserAsync(user);

            var currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            await _configRepo.LogAuditAsync(currentUserId, "EditOwner", "User", user.UserID, $"Updated owner {user.FullName}");

            return RedirectToAction(nameof(ManageOwners));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteOwner(int id)
        {
            await _userRepo.DeleteUserAsync(id);

            var currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            await _configRepo.LogAuditAsync(currentUserId, "DeleteOwner", "User", id, $"Deleted owner #{id}");

            return RedirectToAction(nameof(ManageOwners));
        }

        // --- Full CRUD for Employees ---
        public async Task<IActionResult> ManageEmployees()
        {
            var employees = await _userRepo.GetUsersByRoleAsync("Employee");
            return View(employees);
        }

        [HttpGet]
        public IActionResult CreateEmployee()
        {
            return View(new CreateEmployeeViewModel { IsActive = true });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateEmployee(CreateEmployeeViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var cleanEmail = model.Email.Trim().ToLowerInvariant();
            var existingUser = await _userRepo.GetByEmailAsync(cleanEmail);
            if (existingUser != null)
            {
                ModelState.AddModelError("Email", "An account with this email address already exists.");
                return View(model);
            }

            string passwordToHash = string.IsNullOrWhiteSpace(model.Password) ? "Emp@123" : model.Password.Trim();

            var newEmployee = new User
            {
                FullName = model.FullName.Trim(),
                Email = cleanEmail,
                Phone = string.IsNullOrWhiteSpace(model.Phone) ? null : model.Phone.Trim(),
                PasswordHash = AccountController.HashPassword(passwordToHash),
                Role = "Employee",
                IsActive = model.IsActive,
                SubscriptionTier = "Free",
                CreatedAt = DateTime.UtcNow
            };

            var id = await _userRepo.CreateUserAsync(newEmployee);
            if (id <= 0)
            {
                ModelState.AddModelError("", "Unable to create employee account. Please try again.");
                return View(model);
            }

            var currentUserIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!string.IsNullOrEmpty(currentUserIdStr) && int.TryParse(currentUserIdStr, out int currentUserId))
            {
                await _configRepo.LogAuditAsync(currentUserId, "CreateEmployee", "User", id, $"Created employee {newEmployee.FullName} ({newEmployee.Email})");
            }

            TempData["Success"] = $"Employee '{newEmployee.FullName}' (Account #{id}) was successfully added!";
            return RedirectToAction(nameof(ManageEmployees));
        }

        [HttpGet]
        public async Task<IActionResult> EditEmployee(int id)
        {
            var user = await _userRepo.GetByIdAsync(id);
            if (user == null || !string.Equals(user.Role, "Employee", StringComparison.OrdinalIgnoreCase))
            {
                TempData["Error"] = "Employee not found.";
                return RedirectToAction(nameof(ManageEmployees));
            }

            var vm = new EditEmployeeViewModel
            {
                UserID = user.UserID,
                FullName = user.FullName,
                Email = user.Email,
                Phone = user.Phone,
                IsActive = user.IsActive
            };

            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditEmployee(EditEmployeeViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var user = await _userRepo.GetByIdAsync(model.UserID);
            if (user == null || !string.Equals(user.Role, "Employee", StringComparison.OrdinalIgnoreCase))
            {
                TempData["Error"] = "Employee not found.";
                return RedirectToAction(nameof(ManageEmployees));
            }

            user.FullName = model.FullName.Trim();
            user.Phone = string.IsNullOrWhiteSpace(model.Phone) ? null : model.Phone.Trim();
            user.IsActive = model.IsActive;

            if (!string.IsNullOrWhiteSpace(model.NewPassword))
            {
                user.PasswordHash = AccountController.HashPassword(model.NewPassword.Trim());
                await _userRepo.ResetPasswordAsync(user.Email, user.PasswordHash);
            }

            await _userRepo.UpdateUserAsync(user);

            var currentUserIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!string.IsNullOrEmpty(currentUserIdStr) && int.TryParse(currentUserIdStr, out int currentUserId))
            {
                await _configRepo.LogAuditAsync(currentUserId, "EditEmployee", "User", user.UserID, $"Updated employee {user.FullName}");
            }

            TempData["Success"] = $"Employee '{user.FullName}' details updated successfully.";
            return RedirectToAction(nameof(ManageEmployees));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteEmployee(int id)
        {
            var user = await _userRepo.GetByIdAsync(id);
            if (user != null && string.Equals(user.Role, "Employee", StringComparison.OrdinalIgnoreCase))
            {
                await _userRepo.DeleteUserAsync(id);

                var currentUserIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (!string.IsNullOrEmpty(currentUserIdStr) && int.TryParse(currentUserIdStr, out int currentUserId))
                {
                    await _configRepo.LogAuditAsync(currentUserId, "DeleteEmployee", "User", id, $"Deleted employee #{id} ({user.FullName})");
                }

                TempData["Success"] = $"Employee '{user.FullName}' (#{id}) was removed successfully.";
            }
            else
            {
                TempData["Error"] = "Employee not found.";
            }

            return RedirectToAction(nameof(ManageEmployees));
        }

        // --- Full CRUD for Properties on behalf of Owners ---
        public async Task<IActionResult> ManageProperties()
        {
            var properties = await _propRepo.GetAllPropertiesAsync();
            return View(properties);
        }

        [HttpGet]
        public async Task<IActionResult> ManageBookings(string? status = null)
        {
            var bookings = await _bookingRepo.GetAllBookingsAsync();
            if (!string.IsNullOrEmpty(status) && !string.Equals(status, "All", StringComparison.OrdinalIgnoreCase))
            {
                bookings = bookings.Where(b => string.Equals(b.Status, status, StringComparison.OrdinalIgnoreCase)).ToList();
            }
            ViewBag.SelectedStatus = status ?? "All";
            return View(bookings);
        }

        [HttpGet]
        public async Task<IActionResult> CreateProperty()
        {
            var vm = new PropertyEditViewModel
            {
                AvailableTypes = await _propRepo.GetPropertyTypesAsync(),
                AvailableOccasions = await _propRepo.GetOccasionsAsync(),
                AvailableAmenities = await _propRepo.GetAllAmenitiesAsync(),
                AvailableOwners = await _userRepo.GetUsersByRoleAsync("Owner"),
                AvailableEventPackages = await _eventPackageRepo.GetActivePackagesAsync()
            };
            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateProperty(PropertyEditViewModel vm)
        {
            var prop = new Property
            {
                OwnerID = vm.OwnerID,
                PropertyTypeID = vm.PropertyTypeID,
                Title = vm.Title,
                Description = vm.Description,
                Location = vm.Location,
                FormattedAddress = vm.FormattedAddress,
                Latitude = vm.Latitude,
                Longitude = vm.Longitude,
                MapUrl = vm.MapUrl,
                PricePerNight = vm.PricePerNight,
                SecurityDepositAmount = vm.SecurityDepositAmount,
                CommissionRate = vm.CommissionRate,
                EventCommissionRate = vm.EventCommissionRate,
                FreeCancellationWindowHours = vm.FreeCancellationWindowHours,
                CancellationCutoffHours = vm.CancellationCutoffHours,
                CancellationFeeType = vm.CancellationFeeType,
                CancellationFeeValue = vm.CancellationFeeValue,
                MaxGuests = vm.MaxGuests,
                ListingStatus = "Active",
                IsApproved = true,
                OccasionIDs = vm.SelectedOccasionIDs,
                AmenityIDs = vm.SelectedAmenityIDs
            };

            var propId = await _propRepo.CreatePropertyAsync(prop);

            // Persist per-package custom event commissions if set
            if (propId > 0 && Request.Form.Keys.Any(k => k.StartsWith("pkgComm_")))
            {
                var commList = new List<(int packageId, decimal rate)>();
                foreach (var key in Request.Form.Keys.Where(k => k.StartsWith("pkgComm_")))
                {
                    var pkgIdStr = key.Substring("pkgComm_".Length);
                    if (int.TryParse(pkgIdStr, out int pkgId) && decimal.TryParse(Request.Form[key], out decimal rate) && rate >= 0)
                    {
                        commList.Add((pkgId, rate));
                    }
                }
                await _propRepo.SetPropertyEventCommissionsAsync(propId, commList);
            }
            if (!string.IsNullOrEmpty(vm.ImageUrlInput))
            {
                await _propRepo.AddPropertyImageAsync(propId, vm.ImageUrlInput, true);
                await _propRepo.AddPropertyMediaAsync(new PropertyMedia
                {
                    PropertyID = propId,
                    MediaType = "photo",
                    SourceType = "url",
                    StorageUrl = vm.ImageUrlInput,
                    IsPrimary = true
                });
            }

            if (!string.IsNullOrEmpty(vm.AdditionalMediaJson))
            {
                try
                {
                    var mediaItems = System.Text.Json.JsonSerializer.Deserialize<List<PropertyMedia>>(vm.AdditionalMediaJson, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (mediaItems != null)
                    {
                        foreach (var item in mediaItems)
                        {
                            if (!string.IsNullOrWhiteSpace(item.StorageUrl))
                            {
                                item.PropertyID = propId;
                                await _propRepo.AddPropertyMediaAsync(item);
                                if (item.IsPrimary || string.IsNullOrEmpty(vm.ImageUrlInput))
                                {
                                    await _propRepo.AddPropertyImageAsync(propId, item.StorageUrl, item.IsPrimary);
                                }
                            }
                        }
                    }
                }
                catch { }
            }

            var currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            await _configRepo.LogAuditAsync(currentUserId, "CreateProperty", "Property", propId, $"Created property {prop.Title} on behalf of Owner #{prop.OwnerID}");

            return RedirectToAction(nameof(ManageProperties));
        }

        [HttpGet]
        public async Task<IActionResult> EditProperty(int id)
        {
            var prop = await _propRepo.GetByIdAsync(id);
            if (prop == null) return NotFound();

            var vm = new PropertyEditViewModel
            {
                PropertyID = prop.PropertyID,
                OwnerID = prop.OwnerID,
                PropertyTypeID = prop.PropertyTypeID,
                Title = prop.Title,
                Description = prop.Description,
                Location = prop.Location,
                FormattedAddress = prop.FormattedAddress,
                Latitude = prop.Latitude,
                Longitude = prop.Longitude,
                MapUrl = prop.MapUrl,
                PricePerNight = prop.PricePerNight,
                SecurityDepositAmount = prop.SecurityDepositAmount,
                CommissionRate = prop.CommissionRate,
                EventCommissionRate = prop.EventCommissionRate,
                FreeCancellationWindowHours = prop.FreeCancellationWindowHours,
                CancellationCutoffHours = prop.CancellationCutoffHours,
                CancellationFeeType = prop.CancellationFeeType,
                CancellationFeeValue = prop.CancellationFeeValue,
                MaxGuests = prop.MaxGuests,
                ListingStatus = prop.ListingStatus,
                IsApproved = prop.IsApproved,
                SelectedOccasionIDs = prop.OccasionIDs,
                SelectedAmenityIDs = prop.AmenityIDs,
                ExistingMedia = prop.MediaList,
                ImageUrlInput = prop.PrimaryImageUrl,
                AvailableTypes = await _propRepo.GetPropertyTypesAsync(),
                AvailableOccasions = await _propRepo.GetOccasionsAsync(),
                AvailableAmenities = await _propRepo.GetAllAmenitiesAsync(),
                AvailableOwners = await _userRepo.GetUsersByRoleAsync("Owner"),
                PropertyEventCommissions = await _propRepo.GetPropertyEventCommissionsAsync(id),
                AvailableEventPackages = await _eventPackageRepo.GetActivePackagesAsync()
            };
            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditProperty(PropertyEditViewModel vm)
        {
            var prop = new Property
            {
                PropertyID = vm.PropertyID,
                OwnerID = vm.OwnerID,
                PropertyTypeID = vm.PropertyTypeID,
                Title = vm.Title,
                Description = vm.Description,
                Location = vm.Location,
                FormattedAddress = vm.FormattedAddress,
                Latitude = vm.Latitude,
                Longitude = vm.Longitude,
                MapUrl = vm.MapUrl,
                PricePerNight = vm.PricePerNight,
                SecurityDepositAmount = vm.SecurityDepositAmount,
                CommissionRate = vm.CommissionRate,
                EventCommissionRate = vm.EventCommissionRate,
                FreeCancellationWindowHours = vm.FreeCancellationWindowHours,
                CancellationCutoffHours = vm.CancellationCutoffHours,
                CancellationFeeType = vm.CancellationFeeType,
                CancellationFeeValue = vm.CancellationFeeValue,
                MaxGuests = vm.MaxGuests,
                ListingStatus = vm.ListingStatus,
                OccasionIDs = vm.SelectedOccasionIDs,
                AmenityIDs = vm.SelectedAmenityIDs
            };

            await _propRepo.UpdatePropertyAsync(prop);

            // Persist per-package custom event commissions if set
            if (Request.Form.Keys.Any(k => k.StartsWith("pkgComm_")))
            {
                var commList = new List<(int packageId, decimal rate)>();
                foreach (var key in Request.Form.Keys.Where(k => k.StartsWith("pkgComm_")))
                {
                    var pkgIdStr = key.Substring("pkgComm_".Length);
                    if (int.TryParse(pkgIdStr, out int pkgId) && decimal.TryParse(Request.Form[key], out decimal rate) && rate >= 0)
                    {
                        commList.Add((pkgId, rate));
                    }
                }
                await _propRepo.SetPropertyEventCommissionsAsync(vm.PropertyID, commList);
            }

            if (!string.IsNullOrEmpty(vm.ImageUrlInput))
            {
                await _propRepo.AddPropertyImageAsync(prop.PropertyID, vm.ImageUrlInput, true);
            }

            if (!string.IsNullOrEmpty(vm.AdditionalMediaJson))
            {
                try
                {
                    var mediaItems = System.Text.Json.JsonSerializer.Deserialize<List<PropertyMedia>>(vm.AdditionalMediaJson, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (mediaItems != null)
                    {
                        foreach (var item in mediaItems)
                        {
                            if (!string.IsNullOrWhiteSpace(item.StorageUrl) && item.MediaID == 0)
                            {
                                item.PropertyID = prop.PropertyID;
                                await _propRepo.AddPropertyMediaAsync(item);
                            }
                        }
                    }
                }
                catch { }
            }

            var currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            await _configRepo.LogAuditAsync(currentUserId, "EditProperty", "Property", prop.PropertyID, $"Updated property #{prop.PropertyID}");

            return RedirectToAction(nameof(ManageProperties));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeactivateProperty(int id, bool deactivate = true, string? returnUrl = null)
        {
            var success = await _propRepo.DeactivatePropertyAsync(id, deactivate);
            var currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            if (success)
            {
                await _configRepo.LogAuditAsync(currentUserId, deactivate ? "AdminDeactivateProperty" : "AdminActivateProperty", "Property", id, $"Admin {(deactivate ? "deactivated" : "activated")} property #{id}");
                TempData["Success"] = $"Property #{id} has been {(deactivate ? "deactivated" : "activated")} successfully.";
            }
            else
            {
                TempData["Error"] = $"Unable to update status for property #{id}.";
            }

            if (!string.IsNullOrWhiteSpace(returnUrl) && (Url == null || Url.IsLocalUrl(returnUrl)))
            {
                return Redirect(returnUrl);
            }

            return RedirectToAction(nameof(ManageProperties));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteProperty(int id, string? returnUrl = null)
        {
            var success = await _propRepo.PermanentlyDeletePropertyAsync(id);
            var currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            if (success)
            {
                await _configRepo.LogAuditAsync(currentUserId, "AdminPermanentlyDeleteProperty", "Property", id, $"Admin permanently deleted property #{id}");
                TempData["Success"] = $"Property #{id} has been permanently deleted successfully.";
            }
            else
            {
                TempData["Error"] = $"Unable to permanently delete property #{id}.";
            }

            if (!string.IsNullOrWhiteSpace(returnUrl) && (Url == null || Url.IsLocalUrl(returnUrl)))
            {
                return Redirect(returnUrl);
            }

            return RedirectToAction(nameof(ManageProperties));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ApproveProperty(int id, bool approve, string? status = null, string? returnUrl = null)
        {
            var prop = await _propRepo.GetByIdAsync(id);
            if (prop == null)
            {
                return NotFound();
            }

            var targetStatus = approve ? (status ?? "Active") : (status ?? "Rejected");
            var success = await _propRepo.ApprovePropertyAsync(id, approve, targetStatus);
            var currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            if (success)
            {
                var actionName = approve ? "AdminApproveProperty" : (targetStatus == "Draft" ? "AdminRevokeProperty" : "AdminRejectProperty");
                await _configRepo.LogAuditAsync(currentUserId, actionName, "Property", id, $"Admin approval set to {approve} with status '{targetStatus}' for property '{prop.Title}'");
                TempData["Success"] = $"Property #{id} ({prop.Title}) has been {(approve ? "approved and made live" : (targetStatus == "Rejected" ? "rejected" : "revoked / set to draft"))}.";
            }
            else
            {
                TempData["Error"] = $"Unable to update approval status for property #{id}.";
            }

            if (!string.IsNullOrWhiteSpace(returnUrl) && (Url == null || Url.IsLocalUrl(returnUrl)))
            {
                return Redirect(returnUrl);
            }

            return RedirectToAction(nameof(ManageProperties));
        }

        // --- System Settings & API Configs ---
        public async Task<IActionResult> Settings()
        {
            var configs = await _configRepo.GetAllConfigsAsync();
            var settings = await _configRepo.GetAllSettingsAsync();
            var branding = await _siteSettingsService.GetSettingsAsync();
            var eventPackages = await _eventPackageRepo.GetAllPackagesAsync();

            ViewBag.Configs = configs;
            ViewBag.Settings = settings;
            ViewBag.Branding = branding;
            ViewBag.EventPackages = eventPackages;
            ViewBag.EventManagers = await _userRepo.GetUsersByRoleAsync("EventManager");

            return View(branding);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveApiConfig(ApiConfiguration config)
        {
            await _configRepo.SaveOrUpdateConfigAsync(config);
            TempData["Success"] = $"API configuration for {config.ServiceProvider} updated successfully!";
            return RedirectToAction(nameof(Settings));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveSiteBranding(SiteSettingsModel model, IFormFile? logoFile)
        {
            // If user uploads an image file, optimize and store with high-res crisp formatting in wwwroot/uploads
            if (logoFile != null && logoFile.Length > 0)
            {
                var (uploadedUrl, _) = await _mediaStorage.SaveMediaFileAsync(logoFile, "photo");
                model.LogoUrl = uploadedUrl;
            }

            await _siteSettingsService.SaveSettingsAsync(model);

            // Persist per-event specific commission overrides if submitted
            if (Request.Form.Keys.Any(k => k.StartsWith("eventPkgComm_")))
            {
                foreach (var key in Request.Form.Keys.Where(k => k.StartsWith("eventPkgComm_")))
                {
                    var rawId = key.Replace("eventPkgComm_", "");
                    if (int.TryParse(rawId, out int pkgId))
                    {
                        var rawVal = Request.Form[key].ToString().Trim();
                        decimal? rate = null;
                        if (!string.IsNullOrEmpty(rawVal) && decimal.TryParse(rawVal, out var parsedRate))
                        {
                            rate = parsedRate;
                        }
                        await _eventPackageRepo.UpdatePackageCommissionRateAsync(pkgId, rate);
                    }
                }
            }

            TempData["Success"] = "Site branding, standing commission, and specific event commissions updated successfully!";
            return RedirectToAction(nameof(Settings));
        }

        [HttpGet]
        public async Task<IActionResult> EventCommissions()
        {
            var settings = await _siteSettingsService.GetSettingsAsync();
            var eventManagers = await _userRepo.GetUsersByRoleAsync("EventManager");
            var eventPackages = await _eventPackageRepo.GetAllPackagesAsync();

            var vm = new EventCommissionsManagementViewModel
            {
                GlobalStandingEventCommission = settings.EventPlatformCommissionPercent,
                GlobalHostCommission = settings.PlatformCommissionPercent,
                EventManagers = eventManagers,
                EventPackages = eventPackages
            };

            return View(vm);
        }

        [HttpPost]
        public async Task<IActionResult> UpdateGlobalEventCommission([FromBody] UpdateGlobalEventCommissionRequest model)
        {
            if (model.CommissionRate < 0 || model.CommissionRate > 100)
            {
                return Json(new { success = false, message = "Commission rate must be between 0% and 100%." });
            }

            var currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var settings = await _siteSettingsService.GetSettingsAsync();
            settings.EventPlatformCommissionPercent = model.CommissionRate;
            await _siteSettingsService.SaveSettingsAsync(settings);

            await _configRepo.LogAuditAsync(currentUserId, "UpdateGlobalEventCommission", "SiteSetting", null,
                $"Updated default global event manager commission rate to {model.CommissionRate:0.##}%");

            return Json(new
            {
                success = true,
                message = $"Default global event manager commission rate updated to {model.CommissionRate:0.##}%!",
                rate = model.CommissionRate
            });
        }

        [HttpPost]
        public async Task<IActionResult> UpdateEventManagerCommission([FromBody] UpdateEventManagerCommissionRequest model)
        {
            if (model.UserId <= 0)
            {
                return Json(new { success = false, message = "Invalid event manager user ID." });
            }

            if (model.CommissionRate.HasValue && (model.CommissionRate.Value < 0 || model.CommissionRate.Value > 100))
            {
                return Json(new { success = false, message = "Commission rate must be between 0% and 100%." });
            }

            var user = await _userRepo.GetByIdAsync(model.UserId);
            if (user == null || user.Role != "EventManager")
            {
                return Json(new { success = false, message = "Event Manager not found." });
            }

            bool updated = await _userRepo.UpdateUserCommissionRateAsync(model.UserId, model.CommissionRate);
            if (updated)
            {
                var currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
                string desc = model.CommissionRate.HasValue
                    ? $"Set custom commission for event manager '{user.FullName}' (#{user.UserID}) to {model.CommissionRate.Value:0.##}%"
                    : $"Reverted commission for event manager '{user.FullName}' (#{user.UserID}) to default standing rate";

                await _configRepo.LogAuditAsync(currentUserId, "UpdateEventManagerCommission", "User", model.UserId, desc);

                return Json(new
                {
                    success = true,
                    message = $"Commission rate for '{user.FullName}' updated successfully!",
                    rate = model.CommissionRate
                });
            }

            return Json(new { success = false, message = "Failed to update event manager commission rate." });
        }

        [HttpPost]
        public async Task<IActionResult> UpdateEventCommission([FromBody] UpdateEventCommissionRequest model)
        {
            if (model.PackageId <= 0)
            {
                return Json(new { success = false, message = "Invalid event package ID." });
            }

            var pkg = await _eventPackageRepo.GetPackageByIdAsync(model.PackageId);
            if (pkg == null)
            {
                return Json(new { success = false, message = "Event package not found." });
            }

            bool updated = await _eventPackageRepo.UpdatePackageCommissionRateAsync(model.PackageId, model.CommissionRate);
            if (updated)
            {
                var currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
                string desc = model.CommissionRate.HasValue 
                    ? $"Set custom commission for event '{pkg.Name}' to {model.CommissionRate.Value}%" 
                    : $"Reverted commission for event '{pkg.Name}' to standing default";
                await _configRepo.LogAuditAsync(currentUserId, "UpdateEventCommission", "EventPackage", model.PackageId, desc);

                return Json(new { 
                    success = true, 
                    message = $"Commission for '{pkg.Name}' updated successfully!",
                    rate = model.CommissionRate
                });
            }

            return Json(new { success = false, message = "Failed to update event commission." });
        }

        [HttpPost]
        public async Task<IActionResult> UpdateBookingEventCommission([FromBody] UpdateBookingEventCommissionRequest model)
        {
            if (model.BookingId <= 0 || model.CommissionRate < 0 || model.CommissionRate > 100)
            {
                return Json(new { success = false, message = "Invalid booking ID or commission percentage." });
            }

            bool updated = await _bookingRepo.UpdateBookingEventCommissionAsync(model.BookingId, model.CommissionRate);
            if (updated)
            {
                var booking = await _bookingRepo.GetByIdAsync(model.BookingId);
                var currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
                await _configRepo.LogAuditAsync(currentUserId, "UpdateBookingEventCommission", "Booking", model.BookingId, 
                    $"Adjusted event commission on booking #{model.BookingId} to {model.CommissionRate}% (Fee: ₹{booking?.EventPlatformCommissionAmount:N2}, Payout: ₹{booking?.EventManagerPayoutAmount:N2})");

                return Json(new { 
                    success = true, 
                    message = $"Event commission for Booking #{model.BookingId} updated to {model.CommissionRate}%!",
                    platformCommission = booking?.EventPlatformCommissionAmount ?? 0m,
                    managerPayout = booking?.EventManagerPayoutAmount ?? 0m
                });
            }

            return Json(new { success = false, message = "Failed to update event commission for this booking." });
        }

        // --- Analytics & Financial Reports ---
        public async Task<IActionResult> Reports(string period = "6months")
        {
            var users       = await _userRepo.GetAllUsersAsync();
            var properties  = await _propRepo.GetAllPropertiesAsync();
            var bookings    = await _bookingRepo.GetAllBookingsAsync();
            var ledger      = await _financialRepo.GetAllEntriesAsync();
            var (totalIncome, totalExpense, _) = await _financialRepo.GetFinancialSummaryAsync();

            decimal grossBookingRevenue     = bookings.Where(b => b.Status == "Confirmed").Sum(b => b.TotalAmount);
            decimal platformCommissionEarned = Math.Round(grossBookingRevenue * 0.10m, 2);

            // ── Determine period slots ────────────────────────────────────────
            var periodSlots = BuildPeriodSlots(period);

            // ── Build per-slot stats ──────────────────────────────────────────
            var breakdown = new List<PeriodFinancialStats>();
            foreach (var (label, start, end) in periodSlots)
            {
                var slotLedger   = ledger.Where(l => l.TransactionDate.Date >= start && l.TransactionDate.Date <= end).ToList();
                var slotBookings = bookings.Where(b => b.CreatedAt.Date >= start && b.CreatedAt.Date <= end).ToList();
                var slotIncome   = slotLedger.Where(l => l.TransactionType == "Income").Sum(l => l.Amount);
                var slotExpense  = slotLedger.Where(l => l.TransactionType == "Expense").Sum(l => l.Amount);
                var slotRevenue  = slotBookings.Where(b => b.Status == "Confirmed").Sum(b => b.TotalAmount);
                var slotComm     = Math.Round(slotRevenue * 0.10m, 2);

                breakdown.Add(new PeriodFinancialStats
                {
                    Label           = label,
                    PeriodStart     = start,
                    PeriodEnd       = end,
                    TotalIncome     = slotIncome + slotComm,
                    TotalExpense    = slotExpense,
                    BookingCount    = slotBookings.Count,
                    BookingRevenue  = slotRevenue,
                    CommissionEarned = slotComm
                });
            }

            // ── Statistical analysis ──────────────────────────────────────────
            PeriodFinancialStats? highestIncome   = breakdown.MaxBy(x => x.TotalIncome);
            PeriodFinancialStats? lowestIncome    = breakdown.MinBy(x => x.TotalIncome);
            PeriodFinancialStats? highestExpense  = breakdown.MaxBy(x => x.TotalExpense);
            PeriodFinancialStats? lowestExpense   = breakdown.MinBy(x => x.TotalExpense);
            PeriodFinancialStats? highestProfit   = breakdown.MaxBy(x => x.NetProfit);
            PeriodFinancialStats? lowestProfit    = breakdown.MinBy(x => x.NetProfit);
            PeriodFinancialStats? highestBooking  = breakdown.MaxBy(x => x.BookingCount);
            decimal avgIncome    = breakdown.Count > 0 ? breakdown.Average(x => x.TotalIncome)  : 0;
            decimal avgExpense   = breakdown.Count > 0 ? breakdown.Average(x => x.TotalExpense) : 0;
            decimal avgNetProfit = breakdown.Count > 0 ? breakdown.Average(x => x.NetProfit)    : 0;
            decimal avgBookings  = breakdown.Count > 0 ? (decimal)breakdown.Average(x => x.BookingCount) : 0;

            // ── Chart arrays (all slots) ──────────────────────────────────────
            var months          = breakdown.Select(x => x.Label).ToList();
            var monthlyIncomes  = breakdown.Select(x => x.TotalIncome).ToList();
            var monthlyExpenses = breakdown.Select(x => x.TotalExpense).ToList();
            var monthlyBookings = breakdown.Select(x => x.BookingCount).ToList();

            var vm = new ReportsViewModel
            {
                TotalUsers               = users.Count,
                TotalOwners              = users.Count(u => u.Role == "Owner"),
                TotalClients             = users.Count(u => u.Role == "Client"),
                TotalProperties          = properties.Count,
                ActiveProperties         = properties.Count(p => p.ListingStatus == "Active"),
                TotalBookings            = bookings.Count,
                ConfirmedBookings        = bookings.Count(b => b.Status == "Confirmed"),
                GrossBookingRevenue      = grossBookingRevenue,
                PlatformCommissionEarned = platformCommissionEarned,
                TotalRecordedExpenses    = totalExpense,
                NetProfit                = (platformCommissionEarned + totalIncome) - totalExpense,
                SelectedPeriod           = period,
                PeriodBreakdown          = breakdown,
                HighestIncomePeriod      = highestIncome,
                LowestIncomePeriod       = lowestIncome,
                HighestExpensePeriod     = highestExpense,
                LowestExpensePeriod      = lowestExpense,
                HighestProfitPeriod      = highestProfit,
                LowestProfitPeriod       = lowestProfit,
                HighestBookingPeriod     = highestBooking,
                AvgIncome                = Math.Round(avgIncome, 2),
                AvgExpense               = Math.Round(avgExpense, 2),
                AvgNetProfit             = Math.Round(avgNetProfit, 2),
                AvgBookings              = Math.Round(avgBookings, 2),
                RecentLedgerEntries      = ledger.Take(30).ToList(),
                Months                   = months,
                MonthlyIncomes           = monthlyIncomes,
                MonthlyExpenses          = monthlyExpenses,
                MonthlyBookings          = monthlyBookings
            };

            return View(vm);
        }

        // ── Period slot builder ───────────────────────────────────────────────
        private static List<(string Label, DateTime Start, DateTime End)> BuildPeriodSlots(string period)
        {
            var today = DateTime.Today;
            var slots = new List<(string, DateTime, DateTime)>();

            switch (period)
            {
                case "daily":
                    // Last 14 days
                    for (int i = 13; i >= 0; i--)
                    {
                        var d = today.AddDays(-i);
                        slots.Add((d.ToString("dd MMM"), d, d));
                    }
                    break;

                case "weekly":
                    // Last 12 weeks
                    for (int i = 11; i >= 0; i--)
                    {
                        var weekEnd   = today.AddDays(-(i * 7));
                        var weekStart = weekEnd.AddDays(-6);
                        slots.Add(($"W{weekStart:dd MMM}", weekStart, weekEnd));
                    }
                    break;

                case "monthly":
                    // Last 12 months
                    for (int i = 11; i >= 0; i--)
                    {
                        var dt  = today.AddMonths(-i);
                        var ms  = new DateTime(dt.Year, dt.Month, 1);
                        var me  = ms.AddMonths(1).AddDays(-1);
                        slots.Add((dt.ToString("MMM yyyy"), ms, me));
                    }
                    break;

                case "2months":
                    // Last 12 bi-monthly periods
                    for (int i = 11; i >= 0; i--)
                    {
                        var endMonth   = today.AddMonths(-(i * 2));
                        var startMonth = endMonth.AddMonths(-1);
                        var ms = new DateTime(startMonth.Year, startMonth.Month, 1);
                        var me = new DateTime(endMonth.Year, endMonth.Month, 1).AddMonths(1).AddDays(-1);
                        slots.Add(($"{ms:MMM}-{me:MMM yy}", ms, me));
                    }
                    break;

                case "quarterly":
                    // Last 8 quarters
                    for (int i = 7; i >= 0; i--)
                    {
                        var refDate    = today.AddMonths(-(i * 3));
                        int qMonth     = ((refDate.Month - 1) / 3) * 3 + 1;
                        var ms = new DateTime(refDate.Year, qMonth, 1);
                        var me = ms.AddMonths(3).AddDays(-1);
                        slots.Add(($"Q{(qMonth - 1) / 3 + 1} {refDate.Year}", ms, me));
                    }
                    break;

                case "4months":
                    // Last 6 four-month periods
                    for (int i = 5; i >= 0; i--)
                    {
                        var endMonth   = today.AddMonths(-(i * 4));
                        var startMonth = endMonth.AddMonths(-3);
                        var ms = new DateTime(startMonth.Year, startMonth.Month, 1);
                        var me = new DateTime(endMonth.Year, endMonth.Month, 1).AddMonths(1).AddDays(-1);
                        slots.Add(($"{ms:MMM}-{me:MMM yy}", ms, me));
                    }
                    break;

                case "6months":
                    // Last 6 semi-annual periods
                    for (int i = 5; i >= 0; i--)
                    {
                        var endMonth   = today.AddMonths(-(i * 6));
                        var startMonth = endMonth.AddMonths(-5);
                        var ms = new DateTime(startMonth.Year, startMonth.Month, 1);
                        var me = new DateTime(endMonth.Year, endMonth.Month, 1).AddMonths(1).AddDays(-1);
                        slots.Add(($"{ms:MMM yy}-{me:MMM yy}", ms, me));
                    }
                    break;

                case "yearly":
                    // Last 5 years, monthly within each year
                    for (int i = 4; i >= 0; i--)
                    {
                        int yr = today.Year - i;
                        var ms = new DateTime(yr, 1, 1);
                        var me = new DateTime(yr, 12, 31);
                        slots.Add((yr.ToString(), ms, me));
                    }
                    break;

                case "2years":
                    // Last 6 two-year periods
                    for (int i = 5; i >= 0; i--)
                    {
                        int endYr  = today.Year - (i * 2);
                        int startYr = endYr - 1;
                        var ms = new DateTime(startYr, 1, 1);
                        var me = new DateTime(endYr, 12, 31);
                        slots.Add(($"{startYr}-{endYr}", ms, me));
                    }
                    break;

                default:
                    goto case "6months";
            }

            return slots;
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> LogFinancialEntry(FinancialEntry entry)
        {
            if (entry.Amount <= 0 || string.IsNullOrWhiteSpace(entry.Title))
            {
                TempData["Error"] = "Please provide a valid title and amount greater than 0.";
                return RedirectToAction(nameof(Reports));
            }

            var currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            entry.LoggedByUserID = currentUserId;

            await _financialRepo.AddEntryAsync(entry);
            TempData["Success"] = $"{entry.TransactionType} record '{entry.Title}' of ₹{entry.Amount:N2} logged successfully!";
            return RedirectToAction(nameof(Reports));
        }

        // ── EXPORT: Excel ─────────────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> ExportExcel(string period = "6months")
        {
            var ledger   = await _financialRepo.GetAllEntriesAsync();
            var bookings = await _bookingRepo.GetAllBookingsAsync();
            var slots    = BuildPeriodSlots(period);

            using var wb = new XLWorkbook();

            // Sheet 1: Summary
            var ws = wb.Worksheets.Add("Financial Summary");
            ws.Cell(1, 1).Value = "BookMyFun – Financial Report";
            ws.Cell(1, 1).Style.Font.Bold = true;
            ws.Cell(1, 1).Style.Font.FontSize = 16;
            ws.Range(1, 1, 1, 7).Merge();

            ws.Cell(2, 1).Value = $"Period: {period}   |   Generated: {DateTime.Now:dd MMM yyyy HH:mm}";
            ws.Range(2, 1, 2, 7).Merge();
            ws.Cell(2, 1).Style.Font.Italic = true;

            // Header row
            int row = 4;
            var headers = new[] { "Period", "Start Date", "End Date", "Total Income (₹)", "Total Expense (₹)", "Net Profit (₹)", "Bookings", "Booking Revenue (₹)", "Commission (₹)" };
            for (int c = 0; c < headers.Length; c++)
            {
                ws.Cell(row, c + 1).Value = headers[c];
                ws.Cell(row, c + 1).Style.Font.Bold = true;
                ws.Cell(row, c + 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#1e3a5f");
                ws.Cell(row, c + 1).Style.Font.FontColor = XLColor.White;
                ws.Cell(row, c + 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            }
            row++;

            decimal totalInc = 0, totalExp = 0;
            int totalBook = 0;

            foreach (var (label, start, end) in slots)
            {
                var slotLedger   = ledger.Where(l => l.TransactionDate.Date >= start && l.TransactionDate.Date <= end).ToList();
                var slotBookings = bookings.Where(b => b.CreatedAt.Date >= start && b.CreatedAt.Date <= end).ToList();
                var income       = slotLedger.Where(l => l.TransactionType == "Income").Sum(l => l.Amount);
                var expense      = slotLedger.Where(l => l.TransactionType == "Expense").Sum(l => l.Amount);
                var revenue      = slotBookings.Where(b => b.Status == "Confirmed").Sum(b => b.TotalAmount);
                var comm         = Math.Round(revenue * 0.10m, 2);
                income += comm;

                ws.Cell(row, 1).Value = label;
                ws.Cell(row, 2).Value = start.ToString("dd MMM yyyy");
                ws.Cell(row, 3).Value = end.ToString("dd MMM yyyy");
                ws.Cell(row, 4).Value = (double)income;
                ws.Cell(row, 5).Value = (double)expense;
                ws.Cell(row, 6).Value = (double)(income - expense);
                ws.Cell(row, 7).Value = slotBookings.Count;
                ws.Cell(row, 8).Value = (double)revenue;
                ws.Cell(row, 9).Value = (double)comm;

                // Colour net profit column
                var netCell = ws.Cell(row, 6);
                netCell.Style.Font.FontColor = (income - expense) >= 0 ? XLColor.DarkGreen : XLColor.Red;
                netCell.Style.Font.Bold = true;

                totalInc  += income;
                totalExp  += expense;
                totalBook += slotBookings.Count;
                row++;
            }

            // Totals row
            ws.Cell(row, 1).Value = "TOTAL";
            ws.Cell(row, 1).Style.Font.Bold = true;
            ws.Cell(row, 4).Value = (double)totalInc;
            ws.Cell(row, 5).Value = (double)totalExp;
            ws.Cell(row, 6).Value = (double)(totalInc - totalExp);
            ws.Cell(row, 7).Value = totalBook;
            for (int c = 1; c <= 9; c++)
            {
                ws.Cell(row, c).Style.Fill.BackgroundColor = XLColor.FromHtml("#f1f5f9");
                ws.Cell(row, c).Style.Font.Bold = true;
            }

            // Averages row
            row++;
            ws.Cell(row, 1).Value = "AVERAGE";
            ws.Cell(row, 4).Value = slots.Count > 0 ? (double)(totalInc / slots.Count) : 0;
            ws.Cell(row, 5).Value = slots.Count > 0 ? (double)(totalExp / slots.Count) : 0;
            ws.Cell(row, 6).Value = slots.Count > 0 ? (double)((totalInc - totalExp) / slots.Count) : 0;
            ws.Cell(row, 7).Value = slots.Count > 0 ? (double)totalBook / slots.Count : 0;
            ws.Cell(row, 1).Style.Font.Italic = true;

            ws.Columns().AdjustToContents();

            // Sheet 2: Full Ledger
            var ws2 = wb.Worksheets.Add("Full Ledger");
            var ledgerHeaders = new[] { "Date", "Type", "Category", "Title", "Details", "Amount (₹)" };
            for (int c = 0; c < ledgerHeaders.Length; c++)
            {
                ws2.Cell(1, c + 1).Value = ledgerHeaders[c];
                ws2.Cell(1, c + 1).Style.Font.Bold = true;
                ws2.Cell(1, c + 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#1e3a5f");
                ws2.Cell(1, c + 1).Style.Font.FontColor = XLColor.White;
            }
            int lr = 2;
            foreach (var e in ledger)
            {
                ws2.Cell(lr, 1).Value = e.TransactionDate.ToString("dd MMM yyyy");
                ws2.Cell(lr, 2).Value = e.TransactionType;
                ws2.Cell(lr, 3).Value = e.Category;
                ws2.Cell(lr, 4).Value = e.Title;
                ws2.Cell(lr, 5).Value = e.Details ?? "";
                ws2.Cell(lr, 6).Value = (double)e.Amount;
                ws2.Cell(lr, 2).Style.Font.FontColor = e.TransactionType == "Income" ? XLColor.DarkGreen : XLColor.Red;
                lr++;
            }
            ws2.Columns().AdjustToContents();

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return File(ms.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"BookMyFun_Report_{period}_{DateTime.Today:yyyyMMdd}.xlsx");
        }

        // ── EXPORT: CSV ───────────────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> ExportCsv(string period = "6months")
        {
            var ledger   = await _financialRepo.GetAllEntriesAsync();
            var bookings = await _bookingRepo.GetAllBookingsAsync();
            var slots    = BuildPeriodSlots(period);

            var sb = new StringBuilder();
            sb.AppendLine("Period,Start Date,End Date,Total Income (INR),Total Expense (INR),Net Profit (INR),Bookings,Booking Revenue (INR),Commission (INR)");

            foreach (var (label, start, end) in slots)
            {
                var slotLedger   = ledger.Where(l => l.TransactionDate.Date >= start && l.TransactionDate.Date <= end).ToList();
                var slotBookings = bookings.Where(b => b.CreatedAt.Date >= start && b.CreatedAt.Date <= end).ToList();
                var income       = slotLedger.Where(l => l.TransactionType == "Income").Sum(l => l.Amount);
                var expense      = slotLedger.Where(l => l.TransactionType == "Expense").Sum(l => l.Amount);
                var revenue      = slotBookings.Where(b => b.Status == "Confirmed").Sum(b => b.TotalAmount);
                var comm         = Math.Round(revenue * 0.10m, 2);
                income += comm;

                sb.AppendLine($"\"{label}\",{start:dd MMM yyyy},{end:dd MMM yyyy},{income},{expense},{income - expense},{slotBookings.Count},{revenue},{comm}");
            }

            // Full ledger
            sb.AppendLine();
            sb.AppendLine("--- Full Ledger ---");
            sb.AppendLine("Date,Type,Category,Title,Details,Amount (INR)");
            foreach (var e in ledger)
                sb.AppendLine($"{e.TransactionDate:dd MMM yyyy},{e.TransactionType},{e.Category},\"{e.Title}\",\"{e.Details ?? ""}\",{e.Amount}");

            return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv",
                $"BookMyFun_Report_{period}_{DateTime.Today:yyyyMMdd}.csv");
        }

        // ── EXPORT: PDF (HTML-based, browser-printable) ───────────────────────
        [HttpGet]
        public async Task<IActionResult> ExportPdf(string period = "6months")
        {
            var ledger   = await _financialRepo.GetAllEntriesAsync();
            var bookings = await _bookingRepo.GetAllBookingsAsync();
            var slots    = BuildPeriodSlots(period);
            var siteSettings = await _siteSettingsService.GetSettingsAsync();

            decimal grandInc = 0, grandExp = 0;
            int grandBook = 0;

            var rows = new StringBuilder();
            foreach (var (label, start, end) in slots)
            {
                var slotLedger   = ledger.Where(l => l.TransactionDate.Date >= start && l.TransactionDate.Date <= end).ToList();
                var slotBookings = bookings.Where(b => b.CreatedAt.Date >= start && b.CreatedAt.Date <= end).ToList();
                var income       = slotLedger.Where(l => l.TransactionType == "Income").Sum(l => l.Amount);
                var expense      = slotLedger.Where(l => l.TransactionType == "Expense").Sum(l => l.Amount);
                var revenue      = slotBookings.Where(b => b.Status == "Confirmed").Sum(b => b.TotalAmount);
                var comm         = Math.Round(revenue * 0.10m, 2);
                income += comm;
                var net  = income - expense;
                var netColor = net >= 0 ? "#166534" : "#991b1b";

                grandInc  += income;
                grandExp  += expense;
                grandBook += slotBookings.Count;

                rows.Append($@"<tr>
                    <td>{label}</td>
                    <td>{start:dd MMM yyyy}</td>
                    <td>{end:dd MMM yyyy}</td>
                    <td class='num'>₹{income:N2}</td>
                    <td class='num'>₹{expense:N2}</td>
                    <td class='num' style='color:{netColor};font-weight:700'>₹{net:N2}</td>
                    <td class='num'>{slotBookings.Count}</td>
                    <td class='num'>₹{revenue:N2}</td>
                    <td class='num'>₹{comm:N2}</td>
                </tr>");
            }

            var periodLabel = period switch
            {
                "daily"     => "Daily (Last 14 Days)",
                "weekly"    => "Weekly (Last 12 Weeks)",
                "monthly"   => "Monthly (Last 12 Months)",
                "2months"   => "Bi-Monthly",
                "quarterly" => "Quarterly",
                "4months"   => "4-Month Periods",
                "6months"   => "Semi-Annual (6 Months)",
                "yearly"    => "Annual (Last 5 Years)",
                "2years"    => "Bi-Annual (2-Year Periods)",
                _ => period
            };

            var html = $@"<!DOCTYPE html>
<html lang='en'>
<head>
<meta charset='UTF-8'/>
<title>{siteSettings.SiteName} – Financial Report</title>
<style>
  @page {{ size: A4 landscape; margin: 1.5cm; }}
  * {{ box-sizing: border-box; margin: 0; padding: 0; font-family: 'Segoe UI', Arial, sans-serif; }}
  body {{ background: #fff; color: #1e293b; font-size: 11px; }}
  .header {{ background: #0f172a; color: #f8fafc; padding: 18px 24px; border-radius: 8px; margin-bottom: 18px; display: flex; justify-content: space-between; align-items: center; }}
  .header h1 {{ font-size: 20px; font-weight: 700; letter-spacing: -0.5px; }}
  .header .meta {{ font-size: 11px; opacity: 0.7; text-align: right; line-height: 1.6; }}
  .kpi-grid {{ display: grid; grid-template-columns: repeat(4, 1fr); gap: 12px; margin-bottom: 18px; }}
  .kpi {{ background: #f8fafc; border: 1px solid #e2e8f0; border-radius: 8px; padding: 12px 14px; border-left: 4px solid #2563eb; }}
  .kpi.green {{ border-left-color: #16a34a; }}
  .kpi.red {{ border-left-color: #dc2626; }}
  .kpi label {{ font-size: 9px; text-transform: uppercase; letter-spacing: 0.05em; color: #64748b; font-weight: 600; }}
  .kpi span {{ display: block; font-size: 16px; font-weight: 700; color: #1e293b; margin-top: 2px; }}
  table {{ width: 100%; border-collapse: collapse; font-size: 10px; }}
  thead th {{ background: #1e3a5f; color: #fff; padding: 8px 10px; text-align: center; font-size: 9px; text-transform: uppercase; letter-spacing: 0.04em; }}
  tbody td {{ padding: 7px 10px; border-bottom: 1px solid #e2e8f0; vertical-align: middle; }}
  tbody tr:nth-child(even) {{ background: #f8fafc; }}
  .num {{ text-align: right; font-variant-numeric: tabular-nums; }}
  tfoot td {{ background: #f1f5f9; font-weight: 700; border-top: 2px solid #cbd5e1; padding: 8px 10px; }}
  .section-title {{ font-size: 13px; font-weight: 700; margin-bottom: 10px; color: #1e293b; border-left: 4px solid #2563eb; padding-left: 10px; }}
  .print-note {{ text-align: center; margin-top: 20px; font-size: 10px; color: #64748b; }}
  @media print {{ .no-print {{ display: none; }} body {{ print-color-adjust: exact; -webkit-print-color-adjust: exact; }} }}
  .btn-print {{ display: block; margin: 0 auto 18px; padding: 8px 24px; background: #2563eb; color: #fff; border: none; border-radius: 6px; cursor: pointer; font-size: 13px; font-weight: 600; }}
</style>
</head>
<body>
<button class='btn-print no-print' onclick='window.print()'>🖨️ Print / Save as PDF</button>
<div class='header'>
  <div>
    <h1>📊 {siteSettings.SiteName} – Financial Report</h1>
    <div style='font-size:12px;opacity:0.8;margin-top:4px'>Period Basis: {periodLabel}</div>
  </div>
  <div class='meta'>
    Generated: {DateTime.Now:dd MMM yyyy, hh:mm tt}<br/>
    {siteSettings.SupportEmail}
  </div>
</div>

<div class='kpi-grid'>
  <div class='kpi'>
    <label>Total Income (Ledger + Commission)</label>
    <span>₹{grandInc:N2}</span>
  </div>
  <div class='kpi red'>
    <label>Total Expenses</label>
    <span>₹{grandExp:N2}</span>
  </div>
  <div class='kpi {(grandInc - grandExp >= 0 ? "green" : "red")}'>
    <label>Net Profit / Loss</label>
    <span>₹{grandInc - grandExp:N2}</span>
  </div>
  <div class='kpi green'>
    <label>Total Bookings (Period)</label>
    <span>{grandBook}</span>
  </div>
</div>

<div class='section-title'>Period-wise Breakdown</div>
<table>
<thead>
  <tr>
    <th>Period</th><th>From</th><th>To</th>
    <th>Income (₹)</th><th>Expense (₹)</th><th>Net (₹)</th>
    <th>Bookings</th><th>Booking Rev. (₹)</th><th>Commission (₹)</th>
  </tr>
</thead>
<tbody>{rows}</tbody>
<tfoot>
  <tr>
    <td colspan='3'>TOTAL</td>
    <td class='num'>₹{grandInc:N2}</td>
    <td class='num'>₹{grandExp:N2}</td>
    <td class='num'>₹{grandInc - grandExp:N2}</td>
    <td class='num'>{grandBook}</td>
    <td colspan='2'></td>
  </tr>
  <tr>
    <td colspan='3'>AVERAGE / PERIOD</td>
    <td class='num'>₹{(slots.Count > 0 ? grandInc / slots.Count : 0):N2}</td>
    <td class='num'>₹{(slots.Count > 0 ? grandExp / slots.Count : 0):N2}</td>
    <td class='num'>₹{(slots.Count > 0 ? (grandInc - grandExp) / slots.Count : 0):N2}</td>
    <td class='num'>{(slots.Count > 0 ? (decimal)grandBook / slots.Count : 0):N1}</td>
    <td colspan='2'></td>
  </tr>
</tfoot>
</table>
<p class='print-note'>This report was generated by {siteSettings.SiteName} platform. For queries contact {siteSettings.SupportEmail}.</p>
</body>
</html>";

            return Content(html, "text/html");
        }



        // --- Owner Application & Trial Period Reviews ---
        public async Task<IActionResult> OwnerRequests()
        {
            var requests = await _ownerReqRepo.GetAllRequestsAsync();
            return View(requests);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ApproveOwnerRequest(int requestId, string? notes)
        {
            var currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var (success, accountId, generatedPassword, email, phone, fullName) = await _partnerLeadService.ApproveOwnerRequestAsync(requestId, currentUserId, notes);

            if (success)
            {
                await _configRepo.LogAuditAsync(currentUserId, "ApproveOwnerRequest", "OwnerRequest", requestId, $"Approved owner request #{requestId} for account #{accountId}");
                TempData["Success"] = $"Owner application #{requestId} approved! Account ID: #{accountId}, Temporary Password: {generatedPassword}. Credentials dispatched to {email} via WhatsApp and Email.";
            }
            else
            {
                TempData["Error"] = $"Unable to approve owner request #{requestId}.";
            }
            return RedirectToAction(nameof(OwnerRequests));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RejectOwnerRequest(int requestId, string? notes)
        {
            var currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            await _ownerReqRepo.RejectOwnerRequestAsync(requestId, currentUserId, notes);

            await _configRepo.LogAuditAsync(currentUserId, "RejectOwnerRequest", "OwnerRequest", requestId, $"Rejected owner request #{requestId}. Purged properties and converted back to client.");
            TempData["Success"] = "Owner application rejected. Trial properties have been purged and user demoted to Client.";
            return RedirectToAction(nameof(OwnerRequests));
        }

        // --- Customer Support Queries Management ---
        public async Task<IActionResult> SupportQueries()
        {
            var contacts = await _contactRepo.GetAllContactsAsync();
            return View(contacts);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateContactStatus(int contactId, string status, string? responseNotes = null)
        {
            var currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            await _contactRepo.UpdateStatusAsync(contactId, status, currentUserId, responseNotes);
            await _configRepo.LogAuditAsync(currentUserId, "UpdateSupportQuery", "ContactUs", contactId, $"Updated ticket #{contactId} status to {status}");
            TempData["Success"] = $"Ticket #{contactId} status updated to {status} with staff accountability recorded.";
            return RedirectToAction(nameof(SupportQueries));
        }

        // --- Dedicated Refund Operations Queue ---
        public async Task<IActionResult> RefundsQueue(string? status)
        {
            var refunds = await _refundEngine.GetRefundsQueueAsync(status);
            ViewBag.SelectedStatus = status;
            return View(refunds);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> OverrideRefund(int refundId, string status, string? notes)
        {
            var currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            bool updated = await _refundEngine.OverrideRefundStatusAsync(refundId, status, currentUserId, notes);
            if (updated)
            {
                await _configRepo.LogAuditAsync(currentUserId, "AdminOverrideRefund", "BookingRefund", refundId, $"Refund #{refundId} manually updated to {status}. Notes: {notes}");
                TempData["Success"] = $"Refund #{refundId} overridden to '{status}'.";
            }
            return RedirectToAction(nameof(RefundsQueue));
        }

        // --- Partner Leads Pipeline ---
        public async Task<IActionResult> PartnerLeads(string? status)
        {
            var leads = await _partnerLeadService.GetAllLeadsAsync(status);
            ViewBag.SelectedStatus = status;
            return View(leads);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ApprovePartnerLead(int leadId)
        {
            var currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var (success, initialPassword, token, accountId, roleDisplayName, email, phone) = await _partnerLeadService.ApproveLeadAsync(leadId, currentUserId);
            if (success)
            {
                await _configRepo.LogAuditAsync(currentUserId, "AdminApproveLead", "PartnerLead", leadId, $"Approved partner lead #{leadId} and generated {roleDisplayName} account #{accountId} with temporary credentials.");
                TempData["Success"] = $"Partner Lead #{leadId} approved! Account #{accountId} ({roleDisplayName}) generated with Temporary Password: {initialPassword}. Credentials dispatched to {email} via WhatsApp and Email.";
            }
            return RedirectToAction(nameof(PartnerLeads));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RejectPartnerLead(int leadId)
        {
            var currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            bool rejected = await _partnerLeadService.RejectLeadAsync(leadId, currentUserId);
            if (rejected)
            {
                await _configRepo.LogAuditAsync(currentUserId, "AdminRejectLead", "PartnerLead", leadId, $"Rejected partner lead #{leadId}");
                TempData["Success"] = $"Partner Lead #{leadId} marked as Rejected.";
            }
            return RedirectToAction(nameof(PartnerLeads));
        }

        // --- Damage Claims Adjudication & Logging ---
        public async Task<IActionResult> DamageClaims(string? status)
        {
            var claims = await _damageClaimService.GetAllClaimsAsync(status);
            var bookings = await _bookingRepo.GetAllBookingsAsync();
            ViewBag.Bookings = bookings;
            ViewBag.SelectedStatus = status;
            return View(claims);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> LogDamageClaim(DamageClaimCreateViewModel model)
        {
            var currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            try
            {
                int claimId = await _damageClaimService.CreateClaimAsync(model, currentUserId, isStaff: true);
                await _configRepo.LogAuditAsync(currentUserId, "AdminLogDamageClaim", "DamageClaim", claimId, $"Admin logged damage claim #{claimId} for booking #{model.BookingID}");
                TempData["Success"] = $"Damage claim #{claimId} has been successfully logged on behalf of the host.";
            }
            catch (Exception ex)
            {
                TempData["Error"] = ex.Message;
            }
            return RedirectToAction(nameof(DamageClaims));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AdjudicateDamageClaim(DamageClaimAdjudicateViewModel model)
        {
            var currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            bool adjudicated = await _damageClaimService.AdjudicateClaimAsync(model, currentUserId);
            if (adjudicated)
            {
                await _configRepo.LogAuditAsync(currentUserId, "AdminAdjudicateClaim", "DamageClaim", model.ClaimID, $"Adjudicated claim #{model.ClaimID} as {model.Action}. Approved amount: {model.ApprovedAmount}");
                TempData["Success"] = $"Claim #{model.ClaimID} has been successfully adjudicated ({model.Action}).";
            }
            return RedirectToAction(nameof(DamageClaims));
        }

        // --- Client ID Verification & KYC Governance ---
        public async Task<IActionResult> IdVerifications(string? status)
        {
            var verifications = await _idVerificationService.GetAllVerificationsAsync(status);
            ViewBag.SelectedStatus = status;
            return View(verifications);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ReviewIdVerification(int verificationId, string action, string? reason)
        {
            var currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            bool reviewed = await _idVerificationService.ReviewVerificationAsync(verificationId, action, currentUserId, reason);
            if (reviewed)
            {
                await _configRepo.LogAuditAsync(currentUserId, "AdminReviewIdVerification", "ClientIdVerification", verificationId, $"Reviewed verification #{verificationId}: {action}");
                TempData["Success"] = $"Verification #{verificationId} has been marked as {action}.";
            }
            return RedirectToAction(nameof(IdVerifications));
        }

        [HttpGet]
        public IActionResult GetDocumentSignedUrl(string storageKey)
        {
            var currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            string role = User.FindFirstValue(ClaimTypes.Role) ?? "Admin";

            string token = _mediaStorage.GenerateExpiringDocumentToken(storageKey, currentUserId, role, 15);
            string url = $"/api/media/view-id-document?key={Uri.EscapeDataString(storageKey)}&token={Uri.EscapeDataString(token)}";

            return Json(new { success = true, url });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateUserSubscriptionTier(int userId, string tier, decimal? customCommissionRate)
        {
            var currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            bool updated = await _userRepo.UpdateUserTierAndCommissionAsync(userId, tier, customCommissionRate);
            if (updated)
            {
                await _configRepo.LogAuditAsync(currentUserId, "UpdateOwnerTier", "User", userId, $"Updated owner #{userId} tier to {tier}, custom commission: {customCommissionRate}%");
                TempData["Success"] = $"Owner subscription tier and custom commission rate updated successfully.";
            }
            return RedirectToAction(nameof(ManageOwners));
        }

        // --- Dynamic Analytics Dashboard & Multidimensional Filtering ---
        public async Task<IActionResult> DynamicAnalytics(string? datePreset = "7D", DateTime? startDate = null, DateTime? endDate = null, int? propertyId = null, int? ownerId = null, string? status = null)
        {
            var now = DateTime.UtcNow;
            DateTime filterStart = datePreset switch
            {
                "Today" => DateTime.Today,
                "7D" => now.AddDays(-7),
                "MTD" => new DateTime(now.Year, now.Month, 1),
                "YTD" => new DateTime(now.Year, 1, 1),
                "Custom" => startDate ?? now.AddDays(-30),
                _ => now.AddDays(-7)
            };

            DateTime filterEnd = datePreset == "Custom" && endDate.HasValue ? endDate.Value : now;

            var allBookings = await _bookingRepo.GetAllBookingsAsync();
            var allProperties = await _propRepo.GetAllPropertiesAsync();

            var filteredBookings = allBookings.Where(b => b.CreatedAt >= filterStart && b.CreatedAt <= filterEnd).ToList();

            if (propertyId.HasValue && propertyId.Value > 0)
                filteredBookings = filteredBookings.Where(b => b.PropertyID == propertyId.Value).ToList();

            if (status != null && !string.Equals(status, "All", StringComparison.OrdinalIgnoreCase))
                filteredBookings = filteredBookings.Where(b => string.Equals(b.Status, status, StringComparison.OrdinalIgnoreCase)).ToList();

            decimal gmv = filteredBookings.Where(b => b.Status == "Confirmed").Sum(b => b.TotalAmount);
            decimal eventGmv = filteredBookings.Where(b => b.Status == "Confirmed" && b.IsEventBooking).Sum(b => b.EventPackagePriceSnapshot);
            decimal propertyGmv = gmv - eventGmv;
            int totalEventBookings = filteredBookings.Count(b => b.IsEventBooking);
            decimal netPlatformCommission = filteredBookings.Where(b => b.Status == "Confirmed").Sum(b => 
                (b.PlatformCommissionAmount > 0 ? b.PlatformCommissionAmount : (b.TotalAmount - b.EventPackagePriceSnapshot) * 0.10m) +
                (b.EventPlatformCommissionAmount > 0 ? b.EventPlatformCommissionAmount : (b.IsEventBooking ? b.EventPackagePriceSnapshot * 0.10m : 0m)));
            decimal pendingPayouts = filteredBookings.Where(b => b.Status == "Confirmed").Sum(b => b.OwnerPayoutAmount > 0 ? b.OwnerPayoutAmount : (b.TotalAmount - b.EventPackagePriceSnapshot) * 0.90m);
            int activeProps = allProperties.Count(p => p.ListingStatus == "Active");
            double occupancyRate = activeProps > 0 ? Math.Min(100.0, Math.Round((double)filteredBookings.Count(b => b.Status == "Confirmed") / (activeProps * 30.0) * 100.0, 1)) : 0;

            var vm = new AdminAnalyticsViewModel
            {
                GMV = gmv,
                PropertyGMV = propertyGmv,
                EventGMV = eventGmv,
                TotalEventBookings = totalEventBookings,
                NetPlatformCommission = netPlatformCommission,
                PendingPayouts = pendingPayouts,
                ActiveProperties = activeProps,
                OccupancyRate = occupancyRate,
                DateRangePreset = datePreset ?? "7D",
                CustomStartDate = startDate,
                CustomEndDate = endDate,
                FilterPropertyID = propertyId,
                FilterOwnerID = ownerId,
                FilterStatus = status
            };

            ViewBag.Properties = allProperties;
            ViewBag.Owners = await _userRepo.GetUsersByRoleAsync("Owner");
            return View(vm);
        }

        [HttpGet]
        public async Task<IActionResult> ExportAnalyticsCsv(string? datePreset = "7D", DateTime? startDate = null, DateTime? endDate = null)
        {
            var bookings = await _bookingRepo.GetAllBookingsAsync();
            var sb = new StringBuilder();
            sb.AppendLine("BookingID,PropertyTitle,ClientName,CheckInDate,CheckOutDate,TotalAmount,Status,PlatformCommission,OwnerPayout,CreatedAt");

            foreach (var b in bookings)
            {
                sb.AppendLine($"{b.BookingID},\"{b.PropertyTitle?.Replace("\"", "\"\"")}\",\"{b.ClientName?.Replace("\"", "\"\"")}\",{b.CheckInDate:yyyy-MM-dd},{b.CheckOutDate:yyyy-MM-dd},{b.TotalAmount},{b.Status},{b.PlatformCommissionAmount},{b.OwnerPayoutAmount},{b.CreatedAt:yyyy-MM-dd HH:mm}");
            }

            return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", $"Analytics_Export_{DateTime.UtcNow:yyyyMMdd_HHmm}.csv");
        }

        [HttpGet]
        public async Task<IActionResult> ExportAnalyticsExcel(string? datePreset = "7D", DateTime? startDate = null, DateTime? endDate = null)
        {
            var bookings = await _bookingRepo.GetAllBookingsAsync();
            using var workbook = new XLWorkbook();
            var ws = workbook.Worksheets.Add("Bookings Analytics");

            ws.Cell(1, 1).Value = "Booking ID";
            ws.Cell(1, 2).Value = "Property Title";
            ws.Cell(1, 3).Value = "Client Name";
            ws.Cell(1, 4).Value = "Check-in Date";
            ws.Cell(1, 5).Value = "Check-out Date";
            ws.Cell(1, 6).Value = "Total Amount (INR)";
            ws.Cell(1, 7).Value = "Status";
            ws.Cell(1, 8).Value = "Platform Commission";
            ws.Cell(1, 9).Value = "Owner Payout";
            ws.Cell(1, 10).Value = "Security Deposit";
            ws.Cell(1, 11).Value = "Created At";

            var headerRow = ws.Range(1, 1, 1, 11);
            headerRow.Style.Font.Bold = true;
            headerRow.Style.Fill.BackgroundColor = XLColor.FromHtml("#059669");
            headerRow.Style.Font.FontColor = XLColor.White;

            int row = 2;
            foreach (var b in bookings)
            {
                ws.Cell(row, 1).Value = b.BookingID;
                ws.Cell(row, 2).Value = b.PropertyTitle ?? "";
                ws.Cell(row, 3).Value = b.ClientName ?? "";
                ws.Cell(row, 4).Value = b.CheckInDate.ToString("yyyy-MM-dd");
                ws.Cell(row, 5).Value = b.CheckOutDate.ToString("yyyy-MM-dd");
                ws.Cell(row, 6).Value = b.TotalAmount;
                ws.Cell(row, 7).Value = b.Status;
                ws.Cell(row, 8).Value = b.PlatformCommissionAmount;
                ws.Cell(row, 9).Value = b.OwnerPayoutAmount;
                ws.Cell(row, 10).Value = b.SecurityDepositAmount;
                ws.Cell(row, 11).Value = b.CreatedAt.ToString("yyyy-MM-dd HH:mm");
                row++;
            }

            ws.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"Analytics_Report_{DateTime.UtcNow:yyyyMMdd_HHmm}.xlsx");
        }
    }
}
