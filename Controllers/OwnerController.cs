using System.Security.Claims;
using BookMyFun.Models.Entities;
using BookMyFun.Models.ViewModels;
using BookMyFun.Repositories;
using BookMyFun.Services;
using BookMyFun.Services.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookMyFun.Controllers
{
    [Authorize(Roles = "Owner")]
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
            IWhatsAppService? whatsAppService = null)
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
        }

        private int GetCurrentUserId() => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        [AllowAnonymous]
        [HttpGet]
        public IActionResult Login(string? returnUrl) => RedirectToAction("Login", "Account", new { returnUrl });

        public async Task<IActionResult> Index()
        {
            var ownerId = GetCurrentUserId();
            var owner = await _userRepo.GetByIdAsync(ownerId);
            var properties = await _propRepo.GetPropertiesByOwnerIdAsync(ownerId);
            var bookings = await _bookingRepo.GetByOwnerIdAsync(ownerId);
            var sub = await _subRepo.GetActiveSubscriptionByOwnerIdAsync(ownerId);
            var plans = await _subRepo.GetAllPlansAsync();

            int maxAllowed = 1; // Default 1 complimentary listing for approved trial hosts
            if (sub != null)
            {
                var plan = await _subRepo.GetPlanByIdAsync(sub.PlanID);
                maxAllowed = plan?.MaxListings ?? 1;
            }

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
                ActiveListingsCount = properties.Count(p => p.ListingStatus == "Active"),
                MaxListingsAllowed = maxAllowed,
                TotalEarnings = totalEarnings,
                Properties = properties,
                Bookings = bookings,
                AvailablePlans = plans,
                AvailableCategories = await _propRepo.GetPropertyTypesAsync()
            };

            return View(vm);
        }

        [HttpGet]
        public async Task<IActionResult> CreateProperty()
        {
            var ownerId = GetCurrentUserId();
            bool canAdd = await _subRepo.CanOwnerAddPropertyAsync(ownerId);
            if (!canAdd)
            {
                TempData["Error"] = "Your active subscription limit is reached or expired. Please purchase or upgrade your plan first.";
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
                Location = vm.Location,
                FormattedAddress = vm.FormattedAddress,
                Latitude = vm.Latitude,
                Longitude = vm.Longitude,
                MapUrl = vm.MapUrl,
                PricePerNight = vm.PricePerNight,
                SecurityDepositAmount = vm.SecurityDepositAmount,
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
                FreeCancellationWindowHours = prop.FreeCancellationWindowHours,
                CancellationCutoffHours = prop.CancellationCutoffHours,
                CancellationFeeType = prop.CancellationFeeType,
                CancellationFeeValue = prop.CancellationFeeValue,
                MaxGuests = prop.MaxGuests,
                ListingStatus = prop.ListingStatus,
                IsApproved = prop.IsApproved,
                IsFeatured = prop.IsFeatured,
                SelectedOccasionIDs = prop.OccasionIDs,
                SelectedAmenityIDs = prop.AmenityIDs,
                ExistingMedia = prop.MediaList,
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
            prop.Location = vm.Location;
            prop.FormattedAddress = vm.FormattedAddress;
            prop.Latitude = vm.Latitude;
            prop.Longitude = vm.Longitude;
            prop.MapUrl = vm.MapUrl;
            prop.PricePerNight = vm.PricePerNight;
            prop.SecurityDepositAmount = vm.SecurityDepositAmount;
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
            return await DeactivateProperty(id, true);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> PublishProperty(int id)
        {
            return await DeactivateProperty(id, false);
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
            var bookings = await _bookingRepo.GetByOwnerIdAsync(ownerId);
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
        public async Task<IActionResult> Subscribe(int planId)
        {
            var ownerId = GetCurrentUserId();
            var plan = await _subRepo.GetPlanByIdAsync(planId);
            if (plan == null) return NotFound();

            var sub = new OwnerSubscription
            {
                OwnerID = ownerId,
                PlanID = planId,
                StartDate = DateTime.UtcNow,
                EndDate = DateTime.UtcNow.AddDays(plan.DurationInDays),
                PaymentStatus = "Pending",
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
            TempData["Success"] = $"Razorpay payment verified successfully! Your subscription to {sub.PlanName} is now active.";
            return RedirectToAction(nameof(Subscriptions));
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
    }
}
