using System.Security.Claims;
using BookMyFun.Models.Entities;
using BookMyFun.Models.ViewModels;
using BookMyFun.Repositories;
using BookMyFun.Services;
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

        public OwnerController(
            IPropertyRepository propRepo,
            IUserRepository userRepo,
            IBookingRepository bookingRepo,
            ISubscriptionRepository subRepo,
            IPaymentGatewayService paymentService,
            IDamageClaimService damageClaimService)
        {
            _propRepo = propRepo;
            _userRepo = userRepo;
            _bookingRepo = bookingRepo;
            _subRepo = subRepo;
            _paymentService = paymentService;
            _damageClaimService = damageClaimService;
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

            decimal totalEarnings = bookings.Where(b => b.Status == "Confirmed").Sum(b => b.OwnerPayoutAmount > 0 ? b.OwnerPayoutAmount : b.TotalAmount * 0.90m);

            var vm = new OwnerDashboardViewModel
            {
                Owner = owner!,
                ActiveSubscription = sub,
                ActiveListingsCount = properties.Count(p => p.ListingStatus == "Active"),
                MaxListingsAllowed = maxAllowed,
                TotalEarnings = totalEarnings,
                Properties = properties,
                Bookings = bookings,
                AvailablePlans = plans
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

            prop.PropertyTypeID = vm.PropertyTypeID;
            prop.Title = vm.Title;
            prop.Description = vm.Description;
            prop.Location = vm.Location;
            prop.FormattedAddress = vm.FormattedAddress;
            prop.Latitude = vm.Latitude;
            prop.Longitude = vm.Longitude;
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
                return View(model);
            }

            try
            {
                int claimId = await _damageClaimService.CreateClaimAsync(model, ownerId);
                TempData["Success"] = $"Damage claim #{claimId} submitted successfully. An operations executive will review your evidence within 24 hours.";
                return RedirectToAction(nameof(DamageClaims));
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", ex.Message);
                return View(model);
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

            var order = await _paymentService.CreateOrderAsync(plan.Price, $"sub_{ownerId}_{planId}");

            var sub = new OwnerSubscription
            {
                OwnerID = ownerId,
                PlanID = planId,
                StartDate = DateTime.UtcNow,
                EndDate = DateTime.UtcNow.AddDays(plan.DurationInDays),
                PaymentStatus = "Paid",
                GatewayTransactionID = order.OrderId
            };

            await _subRepo.SubscribeOwnerAsync(sub);
            TempData["Success"] = $"Subscribed to {plan.PlanName} successfully!";
            return RedirectToAction(nameof(Index));
        }
    }
}
