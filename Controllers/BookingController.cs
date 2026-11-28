using BookMyFun.Services.Checkout;
using BookMyFun.Models.Configuration;
using Microsoft.Extensions.Options;

using BookMyFun.Services.Security;

using System.Collections.Concurrent;

using System.Security.Claims;

using BookMyFun.Models.Entities;

using BookMyFun.Models.ViewModels;

using BookMyFun.Repositories;

using BookMyFun.Services;

using BookMyFun.Services.Common;

using BookMyFun.Services.Notifications;

using Microsoft.AspNetCore.Authorization;

using Microsoft.AspNetCore.Http;

using Microsoft.AspNetCore.Mvc;

using Microsoft.Extensions.Logging;

namespace BookMyFun.Controllers

{

    public class BookingController : Controller

    {

        private readonly IPropertyRepository _propRepo;

        private readonly IBookingRepository _bookingRepo;

        private readonly IEventPackageRepository _eventPackageRepo;

        private readonly IBookingPricingService _pricingService;

        private readonly ICommissionService _commissionService;

        private readonly IRefundEngineService _refundEngine;

        private readonly IRefundRepository _refundRepo;

        private readonly IPaymentGatewayService _paymentService;

        private readonly IEmailService _emailService;

        private readonly IWhatsAppService _whatsAppService;

        private readonly IUserRepository _userRepo;

        private readonly IIdVerificationService _idVerificationService;

        private readonly IMediaStorageService _mediaStorage;

        private readonly IConfigRepository? _configRepo;

        private readonly ILogger<BookingController>? _logger;

        private readonly ILegalDocumentService? _legalService;

        private readonly ICalendarRangeService _calendarRangeService;

        private readonly ICustomErrorHandlerService? _errorHandler;

        private readonly ICheckoutEventManagerService _checkoutEventMgrService;

        private readonly INotificationDispatcherService? _notificationDispatcher;

        private readonly IStayCelebrationEligibilityService _stayEligibilityService;
        private readonly BookingTimingOptions _timingOptions;
        private readonly BookingOptions _bookingOptions;
        private readonly IAvailabilityService _availabilityService;

        public BookingController(

            IPropertyRepository propRepo,

            IBookingRepository bookingRepo,

            IEventPackageRepository eventPackageRepo,

            IBookingPricingService pricingService,

            ICommissionService commissionService,

            IRefundEngineService refundEngine,

            IRefundRepository refundRepo,

            IPaymentGatewayService paymentService,

            IEmailService emailService,

            IWhatsAppService whatsAppService,

            IUserRepository userRepo,

            IIdVerificationService idVerificationService,

            IMediaStorageService mediaStorage,

            IConfigRepository? configRepo = null,

            ILogger<BookingController>? logger = null,

            ILegalDocumentService? legalService = null,

            ICalendarRangeService? calendarRangeService = null,

            ICustomErrorHandlerService? errorHandler = null,

            ICheckoutEventManagerService? checkoutEventMgrService = null,

            INotificationDispatcherService? notificationDispatcher = null,

            IStayCelebrationEligibilityService? stayEligibilityService = null,

            IOptions<BookingTimingOptions>? timingOptions = null,

            IOptions<BookingOptions>? bookingOptions = null,

            IAvailabilityService? availabilityService = null)

        {

            _propRepo = propRepo;

            _bookingRepo = bookingRepo;

            _eventPackageRepo = eventPackageRepo;

            _pricingService = pricingService;

            _commissionService = commissionService;

            _refundEngine = refundEngine;

            _refundRepo = refundRepo;

            _paymentService = paymentService;

            _emailService = emailService;

            _whatsAppService = whatsAppService;

            _userRepo = userRepo;

            _idVerificationService = idVerificationService;

            _mediaStorage = mediaStorage;

            _configRepo = configRepo;

            _logger = logger;

            _legalService = legalService;

            _calendarRangeService = calendarRangeService ?? new CalendarRangeService(bookingRepo);

            _errorHandler = errorHandler;

            _checkoutEventMgrService = checkoutEventMgrService ?? new CheckoutEventManagerService(eventPackageRepo, userRepo, propRepo);

            _notificationDispatcher = notificationDispatcher;

            _stayEligibilityService = stayEligibilityService ?? new StayCelebrationEligibilityService(
                propRepo,
                eventPackageRepo,
                userRepo,
                new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(),
                new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()),
                null);
            _timingOptions = timingOptions?.Value ?? new BookingTimingOptions();
            _bookingOptions = bookingOptions?.Value ?? new BookingOptions();
            _availabilityService = availabilityService ?? new AvailabilityService(bookingRepo, timingOptions, bookingOptions);
        }

        [Authorize]

        [HttpGet]

        public async Task<IActionResult> Checkout(
            int propertyId,
            string bookingType = "FullDay",
            string? slotType = null,
            int daysOrHours = 1,
            DateTime? checkIn = null,
            DateTime? checkOut = null,
            int? occasionId = null,
            int? packageId = null)

        {

            if (RoleAccessGuard.IsBookingOrPropertyViewBlocked(User))
            {
                return RoleAccessGuard.RedirectToRoleDashboard(this, "This page is for guests. Use your dashboard.");
            }

            if (propertyId <= 0)

            {

                TempData["Error"] = "Invalid property selected. Please choose a valid property.";

                return RedirectToAction("Index", "Properties");

            }

            var property = await _propRepo.GetByIdAsync(propertyId);

            if (property == null || !await _propRepo.IsPropertyPubliclyVisibleAsync(property))
            {
                TempData["Error"] = "This property is currently not available for bookings.";
                return RedirectToAction("Index", "Properties");
            }

            var effectiveIn = checkIn?.Date ?? DateTime.Today;

            var effectiveOut = checkOut.HasValue && checkOut.Value.Date > effectiveIn

                ? checkOut.Value.Date

                : (bookingType == "FullDay" ? effectiveIn.AddDays(Math.Max(1, daysOrHours)) : effectiveIn);

            if (bookingType == "FullDay" && checkIn.HasValue && checkOut.HasValue)

            {

                daysOrHours = Math.Max(1, (effectiveOut - effectiveIn).Days);

            }

            decimal calculatedAmount = _pricingService.CalculatePrice(property.PricePerNight, bookingType, slotType, daysOrHours);

            var clientIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            bool isVerified = false;
            string? existingDocKey = null;
            string? existingDocNumber = null;
            User? currentUser = null;
            if (int.TryParse(clientIdStr, out int clientId))
            {
                currentUser = await _userRepo.GetByIdAsync(clientId);
                isVerified = await _idVerificationService.IsClientVerifiedAsync(clientId);
                try
                {
                    var existingVerification = await _idVerificationService.GetClientVerificationAsync(clientId, clientId, "Client");
                    if (existingVerification != null)
                    {
                        existingDocKey = existingVerification.DocumentStorageKey;
                        existingDocNumber = existingVerification.MaskedIdentifier;
                    }
                }
                catch
                {
                    // Suppress optional lookup error
                }
            }

            Booking? checkoutHold = null;
            if (int.TryParse(clientIdStr, out int parsedClientId))
            {
                checkoutHold = await _bookingRepo.FindActiveHoldAsync(parsedClientId, property.PropertyID, effectiveIn, effectiveOut, bookingType, slotType);
            }

            TimeSpan propCheckInTime = GetPropertyCheckInTime(property);
            bool isDateAvailable = await _availabilityService.IsAvailableAsync(
                property.PropertyID, effectiveIn, effectiveOut, checkoutHold?.BookingID, bookingType, slotType)
                && await _bookingRepo.IsSlotAvailableAsync(property.PropertyID, effectiveIn, effectiveOut, bookingType, slotType, checkoutHold?.BookingID);

            var eligibility = _stayEligibilityService != null
                ? await _stayEligibilityService.CheckEligibilityAsync(property.PropertyID, effectiveIn, propCheckInTime)
                : new StayCelebrationEligibilityResult { Available = false, ReasonCode = "disabled" };

            bool isStayAndCelebrateAvailable = eligibility.Available && isDateAvailable;
            ViewBag.HasActiveVerifiedDecorator = eligibility.Available;

            // Categories offered: distinct categories from packages that meet rule 3 (Do not use PropertyOccasions)
            int catIdx = 1;
            var propertyOccasions = eligibility.Categories.Select(c => new Occasion
            {
                OccasionID = catIdx++,
                OccasionName = c.Category
            }).ToList();
            bool hasLinked = propertyOccasions.Any();

            int? selectedOccasionId = occasionId.HasValue && occasionId.Value > 0 ? occasionId : null;

            var availableEventManagers = (isStayAndCelebrateAvailable && selectedOccasionId.HasValue)

                ? await _checkoutEventMgrService.GetAvailableEventManagersAsync(property.Location, selectedOccasionId)

                : new List<CheckoutEventManagerViewModel>();

            var vm = new BookingCheckoutViewModel

            {

                PropertyID = property.PropertyID,

                PropertyTitle = property.Title,

                Location = property.Location,

                PricePerNight = property.PricePerNight,

                ImageUrl = property.PrimaryImageUrl,

                BookingType = bookingType,

                SlotType = slotType,

                HourlyDuration = bookingType == "Hourly" ? daysOrHours : null,

                CheckInDate = effectiveIn,

                CheckOutDate = effectiveOut,

                CalculatedAmount = calculatedAmount,

                AvailableOccasions = propertyOccasions,

                HasPropertyOccasions = hasLinked,

                OccasionID = selectedOccasionId,

                AvailableEventManagers = availableEventManagers,

                AvailableEventPackages = availableEventManagers.SelectMany(m => m.PackagesForOccasion).ToList(),

                IsEventServiceAvailable = isStayAndCelebrateAvailable,
                IsClientAlreadyVerified = isVerified,
                GuestFullName = currentUser?.FullName ?? User.Identity?.Name ?? "",
                GuestPhone = BookMyFun.Models.Validation.IndianMobileHelper.NormalizeTo10Digits(currentUser?.Phone),
                DocumentType = "Aadhaar",
                ExistingDocumentKey = existingDocKey,
                ExistingDocumentNumber = existingDocNumber,

                MinStayNights = property.MinStayNights,

                MaxStayNights = property.MaxStayNights,

                BlockedDates = await _calendarRangeService.GetBlockedDatesForPropertyAsync(property.PropertyID)

            };

            // Pre-select package if redirected from Event Manager Public Profile
            if (isStayAndCelebrateAvailable && packageId.HasValue && packageId.Value > 0)
            {
                var valResult = await _checkoutEventMgrService.ValidateSelectedPackageAsync(packageId.Value, selectedOccasionId, property.Location);
                if (valResult.IsValid && valResult.Package != null)
                {
                    vm.EventPackageID = packageId.Value;
                    vm.IsEventBooking = true;
                    vm.DecoratorAssignedID = valResult.Package.CreatedByUserID;

                    if (!selectedOccasionId.HasValue && propertyOccasions.Any())
                    {
                        var matchedOcc = propertyOccasions.FirstOrDefault(o => _checkoutEventMgrService.MatchesOccasion(valResult.Package.Category, o.OccasionName));
                        if (matchedOcc != null)
                        {
                            vm.OccasionID = matchedOcc.OccasionID;
                            vm.AvailableEventManagers = await _checkoutEventMgrService.GetAvailableEventManagersAsync(property.Location, matchedOcc.OccasionID);
                            vm.AvailableEventPackages = vm.AvailableEventManagers.SelectMany(mgr => mgr.PackagesForOccasion).ToList();
                        }
                    }
                }
            }

            return View(vm);

        }

        public static DateTime GetKolkataNow()
        {
            try
            {
                var ist = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");
                return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, ist);
            }
            catch
            {
                try
                {
                    var ist = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");
                    return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, ist);
                }
                catch
                {
                    return DateTime.UtcNow.AddHours(5).AddMinutes(30);
                }
            }
        }

        public static bool IsLeadTimeMoreThan48Hours(DateTime checkInDateTime, DateTime? currentIstTime = null)
        {
            var nowIst = currentIstTime ?? GetKolkataNow();
            return checkInDateTime > nowIst.AddHours(48);
        }

        public static TimeSpan GetPropertyCheckInTime(Property property, string? checkInTime = null)
        {
            if (!string.IsNullOrWhiteSpace(checkInTime) && TimeSpan.TryParse(checkInTime, out var parsed))
            {
                return parsed;
            }

            var prop = property.GetType().GetProperty("CheckInTime");
            if (prop != null)
            {
                var val = prop.GetValue(property);
                if (val is TimeSpan ts) return ts;
                if (val is string s && TimeSpan.TryParse(s, out var pts)) return pts;
            }

            return new TimeSpan(12, 0, 0); // Default 12:00 if none is set
        }

        public static (string City, string State) ExtractCityAndState(Property property)
        {
            string city = property.City?.Trim() ?? "";
            string state = property.State?.Trim() ?? "";

            if ((string.IsNullOrWhiteSpace(city) || string.IsNullOrWhiteSpace(state)) && !string.IsNullOrWhiteSpace(property.Location))
            {
                var parts = property.Location.Split(new[] { ',', '-' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (string.IsNullOrWhiteSpace(city) && parts.Length > 0)
                {
                    city = parts[0];
                }
                if (string.IsNullOrWhiteSpace(state))
                {
                    state = parts.Length > 1 ? parts[parts.Length - 1] : parts[0];
                }
            }

            return (city, state);
        }

        public async Task<bool> HasActiveVerifiedDecoratorForAreaAsync(Property property)
        {
            try
            {
                if (_stayEligibilityService != null)
                {
                    var result = await _stayEligibilityService.CheckEligibilityAsync(property.PropertyID, DateTime.Today.AddDays(3));
                    return result.Available;
                }
            }
            catch
            {
                // Fallback
            }
            return false;
        }

        private async Task PopulateCheckoutViewModelAsync(BookingCheckoutViewModel model, Property property)
        {
            TimeSpan propCheckInTime = GetPropertyCheckInTime(property, model.CheckInTime != TimeSpan.Zero ? model.CheckInTime.ToString(@"hh\:mm") : null);
            DateTime inDate = model.CheckInDate != default ? model.CheckInDate : DateTime.Today;

            var eligibility = _stayEligibilityService != null
                ? await _stayEligibilityService.CheckEligibilityAsync(property.PropertyID, inDate, propCheckInTime)
                : new StayCelebrationEligibilityResult { Available = false, ReasonCode = "disabled" };

            model.IsEventServiceAvailable = eligibility.Available;
            ViewBag.HasActiveVerifiedDecorator = eligibility.Available;

            model.PropertyTitle = property.Title;
            model.Location = property.Location;
            model.PricePerNight = property.PricePerNight;
            model.ImageUrl = property.PrimaryImageUrl;

            // Categories offered: distinct categories from packages that meet rule 3 (Do not use PropertyOccasions)
            int catIdx = 1;
            model.AvailableOccasions = eligibility.Categories.Select(c => new Occasion
            {
                OccasionID = catIdx++,
                OccasionName = c.Category
            }).ToList();
            model.HasPropertyOccasions = model.AvailableOccasions.Any();

            int? selectedOccasionId = model.OccasionID.HasValue && model.OccasionID.Value > 0 ? model.OccasionID : null;

            if (eligibility.Available && selectedOccasionId.HasValue)
            {
                model.AvailableEventManagers = await _checkoutEventMgrService.GetAvailableEventManagersAsync(property.Location, selectedOccasionId);
                model.AvailableEventPackages = model.AvailableEventManagers.SelectMany(m => m.PackagesForOccasion).ToList();
            }
            else
            {
                model.AvailableEventManagers = new();
                model.AvailableEventPackages = new();
            }

            model.MinStayNights = property.MinStayNights;
            model.MaxStayNights = property.MaxStayNights;
            model.BlockedDates = await _calendarRangeService.GetBlockedDatesForPropertyAsync(property.PropertyID);

            var clientIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (int.TryParse(clientIdStr, out int clientId))
            {
                model.IsClientAlreadyVerified = await _idVerificationService.IsClientVerifiedAsync(clientId);
                if (string.IsNullOrEmpty(model.ExistingDocumentKey))
                {
                    try
                    {
                        var existingVerification = await _idVerificationService.GetClientVerificationAsync(clientId, clientId, "Client");
                        if (existingVerification != null)
                        {
                            model.ExistingDocumentKey = existingVerification.DocumentStorageKey;
                            model.ExistingDocumentNumber ??= existingVerification.MaskedIdentifier;
                        }
                    }
                    catch
                    {
                        // Suppress optional lookup error
                    }
                }
            }
        }

        [HttpGet]
        public async Task<IActionResult> CelebrationEligibility(
            int propertyId,
            string? checkIn = null,
            string? checkOut = null,
            string? checkInTime = null,
            string? category = null,
            string? sort = "rating")
        {
            if (RoleAccessGuard.IsBookingOrPropertyViewBlocked(User))
            {
                return StatusCode(StatusCodes.Status403Forbidden, new { error = "Access restricted to client accounts." });
            }
            if (propertyId <= 0)
            {
                return Json(new
                {
                    success = false,
                    allowed = false,
                    available = false,
                    reasonCode = "invalid_property",
                    reason = "Invalid property selected.",
                    reasonMessage = "Invalid property selected.",
                    availableCategories = new List<string>(),
                    categories = new List<CategoryCountDto>(),
                    packages = new List<ManagerPackageCardDto>(),
                    alternativePackages = new List<ManagerPackageCardDto>(),
                    sort = sort ?? "rating"
                });
            }

            var property = await _propRepo.GetByIdAsync(propertyId);
            if (property == null)
            {
                return Json(new
                {
                    success = false,
                    allowed = false,
                    available = false,
                    reasonCode = "property_not_found",
                    reason = "Property not found.",
                    reasonMessage = "Property not found.",
                    availableCategories = new List<string>(),
                    categories = new List<CategoryCountDto>(),
                    packages = new List<ManagerPackageCardDto>(),
                    alternativePackages = new List<ManagerPackageCardDto>(),
                    sort = sort ?? "rating"
                });
            }

            DateTime inDate = DateTime.Today;
            if (!string.IsNullOrWhiteSpace(checkIn) && DateTime.TryParse(checkIn, out var parsedIn))
            {
                inDate = parsedIn.Date;
            }

            DateTime outDate = inDate.AddDays(1);
            if (!string.IsNullOrWhiteSpace(checkOut) && DateTime.TryParse(checkOut, out var parsedOut) && parsedOut.Date > inDate)
            {
                outDate = parsedOut.Date;
            }

            TimeSpan effectiveCheckInTime = GetPropertyCheckInTime(property, checkInTime);

            var eligibility = await _stayEligibilityService.CheckEligibilityAsync(
                propertyId, inDate, effectiveCheckInTime, category, sort);

            bool isDateAvailable = await _bookingRepo.IsSlotAvailableAsync(property.PropertyID, inDate, outDate, "FullDay", null);

            bool overallAvailable = eligibility.Available && isDateAvailable;
            string reasonCode = !isDateAvailable ? "dates_unavailable" : eligibility.ReasonCode;
            string? reasonMessage = !isDateAvailable ? "These dates are already booked." : eligibility.ReasonMessage;

            var availableCategoryNames = eligibility.Categories.Select(c => c.Category).ToList();

            return Json(new
            {
                success = true,
                allowed = overallAvailable,
                available = overallAvailable,
                reasonCode = reasonCode,
                reason = reasonMessage,
                reasonMessage = reasonMessage,
                categories = eligibility.Categories,
                availableCategories = availableCategoryNames,
                packages = eligibility.Packages,
                alternativePackages = eligibility.AlternativePackages,
                sort = sort ?? "rating"
            });
        }

        [HttpGet]
        public async Task<IActionResult> GetEventManagersForOccasion(
            int propertyId,
            int occasionId = 0,
            string? category = null,
            string? checkIn = null,
            string? sort = "rating")
        {
            if (propertyId <= 0)
            {
                return Json(new { success = false, message = "Invalid property selected." });
            }

            var property = await _propRepo.GetByIdAsync(propertyId);
            if (property == null)
            {
                return Json(new { success = false, message = "Property not found." });
            }

            DateTime inDate = DateTime.Today.AddDays(3);
            if (!string.IsNullOrWhiteSpace(checkIn) && DateTime.TryParse(checkIn, out var parsedIn))
            {
                inDate = parsedIn.Date;
            }

            TimeSpan effectiveCheckInTime = GetPropertyCheckInTime(property);

            string? targetCategory = category;
            if (string.IsNullOrEmpty(targetCategory) && occasionId > 0)
            {
                var allOccasions = await _propRepo.GetOccasionsAsync();
                var occ = allOccasions.FirstOrDefault(o => o.OccasionID == occasionId);
                targetCategory = occ?.OccasionName;
            }

            var eligibility = await _stayEligibilityService.CheckEligibilityAsync(
                propertyId, inDate, effectiveCheckInTime, targetCategory, sort);

            var managersGrouped = eligibility.Packages
                .GroupBy(p => p.ManagerId)
                .Select(g =>
                {
                    var first = g.First();
                    return new
                    {
                        userId = first.ManagerId,
                        displayName = first.DisplayName,
                        profileImageUrl = "/images/default-avatar.svg",
                        isVerified = first.IsVerified,
                        averageRating = first.Rating,
                        rating = first.Rating,
                        eventsCompleted = first.EventsCompleted,
                        startingPrice = g.Min(p => p.Price),
                        profileLink = first.ProfileLink,
                        packages = g.Select(p => new
                        {
                            packageId = p.PackageId,
                            id = p.PackageId,
                            name = p.PackageName,
                            packageName = p.PackageName,
                            category = p.Category,
                            basePrice = p.Price,
                            price = p.Price
                        }).ToList()
                    };
                }).ToList();

            var alternativeList = eligibility.AlternativePackages.Select(p => new
            {
                packageId = p.PackageId,
                id = p.PackageId,
                name = p.PackageName,
                packageName = p.PackageName,
                category = p.Category,
                basePrice = p.Price,
                price = p.Price,
                managerId = p.ManagerId,
                displayName = p.DisplayName,
                isVerified = p.IsVerified,
                rating = p.Rating,
                eventsCompleted = p.EventsCompleted,
                profileLink = p.ProfileLink
            }).ToList();

            var availableCategoryNames = eligibility.Categories.Select(c => c.Category).ToList();

            return Json(new
            {
                success = true,
                available = eligibility.Available,
                reasonCode = eligibility.ReasonCode,
                reasonMessage = eligibility.ReasonMessage,
                categories = eligibility.Categories,
                availableCategories = availableCategoryNames,
                selectedCategory = targetCategory,
                count = managersGrouped.Count,
                managers = managersGrouped,
                packages = eligibility.Packages,
                alternativePackages = alternativeList,
                youMightAlsoLike = alternativeList,
                sort = sort ?? "rating"
            });
        }

        [Authorize]

        [HttpPost]

        [ValidateAntiForgeryToken]

        public async Task<IActionResult> ConfirmBooking(BookingCheckoutViewModel model)

        {

            if (RoleAccessGuard.IsBookingOrPropertyViewBlocked(User))
            {
                return RoleAccessGuard.RedirectToRoleDashboard(this, "This page is for guests. Use your dashboard.");
            }

            try

            {

                if (model.PropertyID <= 0)

                {

                    TempData["Error"] = "Invalid property selected. Please choose a valid property.";

                    return RedirectToAction("Index", "Properties");

                }

                var property = await _propRepo.GetByIdAsync(model.PropertyID);

                if (property == null || !await _propRepo.IsPropertyPubliclyVisibleAsync(property))
                {
                    TempData["Error"] = "This property is currently not available for bookings.";
                    return RedirectToAction("Index", "Properties");
                }

                var clientIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);

                if (string.IsNullOrEmpty(clientIdStr) || !int.TryParse(clientIdStr, out int clientId))

                {

                    return Challenge();

                }

                // Mandatory policy consent check (Item 3e)
                if (Request.HasFormContentType)
                {
                    string? consent = Request.Form["PolicyConsent"];
                    if (consent != "true")
                    {
                        ModelState.AddModelError("PolicyConsent", "You must agree to the Terms of Service, Privacy Policy, and Refund Policy to proceed with checkout.");
                    }
                }

                // Guest identity & contact validation
                bool isDocumentSubmitted = !string.IsNullOrWhiteSpace(model.DocumentType) ||
                                           (model.IdDocumentFile != null && model.IdDocumentFile.Length > 0);

                if (Request.HasFormContentType || isDocumentSubmitted || !string.IsNullOrWhiteSpace(model.GuestFullName) || !string.IsNullOrWhiteSpace(model.GuestPhone))
                {
                    if (string.IsNullOrWhiteSpace(model.GuestFullName) || model.GuestFullName.Trim().Length < 3)
                    {
                        ModelState.AddModelError("GuestFullName", "Please enter guest full name (at least 3 characters).");
                    }

                    model.GuestPhone = BookMyFun.Models.Validation.IndianMobileHelper.NormalizeTo10Digits(model.GuestPhone);
                    if (!BookMyFun.Models.Validation.IndianMobileHelper.IsValidIndianMobile(model.GuestPhone, out _))
                    {
                        ModelState.Remove("GuestPhone");
                        ModelState.AddModelError("GuestPhone", BookMyFun.Models.Validation.IndianMobileAttribute.DefaultErrorMessage);
                    }

                    bool fileRequired = false;
                    if (!model.IsClientAlreadyVerified)
                    {
                        // ID Type validation (B11): Aadhaar, Driving Licence, Voter ID, PAN, Passport
                        var docType = (model.DocumentType ?? "Aadhaar").Trim();
                        bool isAadhaar = docType.Equals("Aadhaar", StringComparison.OrdinalIgnoreCase) || docType.Equals("Aadhaar (masked copy)", StringComparison.OrdinalIgnoreCase);
                        bool isPAN = docType.Equals("PAN", StringComparison.OrdinalIgnoreCase);
                        bool isPassport = docType.Equals("Passport", StringComparison.OrdinalIgnoreCase);
                        bool isDL = docType.Equals("Driving Licence", StringComparison.OrdinalIgnoreCase) || docType.Equals("DrivingLicense", StringComparison.OrdinalIgnoreCase);
                        bool isVoter = docType.Equals("Voter ID", StringComparison.OrdinalIgnoreCase) || docType.Equals("VoterID", StringComparison.OrdinalIgnoreCase);

                        if (!isAadhaar && !isPAN && !isPassport && !isDL && !isVoter)
                        {
                            ModelState.AddModelError("DocumentType", "Document type must be one of: Aadhaar, Driving Licence, Voter ID, PAN, Passport.");
                        }

                        // Document File validation
                        bool hasFile = model.IdDocumentFile != null && model.IdDocumentFile.Length > 0;
                        bool hasExistingDoc = !string.IsNullOrWhiteSpace(model.ExistingDocumentKey);
                        fileRequired = !hasExistingDoc;

                        if (fileRequired && !hasFile)
                        {
                            ModelState.AddModelError("IdDocumentFile", "Please upload a copy of your document.");
                        }
                    }

                    if (model.IdDocumentFile != null)
                    {
                        if (model.IdDocumentFile.Length == 0)
                        {
                            if (fileRequired)
                            {
                                ModelState.AddModelError("IdDocumentFile", "Please upload a copy of your document.");
                            }
                        }
                        else
                        {
                            if (model.IdDocumentFile.Length > BookMyFun.Models.Validation.RequiredDocumentFileAttribute.MaxFileSizeBytes)
                            {
                                ModelState.AddModelError("IdDocumentFile", "Document exceeds maximum permitted size of 5MB.");
                            }

                            string ext = Path.GetExtension(model.IdDocumentFile.FileName)?.ToLowerInvariant() ?? "";
                            if (string.IsNullOrEmpty(ext) || !BookMyFun.Models.Validation.RequiredDocumentFileAttribute.AllowedExtensions.Contains(ext))
                            {
                                ModelState.AddModelError("IdDocumentFile", "Only JPG, JPEG, PNG, and PDF files are accepted.");
                            }

                            string contentType = model.IdDocumentFile.ContentType?.ToLowerInvariant() ?? "";
                            if (string.IsNullOrEmpty(contentType) || !BookMyFun.Models.Validation.RequiredDocumentFileAttribute.AllowedContentTypes.Contains(contentType))
                            {
                                ModelState.AddModelError("IdDocumentFile", "Invalid file content type. Please upload a valid image or PDF document.");
                            }
                        }
                    }
                }

                // Date and input validations

                if (model.CheckInDate.Date < DateTime.Today)

                {

                    ModelState.AddModelError("CheckInDate", "Check-in date cannot be in the past. Please select today or a future date.");

                }

                if (model.CheckOutDate.Date < model.CheckInDate.Date)

                {

                    ModelState.AddModelError("CheckOutDate", "Check-out date cannot be earlier than check-in date. Please choose valid dates.");

                }

                if (model.BookingType == "Hourly")
                {
                    if (!model.HourlyDuration.HasValue || model.HourlyDuration.Value <= 0)
                    {
                        ModelState.AddModelError("HourlyDuration", "Please select a valid hourly booking duration.");
                    }
                    if (!_timingOptions.IsHourlyStartAllowed(model.CheckInTime))
                    {
                        ModelState.AddModelError("CheckInTime", $"Hourly booking check-in time cannot be earlier than {_timingOptions.CheckInTimeSpan:hh\\:mm}.");
                    }
                }

                EventPackage? selectedPackage = null;
                decimal eventPackagePrice = 0m;

                if (model.IsEventBooking || (model.EventPackageID.HasValue && model.EventPackageID.Value > 0))
                {
                    TimeSpan propCheckInTime = GetPropertyCheckInTime(property, model.CheckInTime != TimeSpan.Zero ? model.CheckInTime.ToString(@"hh\:mm") : null);

                    if (model.EventPackageID.HasValue && model.EventPackageID.Value > 0)
                    {
                        var valResult = await _stayEligibilityService.ValidateBookingPackageAsync(
                            property.PropertyID, model.EventPackageID.Value, model.CheckInDate, propCheckInTime);

                        if (!valResult.IsEligible)
                        {
                            if (valResult.ReasonCode == "too_soon")
                            {
                                ModelState.AddModelError("CheckInDate", "Event setup & floral décor services need at least 2 days' advance notice for crew dispatch and staging preparation.");
                                TempData["Error"] = "Décor needs more than 48 hours' notice, so we've switched you to a standard stay.";
                                model.IsEventBooking = false;
                                model.EventPackageID = null;
                                model.DecoratorAssignedID = null;
                                model.CustomEventNotes = null;
                                eventPackagePrice = 0m;
                            }
                            else
                            {
                                string msg = valResult.ErrorMessage ?? "The selected décor package is not eligible.";
                                ModelState.AddModelError("EventPackageID", msg);
                                TempData["Error"] = msg;
                            }
                        }
                        else
                        {
                            selectedPackage = valResult.Package;
                            eventPackagePrice = selectedPackage!.BasePrice; // Price from server, never from form!
                            model.IsEventBooking = true;
                            model.DecoratorAssignedID = selectedPackage.CreatedByUserID;
                        }
                    }
                    else
                    {
                        var generalEligibility = await _stayEligibilityService.CheckEligibilityAsync(
                            property.PropertyID, model.CheckInDate, propCheckInTime);

                        if (!generalEligibility.Available)
                        {
                            string msg = generalEligibility.ReasonMessage ?? "Stay & Celebrate is not available for this stay.";
                            ModelState.AddModelError("IsEventBooking", msg);
                            TempData["Error"] = msg;
                            model.IsEventBooking = false;
                            model.EventPackageID = null;
                            model.DecoratorAssignedID = null;
                            model.CustomEventNotes = null;
                            eventPackagePrice = 0m;
                        }
                        else
                        {
                            ModelState.AddModelError("EventPackageID", "Please select a décor package for your Stay & Celebrate booking.");
                            TempData["Error"] = "Please select a décor package for your Stay & Celebrate booking.";
                        }
                    }
                }
                else
                {
                    model.IsEventBooking = false;
                    model.EventPackageID = null;
                    model.DecoratorAssignedID = null;
                    model.CustomEventNotes = null;
                    eventPackagePrice = 0m;
                }

                Booking? existingHold = await _bookingRepo.FindActiveHoldAsync(
                    clientId, model.PropertyID, model.CheckInDate, model.CheckOutDate, model.BookingType, model.SlotType);

                // Server-side availability and stay limits check before proceeding (Item 8)

                if (ModelState.IsValid)

                {

                    var blocked = await _calendarRangeService.GetBlockedDatesForPropertyAsync(property.PropertyID);

                    var rangeVal = _calendarRangeService.ValidateAndCalculate(

                        model.CheckInDate,

                        model.CheckOutDate,

                        property.MinStayNights,

                        property.MaxStayNights,

                        property.PricePerNight,

                        model.BookingType,

                        model.SlotType,

                        model.HourlyDuration,

                        0,

                        existingHold != null ? null : blocked);

                    if (!rangeVal.IsValid && !string.IsNullOrEmpty(rangeVal.ErrorMessage))

                    {

                        string msg = rangeVal.ErrorMessage.Contains("blocked", StringComparison.OrdinalIgnoreCase) || rangeVal.ErrorMessage.Contains("available", StringComparison.OrdinalIgnoreCase)
                            ? "These dates are already booked."
                            : rangeVal.ErrorMessage;
                        ModelState.AddModelError("", msg);

                        TempData["Error"] = rangeVal.ErrorMessage.Contains("blocked", StringComparison.OrdinalIgnoreCase) || rangeVal.ErrorMessage.Contains("available", StringComparison.OrdinalIgnoreCase)
                            ? "These dates are no longer available. Please choose different dates."
                            : msg;

                    }

                    else

                    {

                        bool isAvailable = await _availabilityService.IsAvailableAsync(
                            model.PropertyID,
                            model.CheckInDate,
                            model.CheckOutDate,
                            existingHold?.BookingID,
                            model.BookingType,
                            model.SlotType,
                            model.CheckInTime,
                            model.HourlyDuration)
                            && await _bookingRepo.IsSlotAvailableAsync(
                            model.PropertyID,
                            model.CheckInDate,
                            model.CheckOutDate,
                            model.BookingType,
                            model.SlotType,
                            existingHold?.BookingID,
                            model.CheckInTime,
                            model.HourlyDuration);

                        if (!isAvailable)

                        {

                            const string unavailableMsg = "These dates are already booked.";

                            ModelState.AddModelError("", unavailableMsg);

                            TempData["Error"] = "These dates are no longer available. Please choose different dates.";

                        }

                    }

                }

                if (!ModelState.IsValid)

                {

                    if (Request.Headers["X-Requested-With"] == "XMLHttpRequest" ||
                        (Request.Headers.ContainsKey("Accept") && Request.Headers["Accept"].ToString().Contains("application/json")))
                    {
                        var errors = ModelState.Where(ms => ms.Value != null && ms.Value.Errors.Any())
                            .ToDictionary(
                                kvp => kvp.Key,
                                kvp => kvp.Value!.Errors.Select(e => e.ErrorMessage).ToArray()
                            );
                        var firstError = ModelState.Values.SelectMany(v => v.Errors).FirstOrDefault()?.ErrorMessage ?? "Validation failed.";
                        return Json(new { success = false, message = firstError, errors });
                    }

                    await PopulateCheckoutViewModelAsync(model, property);

                    return View("Checkout", model);

                }

                int count = model.BookingType == "Hourly" ? (model.HourlyDuration ?? 1) : Math.Max(1, (model.CheckOutDate - model.CheckInDate).Days);

                decimal propertyStayAmount = _pricingService.CalculatePrice(property.PricePerNight, model.BookingType, model.SlotType, count);

                // Package and price already verified and loaded from server above

                if (selectedPackage != null)

                {

                    eventPackagePrice = selectedPackage.BasePrice;

                }

                decimal totalAmount = propertyStayAmount + eventPackagePrice;

                // If guest uploaded government ID at booking checkout, securely save into private vault
                if (model.IdDocumentFile != null && model.IdDocumentFile.Length > 0 && _mediaStorage != null)
                {
                    try
                    {
                        string docKey = await _mediaStorage.SavePrivateDocumentAsync(model.IdDocumentFile, clientId);
                        if (_idVerificationService != null)
                        {
                            await _idVerificationService.SubmitVerificationAsync(clientId, model.DocumentType ?? "Aadhaar", docKey, model.IdDocumentNumber);
                        }
                        model.ExistingDocumentKey = docKey;
                    }
                    catch (BookMyFun.Services.Exceptions.ValidationException vex)
                    {
                        ModelState.AddModelError("IdDocumentFile", vex.Message);
                        await PopulateCheckoutViewModelAsync(model, property);
                        return View("Checkout", model);
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "Failed to upload or verify identity document for client {ClientId}", clientId);
                        ModelState.AddModelError("IdDocumentFile", "We could not process your identity document. Please ensure it is a valid file (under 5MB) and try again.");
                        await PopulateCheckoutViewModelAsync(model, property);
                        return View("Checkout", model);
                    }
                }

                // Dynamic Commission calculation: Calculated on Property Stay Amount

                var (rateSnapshot, platformCommission, ownerPayout) = await _commissionService.CalculateDynamicCommissionAsync(

                    propertyStayAmount, property.CommissionRate, property.OwnerID);

                // Dynamic Event Commission calculation: Calculated on Event Package Price

                decimal eventRateSnapshot = 0.00m;

                decimal eventPlatformCommission = 0.00m;

                decimal eventManagerPayout = 0.00m;

                if (selectedPackage != null && eventPackagePrice > 0)

                {

                    var eventComm = await _commissionService.CalculateEventCommissionAsync(

                        eventPackagePrice, property.PropertyID, selectedPackage.PackageID, property.EventCommissionRate);

                    eventRateSnapshot = eventComm.rateSnapshot;

                    eventPlatformCommission = eventComm.commissionAmount;

                    eventManagerPayout = eventComm.managerPayout;

                }

                int bookingId;
                if (existingHold != null)
                {
                    bookingId = existingHold.BookingID;
                }
                else
                {
                    var booking = new Booking

                    {

                        PropertyID = model.PropertyID,

                        ClientID = clientId,

                        OccasionID = model.OccasionID.HasValue && model.OccasionID.Value > 0 ? model.OccasionID.Value : null,

                        CheckInDate = model.CheckInDate,

                        CheckOutDate = model.CheckOutDate,

                        TotalAmount = totalAmount,

                        Status = "Hold",

                        HoldExpiresAt = DateTime.UtcNow.AddMinutes(Math.Max(1, _bookingOptions.HoldMinutes)),

                        BookingType = model.BookingType,

                        SlotType = model.SlotType,

                        SlotStartTime = model.CheckInTime,

                        SlotEndTime = model.CheckOutTime,

                        FreeCancellationWindowHoursSnapshot = property.FreeCancellationWindowHours,

                        CancellationCutoffHoursSnapshot = property.CancellationCutoffHours,

                        CancellationFeeTypeSnapshot = property.CancellationFeeType,

                        CancellationFeeValueSnapshot = property.CancellationFeeValue,

                        CommissionRateSnapshot = rateSnapshot,

                        PlatformCommissionAmount = platformCommission,

                        OwnerPayoutAmount = ownerPayout,

                        SecurityDepositAmount = property.SecurityDepositAmount,

                        SecurityDepositStatus = property.SecurityDepositAmount > 0 ? "Held" : "Released",

                        IsEventBooking = selectedPackage != null,

                        EventPackageID = selectedPackage?.PackageID,

                        DecoratorAssignedID = selectedPackage?.CreatedByUserID,

                        EventPackagePriceSnapshot = eventPackagePrice,

                        EventCommissionRateSnapshot = eventRateSnapshot,

                        EventPlatformCommissionAmount = eventPlatformCommission,

                        EventManagerPayoutAmount = eventManagerPayout,

                        CustomEventNotes = selectedPackage != null ? model.CustomEventNotes : null,

                        EventDate = selectedPackage != null ? model.CheckInDate : null,

                        SetupStatus = selectedPackage != null ? "Pending" : null,

                        IdempotencyKey = Guid.NewGuid().ToString("N")

                    };

                    var (atomicAvailable, createdBookingId) = await _bookingRepo.CreateHoldAtomicAsync(booking, model.CheckInTime, model.HourlyDuration);
                    if (!atomicAvailable || createdBookingId <= 0)
                    {
                        ModelState.AddModelError("", "These dates are already booked.");
                        TempData["Error"] = "These dates are no longer available. Please choose different dates.";
                        await PopulateCheckoutViewModelAsync(model, property);
                        return View("Checkout", model);
                    }
                    bookingId = createdBookingId;

                    try
                    {
                        var client = await _userRepo.GetByIdAsync(clientId);
                        if (client != null && !string.IsNullOrWhiteSpace(client.Email) && _emailService != null)
                        {
                            string baseUrl = Request != null && Request.Host.HasValue
                                ? $"{Request.Scheme}://{Request.Host}"
                                : "https://bookmyfun.in";
                            string paymentUrl = $"{baseUrl}/Booking/Payment/{bookingId}";
                            string safeTitle = System.Net.WebUtility.HtmlEncode(property.Title);
                            string safeName = System.Net.WebUtility.HtmlEncode(client.FullName);
                            string checkInStr = $"{model.CheckInDate:dd MMM yyyy} at 11:00";
                            string checkOutStr = $"{model.CheckOutDate:dd MMM yyyy} at 10:00";

                            string bodyContent = $@"
                                <p>Hello {safeName},</p>
                                <p>Your booking hold has been created. Payment is pending and the hold expires in <strong>15 minutes</strong>.</p>
                                <table style='width:100%; border-collapse:collapse; margin:16px 0; background:#F8FAFC; border:1px solid #E2E8F0;'>
                                    <tr><td style='padding:8px 12px; color:#64748B;'>Property</td><td style='padding:8px 12px; font-weight:600;'>{safeTitle}</td></tr>
                                    <tr><td style='padding:8px 12px; color:#64748B;'>Check-in</td><td style='padding:8px 12px;'>{checkInStr}</td></tr>
                                    <tr><td style='padding:8px 12px; color:#64748B;'>Check-out</td><td style='padding:8px 12px;'>{checkOutStr}</td></tr>
                                    <tr><td style='padding:8px 12px; color:#64748B;'>Amount</td><td style='padding:8px 12px; font-weight:700;'>₹{totalAmount:N0}</td></tr>
                                </table>";

                            string plainText = $"Hello {client.FullName}, your booking hold #{bookingId} for {property.Title} is created (Check-in: {checkInStr}, Check-out: {checkOutStr}, Amount: INR {totalAmount:N0}). Payment is pending and the hold expires in 15 minutes. Complete payment at {paymentUrl}";

                            await _emailService.SendTemplatedEmailAsync(
                                clientId,
                                bookingId,
                                client.Email,
                                "BookingCreated",
                                $"booking-created-{bookingId}",
                                $"Booking hold created for {property.Title}",
                                "Booking hold created",
                                bodyContent,
                                "Complete payment",
                                paymentUrl,
                                plainText);
                        }
                    }
                    catch (Exception emailEx)
                    {
                        _logger?.LogWarning(emailEx, "Failed to send BookingCreated email for Booking #{BookingId}", bookingId);
                    }
                }

                // Record policy consent acceptances (Item 3f)

                if (_legalService != null)

                {

                    try

                    {

                        var ip = HttpContext?.Connection?.RemoteIpAddress?.ToString();

                        var pageUrl = Request?.Path.Value;

                        await _legalService.RecordAllStandardConsentsAsync(clientId, null, ip, pageUrl);

                    }

                    catch (Exception ex)

                    {

                        _logger?.LogWarning(ex, "Failed to record legal policy acceptances for client {ClientId} during booking checkout", clientId);

                    }

                }

                // Redirect to Dedicated Payment Page

                return RedirectToAction("Payment", new { id = bookingId });

            }

            catch (Exception ex)

            {

                _logger?.LogError(ex, "Unexpected error in ConfirmBooking for PropertyId {PropertyId}", model.PropertyID);

                TempData["Error"] = "An unexpected error occurred while processing your booking. Please try again or contact support if the problem persists.";

                var property = await _propRepo.GetByIdAsync(model.PropertyID);

                if (property != null)

                {

                    await PopulateCheckoutViewModelAsync(model, property);

                    return View("Checkout", model);

                }

                return RedirectToAction("Index", "Properties");

            }

        }

        [Authorize]

        [HttpGet]

        public async Task<IActionResult> Payment(int id)

        {

            if (RoleAccessGuard.IsBookingOrPropertyViewBlocked(User))
            {
                return RoleAccessGuard.RedirectToRoleDashboard(this, "This page is for guests. Use your dashboard.");
            }

            try

            {

                if (id <= 0)

                {

                    TempData["Error"] = "Invalid booking reference provided.";

                    return RedirectToAction("Index", "Properties");

                }

                var booking = await _bookingRepo.GetByIdAsync(id);

                if (booking == null)

                {

                    TempData["Error"] = "The requested booking could not be found or has expired. Please start a new booking.";

                    return RedirectToAction("Index", "Properties");

                }

                var currentUserIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);

                if (string.IsNullOrEmpty(currentUserIdStr) || !int.TryParse(currentUserIdStr, out int currentUserId))

                {

                    return Challenge();

                }

                if (booking.ClientID != currentUserId && !User.IsInRole("Admin"))

                {

                    _logger?.LogWarning("Unauthorized payment access attempt: User {UserId} attempted to access booking {BookingId} belonging to user {OwnerId}",

                        currentUserId, booking.BookingID, booking.ClientID);

                    return NotFound();

                }

                bool isHoldExpired = string.Equals(booking.Status, "Expired", StringComparison.OrdinalIgnoreCase) ||
                    (string.Equals(booking.Status, "Hold", StringComparison.OrdinalIgnoreCase) &&
                     booking.HoldExpiresAt.HasValue && booking.HoldExpiresAt.Value <= DateTime.UtcNow);

                if (isHoldExpired)
                {
                    if (string.Equals(booking.Status, "Hold", StringComparison.OrdinalIgnoreCase))
                    {
                        await _bookingRepo.UpdateBookingStatusAsync(booking.BookingID, "Expired");
                    }
                    TempData["Error"] = "Your hold on these dates has expired. Please select your dates again.";
                    return RedirectToAction("Checkout", new
                    {
                        propertyId = booking.PropertyID,
                        bookingType = booking.BookingType,
                        slotType = booking.SlotType,
                        checkIn = booking.CheckInDate.ToString("yyyy-MM-dd"),
                        checkOut = booking.CheckOutDate.ToString("yyyy-MM-dd")
                    });
                }

                if (booking.Status == "Confirmed" || booking.Status == "Paid")

                {

                    return RedirectToAction("Confirmation", new { id = booking.BookingID });

                }

                var property = await _propRepo.GetByIdAsync(booking.PropertyID);

                if (property == null)

                {

                    TempData["Error"] = "The property associated with this booking is no longer available.";

                    return RedirectToAction("Index", "Properties");

                }

                // Check availability AGAIN right before starting payment to prevent double bookings

                bool isAvailable = await _bookingRepo.IsSlotAvailableAsync(

                    booking.PropertyID,

                    booking.CheckInDate,

                    booking.CheckOutDate,

                    booking.BookingType,

                    booking.SlotType,

                    excludeBookingId: booking.BookingID);

                if (!isAvailable)

                {

                    _logger?.LogWarning("Slot conflict detected at payment initialization for Booking {BookingId}, Property {PropertyId} ({CheckIn} to {CheckOut})",

                        booking.BookingID, booking.PropertyID, booking.CheckInDate, booking.CheckOutDate);

                    TempData["Error"] = "These dates are no longer available. Please choose different dates.";

                    return RedirectToAction("Checkout", new { propertyId = booking.PropertyID, bookingType = booking.BookingType, slotType = booking.SlotType });

                }

                PaymentOrderResponse order;

                try

                {

                    order = await _paymentService.CreateOrderAsync(booking.TotalAmount, $"booking_{booking.BookingID}");

                }

                catch (Exception ex)

                {

                    _logger?.LogError(ex, "Payment setup failure for booking {BookingId} with amount {Amount}", booking.BookingID, booking.TotalAmount);

                    TempData["Error"] = "We were unable to set up the payment gateway at this moment. Please try again shortly or contact our 24/7 support team.";

                    return RedirectToAction("Checkout", new { propertyId = booking.PropertyID, bookingType = booking.BookingType, slotType = booking.SlotType });

                }

                var client = await _userRepo.GetByIdAsync(booking.ClientID);

                var vm = new PaymentPageViewModel

                {

                    BookingID = booking.BookingID,

                    PropertyTitle = property?.Title ?? "Property Stay",

                    Location = property?.Location ?? "",

                    Amount = booking.TotalAmount,

                    Currency = order.Currency,

                    OrderId = order.OrderId,

                    KeyId = order.KeyId,

                    IsSimulated = order.IsSimulated,

                    ClientName = client?.FullName ?? "",

                    ClientEmail = client?.Email ?? "",

                    ClientPhone = client?.Phone,

                    BookingType = booking.BookingType,

                    SlotType = booking.SlotType,

                    CheckInDate = booking.CheckInDate,

                    CheckOutDate = booking.CheckOutDate,

                    CheckInTime = booking.SlotStartTime,

                    CheckOutTime = booking.SlotEndTime

                };

                return View(vm);

            }

            catch (Exception ex)

            {

                _logger?.LogError(ex, "Unexpected error occurred during payment processing for booking {BookingId}", id);

                TempData["Error"] = "An unexpected error occurred while preparing your payment. Please try again or contact support if the problem persists.";

                return RedirectToAction("Index", "Properties");

            }

        }

        [Authorize]

        [HttpPost]

        [ValidateAntiForgeryToken]

        public async Task<IActionResult> VerifyPayment(int bookingId, string razorpay_order_id, string razorpay_payment_id, string razorpay_signature)

        {

            try

            {

                if (bookingId <= 0)

                {

                    TempData["Error"] = "Invalid booking reference.";

                    return RedirectToAction("Index", "Properties");

                }

                var booking = await _bookingRepo.GetByIdAsync(bookingId);

                if (booking == null)

                {

                    TempData["Error"] = "The requested booking could not be found or has expired.";

                    return RedirectToAction("Index", "Properties");

                }

                var currentUserIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);

                if (string.IsNullOrEmpty(currentUserIdStr) || !int.TryParse(currentUserIdStr, out var currentUserId))

                {

                    return Challenge();

                }

                if (booking.ClientID != currentUserId && !User.IsInRole("Admin"))

                {

                    _logger?.LogWarning("Unauthorized payment verification attempt: User {UserId} for booking {BookingId}", currentUserId, bookingId);

                    return NotFound();

                }

                bool isHoldExpired = string.Equals(booking.Status, "Expired", StringComparison.OrdinalIgnoreCase) ||
                    (string.Equals(booking.Status, "Hold", StringComparison.OrdinalIgnoreCase) &&
                     booking.HoldExpiresAt.HasValue && booking.HoldExpiresAt.Value <= DateTime.UtcNow);

                if (isHoldExpired)
                {
                    if (string.Equals(booking.Status, "Hold", StringComparison.OrdinalIgnoreCase))
                    {
                        await _bookingRepo.UpdateBookingStatusAsync(booking.BookingID, "Expired");
                    }
                    TempData["Error"] = "Your hold on these dates has expired. Please select your dates again.";
                    return RedirectToAction("Checkout", new
                    {
                        propertyId = booking.PropertyID,
                        bookingType = booking.BookingType,
                        slotType = booking.SlotType,
                        checkIn = booking.CheckInDate.ToString("yyyy-MM-dd"),
                        checkOut = booking.CheckOutDate.ToString("yyyy-MM-dd")
                    });
                }

                if (booking.Status == "Confirmed" || booking.Status == "Paid")

                {

                    return RedirectToAction("Confirmation", new { id = bookingId });

                }

                if (string.IsNullOrWhiteSpace(razorpay_order_id) || string.IsNullOrWhiteSpace(razorpay_payment_id) || string.IsNullOrWhiteSpace(razorpay_signature))

                {

                    TempData["Error"] = "Incomplete payment verification payload received. Please retry payment or contact support.";

                    return RedirectToAction("Payment", new { id = bookingId });

                }

                bool isValid = await _paymentService.VerifyPaymentSignatureAsync(razorpay_order_id, razorpay_payment_id, razorpay_signature);

                if (!isValid)

                {

                    _logger?.LogWarning("Payment signature verification failed for booking {BookingId}, Order {OrderId}, Payment {PaymentId}",

                        bookingId, razorpay_order_id, razorpay_payment_id);

                    if (_errorHandler != null)

                    {

                        await _errorHandler.OnPaymentFailedAsync(bookingId, "Payment cryptographic signature verification failed.");

                    }

                    TempData["Error"] = "Payment signature verification failed. Please try again or reach our 24/7 helpdesk.";

                    return RedirectToAction("Payment", new { id = bookingId });

                }

                // Payment signature verified on client return.

                if (booking.Status != "Confirmed")

                {

                    await _bookingRepo.ConfirmBookingPaymentAsync(booking.BookingID, new Payment
                    {
                        BookingID = booking.BookingID,
                        GatewayTransactionID = $"RAZORPAY_{razorpay_payment_id}",
                        Amount = booking.TotalAmount,
                        PaymentStatus = "Success"
                    });

                    booking.Status = "Confirmed";

                    _ = _notificationDispatcher?.DispatchBookingConfirmedAsync(booking.BookingID);
                    _ = _notificationDispatcher?.DispatchBookingCreatedStaffAlertAsync(booking.BookingID, booking.ClientID, booking.PropertyID);

                    // Invalidate popular stays 15-minute cache immediately upon confirmed booking (Item 9b)

                    SiteSettingsService.InvalidatePopularStaysCacheStatic();

                    var property = await _propRepo.GetByIdAsync(booking.PropertyID);

                    var client = await _userRepo.GetByIdAsync(booking.ClientID);

                    var owner = property != null ? await _userRepo.GetByIdAsync(property.OwnerID) : null;

                    User? eventManager = null;

                    if (booking.IsEventBooking && booking.EventPackagePriceSnapshot > 0)

                    {

                        if (booking.DecoratorAssignedID.HasValue)

                        {

                            eventManager = await _userRepo.GetByIdAsync(booking.DecoratorAssignedID.Value);

                        }

                        if (eventManager == null)

                        {

                            var decorators = await _eventPackageRepo.GetDecoratorsAsync();

                            eventManager = decorators.FirstOrDefault();

                        }

                    }

                    if (property != null && client != null)

                    {

                        string baseUrl = Request != null && Request.Host.HasValue

                            ? $"{Request.Scheme}://{Request.Host}"

                            : "https://bookmyfun.in";

                        await _emailService.DispatchConfirmedBookingNotificationsAsync(booking, property, client, owner, eventManager, baseUrl);

                    }

                }

                TempData["Success"] = "Payment verified! Your booking is confirmed.";

                return RedirectToAction("Confirmation", new { id = bookingId });

            }

            catch (Exception ex)

            {

                _logger?.LogError(ex, "Unexpected error in VerifyPayment for booking {BookingId}", bookingId);

                TempData["Error"] = "An unexpected error occurred while verifying your payment. Please contact our support team.";

                return RedirectToAction("Payment", new { id = bookingId });

            }

        }

        private async Task DispatchConfirmedBookingAlertsAsync(Booking booking, Property property, User client, string transactionId, string paymentMethod)

        {

            var owner = await _userRepo.GetByIdAsync(property.OwnerID);

            var receiptVm = new BookingReceiptViewModel

            {

                Booking = booking,

                Property = property,

                Client = client,

                Host = owner,

                PrimaryPayment = new Payment

                {

                    BookingID = booking.BookingID,

                    GatewayTransactionID = transactionId.StartsWith("RAZORPAY_") || transactionId.StartsWith("PAY_") ? transactionId : $"PAY_{transactionId}",

                    Amount = booking.TotalAmount,

                    PaymentStatus = "Success"

                }

            };

            string baseUrl = Request != null && Request.Host.HasValue

                ? $"{Request.Scheme}://{Request.Host}"

                : "https://bookmyfun.in";

            string clientReceiptUrl = $"{baseUrl}/Booking/Receipt/{booking.BookingID}";

            string ownerReceiptUrl = $"{baseUrl}/Owner/BookingReceipt/{booking.BookingID}";

            // 1. Transactional Receipt to Guest / Client (Email & WhatsApp)

            await _emailService.SendBookingReceiptEmailAsync(client.Email, client.FullName, receiptVm, clientReceiptUrl);

            _ = _notificationDispatcher?.DispatchReceiptAvailableAsync(booking.BookingID, receiptVm.ReceiptNumber);

            if (!string.IsNullOrEmpty(client.Phone))

            {

                _ = _whatsAppService.SendBookingReceiptWhatsAppAsync(

                    client.Phone,

                    client.FullName,

                    property.Title,

                    booking.BookingID.ToString(),

                    receiptVm.ReceiptNumber,

                    booking.TotalAmount,

                    clientReceiptUrl,

                    booking.CheckInDate,

                    booking.CheckOutDate);

            }

            // 2. Transactional Settlement Receipt to Property Owner (Email & WhatsApp)

            if (owner != null)

            {

                await _emailService.SendOwnerSettlementReceiptEmailAsync(owner.Email, owner.FullName, receiptVm, ownerReceiptUrl);

                if (!string.IsNullOrEmpty(owner.Phone))

                {

                    _ = _whatsAppService.SendOwnerBookingReceiptWhatsAppAsync(

                        owner.Phone,

                        owner.FullName,

                        property.Title,

                        booking.BookingID.ToString(),

                        receiptVm.HostReceiptNumber,

                        receiptVm.PropertyStayGross,

                        receiptVm.HostNetPayout,

                        client.FullName,

                        booking.CheckInDate,

                        booking.CheckOutDate,

                        booking.IsEventBooking,

                        booking.EventPackagePriceSnapshot,

                        ownerReceiptUrl);

                }

            }

            // 3. WhatsApp & Email to Event Manager if décor package was booked

            if (booking.IsEventBooking && booking.EventPackagePriceSnapshot > 0)

            {

                var decorators = await _eventPackageRepo.GetDecoratorsAsync();

                var eventManager = decorators.FirstOrDefault();

                if (eventManager != null)

                {

                    string pkgName = booking.EventPackageName ?? "Selected Décor Package";

                    if (!string.IsNullOrEmpty(eventManager.Phone))

                    {

                        _ = _whatsAppService.SendEventManagerSetupAlertAsync(

                            eventManager.Phone,

                            eventManager.FullName,

                            property.Title,

                            booking.BookingID.ToString(),

                            pkgName,

                            booking.EventPackagePriceSnapshot,

                            booking.CheckInDate,

                            client.FullName);

                    }

                    string emEmailBody = $@"

                        <div style='font-family: Arial, sans-serif; line-height: 1.6; color: #333; max-width: 600px;'>

                            <h2 style='color: #d97706;'>New Event Staging Order #{booking.BookingID}</h2>

                            <p>Dear {eventManager.FullName},</p>

                            <p>A new event décor setup order has been booked on BookMyFun for venue <strong>{property.Title}</strong>.</p>

                            <div style='background: #fffbeb; border: 1px solid #fde68a; border-radius: 8px; padding: 15px; margin: 15px 0;'>

                                <p style='margin: 4px 0;'><strong>Décor Package:</strong> {pkgName}</p>

                                <p style='margin: 4px 0;'><strong>Event Date:</strong> {booking.CheckInDate:dd MMM yyyy}</p>

                                <p style='margin: 4px 0;'><strong>Venue Location:</strong> {property.Location}</p>

                                <p style='margin: 4px 0;'><strong>Client Name:</strong> {client.FullName}</p>

                                <p style='margin: 4px 0;'><strong>Gross Décor Revenue:</strong> ₹{booking.EventPackagePriceSnapshot:N2}</p>

                                <p style='margin: 4px 0;'><strong>Platform Commission ({booking.EventCommissionRateSnapshot:0.#}%):</strong> -₹{booking.EventPlatformCommissionAmount:N2}</p>

                                <p style='margin: 4px 0; font-size: 1.15em; font-weight: bold; color: #b45309;'><strong>Net Decorator Payout:</strong> ₹{booking.EventManagerPayoutAmount:N2}</p>

                            </div>

                            <p>Sign in to your Event Manager portal to accept this setup order and dispatch staging crew.</p>

                        </div>";

                    await _emailService.EnqueueEmailAsync(eventManager.Email, $"New Event Setup Order #{booking.BookingID} - {pkgName}", emEmailBody, "EventManagerSetupAlert");

                }

            }

        }

        [Authorize]

        [HttpPost]

        [ValidateAntiForgeryToken]

        public async Task<IActionResult> ResendReceipt(int bookingId, string? returnUrl = null)

        {

            var booking = await _bookingRepo.GetByIdAsync(bookingId);

            if (booking == null) return NotFound();

            var currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            bool isStaff = User.IsInRole("Admin") || User.IsInRole("Employee");

            if (booking.ClientID != currentUserId && !isStaff)

            {

                return Forbid();

            }

            var client = await _userRepo.GetByIdAsync(booking.ClientID);

            var property = await _propRepo.GetByIdAsync(booking.PropertyID);

            if (client == null || property == null)

            {

                TempData["Error"] = "Unable to resolve reservation metadata for receipt dispatch.";

                return RedirectToAction("Confirmation", new { id = bookingId });

            }

            var owner = await _userRepo.GetByIdAsync(property.OwnerID);

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

                PrimaryPayment = primaryPayment

            };

            string baseUrl = Request != null && Request.Host.HasValue

                ? $"{Request.Scheme}://{Request.Host}"

                : "https://bookmyfun.in";

            string clientReceiptUrl = $"{baseUrl}/Booking/Receipt/{booking.BookingID}";

            await _emailService.SendBookingReceiptEmailAsync(client.Email, client.FullName, receiptVm, clientReceiptUrl);

            if (!string.IsNullOrEmpty(client.Phone))

            {

                _ = _whatsAppService.SendBookingReceiptWhatsAppAsync(

                    client.Phone,

                    client.FullName,

                    property.Title,

                    booking.BookingID.ToString(),

                    receiptVm.ReceiptNumber,

                    booking.TotalAmount,

                    clientReceiptUrl,

                    booking.CheckInDate,

                    booking.CheckOutDate);

            }

            if (_configRepo != null)

            {

                await _configRepo.LogAuditAsync(currentUserId, "ResendReceipt", "Booking", bookingId, $"Resent receipt #{receiptVm.ReceiptNumber} for booking #{bookingId} to {client.Email}");

            }

            TempData["Success"] = $"Official receipt copy ({receiptVm.ReceiptNumber}) has been dispatched to {client.Email} and WhatsApp.";

            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))

            {

                return Redirect(returnUrl);

            }

            return RedirectToAction("Confirmation", new { id = bookingId });

        }

        [Authorize]

        [HttpGet]

        public async Task<IActionResult> GetCancellationQuote(int bookingId)

        {

            var booking = await _bookingRepo.GetByIdAsync(bookingId);

            if (booking == null) return NotFound(new { success = false, message = "Booking not found." });

            var currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            var property = await _propRepo.GetByIdAsync(booking.PropertyID);

            bool isClient = booking.ClientID == currentUserId;

            bool isOwner = property != null && property.OwnerID == currentUserId;

            bool isStaff = User.IsInRole("Admin") || User.IsInRole("Employee");

            if (!isClient && !isOwner && !isStaff)

            {

                return Forbid();

            }

            var quote = await _refundEngine.CalculateCancellationQuoteAsync(booking, DateTime.UtcNow, isOwner);

            return Json(new { success = true, quote });

        }

        [Authorize]

        [HttpPost]

        [ValidateAntiForgeryToken]

        public async Task<IActionResult> CancelBooking(int bookingId, string? reason, bool forceOverride = false)

        {

            var booking = await _bookingRepo.GetByIdAsync(bookingId);

            if (booking == null) return NotFound();

            var currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            var property = await _propRepo.GetByIdAsync(booking.PropertyID);

            bool isClient = booking.ClientID == currentUserId;

            bool isOwner = property != null && property.OwnerID == currentUserId;

            bool isStaff = User.IsInRole("Admin") || User.IsInRole("Employee");

            if (!isClient && !isOwner && !isStaff)

            {

                return Forbid();

            }

            var userRole = isOwner ? "Owner" : (isStaff ? (User.FindFirstValue(ClaimTypes.Role) ?? "Admin") : "Client");

            if (booking.Status == "Cancelled")

            {

                TempData["Error"] = "This booking is already cancelled.";

                return isOwner ? RedirectToAction("Index", "Owner") : RedirectToAction("Index", "Client");

            }

            var refund = await _refundEngine.ProcessCancellationAndRefundAsync(bookingId, currentUserId, userRole, reason, forceOverride);

            _ = _notificationDispatcher?.DispatchBookingCancelledAsync(bookingId, reason);
            if (refund.NetRefundAmount > 0)
            {
                _ = _notificationDispatcher?.DispatchRefundProcessedAsync(bookingId, refund.NetRefundAmount);
            }

            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")

            {

                return Json(new { success = true, refund });

            }

            TempData["Success"] = $"Booking #{bookingId} has been cancelled successfully. Net refund: ₹{refund.NetRefundAmount:N2}";

            return isOwner ? RedirectToAction("Index", "Owner") : RedirectToAction("Index", "Client");

        }

        [Authorize]

        [HttpGet]

        public async Task<IActionResult> GetPaymentAndRefundDetails(int bookingId)

        {

            var booking = await _bookingRepo.GetBookingByIdAsync(bookingId);

            if (booking == null) return NotFound(new { success = false, message = "Booking not found." });

            var currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            var isStaff = User.IsInRole("Admin") || User.IsInRole("Employee");

            if (!isStaff)

            {

                var role = User.FindFirstValue(ClaimTypes.Role) ?? "Client";

                if (role == "Client" && booking.ClientID != currentUserId)

                {

                    return Forbid();

                }

                if (role == "Owner")

                {

                    var property = await _propRepo.GetByIdAsync(booking.PropertyID);

                    if (property == null || property.OwnerID != currentUserId)

                    {

                        return Forbid();

                    }

                }

                if (role != "Client" && role != "Owner")

                {

                    return Forbid();

                }

            }

            var payments = await _bookingRepo.GetPaymentsByBookingIdAsync(bookingId);

            var refund = await _refundRepo.GetRefundByBookingIdAsync(bookingId);

            return Json(new

            {

                success = true,

                booking = new

                {

                    booking.BookingID,

                    booking.PropertyTitle,

                    booking.TotalAmount,

                    booking.Status,

                    booking.SecurityDepositAmount,

                    booking.SecurityDepositStatus,

                    CheckIn = booking.CheckInDate.ToString("MMM dd, yyyy"),

                    CheckOut = booking.CheckOutDate.ToString("MMM dd, yyyy")

                },

                payments = payments.Select(p => new

                {

                    p.PaymentID,

                    p.GatewayTransactionID,

                    p.Amount,

                    p.PaymentStatus,

                    PaymentDate = p.PaymentDate.ToString("MMM dd, yyyy HH:mm")

                }),

                refund = refund != null ? new

                {

                    refund.RefundID,

                    refund.GrossBookingAmount,

                    refund.CancellationFeeDeducted,

                    refund.GatewayFeeDeducted,

                    refund.NetRefundAmount,

                    refund.Status,

                    refund.GatewayRefundID,

                    refund.RefundReason,

                    CreatedAt = refund.CreatedAt.ToString("MMM dd, yyyy HH:mm"),

                    ProcessedAt = refund.ProcessedAt?.ToString("MMM dd, yyyy HH:mm")

                } : null

            });

        }

        [Authorize]

        public async Task<IActionResult> Confirmation(int id)

        {

            var booking = await _bookingRepo.GetByIdAsync(id);

            if (booking == null) return NotFound();

            var property = await _propRepo.GetByIdAsync(booking.PropertyID);

            var currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            var isStaff = User.IsInRole("Admin") || User.IsInRole("Employee");

            if (!isStaff)

            {

                var role = User.FindFirstValue(ClaimTypes.Role) ?? "Client";

                if (role == "Client" && booking.ClientID != currentUserId)

                {

                    return NotFound();

                }

                if (role == "Owner" && (property == null || property.OwnerID != currentUserId))

                {

                    return NotFound();

                }

                if (role != "Client" && role != "Owner")

                {

                    return NotFound();

                }

            }

            var client = await _userRepo.GetByIdAsync(booking.ClientID);

            var host = property != null ? await _userRepo.GetByIdAsync(property.OwnerID) : null;

            var payments = await _bookingRepo.GetPaymentsByBookingIdAsync(id);

            var vm = new BookingReceiptViewModel

            {

                Booking = booking,

                Property = property,

                Client = client,

                Host = host,

                PrimaryPayment = payments.FirstOrDefault(),

                PaymentHistory = payments,

                IsOwnerView = false,

                IsAdminOrStaffView = User.IsInRole("Admin") || User.IsInRole("Employee")

            };

            return View(vm);

        }

        [Authorize]

        [HttpGet]

        public async Task<IActionResult> Receipt(int id)

        {

            var booking = await _bookingRepo.GetByIdAsync(id);

            if (booking == null) return NotFound();

            var property = await _propRepo.GetByIdAsync(booking.PropertyID);

            var currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            var isStaff = User.IsInRole("Admin") || User.IsInRole("Employee");

            if (!isStaff)

            {

                var role = User.FindFirstValue(ClaimTypes.Role) ?? "Client";

                if (role == "Client" && booking.ClientID != currentUserId)

                {

                    return NotFound();

                }

                if (role == "Owner" && (property == null || property.OwnerID != currentUserId))

                {

                    return NotFound();

                }

                if (role != "Client" && role != "Owner")

                {

                    return NotFound();

                }

            }

            var client = await _userRepo.GetByIdAsync(booking.ClientID);

            var host = property != null ? await _userRepo.GetByIdAsync(property.OwnerID) : null;

            var payments = await _bookingRepo.GetPaymentsByBookingIdAsync(id);

            var vm = new BookingReceiptViewModel

            {

                Booking = booking,

                Property = property,

                Client = client,

                Host = host,

                PrimaryPayment = payments.FirstOrDefault(),

                PaymentHistory = payments,

                IsOwnerView = false,

                IsAdminOrStaffView = User.IsInRole("Admin") || User.IsInRole("Employee")

            };

            return View("Receipt", vm);

        }

        [Authorize(Roles = "Owner,Admin,Employee")]

        [HttpGet]

        public IActionResult OwnerReceipt(int id)

        {

            return RedirectToAction("BookingReceipt", "Owner", new { id });

        }

    }

}
