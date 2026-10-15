using System.Security.Claims;
using BookMyFun.Models.Entities;
using BookMyFun.Models.ViewModels;
using BookMyFun.Repositories;
using BookMyFun.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookMyFun.Controllers
{
    [Authorize(Roles = "EventManager,Admin")]
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

        public IActionResult Index() => RedirectToAction(nameof(Dashboard));

        public async Task<IActionResult> Dashboard()
        {
            var userId = GetCurrentUserId();
            var isAdmin = User.IsInRole("Admin");

            var setups = isAdmin 
                ? await _eventRepo.GetAllEventBookingsAsync() 
                : await _eventRepo.GetAssignedEventBookingsAsync(userId);

            var packages = await _eventRepo.GetAllPackagesAsync();

            ViewBag.TotalAssigned = setups.Count;
            ViewBag.PendingJobs = setups.Count(s => s.SetupStatus == "Pending" || s.SetupStatus == "Assigned");
            ViewBag.InProgressJobs = setups.Count(s => s.SetupStatus == "InProgress");
            ViewBag.CompletedJobs = setups.Count(s => s.SetupStatus == "Completed" || s.SetupStatus == "Inspected");
            ViewBag.ActivePackagesCount = packages.Count(p => p.IsActive);
            ViewBag.Packages = packages;

            return View(setups.Take(10).ToList());
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
                IsActive = pkg.IsActive
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
                IsActive = model.IsActive
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

        private int GetCurrentUserId()
        {
            var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return int.TryParse(claim, out int id) ? id : 0;
        }
    }
}
