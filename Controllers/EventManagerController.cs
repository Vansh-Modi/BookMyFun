using System.Security.Claims;
using BookMyFun.Models.Entities;
using BookMyFun.Models.ViewModels;
using BookMyFun.Repositories;
using BookMyFun.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookMyFun.Controllers
{
    [Authorize(Roles = "EventManager,Admin,Employee")]
    public class EventManagerController : Controller
    {
        private readonly IEventPackageRepository _eventRepo;
        private readonly IMediaStorageService _mediaStorage;
        private readonly IConfigRepository _configRepo;
        private readonly ILogger<EventManagerController> _logger;

        public EventManagerController(
            IEventPackageRepository eventRepo,
            IMediaStorageService mediaStorage,
            IConfigRepository configRepo,
            ILogger<EventManagerController> logger)
        {
            _eventRepo = eventRepo;
            _mediaStorage = mediaStorage;
            _configRepo = configRepo;
            _logger = logger;
        }

        [AllowAnonymous]
        [HttpGet]
        public IActionResult Login(string? returnUrl) => RedirectToAction("Login", "Account", new { returnUrl });

        public async Task<IActionResult> Index() => await LoadDashboardViewAsync("Index");

        public async Task<IActionResult> Dashboard() => await LoadDashboardViewAsync("Dashboard");

        private async Task<IActionResult> LoadDashboardViewAsync(string viewName)
        {
            var userId = GetCurrentUserId();
            var isStaffOrAdmin = User.IsInRole("Admin") || User.IsInRole("Employee");

            var allSetups = await _eventRepo.GetAllEventBookingsAsync();
            var setups = isStaffOrAdmin 
                ? allSetups 
                : allSetups.Where(s => s.DecoratorAssignedID == userId || s.DecoratorAssignedID == null || s.SetupStatus == "Pending").ToList();

            var packages = await _eventRepo.GetAllPackagesAsync();
            var decorators = await _eventRepo.GetDecoratorsAsync();

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
            var packages = await _eventRepo.GetAllPackagesAsync();
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
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var userId = GetCurrentUserId();
            var pkg = new EventPackage
            {
                Name = model.Name,
                Category = model.Category,
                BasePrice = model.BasePrice,
                Description = model.Description,
                MediaUrl = model.MediaUrl,
                IncludedItems = model.IncludedItems,
                IsActive = model.IsActive,
                CommissionRate = model.CommissionRate,
                CreatedByUserID = userId,
                CreatedAt = DateTime.UtcNow
            };

            int pkgId = await _eventRepo.CreatePackageAsync(pkg);
            await _configRepo.LogAuditAsync(userId, "CreateEventPackage", "EventPackage", pkgId, $"Created package '{pkg.Name}' in category {pkg.Category}");

            TempData["Success"] = $"Event package '{pkg.Name}' created successfully!";
            return RedirectToAction(nameof(Packages));
        }

        [HttpGet]
        public async Task<IActionResult> EditPackage(int id)
        {
            var pkg = await _eventRepo.GetPackageByIdAsync(id);
            if (pkg == null) return NotFound();

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

            var pkg = new EventPackage
            {
                PackageID = model.PackageID,
                Name = model.Name,
                Category = model.Category,
                BasePrice = model.BasePrice,
                Description = model.Description,
                MediaUrl = model.MediaUrl,
                IncludedItems = model.IncludedItems,
                IsActive = model.IsActive,
                CommissionRate = model.CommissionRate
            };

            bool updated = await _eventRepo.UpdatePackageAsync(pkg);
            if (updated)
            {
                var userId = GetCurrentUserId();
                await _configRepo.LogAuditAsync(userId, "UpdateEventPackage", "EventPackage", pkg.PackageID, $"Updated package '{pkg.Name}'");
                TempData["Success"] = "Event package updated successfully!";
            }

            return RedirectToAction(nameof(Packages));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> TogglePackageStatus(int id)
        {
            var pkg = await _eventRepo.GetPackageByIdAsync(id);
            if (pkg == null) return NotFound();

            pkg.IsActive = !pkg.IsActive;
            await _eventRepo.UpdatePackageAsync(pkg);

            var userId = GetCurrentUserId();
            await _configRepo.LogAuditAsync(userId, "ToggleEventPackageStatus", "EventPackage", id, $"Toggled status to {pkg.IsActive}");

            TempData["Success"] = $"Package status set to {(pkg.IsActive ? "Active" : "Inactive")}.";
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
        public async Task<IActionResult> UpdateSetupStatus(SetupStatusUpdateViewModel model)
        {
            if (!ModelState.IsValid)
            {
                TempData["Error"] = "Invalid setup status update payload.";
                return RedirectToAction(nameof(AssignedSetups));
            }

            var userId = GetCurrentUserId();
            bool success = await _eventRepo.UpdateSetupStatusAsync(model.BookingID, model.SetupStatus, model.SetupProofMediaUrls, userId);

            if (success)
            {
                await _configRepo.LogAuditAsync(userId, "UpdateSetupStatus", "Booking", model.BookingID, $"Updated setup status to '{model.SetupStatus}'");
                TempData["Success"] = $"Setup status for Booking #{model.BookingID} updated to '{model.SetupStatus}'.";
            }
            else
            {
                TempData["Error"] = "Failed to update setup status.";
            }

            return RedirectToAction(nameof(AssignedSetups));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AcceptOrder(int bookingId)
        {
            var userId = GetCurrentUserId();
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
            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }
            return RedirectToAction(nameof(Dashboard));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateServiceAreas(string[]? selectedAreas, string? returnUrl = null)
        {
            var userId = GetCurrentUserId();
            string areasString = selectedAreas != null && selectedAreas.Length > 0
                ? string.Join("; ", selectedAreas.Where(a => !string.IsNullOrWhiteSpace(a)))
                : string.Empty;

            bool updated = await _eventRepo.UpdateEventManagerServiceAreasAsync(userId, areasString);
            if (updated)
            {
                await _configRepo.LogAuditAsync(userId, "UpdateServiceAreas", "User", userId, $"Updated service areas to: {areasString}");
                TempData["Success"] = "Operational service areas updated successfully. Décor add-ons will now only appear for properties in these areas.";
            }
            else
            {
                TempData["Error"] = "Unable to update service areas. Please try again.";
            }

            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }
            return RedirectToAction(nameof(Dashboard));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteEventOrder(int bookingId, string? returnUrl = null)
        {
            var userId = GetCurrentUserId();
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
            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
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

        private int GetCurrentUserId()
        {
            var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return int.TryParse(claim, out int id) ? id : 0;
        }
    }
}
