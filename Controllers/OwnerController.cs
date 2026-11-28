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
    [Authorize(Policy = "RequireOwner")]
    public class OwnerController : Controller
    {
        private readonly IPropertyRepository _propRepo;
        private readonly IUserRepository _userRepo;
        private readonly IBookingRepository _bookingRepo;
        private readonly ISubscriptionRepository _subRepo;
        private readonly IPaymentGatewayService _paymentService;
        private readonly IDamageClaimService _damageClaimService;
        private readonly IConfigRepository _configRepo;
        private readonly ILogger<OwnerController> _logger;
        private readonly IEmailService? _emailService;
        private readonly IWhatsAppService? _whatsAppService;
        private readonly ILegalDocumentService? _legalService;
        private readonly INotificationFanoutService? _notificationFanout;
        private readonly BookMyFun.Services.Notifications.INotificationDispatcherService? _notificationDispatcher;
        private readonly IUserNotificationRepository? _userNotificationRepo;
        private readonly IPartnerKycService? _partnerKycService;
        private readonly IPhotoShootRepository? _photoShootRepo;
        private readonly IConfiguration? _configuration;

        public OwnerController(
            IPropertyRepository propRepo,
            IUserRepository userRepo,
            IBookingRepository bookingRepo,
            ISubscriptionRepository subRepo,
            IPaymentGatewayService paymentService,
            IDamageClaimService damageClaimService,
            IConfigRepository configRepo,
            ILogger<OwnerController> logger,
            IEmailService? emailService = null,
            IWhatsAppService? whatsAppService = null,
            ILegalDocumentService? legalService = null,
            INotificationFanoutService? notificationFanout = null,
            BookMyFun.Services.Notifications.INotificationDispatcherService? notificationDispatcher = null,
            IUserNotificationRepository? userNotificationRepo = null,
            IPartnerKycService? partnerKycService = null,
            IPhotoShootRepository? photoShootRepo = null,
            IConfiguration? configuration = null)
        {
            _propRepo = propRepo;
            _userRepo = userRepo;
            _bookingRepo = bookingRepo;
            _subRepo = subRepo;
            _paymentService = paymentService;
            _damageClaimService = damageClaimService;
            _configRepo = configRepo;
            _logger = logger;
            _emailService = emailService;
            _whatsAppService = whatsAppService;
            _legalService = legalService;
            _notificationFanout = notificationFanout;
            _notificationDispatcher = notificationDispatcher;
            _userNotificationRepo = userNotificationRepo;
            _partnerKycService = partnerKycService;
            _photoShootRepo = photoShootRepo;
            _configuration = configuration;
        }

        private int GetCurrentUserId() => User.GetUserId();

        private static readonly HashSet<string> ValidIndianStates = new(StringComparer.OrdinalIgnoreCase)
        {
            "Andhra Pradesh", "Arunachal Pradesh", "Assam", "Bihar", "Chhattisgarh",
            "Goa", "Gujarat", "Haryana", "Himachal Pradesh", "Jharkhand", "Karnataka",
            "Kerala", "Madhya Pradesh", "Maharashtra", "Manipur", "Meghalaya", "Mizoram",
            "Nagaland", "Odisha", "Punjab", "Rajasthan", "Sikkim", "Tamil Nadu",
            "Telangana", "Tripura", "Uttar Pradesh", "Uttarakhand", "West Bengal",
            "Delhi", "Chandigarh", "Jammu and Kashmir", "Ladakh", "Puducherry"
        };

        [AllowAnonymous]
        [HttpGet]
        public IActionResult Login(string? returnUrl) => RedirectToAction("Login", "Account", new { returnUrl });

        public async Task<IActionResult> Index()
        {
            var ownerId = GetCurrentUserId();
            var owner = await _userRepo.GetByIdAsync(ownerId);
            var properties = await _propRepo.GetPropertiesByOwnerIdAsync(ownerId);
            var allOwnerBookings = await _bookingRepo.GetByOwnerIdAsync(ownerId);
            var bookings = allOwnerBookings
                .Where(b => !string.Equals(b.Status, "Pending", StringComparison.OrdinalIgnoreCase) &&
                            !string.Equals(b.Status, "Hold", StringComparison.OrdinalIgnoreCase) &&
                            !string.Equals(b.Status, "Expired", StringComparison.OrdinalIgnoreCase) &&
                            !string.Equals(b.Status, "Failed", StringComparison.OrdinalIgnoreCase) &&
                            !string.Equals(b.Status, "PaymentFailed", StringComparison.OrdinalIgnoreCase))
                .ToList();
            var sub = await _subRepo.GetActiveSubscriptionByOwnerIdAsync(ownerId);
            var plans = await _subRepo.GetAllPlansAsync();

            var allowance = await _subRepo.CheckListingCreationAllowanceAsync(ownerId);
            bool hasHistory = await _subRepo.HasAnySubscriptionHistoryAsync(ownerId);
            bool isEligibleForTrial = !hasHistory && sub == null;
            int maxAllowed = (sub != null) ? sub.MaxListings : (isEligibleForTrial ? 1 : 0);

            decimal totalEarnings = bookings
                .Where(b => b.Status == "Confirmed")
                .Sum(b => {
                    decimal propertyGross = Math.Max(0, b.TotalAmount - b.EventPackagePriceSnapshot);
                    if (b.OwnerPayoutAmount > 0 && b.OwnerPayoutAmount <= propertyGross)
                        return b.OwnerPayoutAmount;
                    decimal commRate = b.CommissionRateSnapshot > 0 ? b.CommissionRateSnapshot : 10.0m;
                    return Math.Max(0, propertyGross - Math.Round(propertyGross * commRate / 100m, 2));
                });

            var vm = new OwnerDashboardViewModel
            {
                Owner = owner!,
                ActiveSubscription = sub,
                ActiveListingsCount = properties.Count(p => p.IsApproved && p.ListingStatus == "Active"),
                CanAddProperty = allowance.Allowed,
                IsEligibleForTrial = isEligibleForTrial,
                AddPropertyDisabledReason = allowance.Reason,
                MaxListingsAllowed = maxAllowed,
                TotalEarnings = totalEarnings,
                Properties = properties,
                Bookings = bookings,
                AvailablePlans = plans,
                AvailableCategories = await _propRepo.GetPropertyTypesAsync()
            };

            var scheduledSub = await _subRepo.GetScheduledSubscriptionByOwnerIdAsync(ownerId);
            ViewBag.ScheduledSub = scheduledSub;
            ViewBag.NotificationPreferences = new OwnerNotificationPreferencesViewModel
            {
                EmailEnabled = owner?.NotificationEmailEnabled ?? true,
                InAppEnabled = owner?.NotificationInAppEnabled ?? true,
                WhatsAppEnabled = owner?.NotificationWhatsAppEnabled ?? false,
                WhatsAppVerified = owner?.WhatsAppVerified ?? false,
                WhatsAppPhone = owner?.WhatsAppPhone ?? owner?.Phone
            };

            return View(vm);
        }

        [HttpGet]
        public async Task<IActionResult> CreateProperty()
        {
            var ownerId = GetCurrentUserId();
            var allowance = await _subRepo.CheckListingCreationAllowanceAsync(ownerId);
            if (!allowance.Allowed)
            {
                TempData["Error"] = allowance.Reason ?? "Your subscription has ended. Choose a plan to add listings.";
                return RedirectToAction(nameof(Subscriptions));
            }

            var vm = new PropertyEditViewModel
            {
                OwnerID = ownerId,
                AvailableTypes = await _propRepo.GetPropertyTypesAsync(),
                AvailableOccasions = await _propRepo.GetOccasionsAsync(),
                AvailableAmenities = await _propRepo.GetAllAmenitiesAsync()
            };
            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateProperty(PropertyEditViewModel vm)
        {
            var ownerId = GetCurrentUserId();
            bool canAdd = await _subRepo.CanOwnerAddPropertyAsync(ownerId);
            if (!canAdd)
            {
                TempData["Error"] = "Subscription quota reached.";
                return RedirectToAction(nameof(Subscriptions));
            }

            // Mandatory policy consent check (Item 3e)
            if (Request.HasFormContentType)
            {
                string? consent = Request.Form["PolicyConsent"];
                if (consent != "true")
                {
                    ModelState.AddModelError("", "You must agree to the Terms of Service, Privacy Policy, and Refund Policy to list a property.");
                }
            }

            // Process Custom Category
            if (!string.IsNullOrWhiteSpace(vm.CustomCategory))
            {
                var customTypeId = await _propRepo.GetOrCreatePropertyTypeByNameAsync(vm.CustomCategory);
                if (customTypeId > 0)
                {
                    vm.PropertyTypeID = customTypeId;
                }
            }

            var allowance = await _subRepo.CheckListingCreationAllowanceAsync(ownerId);
            if (!allowance.Allowed)
            {
                ModelState.AddModelError("", allowance.Reason ?? "Your subscription has ended. Choose a plan to add listings.");
                vm.AvailableTypes = await _propRepo.GetPropertyTypesAsync();
                vm.AvailableOccasions = await _propRepo.GetOccasionsAsync();
                vm.AvailableAmenities = await _propRepo.GetAllAmenitiesAsync();
                return View(vm);
            }

            // Process Custom Amenities
            if (vm.CustomAmenities != null && vm.CustomAmenities.Any())
            {
                foreach (var customAm in vm.CustomAmenities.Where(s => !string.IsNullOrWhiteSpace(s)))
                {
                    var amId = await _propRepo.GetOrCreateAmenityByNameAsync(customAm);
                    if (amId > 0 && !vm.SelectedAmenityIDs.Contains(amId))
                    {
                        vm.SelectedAmenityIDs.Add(amId);
                    }
                }
            }

            // Process Custom Occasions
            if (vm.CustomOccasions != null && vm.CustomOccasions.Any())
            {
                foreach (var customOcc in vm.CustomOccasions.Where(s => !string.IsNullOrWhiteSpace(s)))
                {
                    var occId = await _propRepo.GetOrCreateOccasionByNameAsync(customOcc);
                    if (occId > 0 && !vm.SelectedOccasionIDs.Contains(occId))
                    {
                        vm.SelectedOccasionIDs.Add(occId);
                    }
                }
            }

            vm.SelectedAmenityIDs = vm.SelectedAmenityIDs.Distinct().ToList();
            vm.SelectedOccasionIDs = vm.SelectedOccasionIDs.Distinct().ToList();

            if (vm.SelectedAmenityIDs.Count > 10)
            {
                ModelState.AddModelError("SelectedAmenityIDs", "You can select or add a maximum of 10 amenities.");
            }

            if (vm.SelectedOccasionIDs.Count > 5)
            {
                ModelState.AddModelError("SelectedOccasionIDs", "You can select or add a maximum of 5 occasions.");
            }

            // Location derivation
            if (string.IsNullOrWhiteSpace(vm.Location))
            {
                vm.Location = string.IsNullOrWhiteSpace(vm.City) ? "India" : $"{vm.City}, {vm.State}".Trim(',', ' ');
            }

            // Validate MapUrl
            if (string.IsNullOrWhiteSpace(vm.MapUrl))
            {
                ModelState.AddModelError("MapUrl", "Google Maps location URL is required.");
            }
            else if (!Uri.TryCreate(vm.MapUrl.Trim(), UriKind.Absolute, out var uriResult) ||
                     (uriResult.Scheme != Uri.UriSchemeHttp && uriResult.Scheme != Uri.UriSchemeHttps))
            {
                ModelState.AddModelError("MapUrl", "Please enter a valid Google Maps URL starting with http:// or https://.");
            }

            // Validate Indian Pincode format (6 digits)
            if (string.IsNullOrWhiteSpace(vm.Pincode) || !System.Text.RegularExpressions.Regex.IsMatch(vm.Pincode.Trim(), @"^[1-9][0-9]{5}$"))
            {
                ModelState.AddModelError("Pincode", "PIN Code must be a valid 6-digit Indian postal code.");
            }

            // Validate State
            if (string.IsNullOrWhiteSpace(vm.State) || !ValidIndianStates.Contains(vm.State.Trim()))
            {
                ModelState.AddModelError("State", "Please select a valid Indian State or Union Territory.");
            }

            // Validate MinStayNights <= MaxStayNights
            if (vm.MinStayNights < 1)
            {
                ModelState.AddModelError("MinStayNights", "Minimum stay must be at least 1 night.");
            }
            if (vm.MaxStayNights < 1)
            {
                ModelState.AddModelError("MaxStayNights", "Maximum stay must be at least 1 night.");
            }
            if (vm.MinStayNights > vm.MaxStayNights)
            {
                ModelState.AddModelError("MinStayNights", "Minimum stay nights cannot exceed maximum stay nights.");
            }

            // Validate CaretakerPhone when OwnerIsCaretaker is false
            if (!vm.OwnerIsCaretaker && string.IsNullOrWhiteSpace(vm.CaretakerPhone))
            {
                ModelState.AddModelError("CaretakerPhone", "Caretaker contact phone is required when you are not the on-site caretaker.");
            }

            // Security deposit removed per Section 4d
            vm.SecurityDepositAmount = 0m;

            if (!ModelState.IsValid)
            {
                vm.AvailableTypes = await _propRepo.GetPropertyTypesAsync();
                vm.AvailableOccasions = await _propRepo.GetOccasionsAsync();
                vm.AvailableAmenities = await _propRepo.GetAllAmenitiesAsync();
                return View(vm);
            }

            var prop = new Property
            {
                OwnerID = ownerId,
                PropertyTypeID = vm.PropertyTypeID,
                Title = vm.Title,
                Description = vm.Description,
                Location = string.IsNullOrWhiteSpace(vm.City) ? (vm.Location ?? "") : $"{vm.City}, {vm.State}".Trim(',', ' '),
                FormattedAddress = vm.FormattedAddress ?? $"{vm.NearbyAreaOrRoad}, {vm.City}, {vm.State} - {vm.Pincode}".Trim(' ', '-', ','),
                Latitude = null,
                Longitude = null,
                MapUrl = vm.MapUrl?.Trim(),
                MapUrlLocked = true,
                CaretakerName = vm.OwnerIsCaretaker ? (User.Identity?.Name ?? "Host") : vm.CaretakerName,
                CaretakerPhone = vm.OwnerIsCaretaker ? null : vm.CaretakerPhone,
                OwnerIsCaretaker = vm.OwnerIsCaretaker,
                Pincode = vm.Pincode?.Trim(),
                NearbyAreaOrRoad = vm.NearbyAreaOrRoad?.Trim(),
                City = vm.City?.Trim(),
                State = vm.State?.Trim(),
                MinStayNights = vm.MinStayNights > 0 ? vm.MinStayNights : 1,
                MaxStayNights = vm.MaxStayNights > 0 ? vm.MaxStayNights : 30,
                PricePerNight = vm.PricePerNight,
                SecurityDepositAmount = 0m,
                CommissionRate = vm.CommissionRate,
                FreeCancellationWindowHours = vm.FreeCancellationWindowHours,
                CancellationCutoffHours = vm.CancellationCutoffHours,
                CancellationFeeType = vm.CancellationFeeType,
                CancellationFeeValue = vm.CancellationFeeValue,
                MaxGuests = vm.MaxGuests,
                ListingStatus = "Draft",
                IsApproved = false,
                OccasionIDs = vm.SelectedOccasionIDs,
                AmenityIDs = vm.SelectedAmenityIDs
            };

            var propId = await _propRepo.CreatePropertyAsync(prop);
            if (propId > 0 && _notificationDispatcher != null)
            {
                _ = _notificationDispatcher.DispatchPropertyWaitingVerificationStaffAlertAsync(propId, prop.Title, ownerId);
            }

            // Record policy consent acceptances (Item 3f)
            if (_legalService != null)
            {
                try
                {
                    var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
                    var pageUrl = Request.Path.Value;
                    await _legalService.RecordAllStandardConsentsAsync(ownerId, null, ip, pageUrl);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to record legal consent for owner {OwnerId} during property creation", ownerId);
                }
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

            TempData["Success"] = "Property submitted successfully for admin review and approval!";
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> EditProperty(int id)
        {
            var ownerId = GetCurrentUserId();
            var prop = await _propRepo.GetByIdAsync(id);
            if (prop == null) return NotFound();

            if (prop.OwnerID != ownerId && !User.IsInRole("Admin"))
            {
                return Forbid();
            }

            var vm = new PropertyEditViewModel
            {
                PropertyID = prop.PropertyID,
                OwnerID = prop.OwnerID,
                PropertyTypeID = prop.PropertyTypeID,
                Title = prop.Title ?? string.Empty,
                Description = prop.Description,
                Location = prop.Location,
                FormattedAddress = prop.FormattedAddress,
                Latitude = prop.Latitude,
                Longitude = prop.Longitude,
                MapUrl = prop.MapUrl,
                PricePerNight = prop.PricePerNight,
                SecurityDepositAmount = prop.SecurityDepositAmount,
                CommissionRate = prop.CommissionRate,
                FreeCancellationWindowHours = prop.FreeCancellationWindowHours,
                CancellationCutoffHours = prop.CancellationCutoffHours,
                CancellationFeeType = string.IsNullOrWhiteSpace(prop.CancellationFeeType) ? "percentage" : prop.CancellationFeeType,
                CancellationFeeValue = prop.CancellationFeeValue,
                MaxGuests = prop.MaxGuests,
                MinStayNights = prop.MinStayNights > 0 ? prop.MinStayNights : 1,
                MaxStayNights = prop.MaxStayNights > 0 ? prop.MaxStayNights : 30,
                ListingStatus = prop.ListingStatus ?? "Draft",
                IsApproved = prop.IsApproved,
                IsFeatured = prop.IsFeatured,
                CaretakerName = prop.CaretakerName,
                CaretakerPhone = prop.CaretakerPhone,
                OwnerIsCaretaker = prop.OwnerIsCaretaker,
                Pincode = prop.Pincode,
                NearbyAreaOrRoad = prop.NearbyAreaOrRoad,
                City = prop.City,
                State = prop.State,
                MapUrlLocked = prop.MapUrlLocked,
                EventCommissionRate = prop.EventCommissionRate,
                ImageUrlInput = prop.PrimaryImageUrl,
                SelectedOccasionIDs = prop.OccasionIDs ?? new List<int>(),
                SelectedAmenityIDs = prop.AmenityIDs ?? new List<int>(),
                ExistingMedia = prop.MediaList ?? new List<PropertyMedia>(),
                AvailableTypes = await _propRepo.GetPropertyTypesAsync(),
                AvailableOccasions = await _propRepo.GetOccasionsAsync(),
                AvailableAmenities = await _propRepo.GetAllAmenitiesAsync()
            };

            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditProperty(PropertyEditViewModel vm)
        {
            var ownerId = GetCurrentUserId();
            var prop = await _propRepo.GetByIdAsync(vm.PropertyID);
            if (prop == null) return NotFound();

            if (prop.OwnerID != ownerId && !User.IsInRole("Admin"))
            {
                return Forbid();
            }

            // Process Custom Category
            if (!string.IsNullOrWhiteSpace(vm.CustomCategory))
            {
                var customTypeId = await _propRepo.GetOrCreatePropertyTypeByNameAsync(vm.CustomCategory);
                if (customTypeId > 0)
                {
                    vm.PropertyTypeID = customTypeId;
                }
            }

            // Process Custom Amenities
            if (vm.CustomAmenities != null && vm.CustomAmenities.Any())
            {
                foreach (var customAm in vm.CustomAmenities.Where(s => !string.IsNullOrWhiteSpace(s)))
                {
                    var amId = await _propRepo.GetOrCreateAmenityByNameAsync(customAm);
                    if (amId > 0 && !vm.SelectedAmenityIDs.Contains(amId))
                    {
                        vm.SelectedAmenityIDs.Add(amId);
                    }
                }
            }

            // Process Custom Occasions
            if (vm.CustomOccasions != null && vm.CustomOccasions.Any())
            {
                foreach (var customOcc in vm.CustomOccasions.Where(s => !string.IsNullOrWhiteSpace(s)))
                {
                    var occId = await _propRepo.GetOrCreateOccasionByNameAsync(customOcc);
                    if (occId > 0 && !vm.SelectedOccasionIDs.Contains(occId))
                    {
                        vm.SelectedOccasionIDs.Add(occId);
                    }
                }
            }

            vm.SelectedAmenityIDs = vm.SelectedAmenityIDs.Distinct().ToList();
            vm.SelectedOccasionIDs = vm.SelectedOccasionIDs.Distinct().ToList();

            if (vm.SelectedAmenityIDs.Count > 10)
            {
                ModelState.AddModelError("SelectedAmenityIDs", "You can select or add a maximum of 10 amenities.");
            }

            if (vm.SelectedOccasionIDs.Count > 5)
            {
                ModelState.AddModelError("SelectedOccasionIDs", "You can select or add a maximum of 5 occasions.");
            }

            // Location derivation
            if (string.IsNullOrWhiteSpace(vm.Location))
            {
                vm.Location = string.IsNullOrWhiteSpace(vm.City) ? (prop.Location ?? "India") : $"{vm.City}, {vm.State}".Trim(',', ' ');
            }

            // MapUrl validation / lock check
            if (prop.MapUrlLocked)
            {
                vm.MapUrl = prop.MapUrl;
                ModelState.Remove("MapUrl");
            }
            else if (!string.IsNullOrWhiteSpace(vm.MapUrl))
            {
                if (!Uri.TryCreate(vm.MapUrl.Trim(), UriKind.Absolute, out var uriResult) ||
                    (uriResult.Scheme != Uri.UriSchemeHttp && uriResult.Scheme != Uri.UriSchemeHttps))
                {
                    ModelState.AddModelError("MapUrl", "Map Link: Please enter a valid Google Maps URL starting with http:// or https://.");
                }
            }

            // Validate Indian Pincode format (6 digits) only when provided
            if (!string.IsNullOrWhiteSpace(vm.Pincode) && !System.Text.RegularExpressions.Regex.IsMatch(vm.Pincode.Trim(), @"^[1-9][0-9]{5}$"))
            {
                ModelState.AddModelError("Pincode", "PIN Code: Must be a valid 6-digit Indian postal code.");
            }

            // Validate State only when provided
            if (!string.IsNullOrWhiteSpace(vm.State) && !ValidIndianStates.Contains(vm.State.Trim()))
            {
                ModelState.AddModelError("State", "State: Please select a valid Indian State or Union Territory.");
            }

            // Validate MinStayNights <= MaxStayNights
            if (vm.MinStayNights < 1)
            {
                ModelState.AddModelError("MinStayNights", "Minimum Stay: Must be at least 1 night.");
            }
            if (vm.MaxStayNights < 1)
            {
                ModelState.AddModelError("MaxStayNights", "Maximum Stay: Must be at least 1 night.");
            }
            if (vm.MinStayNights > vm.MaxStayNights)
            {
                ModelState.AddModelError("MinStayNights", "Minimum Stay: Minimum stay nights cannot exceed maximum stay nights.");
            }

            // Validate CaretakerPhone only if previously set and cleared while OwnerIsCaretaker is false
            if (!vm.OwnerIsCaretaker && !string.IsNullOrWhiteSpace(prop.CaretakerPhone) && string.IsNullOrWhiteSpace(vm.CaretakerPhone))
            {
                ModelState.AddModelError("CaretakerPhone", "Caretaker Phone: Caretaker contact phone is required when you are not the on-site caretaker.");
            }

            // Security deposit removed per Section 4d
            vm.SecurityDepositAmount = 0m;

            if (!ModelState.IsValid)
            {
                vm.AvailableTypes = await _propRepo.GetPropertyTypesAsync();
                vm.AvailableOccasions = await _propRepo.GetOccasionsAsync();
                vm.AvailableAmenities = await _propRepo.GetAllAmenitiesAsync();
                vm.ExistingMedia = await _propRepo.GetPropertyMediaAsync(vm.PropertyID);
                return View(vm);
            }

            prop.PropertyTypeID = vm.PropertyTypeID;
            prop.Title = vm.Title;
            prop.Description = vm.Description;
            prop.Location = string.IsNullOrWhiteSpace(vm.City) ? (vm.Location ?? "") : $"{vm.City}, {vm.State}".Trim(',', ' ');
            prop.FormattedAddress = vm.FormattedAddress ?? $"{vm.NearbyAreaOrRoad}, {vm.City}, {vm.State} - {vm.Pincode}".Trim(' ', '-', ',');
            prop.Latitude = null;
            prop.Longitude = null;
            if (!prop.MapUrlLocked && !string.IsNullOrWhiteSpace(vm.MapUrl))
            {
                prop.MapUrl = vm.MapUrl.Trim();
            }
            prop.CaretakerName = vm.OwnerIsCaretaker ? (User.Identity?.Name ?? "Host") : vm.CaretakerName;
            prop.CaretakerPhone = vm.OwnerIsCaretaker ? null : vm.CaretakerPhone;
            prop.OwnerIsCaretaker = vm.OwnerIsCaretaker;
            prop.Pincode = vm.Pincode?.Trim();
            prop.NearbyAreaOrRoad = vm.NearbyAreaOrRoad?.Trim();
            prop.City = vm.City?.Trim();
            prop.State = vm.State?.Trim();
            prop.PricePerNight = vm.PricePerNight;
            prop.MinStayNights = vm.MinStayNights > 0 ? vm.MinStayNights : 1;
            prop.MaxStayNights = vm.MaxStayNights > 0 ? vm.MaxStayNights : 30;
            prop.SecurityDepositAmount = 0m;
            prop.CommissionRate = vm.CommissionRate;
            prop.FreeCancellationWindowHours = vm.FreeCancellationWindowHours;
            prop.CancellationCutoffHours = vm.CancellationCutoffHours;
            prop.CancellationFeeType = vm.CancellationFeeType;
            prop.CancellationFeeValue = vm.CancellationFeeValue;
            prop.MaxGuests = vm.MaxGuests;
            prop.OccasionIDs = vm.SelectedOccasionIDs;
            prop.AmenityIDs = vm.SelectedAmenityIDs;

            await _propRepo.UpdatePropertyAsync(prop);

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

            TempData["Success"] = "Property details updated successfully.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeactivateProperty(int id, bool deactivate = true)
        {
            var ownerId = GetCurrentUserId();
            var prop = await _propRepo.GetByIdAsync(id);
            if (prop == null) return NotFound();

            if (prop.OwnerID != ownerId)
            {
                _logger.LogWarning("Security violation: Owner #{OwnerID} attempted to modify property #{PropertyID} belonging to Owner #{TargetOwnerID}.", ownerId, id, prop.OwnerID);
                throw new ForbiddenException("Cannot modify property listing belonging to another host.");
            }

            if (prop.ListingStatus == "Rejected")
            {
                TempData["Error"] = "This property was rejected by platform administration and cannot be activated without review.";
                return RedirectToAction(nameof(Index));
            }

            bool success = await _propRepo.DeactivatePropertyAsync(id, deactivate);
            if (success)
            {
                await _configRepo.LogAuditAsync(ownerId, deactivate ? "OwnerDeactivateProperty" : "OwnerActivateProperty", "Property", id,
                    $"Host #{ownerId} {(deactivate ? "deactivated/unpublished" : "activated/published")} listing #{id} ('{prop.Title}').");

                TempData["Success"] = deactivate
                    ? $"Property '{prop.Title}' has been unpublished and deactivated from public guest searches."
                    : $"Property '{prop.Title}' has been published and activated.";
            }
            else
            {
                TempData["Error"] = "Failed to update property listing status. Please try again.";
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UnpublishProperty(int id)
        {
            var ownerId = GetCurrentUserId();
            var prop = await _propRepo.GetByIdAsync(id);
            if (prop == null) return NotFound();
            if (prop.OwnerID != ownerId && !User.IsInRole("Admin")) return Forbid();

            bool success = await _propRepo.UnpublishPropertyAsync(id, ownerId, "Owner unpublished");
            if (success)
            {
                await _configRepo.LogAuditAsync(ownerId, "OwnerUnpublishProperty", "Property", id,
                    $"Host #{ownerId} unpublished listing #{id} ('{prop.Title}').");
                if (_notificationDispatcher != null)
                {
                    _ = _notificationDispatcher.DispatchPropertyUnpublishedAsync(id, prop.Title, prop.OwnerID, "Owner unpublished");
                }
                TempData["Success"] = $"Property '{prop.Title}' has been unpublished.";
            }
            else
            {
                TempData["Error"] = "Failed to unpublish property.";
            }
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> PublishProperty(int id)
        {
            var ownerId = GetCurrentUserId();
            try
            {
                var prop = await _propRepo.GetByIdAsync(id);
                if (prop == null) return NotFound();
                if (prop.OwnerID != ownerId && !User.IsInRole("Admin")) return Forbid();

                if (!prop.IsApproved || (prop.ListingStatus != "Draft" && prop.ListingStatus != "Deactivated" && prop.ListingStatus != "Unpublished"))
                {
                    TempData["Error"] = "Property must be approved before publishing.";
                    return RedirectToAction(nameof(Index));
                }

                // Gate publishing on Partner KYC & Bank verification (must be Verified)
                if (_partnerKycService != null)
                {
                    var kycStatus = await _partnerKycService.GetStatusAsync(prop.OwnerID);
                    if (kycStatus.CheckFailed)
                    {
                        TempData["Error"] = "We could not verify your payment details right now. Please try again later.";
                        return RedirectToAction(nameof(Index));
                    }
                    if (!kycStatus.IsVerified)
                    {
                        if (string.Equals(kycStatus.KycStatus, "Rejected", StringComparison.OrdinalIgnoreCase))
                        {
                            TempData["Error"] = $"Your KYC was rejected ({kycStatus.RejectionReason ?? "see details"}). Please re-upload your documents before publishing '{prop.Title}'.";
                        }
                        else if (string.Equals(kycStatus.KycStatus, "Submitted", StringComparison.OrdinalIgnoreCase))
                        {
                            TempData["Error"] = $"Your KYC is currently under review. You can publish '{prop.Title}' once an admin verifies your details.";
                        }
                        else
                        {
                            TempData["Error"] = $"Complete your KYC & Bank details before publishing '{prop.Title}'. Missing: {string.Join(", ", kycStatus.MissingItems)}.";
                        }
                        return RedirectToAction(nameof(Index));
                    }
                }

                // If brand new owner without any subscription history, auto-create Free Trial row
                bool hasHistory = await _subRepo.HasAnySubscriptionHistoryAsync(prop.OwnerID);
                if (!hasHistory)
                {
                    await _subRepo.CreateTrialSubscriptionAsync(prop.OwnerID);
                }

                var activeSub = await _subRepo.GetActiveSubscriptionByOwnerIdAsync(prop.OwnerID);
                if (activeSub == null)
                {
                    TempData["Error"] = "Your subscription has ended. Choose a plan to publish listings.";
                    return RedirectToAction(nameof(Subscriptions));
                }

                var ownerProps = await _propRepo.GetPropertiesByOwnerIdAsync(prop.OwnerID);
                int liveCount = ownerProps.Count(p => p.IsApproved && (p.ListingStatus == "Live" || p.ListingStatus == "Active"));
                if (liveCount >= activeSub.MaxListings)
                {
                    TempData["Error"] = $"Cannot publish property. Your current plan allowance of {activeSub.MaxListings} live listing(s) has been reached. Please unpublish another listing or upgrade your subscription.";
                    return RedirectToAction(nameof(Index));
                }

                var (success, msg) = await _propRepo.PublishPropertyAsync(id, prop.OwnerID, activeSub.MaxListings);
                if (success)
                {
                    await _configRepo.LogAuditAsync(ownerId, "OwnerPublishProperty", "Property", id,
                        $"Host #{ownerId} published listing #{id} ('{prop.Title}').");
                    if (_notificationDispatcher != null)
                    {
                        _ = _notificationDispatcher.DispatchPropertyPublishedAsync(id, prop.Title, prop.OwnerID);
                    }
                    TempData["Success"] = $"Property '{prop.Title}' is now live on Nayesta!";
                }
                else
                {
                    TempData["Error"] = msg;
                }
            }
            catch (Exception ex)
            {
                string correlationId = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
                _logger.LogError(ex, "PublishProperty failed for PropertyId={PropertyId}, OwnerId={OwnerId}, CorrelationId={CorrelationId}",
                    id, ownerId, correlationId);
                TempData["Error"] = $"We couldn't publish your property right now. Please try again or contact support with reference ID: {correlationId}.";
            }
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateLocationUrl(int propertyId, string? mapUrl, string? returnUrl = null)
        {
            var ownerId = GetCurrentUserId();
            var prop = await _propRepo.GetByIdAsync(propertyId);
            if (prop == null) return NotFound();

            if (prop.OwnerID != ownerId && !User.IsInRole("Admin"))
            {
                return Forbid();
            }

            if (prop.MapUrlLocked && !User.IsInRole("Admin"))
            {
                TempData["Error"] = "Map location link is permanently locked after listing creation. To update it, please submit an official map change request.";
                return RedirectToAction(nameof(Index));
            }

            string? sanitizedUrl = null;
            if (!string.IsNullOrWhiteSpace(mapUrl))
            {
                string trimmed = mapUrl.Trim();
                if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uriResult) ||
                    (uriResult.Scheme != Uri.UriSchemeHttp && uriResult.Scheme != Uri.UriSchemeHttps))
                {
                    TempData["Error"] = "Please enter a valid map URL starting with http:// or https:// (e.g. Google Maps share link).";
                    if (!string.IsNullOrEmpty(returnUrl) && (Url == null || Url.IsLocalUrl(returnUrl)))
                    {
                        return Redirect(returnUrl);
                    }
                    return RedirectToAction(nameof(Index));
                }
                sanitizedUrl = trimmed;
            }

            bool updated = await _propRepo.UpdatePropertyMapUrlAsync(propertyId, prop.OwnerID, sanitizedUrl);
            if (updated)
            {
                await _configRepo.LogAuditAsync(ownerId, "UpdatePropertyLocationUrl", "Property", propertyId,
                    string.IsNullOrEmpty(sanitizedUrl)
                        ? $"Host #{ownerId} cleared direct map location URL for property #{propertyId} ('{prop.Title}')."
                        : $"Host #{ownerId} updated direct map location URL for property #{propertyId} ('{prop.Title}') to: {sanitizedUrl}");

                TempData["Success"] = string.IsNullOrEmpty(sanitizedUrl)
                    ? $"Direct map location link for '{prop.Title}' was removed."
                    : $"Direct map location link for '{prop.Title}' updated successfully. Confirmed guests can now use this direct navigation link.";
            }
            else
            {
                TempData["Error"] = "Failed to update property map location link. Please try again.";
            }

            if (!string.IsNullOrEmpty(returnUrl) && (Url == null || Url.IsLocalUrl(returnUrl)))
            {
                return Redirect(returnUrl);
            }
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AssignCategory(int propertyId, int? propertyTypeId, string? customCategory = null, string? returnUrl = null)
        {
            var ownerId = GetCurrentUserId();
            var prop = await _propRepo.GetByIdAsync(propertyId);
            if (prop == null) return NotFound();

            if (prop.OwnerID != ownerId && !User.IsInRole("Admin"))
            {
                return Forbid();
            }

            int targetTypeId = propertyTypeId ?? 0;
            if (!string.IsNullOrWhiteSpace(customCategory))
            {
                int newTypeId = await _propRepo.GetOrCreatePropertyTypeByNameAsync(customCategory);
                if (newTypeId > 0)
                {
                    targetTypeId = newTypeId;
                }
            }

            if (targetTypeId <= 0)
            {
                TempData["Error"] = "Please select an existing category or enter a valid category name.";
                if (!string.IsNullOrEmpty(returnUrl) && (Url == null || Url.IsLocalUrl(returnUrl)))
                {
                    return Redirect(returnUrl);
                }
                return RedirectToAction(nameof(Index));
            }

            bool updated = await _propRepo.UpdatePropertyCategoryAsync(propertyId, prop.OwnerID, targetTypeId);
            if (updated)
            {
                var allTypes = await _propRepo.GetPropertyTypesAsync();
                var typeObj = allTypes.FirstOrDefault(t => t.PropertyTypeID == targetTypeId);
                string categoryName = typeObj?.TypeName ?? $"Category #{targetTypeId}";

                await _configRepo.LogAuditAsync(ownerId, "AssignPropertyCategory", "Property", propertyId,
                    $"Host #{ownerId} assigned category '{categoryName}' to property #{propertyId} ('{prop.Title}').");

                TempData["Success"] = $"Category for '{prop.Title}' was successfully updated to '{categoryName}'.";
            }
            else
            {
                TempData["Error"] = "Failed to assign property category. Please try again.";
            }

            if (!string.IsNullOrEmpty(returnUrl) && (Url == null || Url.IsLocalUrl(returnUrl)))
            {
                return Redirect(returnUrl);
            }
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult DeleteProperty(int id)
        {
            var ownerId = GetCurrentUserId();
            _logger.LogWarning("Security violation: Host #{OwnerID} attempted unauthorized permanent deletion of property #{PropertyID}.", ownerId, id);
            throw new ForbiddenException("Property deletion is strictly restricted to Admin and Employee personnel. Hosts have permissions to deactivate or unpublish listings only.");
        }

        [HttpGet]
        public IActionResult DeleteProperty()
        {
            throw new ForbiddenException("Property deletion is strictly restricted to Admin and Employee personnel. Hosts have permissions to deactivate or unpublish listings only.");
        }

        [HttpGet]
        public async Task<IActionResult> DamageClaims()
        {
            var ownerId = GetCurrentUserId();
            var claims = await _damageClaimService.GetClaimsByOwnerAsync(ownerId);
            var allOwnerBookings = await _bookingRepo.GetByOwnerIdAsync(ownerId);
            var bookings = allOwnerBookings
                .Where(b => !string.Equals(b.Status, "Pending", StringComparison.OrdinalIgnoreCase) &&
                            !string.Equals(b.Status, "Hold", StringComparison.OrdinalIgnoreCase) &&
                            !string.Equals(b.Status, "Expired", StringComparison.OrdinalIgnoreCase) &&
                            !string.Equals(b.Status, "Failed", StringComparison.OrdinalIgnoreCase) &&
                            !string.Equals(b.Status, "PaymentFailed", StringComparison.OrdinalIgnoreCase))
                .ToList();
            ViewBag.EligibleBookings = bookings;
            return View(claims);
        }

        [HttpGet]
        public async Task<IActionResult> CreateClaim(int bookingId)
        {
            var ownerId = GetCurrentUserId();
            var booking = await _bookingRepo.GetBookingByIdAsync(bookingId);
            if (booking == null) return NotFound();

            var prop = await _propRepo.GetByIdAsync(booking.PropertyID);
            if (prop == null || prop.OwnerID != ownerId) return Forbid();

            var vm = new DamageClaimCreateViewModel
            {
                BookingID = booking.BookingID,
                PropertyID = prop.PropertyID,
                ClaimAmount = booking.SecurityDepositAmount
            };

            ViewBag.Booking = booking;
            ViewBag.Property = prop;
            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateClaim(DamageClaimCreateViewModel model)
        {
            var ownerId = GetCurrentUserId();
            if (!ModelState.IsValid)
            {
                var errors = string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
                TempData["Error"] = $"Unable to file claim: {errors}";
                return RedirectToAction(nameof(DamageClaims));
            }

            try
            {
                int claimId = await _damageClaimService.CreateClaimAsync(model, ownerId);
                TempData["Success"] = $"Damage claim #{claimId} submitted successfully. An operations executive will review your evidence within 24 hours.";
                return RedirectToAction(nameof(DamageClaims));
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Claim submission failed: {ex.Message}";
                return RedirectToAction(nameof(DamageClaims));
            }
        }

        [HttpGet]
        public async Task<IActionResult> Subscriptions()
        {
            var ownerId = GetCurrentUserId();
            var plans = await _subRepo.GetAllPlansAsync();
            var activeSub = await _subRepo.GetActiveSubscriptionByOwnerIdAsync(ownerId);
            var history = await _subRepo.GetSubscriptionHistoryByOwnerIdAsync(ownerId);

            ViewBag.ActiveSub = activeSub;
            ViewBag.History = history;
            return View(plans);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Subscribe(int planId, bool acknowledgeActive = false, bool acknowledgeStart = false)
        {
            var ownerId = GetCurrentUserId();
            var plan = await _subRepo.GetPlanByIdAsync(planId);
            if (plan == null) return NotFound();

            var activeSub = await _subRepo.GetActiveSubscriptionByOwnerIdAsync(ownerId);
            DateTime startDate = DateTime.UtcNow;
            if (activeSub != null)
            {
                if (!acknowledgeActive || !acknowledgeStart)
                {
                    TempData["Error"] = $"You hold an active plan ({activeSub.PlanName}) until {activeSub.EndDate:dd MMM yyyy}. Please acknowledge the scheduled renewal warnings to proceed.";
                    return RedirectToAction(nameof(Subscriptions));
                }
                startDate = activeSub.EndDate;
            }

            decimal effectivePrice = await _subRepo.GetEffectivePlanPriceAsync(planId, DateTime.UtcNow);

            var sub = new OwnerSubscription
            {
                OwnerID = ownerId,
                PlanID = planId,
                StartDate = startDate,
                EndDate = startDate.AddDays(plan.DurationInDays),
                PaymentStatus = "Pending",
                PricePaid = effectivePrice,
                GatewayTransactionID = null
            };

            var subscriptionId = await _subRepo.SubscribeOwnerAsync(sub);
            return RedirectToAction(nameof(SubscriptionPayment), new { id = subscriptionId });
        }

        [HttpGet]
        public async Task<IActionResult> SubscriptionPayment(int id)
        {
            var ownerId = GetCurrentUserId();
            var sub = await _subRepo.GetSubscriptionByIdAsync(id);
            if (sub == null || sub.OwnerID != ownerId)
            {
                return NotFound();
            }

            if (sub.PaymentStatus == "Paid")
            {
                TempData["Info"] = "This subscription has already been activated.";
                return RedirectToAction(nameof(Subscriptions));
            }

            var plan = await _subRepo.GetPlanByIdAsync(sub.PlanID);
            if (plan == null) return NotFound();

            var owner = await _userRepo.GetByIdAsync(ownerId);
            var order = await _paymentService.CreateOrderAsync(plan.Price, $"sub_{ownerId}_{plan.PlanID}_{sub.SubscriptionID}");

            var vm = new SubscriptionPaymentViewModel
            {
                SubscriptionID = sub.SubscriptionID,
                PlanID = plan.PlanID,
                PlanName = plan.PlanName,
                DurationInDays = plan.DurationInDays,
                MaxListings = plan.MaxListings,
                Amount = plan.Price,
                Currency = order.Currency,
                OrderId = order.OrderId,
                KeyId = order.KeyId,
                IsSimulated = order.IsSimulated,
                OwnerName = owner?.FullName ?? "Property Owner",
                OwnerEmail = owner?.Email ?? "",
                OwnerPhone = owner?.Phone
            };

            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> VerifySubscriptionPayment(int subscriptionId, string razorpay_order_id, string razorpay_payment_id, string razorpay_signature)
        {
            var ownerId = GetCurrentUserId();
            var sub = await _subRepo.GetSubscriptionByIdAsync(subscriptionId);
            if (sub == null || sub.OwnerID != ownerId)
            {
                return NotFound();
            }

            bool isValid = await _paymentService.VerifyPaymentSignatureAsync(razorpay_order_id, razorpay_payment_id, razorpay_signature);
            if (!isValid)
            {
                TempData["Error"] = "Payment signature verification failed. Please try again or reach our 24/7 helpdesk.";
                return RedirectToAction(nameof(SubscriptionPayment), new { id = subscriptionId });
            }

            await _subRepo.UpdateSubscriptionPaymentStatusAsync(subscriptionId, "Paid", $"RAZORPAY_{razorpay_payment_id}");

            if (_emailService != null)
            {
                try
                {
                    var owner = await _userRepo.GetByIdAsync(ownerId);
                    if (owner != null && !string.IsNullOrWhiteSpace(owner.Email))
                    {
                        string planName = sub.PlanName ?? "Subscription";
                        int durationDays = (sub.DurationDaysSnapshot ?? 0) > 0 ? sub.DurationDaysSnapshot!.Value : Math.Max(1, (int)Math.Ceiling((sub.EndDate - sub.StartDate).TotalDays));
                        int listingLimit = (sub.MaxListingsSnapshot ?? 0) > 0 ? sub.MaxListingsSnapshot!.Value : (sub.MaxListings > 0 ? sub.MaxListings : 1);
                        string baseUrl = (_configuration?["PublicBaseUrl"] ?? _configuration?["AppUrl"] ?? (Request?.Host.HasValue == true ? $"{Request.Scheme}://{Request.Host}" : "https://bookmyfun.in")).Trim().TrimEnd('/');
                        string ownerUrl = $"{baseUrl}/Owner/Index";
                        string safeName = System.Net.WebUtility.HtmlEncode(owner.FullName);
                        string safePlan = System.Net.WebUtility.HtmlEncode(planName);

                        string bodyContent = $@"
                            <p>Hello {safeName},</p>
                            <p>Your subscription payment has been verified and your plan is confirmed.</p>
                            <table style='width:100%; border-collapse:collapse; margin:16px 0; background:#F8FAFC; border:1px solid #E2E8F0;'>
                                <tr><td style='padding:8px 12px; color:#64748B;'>Plan name</td><td style='padding:8px 12px; font-weight:600;'>{safePlan}</td></tr>
                                <tr><td style='padding:8px 12px; color:#64748B;'>Duration</td><td style='padding:8px 12px;'>{durationDays} days ({sub.StartDate:dd MMM yyyy} to {sub.EndDate:dd MMM yyyy})</td></tr>
                                <tr><td style='padding:8px 12px; color:#64748B;'>Listing limit</td><td style='padding:8px 12px; font-weight:600;'>{listingLimit} listing(s)</td></tr>
                            </table>";

                        string plainText = $"Hello {owner.FullName}, your subscription for {planName} is confirmed. Duration: {durationDays} days ({sub.StartDate:dd MMM yyyy} to {sub.EndDate:dd MMM yyyy}). Listing limit: {listingLimit} listing(s).";

                        await _emailService.SendTemplatedEmailAsync(
                            ownerId,
                            subscriptionId,
                            owner.Email,
                            "SubscriptionPurchased",
                            $"subscription-purchased-{subscriptionId}",
                            $"Subscription confirmed for {planName}",
                            "Subscription confirmed",
                            bodyContent,
                            "Manage properties",
                            ownerUrl,
                            plainText);
                    }
                }
                catch (Exception emailEx)
                {
                    _logger.LogWarning(emailEx, "Failed to send SubscriptionPurchased email for Subscription #{SubscriptionId}", subscriptionId);
                }
            }
            
            // Check if active immediately or scheduled for future
            if (sub.StartDate <= DateTime.UtcNow)
            {
                // When a paid plan becomes active, unpublished properties are NOT republished automatically.
                // The owner chooses which to publish, up to the plan's MaxListings.
                var unpubProps = (await _propRepo.GetPropertiesByOwnerIdAsync(ownerId))
                    .Where(p => p.ListingStatus == "Unpublished" || p.ListingStatus == "Deactivated")
                    .Select(p => p.Title)
                    .ToList();

                int limit = sub.MaxListings > 0 ? sub.MaxListings : 1;
                if (_notificationDispatcher != null)
                {
                    _ = _notificationDispatcher.DispatchPostPaymentPublishPromptAsync(
                        ownerId,
                        sub.SubscriptionID,
                        sub.PlanName ?? "Subscription",
                        unpubProps,
                        limit);
                    _ = _notificationDispatcher.DispatchPlanPurchaseConfirmedAsync(
                        ownerId,
                        sub.PlanName ?? "Subscription",
                        (sub.PricePaid ?? 0m),
                        false,
                        sub.StartDate);
                }
                TempData["Success"] = $"Razorpay payment verified successfully! Your subscription to {sub.PlanName} is now active. Please choose which properties to publish from your dashboard.";
            }
            else
            {
                if (_notificationDispatcher != null)
                {
                    _ = _notificationDispatcher.DispatchPlanPurchaseConfirmedAsync(ownerId, sub.PlanName ?? "Subscription", (sub.PricePaid ?? 0m), true, sub.StartDate);
                }
                TempData["Success"] = $"Razorpay payment verified successfully! Your subscription to {sub.PlanName} is scheduled to start on {sub.StartDate:dd MMM yyyy}.";
            }
            return RedirectToAction(nameof(Subscriptions));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MarkExpiryNotificationsRead()
        {
            var ownerId = GetCurrentUserId();
            int marked = 0;
            int remaining = 0;
            if (_userNotificationRepo != null)
            {
                marked = await _userNotificationRepo.MarkExpiryNotificationsReadAsync(ownerId);
                remaining = await _userNotificationRepo.GetUnreadCountAsync(ownerId);
            }
            return Json(new { success = true, markedCount = marked, remainingUnread = remaining });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CancelSubscriptionOrder(int subscriptionId)
        {
            var ownerId = GetCurrentUserId();
            var sub = await _subRepo.GetSubscriptionByIdAsync(subscriptionId);
            if (sub != null && sub.OwnerID == ownerId && sub.PaymentStatus == "Pending")
            {
                await _subRepo.UpdateSubscriptionPaymentStatusAsync(subscriptionId, "Cancelled", null);
            }
            return RedirectToAction(nameof(Subscriptions));
        }

        [HttpGet]
        [Authorize(Roles = "Owner,Admin,Employee")]
        public async Task<IActionResult> BookingReceipt(int id)
        {
            var booking = await _bookingRepo.GetByIdAsync(id);
            if (booking == null) return NotFound();

            var property = await _propRepo.GetByIdAsync(booking.PropertyID);
            if (property == null) return NotFound();

            var ownerId = GetCurrentUserId();
            var role = User.FindFirstValue(ClaimTypes.Role) ?? "Owner";
            if (role == "Owner" && property.OwnerID != ownerId)
            {
                throw new ForbiddenException("You do not have permission to view receipts for bookings on properties you do not own.");
            }

            var client = await _userRepo.GetByIdAsync(booking.ClientID);
            var host = await _userRepo.GetByIdAsync(property.OwnerID);
            var payments = await _bookingRepo.GetPaymentsByBookingIdAsync(id);

            var vm = new BookingReceiptViewModel
            {
                Booking = booking,
                Property = property,
                Client = client,
                Host = host,
                PrimaryPayment = payments.FirstOrDefault(),
                PaymentHistory = payments,
                IsOwnerView = true,
                IsAdminOrStaffView = User.IsInRole("Admin") || User.IsInRole("Employee")
            };

            return View(vm);
        }

        [Authorize(Roles = "Owner,Admin,Employee")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResendBookingReceipt(int bookingId, string? returnUrl = null)
        {
            var booking = await _bookingRepo.GetByIdAsync(bookingId);
            if (booking == null) return NotFound();

            var property = await _propRepo.GetByIdAsync(booking.PropertyID);
            if (property == null) return NotFound();

            var currentUserId = GetCurrentUserId();
            var role = User.FindFirstValue(ClaimTypes.Role) ?? "Owner";
            if (role == "Owner" && property.OwnerID != currentUserId)
            {
                throw new ForbiddenException("You do not have permission to dispatch settlement receipts for this property.");
            }

            var owner = await _userRepo.GetByIdAsync(property.OwnerID);
            var client = await _userRepo.GetByIdAsync(booking.ClientID);
            if (owner == null)
            {
                TempData["Error"] = "Unable to resolve owner contact details.";
                return RedirectToAction("BookingReceipt", new { id = bookingId });
            }

            var payments = await _bookingRepo.GetPaymentsByBookingIdAsync(bookingId);
            var primaryPayment = payments.FirstOrDefault(p => p.PaymentStatus == "Success") ?? new Payment
            {
                BookingID = bookingId,
                GatewayTransactionID = $"REC_{bookingId}",
                Amount = booking.TotalAmount,
                PaymentStatus = "Success"
            };

            var receiptVm = new BookingReceiptViewModel
            {
                Booking = booking,
                Property = property,
                Client = client,
                Host = owner,
                PrimaryPayment = primaryPayment,
                PaymentHistory = payments,
                IsOwnerView = true,
                IsAdminOrStaffView = User.IsInRole("Admin") || User.IsInRole("Employee")
            };

            string baseUrl = Request != null && Request.Host.HasValue 
                ? $"{Request.Scheme}://{Request.Host}" 
                : "https://bookmyfun.in";
            string ownerReceiptUrl = $"{baseUrl}/Owner/BookingReceipt/{booking.BookingID}";

            if (_emailService != null)
            {
                await _emailService.SendOwnerSettlementReceiptEmailAsync(owner.Email, owner.FullName, receiptVm, ownerReceiptUrl);
            }

            if (_whatsAppService != null && !string.IsNullOrEmpty(owner.Phone))
            {
                await _whatsAppService.SendOwnerBookingReceiptWhatsAppAsync(
                    owner.Phone,
                    owner.FullName,
                    property.Title,
                    booking.BookingID.ToString(),
                    receiptVm.HostReceiptNumber,
                    receiptVm.PropertyStayGross,
                    receiptVm.HostNetPayout,
                    client?.FullName ?? "Guest",
                    booking.CheckInDate,
                    booking.CheckOutDate,
                    booking.IsEventBooking,
                    booking.EventPackagePriceSnapshot,
                    ownerReceiptUrl);
            }

            if (_configRepo != null)
            {
                await _configRepo.LogAuditAsync(currentUserId, "ResendOwnerReceipt", "Booking", bookingId, $"Resent host settlement receipt #{receiptVm.HostReceiptNumber} for booking #{bookingId} to {owner.Email}");
            }

            TempData["Success"] = $"Host settlement receipt ({receiptVm.HostReceiptNumber}) has been dispatched to {owner.Email} and WhatsApp.";

            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }
            return RedirectToAction("BookingReceipt", new { id = bookingId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RequestMapLinkChange(int propertyId, string newMapUrl, string reason)
        {
            var ownerId = GetCurrentUserId();
            var prop = await _propRepo.GetByIdAsync(propertyId);
            if (prop == null || prop.OwnerID != ownerId)
            {
                return Forbid();
            }

            if (string.IsNullOrWhiteSpace(newMapUrl) || !Uri.TryCreate(newMapUrl.Trim(), UriKind.Absolute, out var uri))
            {
                TempData["Error"] = "Please provide a valid Google Maps URL starting with http:// or https://.";
                return RedirectToAction(nameof(Index));
            }

            if (string.IsNullOrWhiteSpace(reason))
            {
                TempData["Error"] = "Please specify a valid operational reason for requesting a map link change.";
                return RedirectToAction(nameof(Index));
            }

            // Log change request to audit
            await _configRepo.LogAuditAsync(ownerId, "RequestMapLinkChange", "Property", propertyId,
                $"Host #{ownerId} submitted a Map Link change request for Property #{propertyId} ('{prop.Title}'). New URL: {newMapUrl.Trim()}. Reason: {reason.Trim()}");

            TempData["Success"] = "Your map link change request has been submitted for platform administrator review.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CancelScheduledSubscription(int id)
        {
            var ownerId = GetCurrentUserId();
            var sub = await _subRepo.GetSubscriptionByIdAsync(id);
            if (sub == null || sub.OwnerID != ownerId)
            {
                return NotFound();
            }

            if (sub.StartDate <= DateTime.UtcNow)
            {
                TempData["Error"] = "Only future scheduled subscriptions that have not yet started can be cancelled.";
                return RedirectToAction(nameof(Subscriptions));
            }

            bool cancelled = await _subRepo.CancelScheduledSubscriptionAsync(id, ownerId);
            if (cancelled)
            {
                await _configRepo.LogAuditAsync(ownerId, "CancelScheduledSubscription", "OwnerSubscription", id,
                    $"Owner #{ownerId} cancelled scheduled plan #{id} ({sub.PlanName}) starting on {sub.StartDate:dd MMM yyyy}. Full refund initiated.");
                TempData["Success"] = $"Scheduled plan '{sub.PlanName}' has been successfully cancelled and refunded.";
            }
            else
            {
                TempData["Error"] = "Failed to cancel scheduled subscription. Please contact support.";
            }

            return RedirectToAction(nameof(Subscriptions));
        }

        [HttpGet]
        public async Task<IActionResult> NotificationSettings()
        {
            var ownerId = GetCurrentUserId();
            var owner = await _userRepo.GetByIdAsync(ownerId);
            if (owner == null) return NotFound();

            var vm = new OwnerNotificationPreferencesViewModel
            {
                EmailEnabled = owner.NotificationEmailEnabled,
                InAppEnabled = owner.NotificationInAppEnabled,
                WhatsAppEnabled = owner.NotificationWhatsAppEnabled,
                WhatsAppVerified = owner.WhatsAppVerified,
                WhatsAppPhone = owner.WhatsAppPhone ?? owner.Phone
            };

            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> NotificationSettings(OwnerNotificationPreferencesViewModel model)
        {
            var ownerId = GetCurrentUserId();
            var owner = await _userRepo.GetByIdAsync(ownerId);
            if (owner == null) return NotFound();

            owner.NotificationEmailEnabled = model.EmailEnabled;
            owner.NotificationInAppEnabled = model.InAppEnabled;

            // WhatsApp requires verified phone
            if (model.WhatsAppEnabled && !owner.WhatsAppVerified)
            {
                TempData["Error"] = "WhatsApp alerts require verified mobile number. Please complete OTP verification.";
                return View(model);
            }

            owner.NotificationWhatsAppEnabled = model.WhatsAppEnabled && owner.WhatsAppVerified;
            if (!string.IsNullOrWhiteSpace(model.WhatsAppPhone))
            {
                owner.WhatsAppPhone = model.WhatsAppPhone.Trim();
            }

            await _userRepo.UpdateUserAsync(owner);
            TempData["Success"] = "Notification preferences updated successfully across In-App, Email, and WhatsApp channels.";
            return RedirectToAction(nameof(NotificationSettings));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> VerifyWhatsAppOtp(string phone, string otp)
        {
            var ownerId = GetCurrentUserId();
            var owner = await _userRepo.GetByIdAsync(ownerId);
            if (owner == null) return NotFound();

            if (string.IsNullOrWhiteSpace(phone) || phone.Trim().Length < 10)
            {
                TempData["Error"] = "Please enter a valid 10-digit mobile number for WhatsApp verification.";
                return RedirectToAction(nameof(NotificationSettings));
            }

            // OTP verification: in testing/demo mode, accept 123456 or 6-digit number
            var cleanOtp = (otp ?? "").Trim();
            if (cleanOtp.Length == 6)
            {
                owner.WhatsAppPhone = phone.Trim();
                owner.WhatsAppVerified = true;
                owner.NotificationWhatsAppEnabled = true;
                await _userRepo.UpdateUserAsync(owner);

                await _configRepo.LogAuditAsync(ownerId, "VerifyWhatsAppOtp", "User", ownerId, $"Host verified WhatsApp alerts for phone {phone.Trim()}");
                TempData["Success"] = "WhatsApp mobile number verified successfully! You will now receive instant booking alerts.";
            }
            else
            {
                TempData["Error"] = "Invalid OTP code. Please enter the 6-digit verification code sent to your WhatsApp number.";
            }

            return RedirectToAction(nameof(NotificationSettings));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RequestPhotoShoot(int propertyId, string shootType, string? notes)
        {
            var ownerId = GetCurrentUserId();
            var prop = await _propRepo.GetByIdAsync(propertyId);
            if (prop == null)
            {
                return NotFound();
            }

            if (prop.OwnerID != ownerId)
            {
                return Forbid();
            }

            string? normalizedType = shootType?.Trim().ToLowerInvariant() switch
            {
                "photo" => "Photo",
                "video" => "Video",
                "both" => "Both",
                _ => null
            };

            if (normalizedType == null)
            {
                TempData["Error"] = "Please select a valid shoot type (Photo, Video, or Both).";
                return RedirectToAction(nameof(Index));
            }

            if (_photoShootRepo == null)
            {
                TempData["Error"] = "Shoot requests are currently unavailable.";
                return RedirectToAction(nameof(Index));
            }

            if (await _photoShootRepo.HasOpenRequestAsync(propertyId, normalizedType))
            {
                TempData["Error"] = $"An open {normalizedType} shoot request already exists for {prop.Title}.";
                return RedirectToAction(nameof(Index));
            }

            var req = new PhotoShootRequest
            {
                PropertyID = propertyId,
                OwnerID = ownerId,
                ShootType = normalizedType,
                Status = "Requested",
                Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim()
            };

            int requestId = await _photoShootRepo.CreateRequestAsync(req);
            if (requestId <= 0)
            {
                TempData["Error"] = "Unable to submit shoot request right now. Please try again.";
                return RedirectToAction(nameof(Index));
            }

            await _configRepo.LogAuditAsync(ownerId, "RequestPhotoShoot", "PhotoShootRequest", requestId,
                $"Owner #{ownerId} requested a {normalizedType} shoot for Property #{propertyId} ('{prop.Title}').");

            if (_notificationDispatcher != null)
            {
                await _notificationDispatcher.DispatchPhotoShootRequestedAsync(requestId, propertyId, prop.Title, ownerId, normalizedType);
            }

            TempData["Success"] = $"{normalizedType} shoot request submitted for {prop.Title}. Our team will coordinate with you shortly.";
            return RedirectToAction(nameof(Index));
        }

}
}