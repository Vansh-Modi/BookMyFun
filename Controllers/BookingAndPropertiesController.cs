using System.Security.Claims;
using BookMyFun.Models.Entities;
using BookMyFun.Models.ViewModels;
using BookMyFun.Repositories;
using BookMyFun.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace BookMyFun.Controllers
{
    public class PropertiesController : Controller
    {
        private readonly IPropertyRepository _propRepo;
        private readonly IBookingRepository _bookingRepo;

        public PropertiesController(IPropertyRepository propRepo, IBookingRepository bookingRepo)
        {
            _propRepo = propRepo;
            _bookingRepo = bookingRepo;
        }

        public async Task<IActionResult> Index(string? location, int? typeId, int? occasionId, decimal? minPrice, decimal? maxPrice, int? guests, int page = 1, int pageSize = 12)
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);

            var (properties, totalCount) = await _propRepo.SearchPropertiesPagedAsync(location, typeId, occasionId, minPrice, maxPrice, guests, page, pageSize);

            ViewBag.PropertyTypes = await _propRepo.GetPropertyTypesAsync();
            ViewBag.Occasions = await _propRepo.GetOccasionsAsync();
            ViewBag.SelectedLocation = location;
            ViewBag.SelectedTypeId = typeId;
            ViewBag.SelectedOccasionId = occasionId;
            ViewBag.SelectedMinPrice = minPrice;
            ViewBag.SelectedMaxPrice = maxPrice;
            ViewBag.SelectedGuests = guests;
            ViewBag.CurrentPage = page;
            ViewBag.PageSize = pageSize;
            ViewBag.TotalCount = totalCount;
            ViewBag.TotalPages = Math.Max(1, (int)Math.Ceiling((double)totalCount / pageSize));

            return View(properties);
        }

        public async Task<IActionResult> Details(int id)
        {
            var property = await _propRepo.GetByIdAsync(id);
            if (property == null) return NotFound();

            var reviews = await _bookingRepo.GetReviewsByPropertyIdAsync(id);
            ViewBag.Reviews = reviews;

            return View(property);
        }
    }

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
            ILogger<BookingController>? logger = null)
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
        }

        [Authorize]
        [HttpGet]
        public async Task<IActionResult> Checkout(int propertyId, string bookingType = "FullDay", string? slotType = null, int daysOrHours = 1)
        {
            if (propertyId <= 0)
            {
                TempData["Error"] = "Invalid property selected. Please choose a valid property.";
                return RedirectToAction("Index", "Properties");
            }

            var property = await _propRepo.GetByIdAsync(propertyId);
            if (property == null)
            {
                TempData["Error"] = "The requested property could not be found. Please browse available stays.";
                return RedirectToAction("Index", "Properties");
            }

            decimal calculatedAmount = _pricingService.CalculatePrice(property.PricePerNight, bookingType, slotType, daysOrHours);

            var clientIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            bool isVerified = false;
            if (int.TryParse(clientIdStr, out int clientId))
            {
                isVerified = await _idVerificationService.IsClientVerifiedAsync(clientId);
            }

            bool isServiced = await _eventPackageRepo.IsAreaServicedByAnyEventManagerAsync(property.Location);
            var activePackages = isServiced ? await _eventPackageRepo.GetActivePackagesAsync() : new List<EventPackage>();

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
                CheckInDate = DateTime.Today,
                CheckOutDate = bookingType == "FullDay" ? DateTime.Today.AddDays(daysOrHours) : DateTime.Today,
                CalculatedAmount = calculatedAmount,
                AvailableOccasions = await _propRepo.GetOccasionsAsync(),
                AvailableEventPackages = activePackages,
                IsEventServiceAvailable = isServiced,
                IsClientAlreadyVerified = isVerified
            };

            return View(vm);
        }

        private async Task PopulateCheckoutViewModelAsync(BookingCheckoutViewModel model, Property property)
        {
            bool isServiced = await _eventPackageRepo.IsAreaServicedByAnyEventManagerAsync(property.Location);
            model.PropertyTitle = property.Title;
            model.Location = property.Location;
            model.PricePerNight = property.PricePerNight;
            model.ImageUrl = property.PrimaryImageUrl;
            model.AvailableOccasions = await _propRepo.GetOccasionsAsync();
            model.IsEventServiceAvailable = isServiced;
            model.AvailableEventPackages = isServiced ? await _eventPackageRepo.GetActivePackagesAsync() : new List<EventPackage>();

            var clientIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (int.TryParse(clientIdStr, out int clientId))
            {
                model.IsClientAlreadyVerified = await _idVerificationService.IsClientVerifiedAsync(clientId);
            }
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ConfirmBooking(BookingCheckoutViewModel model)
        {
            try
            {
                if (model.PropertyID <= 0)
                {
                    TempData["Error"] = "Invalid property selected. Please choose a valid property.";
                    return RedirectToAction("Index", "Properties");
                }

                var property = await _propRepo.GetByIdAsync(model.PropertyID);
                if (property == null)
                {
                    TempData["Error"] = "The requested property could not be found. Please browse available stays.";
                    return RedirectToAction("Index", "Properties");
                }

                var clientIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(clientIdStr) || !int.TryParse(clientIdStr, out int clientId))
                {
                    return Challenge();
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

                if (model.BookingType == "Hourly" && (!model.HourlyDuration.HasValue || model.HourlyDuration.Value <= 0))
                {
                    ModelState.AddModelError("HourlyDuration", "Please select a valid hourly booking duration.");
                }

                bool isServiced = await _eventPackageRepo.IsAreaServicedByAnyEventManagerAsync(property.Location);
                if (!isServiced)
                {
                    model.IsEventBooking = false;
                    model.EventPackageID = null;
                    model.CustomEventNotes = null;
                }

                // Server-side availability check before proceeding
                if (ModelState.IsValid)
                {
                    bool isAvailable = await _bookingRepo.IsSlotAvailableAsync(model.PropertyID, model.CheckInDate, model.CheckOutDate, model.BookingType, model.SlotType);
                    if (!isAvailable)
                    {
                        const string unavailableMsg = "These dates are no longer available. Please choose different dates.";
                        ModelState.AddModelError("", unavailableMsg);
                        TempData["Error"] = unavailableMsg;
                    }
                }

                if (!ModelState.IsValid)
                {
                    await PopulateCheckoutViewModelAsync(model, property);
                    return View("Checkout", model);
                }

                int count = model.BookingType == "Hourly" ? (model.HourlyDuration ?? 1) : Math.Max(1, (model.CheckOutDate - model.CheckInDate).Days);
                decimal propertyStayAmount = _pricingService.CalculatePrice(property.PricePerNight, model.BookingType, model.SlotType, count);

                EventPackage? selectedPackage = null;
                decimal eventPackagePrice = 0m;
                if (isServiced && model.IsEventBooking && model.EventPackageID.HasValue)
                {
                    selectedPackage = await _eventPackageRepo.GetPackageByIdAsync(model.EventPackageID.Value);
                    if (selectedPackage != null && selectedPackage.IsActive)
                    {
                        eventPackagePrice = selectedPackage.BasePrice;
                    }
                }

                decimal totalAmount = propertyStayAmount + eventPackagePrice;

                // If guest uploaded government ID at booking checkout, securely save into private vault
                if (model.IdDocumentFile != null && model.IdDocumentFile.Length > 0)
                {
                    try
                    {
                        string docKey = await _mediaStorage.SavePrivateDocumentAsync(model.IdDocumentFile, clientId);
                        await _idVerificationService.SubmitVerificationAsync(clientId, model.DocumentType ?? "Aadhaar", docKey, model.IdDocumentNumber);
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

                var booking = new Booking
                {
                    PropertyID = model.PropertyID,
                    ClientID = clientId,
                    OccasionID = model.OccasionID,
                    CheckInDate = model.CheckInDate,
                    CheckOutDate = model.CheckOutDate,
                    TotalAmount = totalAmount,
                    Status = "Pending", // Pending payment
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
                    EventPackagePriceSnapshot = eventPackagePrice,
                    EventCommissionRateSnapshot = eventRateSnapshot,
                    EventPlatformCommissionAmount = eventPlatformCommission,
                    EventManagerPayoutAmount = eventManagerPayout,
                    CustomEventNotes = selectedPackage != null ? model.CustomEventNotes : null,
                    EventDate = selectedPackage != null ? model.CheckInDate : null,
                    SetupStatus = selectedPackage != null ? "Pending" : null,
                    IdempotencyKey = Guid.NewGuid().ToString("N")
                };

                int bookingId = await _bookingRepo.CreateBookingAsync(booking);

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
                    return Forbid();
                }

                if (booking.Status == "Confirmed")
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
                    return Forbid();
                }

                if (booking.Status == "Confirmed")
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
                    TempData["Error"] = "Payment signature verification failed. Please try again or reach our 24/7 helpdesk.";
                    return RedirectToAction("Payment", new { id = bookingId });
                }

                // Payment signature verified on client return.
                // Booking and payment status updates are handled exclusively via verified Razorpay webhook events.
                TempData["Success"] = "Payment verified! Your booking confirmation is being finalized via our secure webhook.";
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
                    return Forbid();
                }
                if (role == "Owner" && (property == null || property.OwnerID != currentUserId))
                {
                    return Forbid();
                }
                if (role != "Client" && role != "Owner")
                {
                    return Forbid();
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
                    return Forbid();
                }
                if (role == "Owner" && (property == null || property.OwnerID != currentUserId))
                {
                    return Forbid();
                }
                if (role != "Client" && role != "Owner")
                {
                    return Forbid();
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
