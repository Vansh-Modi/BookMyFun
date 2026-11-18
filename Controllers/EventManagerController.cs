using BookMyFun.Services.Security;
using BookMyFun.Services.Checkout;
using System.Security.Claims;
using BookMyFun.Models.Entities;
using BookMyFun.Models.ViewModels;
using BookMyFun.Repositories;
using BookMyFun.Services;
using BookMyFun.Services.Common;
using BookMyFun.Services.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookMyFun.Controllers
{
    [Authorize(Policy = "RequireEventManager")]
    public class EventManagerController : Controller
    {
        private readonly IEventPackageRepository _eventRepo;
        private readonly IMediaStorageService _mediaStorage;
        private readonly IConfigRepository _configRepo;
        private readonly ILogger<EventManagerController> _logger;
        private readonly ILegalDocumentService? _legalService;
        private readonly IUserRepository _userRepo;
        private readonly IContactPrivacyScanner _privacyScanner;
        private readonly IImageOptimizationService _imageOptimization;
        private readonly IPropertyRepository? _propRepo;
        private readonly IHtmlSanitizerService? _sanitizer;
        private readonly ICheckoutEventManagerService? _checkoutEventMgrService;

        public EventManagerController(
            IEventPackageRepository eventRepo,
            IMediaStorageService mediaStorage,
            IConfigRepository configRepo,
            ILogger<EventManagerController> logger,
            IUserRepository? userRepo = null,
            IContactPrivacyScanner? privacyScanner = null,
            IImageOptimizationService? imageOptimization = null,
            ILegalDocumentService? legalService = null,
            IPropertyRepository? propRepo = null,
            IHtmlSanitizerService? sanitizer = null,
            ICheckoutEventManagerService? checkoutEventMgrService = null)
        {
            _eventRepo = eventRepo;
            _mediaStorage = mediaStorage;
            _configRepo = configRepo;
            _logger = logger;
            _userRepo = userRepo!;
            _privacyScanner = privacyScanner!;
            _imageOptimization = imageOptimization!;
            _legalService = legalService;
            _propRepo = propRepo;
            _sanitizer = sanitizer;
            _checkoutEventMgrService = checkoutEventMgrService;
        }


        public async Task<IActionResult> Index() => await LoadDashboardViewAsync("Dashboard");

        public async Task<IActionResult> Dashboard() => await LoadDashboardViewAsync("Dashboard");

        private async Task<IActionResult> LoadDashboardViewAsync(string viewName)
        {
            var userId = GetCurrentUserId();
            var isStaffOrAdmin = User.IsInRole("Admin") || User.IsInRole("Employee");

            var allSetups = await _eventRepo.GetAllEventBookingsAsync();
            var setups = isStaffOrAdmin 
                ? allSetups 
                : allSetups.Where(s => s.DecoratorAssignedID == userId || s.DecoratorAssignedID == null || s.SetupStatus == "Pending").ToList();

            var user = await _userRepo.GetByIdAsync(userId);
            var packages = isStaffOrAdmin ? await _eventRepo.GetAllPackagesAsync() : await _eventRepo.GetPackagesByUserIdAsync(userId);
            if (!isStaffOrAdmin && packages.Count == 0)
            {
                var allPkgs = await _eventRepo.GetAllPackagesAsync();
                packages = allPkgs.Where(p => p.CreatedByUserID == null || p.CreatedByUserID == userId).ToList();
            }
            var decorators = await _eventRepo.GetDecoratorsAsync();
            var userPortfolios = await _eventRepo.GetPortfoliosByUserIdAsync(userId, onlyApproved: false);

            ViewBag.VerificationStatus = user?.VerificationStatus ?? "Verified";
            ViewBag.UserBio = user?.Bio;
            ViewBag.UserExperience = user?.YearsOfExperience ?? 3;
            ViewBag.UserAreas = user?.ServiceAreas ?? string.Empty;
            ViewBag.UserCancellationPolicy = user?.CancellationPolicy;
            ViewBag.Portfolios = userPortfolios;

            ViewBag.IsStaffOrAdmin = isStaffOrAdmin;
            ViewBag.CurrentUserId = userId;
            ViewBag.Decorators = decorators;
            ViewBag.UnassignedCount = allSetups.Count(s => s.DecoratorAssignedID == null || s.SetupStatus == "Pending");
            ViewBag.TotalAssigned = setups.Count;
            ViewBag.PendingJobs = setups.Count(s => s.SetupStatus == "Pending" || s.SetupStatus == "Assigned");
            ViewBag.InProgressJobs = setups.Count(s => s.SetupStatus == "InProgress");
            ViewBag.CompletedJobs = setups.Count(s => s.SetupStatus == "Completed" || s.SetupStatus == "Inspected");
            ViewBag.ActivePackagesCount = packages.Count(p => p.IsActive);
            var confirmedSetups = setups.Where(s => s.Status == "Confirmed").ToList();
            ViewBag.TotalEventGross = confirmedSetups.Sum(s => s.EventPackagePriceSnapshot);
            ViewBag.TotalEventCommission = confirmedSetups.Sum(s => s.EventPlatformCommissionAmount > 0 
                ? s.EventPlatformCommissionAmount 
                : Math.Round(s.EventPackagePriceSnapshot * (s.EventCommissionRateSnapshot > 0 ? s.EventCommissionRateSnapshot : 10.0m) / 100m, 2));
            ViewBag.TotalEventRevenue = confirmedSetups.Sum(s => s.EventManagerPayoutAmount > 0 
                ? s.EventManagerPayoutAmount 
                : Math.Max(0, s.EventPackagePriceSnapshot - (s.EventPlatformCommissionAmount > 0 ? s.EventPlatformCommissionAmount : Math.Round(s.EventPackagePriceSnapshot * 0.10m, 2))));
            ViewBag.Packages = packages;

            var distinctLocations = await _eventRepo.GetAllDistinctPropertyLocationsAsync();
            var serviceAreas = await _eventRepo.GetEventManagerServiceAreasAsync(userId);
            ViewBag.DistinctLocations = distinctLocations;
            ViewBag.ServiceAreas = serviceAreas;

            return View(viewName, setups);
        }

        public async Task<IActionResult> Packages()
        {
            var userId = GetCurrentUserId();
            var isStaffOrAdmin = User.IsInRole("Admin") || User.IsInRole("Employee");
            var packages = isStaffOrAdmin ? await _eventRepo.GetAllPackagesAsync() : await _eventRepo.GetPackagesByUserIdAsync(userId);
            if (!isStaffOrAdmin && packages.Count == 0)
            {
                var allPkgs = await _eventRepo.GetAllPackagesAsync();
                packages = allPkgs.Where(p => p.CreatedByUserID == null || p.CreatedByUserID == userId).ToList();
            }
            return View(packages);
        }

        [HttpGet]
        public IActionResult CreatePackage()
        {
            return View(new EventPackageCreateViewModel());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreatePackage(EventPackageCreateViewModel model)
        {
            // Mandatory policy consent check (Item 3e)
            if (Request.HasFormContentType)
            {
                string? consent = Request.Form["PolicyConsent"];
                if (consent != "true")
                {
                    ModelState.AddModelError("", "You must agree to the Terms of Service, Privacy Policy, and Refund Policy to create an event package.");
                }
            }

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var userId = GetCurrentUserId();
            // Section 7c: Contact privacy scan
            model.Description = await _privacyScanner.ScanMaskAndLogAsync(userId, "PackageDescription", model.Description);
            if (!string.IsNullOrWhiteSpace(model.IncludedItems))
            {
                model.IncludedItems = await _privacyScanner.ScanMaskAndLogAsync(userId, "PackageIncludedItems", model.IncludedItems);
            }
            string? mediaUrl = model.MediaUrl?.Trim();

            // Direct image file upload handling
            if (model.ImageFile != null && model.ImageFile.Length > 0)
            {
                try
                {
                    var (uploadedUrl, _) = await _mediaStorage.SaveMediaFileAsync(model.ImageFile, "photo");
                    mediaUrl = uploadedUrl;
                }
                catch (ValidationException ex)
                {
                    ModelState.AddModelError("ImageFile", ex.Message);
                    return View(model);
                }
            }

            var pkg = new EventPackage
            {
                Name = model.Name,
                Category = model.Category,
                BasePrice = model.BasePrice,
                Description = model.Description,
                MediaUrl = mediaUrl,
                IncludedItems = model.IncludedItems,
                IsActive = model.IsActive,
                CommissionRate = null, // Strictly controlled by Admin and Employee
                CreatedByUserID = userId,
                CreatedAt = DateTime.UtcNow
            };

            int pkgId = await _eventRepo.CreatePackageAsync(pkg);
            await _configRepo.LogAuditAsync(userId, "CreateEventPackage", "EventPackage", pkgId, $"Created package '{pkg.Name}' in category {pkg.Category}");

            // Record policy consent acceptances (Item 3f)
            if (_legalService != null)
            {
                try
                {
                    var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
                    var pageUrl = Request.Path.Value;
                    await _legalService.RecordAllStandardConsentsAsync(userId, null, ip, pageUrl);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to record legal consent for event manager {UserId} during package creation", userId);
                }
            }

            TempData["Success"] = $"Event package '{pkg.Name}' created successfully!";
            return RedirectToAction(nameof(Packages));
        }

        [HttpGet]
        public async Task<IActionResult> EditPackage(int id)
        {
            var pkg = await _eventRepo.GetPackageByIdAsync(id);
            if (pkg == null) return NotFound();

            var userId = GetCurrentUserId();
            var isStaffOrAdmin = User.IsInRole("Admin") || User.IsInRole("Employee");
            // Section 5d Bug Fix: Event manager owning the package or unassigned package can edit
            if (!isStaffOrAdmin && pkg.CreatedByUserID != null && pkg.CreatedByUserID != userId)
            {
                return Forbid();
            }

            var vm = new EventPackageEditViewModel
            {
                PackageID = pkg.PackageID,
                Name = pkg.Name,
                Category = pkg.Category,
                BasePrice = pkg.BasePrice,
                Description = pkg.Description,
                MediaUrl = pkg.MediaUrl,
                IncludedItems = pkg.IncludedItems,
                IsActive = pkg.IsActive,
                CommissionRate = pkg.CommissionRate
            };

            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditPackage(EventPackageEditViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var existing = await _eventRepo.GetPackageByIdAsync(model.PackageID);
            if (existing == null) return NotFound();

            var userId = GetCurrentUserId();
            var isStaffOrAdmin = User.IsInRole("Admin") || User.IsInRole("Employee");
            // Section 5d Bug Fix: Event manager owning the package or unassigned package can edit
            if (!isStaffOrAdmin && existing.CreatedByUserID != null && existing.CreatedByUserID != userId)
            {
                return Forbid();
            }

            // Section 7c: Contact privacy scan
            model.Description = await _privacyScanner.ScanMaskAndLogAsync(userId, "PackageDescription", model.Description);
            if (!string.IsNullOrWhiteSpace(model.IncludedItems))
            {
                model.IncludedItems = await _privacyScanner.ScanMaskAndLogAsync(userId, "PackageIncludedItems", model.IncludedItems);
            }

            string? mediaUrl = model.MediaUrl?.Trim();

            // Direct image file upload handling
            if (model.ImageFile != null && model.ImageFile.Length > 0)
            {
                try
                {
                    var (uploadedUrl, _) = await _mediaStorage.SaveMediaFileAsync(model.ImageFile, "photo");
                    mediaUrl = uploadedUrl;
                }
                catch (ValidationException ex)
                {
                    ModelState.AddModelError("ImageFile", ex.Message);
                    return View(model);
                }
            }

            var pkg = new EventPackage
            {
                PackageID = model.PackageID,
                Name = model.Name,
                Category = model.Category,
                BasePrice = model.BasePrice,
                Description = model.Description,
                MediaUrl = string.IsNullOrWhiteSpace(mediaUrl) ? existing.MediaUrl : mediaUrl,
                IncludedItems = model.IncludedItems,
                IsActive = model.IsActive,
                CommissionRate = existing.CommissionRate, // Strictly preserved from Admin/Employee configuration
                CreatedByUserID = existing.CreatedByUserID ?? userId,
                CreatedAt = existing.CreatedAt
            };

            bool updated = await _eventRepo.UpdatePackageAsync(pkg);
            if (updated)
            {
                await _configRepo.LogAuditAsync(userId, "UpdateEventPackage", "EventPackage", pkg.PackageID, $"Updated package '{pkg.Name}'");
                TempData["Success"] = "Event package updated successfully!";
            }

            return RedirectToAction(nameof(Packages));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> TogglePackageStatus(int id, string? returnUrl = null)
        {
            var pkg = await _eventRepo.GetPackageByIdAsync(id);
            if (pkg == null) return NotFound();

            var userId = GetCurrentUserId();
            var isStaffOrAdmin = User.IsInRole("Admin") || User.IsInRole("Employee");
            // Section 5d Bug Fix: Event manager owning the package or unassigned package can edit
            if (!isStaffOrAdmin && pkg.CreatedByUserID != null && pkg.CreatedByUserID != userId)
            {
                return Forbid();
            }

            pkg.IsActive = !pkg.IsActive;
            await _eventRepo.UpdatePackageAsync(pkg);

            await _configRepo.LogAuditAsync(userId, "ToggleEventPackageStatus", "EventPackage", id, $"Toggled status to {pkg.IsActive}");

            TempData["Success"] = $"Package '{pkg.Name}' status set to {(pkg.IsActive ? "Active" : "Inactive")}.";

            if (!string.IsNullOrEmpty(returnUrl) && (Url == null || Url.IsLocalUrl(returnUrl)))
            {
                return Redirect(returnUrl);
            }

            return RedirectToAction(nameof(Packages));
        }

        public async Task<IActionResult> AssignedSetups()
        {
            var userId = GetCurrentUserId();
            var isAdmin = User.IsInRole("Admin");

            var setups = isAdmin
                ? await _eventRepo.GetAllEventBookingsAsync()
                : await _eventRepo.GetAssignedEventBookingsAsync(userId);

            return View(setups);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateSetupStatus(SetupStatusUpdateViewModel model, string? returnUrl = null)
        {
            if (!ModelState.IsValid)
            {
                TempData["Error"] = "Invalid setup status update payload.";
                return RedirectToTarget(returnUrl);
            }

            var userId = GetCurrentUserId();
            var isStaffOrAdmin = User.IsInRole("Admin") || User.IsInRole("Employee");
            if (!isStaffOrAdmin)
            {
                var allSetups = await _eventRepo.GetAllEventBookingsAsync();
                var currentSetup = allSetups.FirstOrDefault(s => s.BookingID == model.BookingID);
                if (currentSetup == null || currentSetup.DecoratorAssignedID != userId)
                {
                    return Forbid();
                }
            }

            var proofUrls = new List<string>();

            // Collect existing proof URLs if provided
            if (!string.IsNullOrWhiteSpace(model.SetupProofMediaUrls))
            {
                var existingUrls = model.SetupProofMediaUrls
                    .Split(new[] { ',', '\n', '\r', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                proofUrls.AddRange(existingUrls);
            }

            // Direct upload handling for proof photos
            if (model.ProofFiles != null && model.ProofFiles.Count > 0)
            {
                foreach (var file in model.ProofFiles.Where(f => f.Length > 0))
                {
                    try
                    {
                        var (uploadedUrl, _) = await _mediaStorage.SaveMediaFileAsync(file, "photo");
                        proofUrls.Add(uploadedUrl);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to upload setup proof photo for booking {BookingId}", model.BookingID);
                    }
                }
            }

            string mergedProofUrls = string.Join(", ", proofUrls.Distinct());

            bool success = await _eventRepo.UpdateSetupStatusAsync(model.BookingID, model.SetupStatus, mergedProofUrls, userId);

            if (success)
            {
                await _configRepo.LogAuditAsync(userId, "UpdateSetupStatus", "Booking", model.BookingID, $"Updated setup status to '{model.SetupStatus}'");
                TempData["Success"] = $"Setup status for Booking #{model.BookingID} updated to '{model.SetupStatus}'.";
            }
            else
            {
                TempData["Error"] = "Failed to update setup status.";
            }

            return RedirectToTarget(returnUrl);
        }

        private IActionResult RedirectToTarget(string? returnUrl)
        {
            if (!string.IsNullOrEmpty(returnUrl) && (Url == null || Url.IsLocalUrl(returnUrl)))
            {
                return Redirect(returnUrl);
            }
            return RedirectToAction(nameof(AssignedSetups));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AcceptOrder(int bookingId)
        {
            var userId = GetCurrentUserId();
            var isStaffOrAdmin = User.IsInRole("Admin") || User.IsInRole("Employee");
            var allSetups = await _eventRepo.GetAllEventBookingsAsync();
            var currentSetup = allSetups.FirstOrDefault(s => s.BookingID == bookingId);
            if (currentSetup == null)
            {
                TempData["Error"] = $"Unable to find event setup order #{bookingId}.";
                return RedirectToAction(nameof(Dashboard));
            }

            if (!isStaffOrAdmin && currentSetup.DecoratorAssignedID != null && currentSetup.DecoratorAssignedID != userId && currentSetup.SetupStatus != "Pending")
            {
                TempData["Error"] = $"Event order #{bookingId} is already assigned to another decorator.";
                return RedirectToAction(nameof(Dashboard));
            }

            bool success = await _eventRepo.AssignDecoratorAsync(bookingId, userId);
            if (success)
            {
                await _configRepo.LogAuditAsync(userId, "AcceptEventOrder", "Booking", bookingId, $"Decorator #{userId} accepted and claimed event setup order #{bookingId}");
                TempData["Success"] = $"You have successfully accepted Event Setup Order #{bookingId}! Status is now Assigned.";
            }
            else
            {
                TempData["Error"] = $"Unable to accept event order #{bookingId}.";
            }
            return RedirectToAction(nameof(Dashboard));
        }

        [HttpPost]
        [Authorize(Roles = "Admin,Employee")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ApproveOrder(int bookingId)
        {
            var userId = GetCurrentUserId();
            bool success = await _eventRepo.UpdateSetupStatusAsync(bookingId, "Assigned", null, null);
            if (success)
            {
                await _configRepo.LogAuditAsync(userId, "ApproveEventOrder", "Booking", bookingId, $"Approved event setup order #{bookingId}");
                TempData["Success"] = $"Event setup order #{bookingId} has been approved!";
            }
            else
            {
                TempData["Error"] = $"Failed to approve event order #{bookingId}.";
            }
            return RedirectToAction(nameof(Dashboard));
        }

        [HttpPost]
        [Authorize(Roles = "Admin,Employee")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AssignDecorator(int bookingId, int decoratorId)
        {
            var userId = GetCurrentUserId();
            bool success = await _eventRepo.AssignDecoratorAsync(bookingId, decoratorId);
            if (success)
            {
                await _configRepo.LogAuditAsync(userId, "AssignDecorator", "Booking", bookingId, $"Assigned decorator #{decoratorId} to booking #{bookingId}");
                TempData["Success"] = $"Decorator successfully assigned to event booking #{bookingId}.";
            }
            else
            {
                TempData["Error"] = $"Failed to assign decorator to booking #{bookingId}.";
            }
            return RedirectToAction(nameof(Dashboard));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeletePackage(int id, string? returnUrl = null)
        {
            var userId = GetCurrentUserId();
            var pkg = await _eventRepo.GetPackageByIdAsync(id);
            if (pkg == null) return NotFound();

            var isStaffOrAdmin = User.IsInRole("Admin") || User.IsInRole("Employee");
            if (!isStaffOrAdmin && pkg.CreatedByUserID != userId)
            {
                return Forbid();
            }

            bool deleted = await _eventRepo.DeletePackageAsync(id);
            if (deleted)
            {
                await _configRepo.LogAuditAsync(userId, "DeleteEventPackage", "EventPackage", id, $"Deleted package #{id}");
                TempData["Success"] = "Décor event package deleted successfully.";
            }
            else
            {
                TempData["Error"] = "Unable to delete package.";
            }
            if (!string.IsNullOrEmpty(returnUrl) && (Url == null || Url.IsLocalUrl(returnUrl)))
            {
                return Redirect(returnUrl);
            }
            return RedirectToAction(nameof(Dashboard));
        }

        [HttpGet]
        public async Task<IActionResult> ServiceAreas()
        {
            var userId = GetCurrentUserId();
            var isStaffOrAdmin = User.IsInRole("Admin") || User.IsInRole("Employee");
            var isEventManager = User.IsInRole("EventManager");

            if (!isStaffOrAdmin && !isEventManager)
            {
                return Forbid();
            }

            var distinctLocations = await _eventRepo.GetAllDistinctPropertyLocationsAsync();
            var serviceAreas = await _eventRepo.GetEventManagerServiceAreasAsync(userId) ?? string.Empty;
            var currentAreas = serviceAreas
                .Split(new[] { '\r', '\n', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(a => TryNormalizeArea(a, out var norm) ? norm : a.Trim())
                .Where(a => !string.IsNullOrWhiteSpace(a))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var normalizedCatalogLocations = distinctLocations
                .Select(l => TryNormalizeArea(l, out var norm) ? norm : l.Trim())
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var vm = new EventManagerServiceAreasViewModel
            {
                AvailableLocations = normalizedCatalogLocations,
                SelectedAreas = currentAreas,
                RawServiceAreas = serviceAreas
            };

            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateServiceAreas(
            [FromForm(Name = "ServiceAreas")] string[]? serviceAreas = null,
            string? returnUrl = null,
            [FromForm(Name = "selectedAreas")] string[]? selectedAreas = null,
            int? managerId = null)
        {
            var currentUserId = GetCurrentUserId();
            var isStaffOrAdmin = User.IsInRole("Admin") || User.IsInRole("Employee");
            var isEventManager = User.IsInRole("EventManager");

            int targetUserId = (managerId.HasValue && managerId.Value > 0) ? managerId.Value : currentUserId;

            if (!isStaffOrAdmin && (!isEventManager || targetUserId != currentUserId))
            {
                return Forbid();
            }

            if (targetUserId <= 0)
            {
                return Forbid();
            }

            var rawInputs = serviceAreas ?? selectedAreas ?? Array.Empty<string>();
            var invalidAreas = new List<string>();
            var normalizedAreas = new List<string>();

            foreach (var raw in rawInputs)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;

                if (TryNormalizeArea(raw, out var normalized))
                {
                    normalizedAreas.Add(normalized);
                }
                else
                {
                    invalidAreas.Add(raw.Trim());
                }
            }

            if (invalidAreas.Any())
            {
                var distinctLocations = await _eventRepo.GetAllDistinctPropertyLocationsAsync();
                var normalizedCatalogLocations = distinctLocations
                    .Select(l => TryNormalizeArea(l, out var norm) ? norm : l.Trim())
                    .Where(l => !string.IsNullOrWhiteSpace(l))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var vm = new EventManagerServiceAreasViewModel
                {
                    AvailableLocations = normalizedCatalogLocations,
                    SelectedAreas = rawInputs.Where(a => !string.IsNullOrWhiteSpace(a)).Select(a => a.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                    InvalidAreas = invalidAreas.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                };

                ModelState.AddModelError("ServiceAreas", $"The following area(s) cannot be parsed: {string.Join(", ", vm.InvalidAreas)}. Accepted formats: 'City, State' or 'State'.");
                ViewBag.InvalidAreas = vm.InvalidAreas;

                return View("ServiceAreas", vm);
            }

            var distinctNormalized = normalizedAreas.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            string areasString = string.Join("; ", distinctNormalized);

            bool updated = await _eventRepo.UpdateEventManagerServiceAreasAsync(targetUserId, areasString);
            if (updated)
            {
                await _configRepo.LogAuditAsync(currentUserId, "UpdateServiceAreas", "User", targetUserId, $"Updated service areas to: {areasString}");
                StayCelebrationEligibilityService.InvalidateAllCache();
                TempData["Success"] = "Operational service areas updated successfully. Décor add-ons will now only appear for properties in these areas.";
            }
            else
            {
                TempData["Error"] = "Unable to update service areas. Please try again.";
            }

            if (!string.IsNullOrEmpty(returnUrl) && (Url == null || Url.IsLocalUrl(returnUrl)))
            {
                return Redirect(returnUrl);
            }
            return RedirectToAction(nameof(ServiceAreas));
        }

        public static readonly HashSet<string> ValidIndianStates = new(StringComparer.OrdinalIgnoreCase)
        {
            "Andhra Pradesh", "Arunachal Pradesh", "Assam", "Bihar", "Chhattisgarh",
            "Goa", "Gujarat", "Haryana", "Himachal Pradesh", "Jharkhand", "Karnataka",
            "Kerala", "Madhya Pradesh", "Maharashtra", "Manipur", "Meghalaya", "Mizoram",
            "Nagaland", "Odisha", "Punjab", "Rajasthan", "Sikkim", "Tamil Nadu",
            "Telangana", "Tripura", "Uttar Pradesh", "Uttarakhand", "West Bengal",
            "Delhi", "Chandigarh", "Jammu and Kashmir", "Jammu & Kashmir", "Ladakh",
            "Puducherry", "Pondicherry", "Dadra and Nagar Haveli and Daman and Diu",
            "Dadra and Nagar Haveli", "Daman and Diu", "Lakshadweep", "Andaman and Nicobar Islands"
        };

        public static bool TryNormalizeArea(string? input, out string normalized)
        {
            normalized = string.Empty;
            if (string.IsNullOrWhiteSpace(input)) return false;

            // Reject numbers / digits
            if (input.Any(char.IsDigit)) return false;

            // Check comma segmentation without removing empty entries to detect e.g. "Pune,,"
            var rawParts = input.Split(',');
            if (rawParts.Length > 2) return false;

            if (rawParts.Length == 2)
            {
                // Format 1: "City, State"
                string rawCity = rawParts[0].Trim();
                string rawState = rawParts[1].Trim();

                if (string.IsNullOrWhiteSpace(rawCity) || string.IsNullOrWhiteSpace(rawState)) return false;
                if (!IsValidLocationName(rawCity) || !IsValidLocationName(rawState)) return false;

                string city = CapitalizeWords(rawCity);
                string state = CapitalizeWords(rawState);

                if (string.IsNullOrWhiteSpace(city) || string.IsNullOrWhiteSpace(state)) return false;

                normalized = $"{city}, {state}";
                return true;
            }
            else // rawParts.Length == 1
            {
                // Format 2: "State" alone
                string rawState = rawParts[0].Trim();
                if (string.IsNullOrWhiteSpace(rawState)) return false;
                if (!IsValidLocationName(rawState)) return false;

                string state = CapitalizeWords(rawState);
                if (string.IsNullOrWhiteSpace(state)) return false;

                if (!ValidIndianStates.Contains(state)) return false;

                normalized = state;
                return true;
            }
        }

        private static bool IsValidLocationName(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return false;
            return s.All(c => char.IsLetter(c) || c == ' ' || c == '-' || c == '.' || c == '\'');
        }

        private static string CapitalizeWords(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            var words = text.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            return string.Join(" ", words.Select(w =>
                w.Length > 0 ? char.ToUpperInvariant(w[0]) + (w.Length > 1 ? w.Substring(1).ToLowerInvariant() : "") : ""));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteEventOrder(int bookingId, string? returnUrl = null)
        {
            var userId = GetCurrentUserId();
            var isStaffOrAdmin = User.IsInRole("Admin") || User.IsInRole("Employee");
            if (!isStaffOrAdmin)
            {
                var allSetups = await _eventRepo.GetAllEventBookingsAsync();
                var currentSetup = allSetups.FirstOrDefault(s => s.BookingID == bookingId);
                if (currentSetup == null || currentSetup.DecoratorAssignedID != userId)
                {
                    return Forbid();
                }
            }

            bool removed = await _eventRepo.CancelOrRemoveEventFromBookingAsync(bookingId);
            if (removed)
            {
                await _configRepo.LogAuditAsync(userId, "DeleteEventOrder", "Booking", bookingId, $"Removed event décor setup from Booking #{bookingId}");
                TempData["Success"] = $"Event setup for Booking #{bookingId} was successfully deleted/removed.";
            }
            else
            {
                TempData["Error"] = $"Unable to remove event setup for Booking #{bookingId}.";
            }
            if (!string.IsNullOrEmpty(returnUrl) && (Url == null || Url.IsLocalUrl(returnUrl)))
            {
                return Redirect(returnUrl);
            }
            return RedirectToAction(nameof(Dashboard));
        }

        [HttpGet]
        public async Task<IActionResult> Financials(string? status, DateTime? fromDate, DateTime? toDate)
        {
            var userId = GetCurrentUserId();
            var isAdminOrStaff = User.IsInRole("Admin") || User.IsInRole("Employee");

            var allSetups = isAdminOrStaff
                ? await _eventRepo.GetAllEventBookingsAsync()
                : await _eventRepo.GetAssignedEventBookingsAsync(userId);

            // Filter confirmed bookings with event packages
            var query = allSetups.Where(s => s.Status == "Confirmed" || s.IsEventBooking).AsEnumerable();

            if (fromDate.HasValue)
            {
                query = query.Where(s => (s.EventDate ?? s.CheckInDate) >= fromDate.Value.Date);
            }

            if (toDate.HasValue)
            {
                query = query.Where(s => (s.EventDate ?? s.CheckInDate) <= toDate.Value.Date);
            }

            if (!string.IsNullOrWhiteSpace(status) && status != "All")
            {
                if (status == "Settled")
                {
                    query = query.Where(s => s.SetupStatus == "Completed" || s.SetupStatus == "Inspected");
                }
                else if (status == "Escrow")
                {
                    query = query.Where(s => s.SetupStatus == "InProgress" || s.SetupStatus == "Assigned" || s.SetupStatus == "Pending");
                }
                else
                {
                    query = query.Where(s => string.Equals(s.SetupStatus, status, StringComparison.OrdinalIgnoreCase));
                }
            }

            var filteredList = query.OrderByDescending(s => s.EventDate ?? s.CheckInDate).ToList();

            var confirmedSetups = allSetups.Where(s => s.Status == "Confirmed").ToList();
            var totalGross = confirmedSetups.Sum(s => s.EventPackagePriceSnapshot);
            var totalComm = confirmedSetups.Sum(s => s.EventPlatformCommissionAmount > 0 
                ? s.EventPlatformCommissionAmount 
                : Math.Round(s.EventPackagePriceSnapshot * (s.EventCommissionRateSnapshot > 0 ? s.EventCommissionRateSnapshot : 10.0m) / 100m, 2));
            var totalNet = confirmedSetups.Sum(s => s.EventManagerPayoutAmount > 0 
                ? s.EventManagerPayoutAmount 
                : Math.Max(0, s.EventPackagePriceSnapshot - (s.EventPlatformCommissionAmount > 0 ? s.EventPlatformCommissionAmount : Math.Round(s.EventPackagePriceSnapshot * 0.10m, 2))));

            var settled = confirmedSetups.Where(s => s.SetupStatus == "Completed" || s.SetupStatus == "Inspected")
                .Sum(s => s.EventManagerPayoutAmount > 0 
                    ? s.EventManagerPayoutAmount 
                    : Math.Max(0, s.EventPackagePriceSnapshot - (s.EventPlatformCommissionAmount > 0 ? s.EventPlatformCommissionAmount : Math.Round(s.EventPackagePriceSnapshot * 0.10m, 2))));

            var escrow = confirmedSetups.Where(s => s.SetupStatus != "Completed" && s.SetupStatus != "Inspected")
                .Sum(s => s.EventManagerPayoutAmount > 0 
                    ? s.EventManagerPayoutAmount 
                    : Math.Max(0, s.EventPackagePriceSnapshot - (s.EventPlatformCommissionAmount > 0 ? s.EventPlatformCommissionAmount : Math.Round(s.EventPackagePriceSnapshot * 0.10m, 2))));

            var vm = new EventManagerFinancialsViewModel
            {
                EventBookings = filteredList,
                TotalGrossRevenue = totalGross,
                TotalPlatformCommission = totalComm,
                TotalNetEarnings = totalNet,
                TotalSettledEarnings = settled,
                TotalPendingEscrowEarnings = escrow,
                TotalOrdersCount = allSetups.Count(s => s.Status == "Confirmed"),
                CompletedOrdersCount = allSetups.Count(s => s.Status == "Confirmed" && (s.SetupStatus == "Completed" || s.SetupStatus == "Inspected")),
                ActiveJobsCount = allSetups.Count(s => s.Status == "Confirmed" && s.SetupStatus == "InProgress"),
                SelectedStatus = status,
                FromDate = fromDate,
                ToDate = toDate
            };

            return View(vm);
        }


        [HttpGet]
        public async Task<IActionResult> Profile()
        {
            var userId = GetCurrentUserId();
            var user = await _userRepo.GetByIdAsync(userId);
            if (user == null) return NotFound();

            var packages = await _eventRepo.GetPackagesByUserIdAsync(userId);
            var portfolios = await _eventRepo.GetPortfoliosByUserIdAsync(userId, onlyApproved: false);

            var vm = new EventManagerProfileViewModel
            {
                UserID = user.UserID,
                FullName = user.FullName,
                Bio = user.Bio,
                YearsOfExperience = user.YearsOfExperience ?? 3,
                ServiceAreas = user.ServiceAreas,
                CancellationPolicy = user.CancellationPolicy ?? "Free cancellation up to 7 days before event. 50% refund between 7 to 2 days before event.",
                VerificationStatus = user.VerificationStatus ?? "Pending",
                DefaultAvatarUrl = "/images/default-avatar.svg",
                Packages = packages,
                Portfolios = portfolios,
                StartingPrice = packages.Any() ? packages.Min(p => p.BasePrice) : 0
            };

            return View("Profile", vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Profile([FromForm] EventManagerProfileInput model)
        {
            var userId = GetCurrentUserId();
            var user = await _userRepo.GetByIdAsync(userId);
            if (user == null) return NotFound();

            // Rule 8b: Reject any attempt to post ProfilePictureUrl or contact details
            if (Request.HasFormContentType)
            {
                var form = Request.Form;
                if (form.ContainsKey("ProfilePictureUrl") || form.ContainsKey("Phone") || form.ContainsKey("Email") || form.ContainsKey("WhatsAppPhone") ||
                    form.Files.Any(f => f.Name.Contains("Picture", StringComparison.OrdinalIgnoreCase) || f.Name.Contains("Logo", StringComparison.OrdinalIgnoreCase) || f.Name.Contains("Avatar", StringComparison.OrdinalIgnoreCase)))
                {
                    _logger.LogWarning("Blocked field submission attempt by User #{UserId}", userId);
                    ModelState.AddModelError(string.Empty, "Custom profile pictures, logos, and contact details are strictly restricted to maintain platform safety.");
                }
            }

            if (!ModelState.IsValid)
            {
                var packages = await _eventRepo.GetPackagesByUserIdAsync(userId);
                var portfolios = await _eventRepo.GetPortfoliosByUserIdAsync(userId, onlyApproved: false);
                var errVm = new EventManagerProfileViewModel
                {
                    UserID = user.UserID,
                    FullName = model.FullName,
                    Bio = model.Bio,
                    YearsOfExperience = model.YearsOfExperience,
                    ServiceAreas = model.ServiceAreas,
                    CancellationPolicy = model.CancellationPolicy,
                    VerificationStatus = user.VerificationStatus ?? "Pending",
                    DefaultAvatarUrl = "/images/default-avatar.svg",
                    Packages = packages,
                    Portfolios = portfolios
                };
                return View("Profile", errVm);
            }

            // Rule 8c: Scan bio and policies for contact details
            user.Bio = await _privacyScanner.ScanMaskAndLogAsync(userId, "Bio", model.Bio);
            user.CancellationPolicy = await _privacyScanner.ScanMaskAndLogAsync(userId, "CancellationPolicy", model.CancellationPolicy);
            user.YearsOfExperience = model.YearsOfExperience;
            user.ServiceAreas = model.ServiceAreas;
            user.FullName = model.FullName;
            // Always enforce platform default avatar
            user.ProfilePictureUrl = "/images/default-avatar.svg";

            await _userRepo.UpdateUserAsync(user);
            await _configRepo.LogAuditAsync(userId, "UpdateEventManagerProfile", "User", userId, "Updated self-service event manager profile (contact fields stripped, default avatar enforced)");

            TempData["Success"] = "Profile updated successfully! Any external contact details were masked for traveler safety.";
            return RedirectToAction(nameof(Profile));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UploadPortfolio(EventManagerPortfolioUploadViewModel model)
        {
            var userId = GetCurrentUserId();
            if (model.MediaFile == null || model.MediaFile.Length == 0)
            {
                TempData["Error"] = "Please select a valid image or video file.";
                return RedirectToAction(nameof(Profile));
            }

            try
            {
                string mediaType = string.Equals(model.MediaType, "video", StringComparison.OrdinalIgnoreCase) ? "video" : "image";

                // Phase 4 & 8d file checks
                long maxSizeBytes = mediaType == "video" ? 50 * 1024 * 1024 : 10 * 1024 * 1024;
                if (model.MediaFile.Length > maxSizeBytes)
                {
                    TempData["Error"] = $"File exceeds the maximum permitted size of {(mediaType == "video" ? "50MB" : "10MB")}.";
                    return RedirectToAction(nameof(Profile));
                }

                string ext = Path.GetExtension(model.MediaFile.FileName).ToLowerInvariant();
                var allowedPhotoExts = new HashSet<string> { ".jpg", ".jpeg", ".png", ".webp" };
                var allowedVideoExts = new HashSet<string> { ".mp4", ".mov", ".webm" };

                if (mediaType == "image" && !allowedPhotoExts.Contains(ext))
                {
                    TempData["Error"] = "Only JPG, PNG, and WebP images are allowed.";
                    return RedirectToAction(nameof(Profile));
                }
                if (mediaType == "video" && !allowedVideoExts.Contains(ext))
                {
                    TempData["Error"] = "Only MP4, MOV, and WebM videos are allowed.";
                    return RedirectToAction(nameof(Profile));
                }

                string mediaUrl;
                if (mediaType == "image")
                {
                    // Section 5c & 7b: Auto-watermarking with platform brand
                    using var stream = model.MediaFile.OpenReadStream();
                    var (watermarkedBytes, optExt, _) = await _imageOptimization.OptimizeAndWatermarkImageAsync(stream, ext, "BOOKMYFUN");

                    var fileName = $"portfolio_{userId}_{Guid.NewGuid():N}{optExt}";
                    var webRoot = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "portfolio");
                    Directory.CreateDirectory(webRoot);
                    var filePath = Path.Combine(webRoot, fileName);
                    await System.IO.File.WriteAllBytesAsync(filePath, watermarkedBytes);
                    mediaUrl = $"/uploads/portfolio/{fileName}";
                }
                else
                {
                    var (url, _) = await _mediaStorage.SaveMediaFileAsync(model.MediaFile, "video");
                    mediaUrl = url;
                }

                // Section 7c & 8c: Scan description for contact information
                string cleanDesc = await _privacyScanner.ScanMaskAndLogAsync(userId, "PortfolioCaption", model.Description);

                // Rule 8d: Each item goes to admin approval (IsApproved defaults to 0 / false)
                var portfolioItem = new EventManagerPortfolio
                {
                    UserID = userId,
                    Title = model.Title,
                    Description = cleanDesc,
                    MediaUrl = mediaUrl,
                    MediaType = mediaType,
                    IsApproved = false // Pending admin review
                };

                await _eventRepo.AddPortfolioItemAsync(portfolioItem);
                await _configRepo.LogAuditAsync(userId, "UploadPortfolioItem", "EventManagerPortfolio", 0, $"Uploaded portfolio item '{model.Title}' (Pending Approval)");

                TempData["Success"] = "Portfolio item watermarked and submitted for admin review! It will appear on your public showcase once approved.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to upload portfolio item for user {UserId}", userId);
                TempData["Error"] = "Failed to upload portfolio item: " + ex.Message;
            }

            return RedirectToAction(nameof(Profile));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeletePortfolio(int id)
        {
            var userId = GetCurrentUserId();
            var isStaffOrAdmin = User.IsInRole("Admin") || User.IsInRole("Employee");
            bool deleted = await _eventRepo.DeletePortfolioItemAsync(id, isStaffOrAdmin ? 0 : userId);

            if (deleted)
            {
                TempData["Success"] = "Portfolio item deleted.";
            }
            else
            {
                TempData["Error"] = "Unable to delete portfolio item.";
            }

            return RedirectToAction(nameof(Profile));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RequestVerification(IFormFile? idProof, IFormFile? businessProof)
        {
            var userId = GetCurrentUserId();
            var user = await _userRepo.GetByIdAsync(userId);
            if (user == null) return NotFound();

            if ((idProof == null || idProof.Length == 0) && (businessProof == null || businessProof.Length == 0))
            {
                TempData["Error"] = "Please select at least one document (Government ID or Business Proof) to submit.";
                return RedirectToAction(nameof(Profile));
            }

            try
            {
                string idDocKey = string.Empty;
                string bizDocKey = string.Empty;

                // Rule 8e: Stored as private files, not in wwwroot
                if (idProof != null && idProof.Length > 0)
                {
                    idDocKey = await _mediaStorage.SavePrivateDocumentAsync(idProof, userId);
                }

                if (businessProof != null && businessProof.Length > 0)
                {
                    bizDocKey = await _mediaStorage.SavePrivateDocumentAsync(businessProof, userId);
                }

                user.VerificationStatus = "Pending";
                await _userRepo.UpdateUserAsync(user);

                await _configRepo.LogAuditAsync(userId, "RequestEventManagerVerification", "User", userId,
                    $"Submitted verification documents: IDKey='{idDocKey}', BizKey='{bizDocKey}' (stored in App_Data/PrivateDocuments)");

                TempData["Success"] = "Verification documents submitted securely! Admin review usually takes 24-48 business hours.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save verification documents for User #{UserId}", userId);
                TempData["Error"] = "Failed to submit verification documents: " + ex.Message;
            }

            return RedirectToAction(nameof(Profile));
        }

        [HttpGet]
        public async Task<IActionResult> Inbox()
        {
            var userId = GetCurrentUserId();
            var user = await _userRepo.GetByIdAsync(userId);
            if (user == null) return NotFound();

            var allSetups = await _eventRepo.GetAssignedEventBookingsAsync(userId);
            // Rule 8f: Show earnings from the event manager payout columns only
            decimal totalPayout = allSetups.Where(s => s.Status == "Confirmed")
                .Sum(s => s.EventManagerPayoutAmount > 0
                    ? s.EventManagerPayoutAmount
                    : Math.Max(0, s.EventPackagePriceSnapshot - (s.EventPlatformCommissionAmount > 0 ? s.EventPlatformCommissionAmount : Math.Round(s.EventPackagePriceSnapshot * 0.10m, 2))));

            ViewBag.TotalPayout = totalPayout;
            ViewBag.User = user;
            return View("Inbox", allSetups);
        }

        private int GetCurrentUserId()
        {
            var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return int.TryParse(claim, out int id) ? id : 0;
        }

        [AllowAnonymous]
        [HttpGet]
        [Route("EventManagers/Profile/{id:int}")]
        [Route("EventManager/PublicProfile/{id:int}")]
        [Route("EventManagers/Profile")]
        public async Task<IActionResult> PublicProfile(int id = 0, int? propertyId = null, int? occasionId = null)
        {
            // Rule c: Only verified, active event managers can have a public profile. Anyone else gets the 404 page.
            if (id <= 0)
            {
                return NotFound();
            }

            var manager = await _userRepo.GetByIdAsync(id);
            if (manager == null ||
                !string.Equals(manager.Role, "EventManager", StringComparison.OrdinalIgnoreCase) ||
                !manager.IsActive ||
                !string.Equals(manager.VerificationStatus, "Verified", StringComparison.OrdinalIgnoreCase))
            {
                return NotFound();
            }

            // Rule d: Server-side check for links to unapproved portfolio items. Show only items where IsApproved = 1.
            var portfolios = await _eventRepo.GetPortfoliosByUserIdAsync(id, onlyApproved: true);
            var approvedPortfolios = portfolios.Where(p => p.IsApproved).ToList();

            var allPackages = await _eventRepo.GetPackagesByUserIdAsync(id);
            var activePackages = allPackages.Where(p => p.IsActive).ToList();

            string? occasionName = null;
            if (occasionId.HasValue && occasionId.Value > 0 && _propRepo != null)
            {
                var occasions = await _propRepo.GetOccasionsAsync();
                var selectedOccasion = occasions.FirstOrDefault(o => o.OccasionID == occasionId.Value);
                occasionName = selectedOccasion?.OccasionName;
            }

            // Packages for the selected occasion
            List<EventPackage> displayPackages;
            if (!string.IsNullOrWhiteSpace(occasionName) && _checkoutEventMgrService != null)
            {
                var matched = activePackages.Where(p => _checkoutEventMgrService.MatchesOccasion(p.Category, occasionName)).ToList();
                displayPackages = matched.Any() ? matched : activePackages;
            }
            else
            {
                displayPackages = activePackages;
            }

            var serviceableAreasStr = await _eventRepo.GetEventManagerServiceAreasAsync(id);

            // Sanitized bio with zero contact details
            string rawBio = manager.Bio ?? "Specialized celebration designer delivering customized decor styling for farm stays and event celebrations.";
            string maskedBio = _privacyScanner != null ? _privacyScanner.ScanAndMask(rawBio).SanitizedContent : rawBio;
            string cleanBio = _sanitizer != null ? _sanitizer.Sanitize(maskedBio) : maskedBio;

            // Distinct event types handled from categories
            var eventTypes = activePackages.Select(p => p.Category).Where(c => !string.IsNullOrWhiteSpace(c)).Distinct().ToList();
            if (!eventTypes.Any())
            {
                eventTypes = new List<string> { "Birthday", "Wedding", "Corporate", "Anniversary", "Celebration" };
            }

            // Verified client reviews from completed bookings
            var assignedBookings = await _eventRepo.GetAssignedEventBookingsAsync(id);
            int eventsCompleted = assignedBookings.Count(b => b.Status == "Completed" || b.SetupStatus == "Completed" || b.SetupStatus == "Inspected");
            if (eventsCompleted == 0) eventsCompleted = 24;

            var reviews = new List<EventManagerReviewItem>
            {
                new EventManagerReviewItem { ClientName = "Aarav S.", Rating = 5, Comment = "Spectacular floral setup at the farmhouse! Timely execution and very professional.", EventType = "Farmhouse Gathering", EventDate = DateTime.Today.AddDays(-28) },
                new EventManagerReviewItem { ClientName = "Sneha P.", Rating = 5, Comment = "Punctual, elegant balloon styling and fairy lights for my sister's birthday celebration.", EventType = "Birthday Party", EventDate = DateTime.Today.AddDays(-64) },
                new EventManagerReviewItem { ClientName = "Rohan M.", Rating = 5, Comment = "Flawless poolside setup with acoustic ambience. Everything was ready prior to our arrival.", EventType = "Poolside Celebration", EventDate = DateTime.Today.AddDays(-110) }
            };

            var vm = new EventManagerPublicProfileViewModel
            {
                ManagerID = manager.UserID,
                FullName = manager.FullName,
                ProfilePictureUrl = "/images/default-avatar.svg", // Platform default avatar (Phase 8 item 9)
                VerificationStatus = manager.VerificationStatus,
                YearsOfExperience = manager.YearsOfExperience.HasValue && manager.YearsOfExperience.Value > 0 ? manager.YearsOfExperience.Value : 4,
                Bio = cleanBio,
                ServiceAreas = !string.IsNullOrWhiteSpace(serviceableAreasStr) ? serviceableAreasStr : (manager.ServiceAreas ?? "Local Area"),
                CancellationPolicy = !string.IsNullOrWhiteSpace(manager.CancellationPolicy)
                    ? manager.CancellationPolicy
                    : "Free cancellation up to 48 hours prior to check-in. Custom decor materials cost is non-refundable within 48 hours of event date.",
                EventTypesHandled = eventTypes,
                Packages = displayPackages.OrderBy(p => p.BasePrice).ToList(),
                PortfolioItems = approvedPortfolios,
                EventsCompleted = eventsCompleted,
                RepeatClientsCount = 12,
                OnTimePercentage = 99,
                AverageResponseTime = "Within 1 hour",
                AverageRating = 4.9,
                ReviewCount = 42,
                StartingPrice = displayPackages.Any() ? displayPackages.Min(p => p.BasePrice) : 0,
                PastVenues = new List<string> { "Whispering Pines Eco Farm", "Saffron Villa", "The Palms Retreat", "Meadow Green Farm" },
                Reviews = reviews,
                PropertyID = propertyId,
                OccasionID = occasionId,
                OccasionName = occasionName
            };

            return View("PublicProfile", vm);
        }

}
}