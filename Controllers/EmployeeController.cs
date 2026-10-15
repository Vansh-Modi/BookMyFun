using System.Security.Claims;
using BookMyFun.Models.Entities;
using BookMyFun.Models.ViewModels;
using BookMyFun.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookMyFun.Controllers
{
    [Authorize(Roles = "Employee")]
    public class EmployeeController : Controller
    {
        private readonly IPropertyRepository _propRepo;
        private readonly IUserRepository _userRepo;
        private readonly IBookingRepository _bookingRepo;
        private readonly IConfigRepository _configRepo;
        private readonly IOwnerRequestRepository _ownerReqRepo;
        private readonly IContactRepository _contactRepo;
        private readonly IRefundEngineService _refundEngine;
        private readonly IPartnerLeadService _partnerLeadService;
        private readonly IDamageClaimService _damageClaimService;
        private readonly IIdVerificationService _idVerificationService;
        private readonly IMediaStorageService _mediaStorage;

        public EmployeeController(
            IPropertyRepository propRepo,
            IUserRepository userRepo,
            IBookingRepository bookingRepo,
            IConfigRepository configRepo,
            IOwnerRequestRepository ownerReqRepo,
            IContactRepository contactRepo,
            IRefundEngineService refundEngine,
            IPartnerLeadService partnerLeadService,
            IDamageClaimService damageClaimService,
            IIdVerificationService idVerificationService,
            IMediaStorageService mediaStorage)
        {
            _propRepo = propRepo;
            _userRepo = userRepo;
            _bookingRepo = bookingRepo;
            _configRepo = configRepo;
            _ownerReqRepo = ownerReqRepo;
            _contactRepo = contactRepo;
            _refundEngine = refundEngine;
            _partnerLeadService = partnerLeadService;
            _damageClaimService = damageClaimService;
            _idVerificationService = idVerificationService;
            _mediaStorage = mediaStorage;
        }

        public async Task<IActionResult> Index()
        {
            var properties = await _propRepo.GetAllPropertiesAsync();
            var owners = await _userRepo.GetUsersByRoleAsync("Owner");
            var bookings = await _bookingRepo.GetAllBookingsAsync();

            ViewBag.TotalProperties = properties.Count;
            ViewBag.TotalOwners = owners.Count;
            ViewBag.PendingBookings = bookings.Count(b => b.Status == "Pending");

            return View(properties);
        }

        // --- Employee CRUD on Owners ---
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
            await _configRepo.LogAuditAsync(currentUserId, "EmployeeCreateOwner", "User", id, $"Employee created owner {user.FullName}");

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
            await _configRepo.LogAuditAsync(currentUserId, "EmployeeEditOwner", "User", user.UserID, $"Employee updated owner {user.FullName}");

            return RedirectToAction(nameof(ManageOwners));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteOwner(int id)
        {
            await _userRepo.DeleteUserAsync(id);

            var currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            await _configRepo.LogAuditAsync(currentUserId, "EmployeeDeleteOwner", "User", id, $"Employee deleted owner #{id}");

            return RedirectToAction(nameof(ManageOwners));
        }

        // --- Employee CRUD on Properties ---
        public async Task<IActionResult> ManageProperties()
        {
            var properties = await _propRepo.GetAllPropertiesAsync();
            return View(properties);
        }

        [HttpGet]
        public async Task<IActionResult> CreateProperty()
        {
            var vm = new PropertyEditViewModel
            {
                AvailableTypes = await _propRepo.GetPropertyTypesAsync(),
                AvailableOccasions = await _propRepo.GetOccasionsAsync(),
                AvailableOwners = await _userRepo.GetUsersByRoleAsync("Owner")
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
                PricePerNight = vm.PricePerNight,
                MaxGuests = vm.MaxGuests,
                ListingStatus = "Active",
                IsApproved = true,
                OccasionIDs = vm.SelectedOccasionIDs
            };

            var propId = await _propRepo.CreatePropertyAsync(prop);
            if (!string.IsNullOrEmpty(vm.ImageUrlInput))
            {
                await _propRepo.AddPropertyImageAsync(propId, vm.ImageUrlInput, true);
            }

            var currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            await _configRepo.LogAuditAsync(currentUserId, "EmployeeCreateProperty", "Property", propId, $"Employee created property {prop.Title} for owner #{prop.OwnerID}");

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
                PricePerNight = prop.PricePerNight,
                MaxGuests = prop.MaxGuests,
                ListingStatus = prop.ListingStatus,
                IsApproved = prop.IsApproved,
                SelectedOccasionIDs = prop.OccasionIDs,
                ImageUrlInput = prop.PrimaryImageUrl,
                AvailableTypes = await _propRepo.GetPropertyTypesAsync(),
                AvailableOccasions = await _propRepo.GetOccasionsAsync(),
                AvailableOwners = await _userRepo.GetUsersByRoleAsync("Owner")
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
                PricePerNight = vm.PricePerNight,
                MaxGuests = vm.MaxGuests,
                ListingStatus = vm.ListingStatus,
                OccasionIDs = vm.SelectedOccasionIDs
            };

            await _propRepo.UpdatePropertyAsync(prop);

            if (!string.IsNullOrEmpty(vm.ImageUrlInput))
            {
                await _propRepo.AddPropertyImageAsync(prop.PropertyID, vm.ImageUrlInput, true);
            }

            var currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            await _configRepo.LogAuditAsync(currentUserId, "EmployeeEditProperty", "Property", prop.PropertyID, $"Employee edited property #{prop.PropertyID}");

            return RedirectToAction(nameof(ManageProperties));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteProperty(int id)
        {
            await _propRepo.DeletePropertyAsync(id);

            var currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            await _configRepo.LogAuditAsync(currentUserId, "EmployeeDeleteProperty", "Property", id, $"Employee deleted property #{id}");

            return RedirectToAction(nameof(ManageProperties));
        }

        // --- Employee Review of Owner Trial Requests ---
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
            await _ownerReqRepo.ApproveOwnerRequestAsync(requestId, currentUserId, notes);

            await _configRepo.LogAuditAsync(currentUserId, "EmployeeApproveOwnerRequest", "OwnerRequest", requestId, $"Employee approved owner request #{requestId}");
            TempData["Success"] = "Owner application approved by Employee! Listings are now live on the platform.";
            return RedirectToAction(nameof(OwnerRequests));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RejectOwnerRequest(int requestId, string? notes)
        {
            var currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            await _ownerReqRepo.RejectOwnerRequestAsync(requestId, currentUserId, notes);

            await _configRepo.LogAuditAsync(currentUserId, "EmployeeRejectOwnerRequest", "OwnerRequest", requestId, $"Employee rejected owner request #{requestId}. Purged properties and converted back to client.");
            TempData["Success"] = "Owner application rejected. Trial properties have been purged and user shifted to Client.";
            return RedirectToAction(nameof(OwnerRequests));
        }

        // --- Customer Support Queries Operations ---
        public async Task<IActionResult> SupportQueries()
        {
            var contacts = await _contactRepo.GetAllContactsAsync();
            return View(contacts);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateContactStatus(int contactId, string status)
        {
            await _contactRepo.UpdateStatusAsync(contactId, status);
            TempData["Success"] = $"Query ticket #{contactId} marked as {status}.";
            return RedirectToAction(nameof(SupportQueries));
        }
    }
}
