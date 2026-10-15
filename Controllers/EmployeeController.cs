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
                await _configRepo.LogAuditAsync(currentUserId, "EmployeeOverrideRefund", "BookingRefund", refundId, $"Refund #{refundId} status set to {status}. Notes: {notes}");
                TempData["Success"] = $"Refund #{refundId} successfully updated to '{status}'.";
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
            var (success, token) = await _partnerLeadService.ApproveLeadAsync(leadId, currentUserId);
            if (success)
            {
                await _configRepo.LogAuditAsync(currentUserId, "EmployeeApproveLead", "PartnerLead", leadId, $"Approved partner lead #{leadId} and generated activation invite.");
                TempData["Success"] = $"Partner Lead #{leadId} approved and activation email dispatched!";
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
                await _configRepo.LogAuditAsync(currentUserId, "EmployeeRejectLead", "PartnerLead", leadId, $"Rejected partner lead #{leadId}");
                TempData["Success"] = $"Partner Lead #{leadId} marked as Rejected.";
            }
            return RedirectToAction(nameof(PartnerLeads));
        }

        // --- Damage Claims Adjudication ---
        public async Task<IActionResult> DamageClaims(string? status)
        {
            var claims = await _damageClaimService.GetAllClaimsAsync(status);
            ViewBag.SelectedStatus = status;
            return View(claims);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AdjudicateDamageClaim(DamageClaimAdjudicateViewModel model)
        {
            var currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            bool adjudicated = await _damageClaimService.AdjudicateClaimAsync(model, currentUserId);
            if (adjudicated)
            {
                await _configRepo.LogAuditAsync(currentUserId, "EmployeeAdjudicateClaim", "DamageClaim", model.ClaimID, $"Adjudicated claim #{model.ClaimID} as {model.Action}. Approved amount: {model.ApprovedAmount}");
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
                await _configRepo.LogAuditAsync(currentUserId, "EmployeeReviewIdVerification", "ClientIdVerification", verificationId, $"Reviewed verification #{verificationId}: {action}");
                TempData["Success"] = $"Verification #{verificationId} has been marked as {action}.";
            }
            return RedirectToAction(nameof(IdVerifications));
        }

        [HttpGet]
        public IActionResult GetDocumentSignedUrl(string storageKey)
        {
            var currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            string role = User.FindFirstValue(ClaimTypes.Role) ?? "Employee";

            string token = _mediaStorage.GenerateExpiringDocumentToken(storageKey, currentUserId, role, 15);
            string url = $"/api/media/view-id-document?key={Uri.EscapeDataString(storageKey)}&token={Uri.EscapeDataString(token)}";

            return Json(new { success = true, url });
        }
    }
}
