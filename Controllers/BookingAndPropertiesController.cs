using System.Security.Claims;
using BookMyFun.Models.Entities;
using BookMyFun.Models.ViewModels;
using BookMyFun.Repositories;
using BookMyFun.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

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

        public async Task<IActionResult> Index(string? location, int? typeId, int? occasionId, decimal? minPrice, decimal? maxPrice, int? guests)
        {
            var properties = await _propRepo.SearchPropertiesAsync(location, typeId, occasionId, minPrice, maxPrice, guests);

            ViewBag.PropertyTypes = await _propRepo.GetPropertyTypesAsync();
            ViewBag.Occasions = await _propRepo.GetOccasionsAsync();
            ViewBag.SelectedLocation = location;
            ViewBag.SelectedTypeId = typeId;
            ViewBag.SelectedOccasionId = occasionId;
            ViewBag.SelectedGuests = guests;

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
            IMediaStorageService mediaStorage)
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
        }

        [Authorize]
        [HttpGet]
        public async Task<IActionResult> Checkout(int propertyId, string bookingType = "FullDay", string? slotType = null, int daysOrHours = 1)
        {
            var property = await _propRepo.GetByIdAsync(propertyId);
            if (property == null) return NotFound();

            decimal calculatedAmount = _pricingService.CalculatePrice(property.PricePerNight, bookingType, slotType, daysOrHours);

            int clientId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            bool isVerified = await _idVerificationService.IsClientVerifiedAsync(clientId);

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
                AvailableEventPackages = await _eventPackageRepo.GetActivePackagesAsync(),
                IsClientAlreadyVerified = isVerified
            };

            return View(vm);
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ConfirmBooking(BookingCheckoutViewModel model)
        {
            var property = await _propRepo.GetByIdAsync(model.PropertyID);
            if (property == null) return NotFound();

            int count = model.BookingType == "Hourly" ? (model.HourlyDuration ?? 1) : Math.Max(1, (model.CheckOutDate - model.CheckInDate).Days);
            decimal propertyStayAmount = _pricingService.CalculatePrice(property.PricePerNight, model.BookingType, model.SlotType, count);

            EventPackage? selectedPackage = null;
            decimal eventPackagePrice = 0m;
            if (model.IsEventBooking && model.EventPackageID.HasValue)
            {
                selectedPackage = await _eventPackageRepo.GetPackageByIdAsync(model.EventPackageID.Value);
                if (selectedPackage != null && selectedPackage.IsActive)
                {
                    eventPackagePrice = selectedPackage.BasePrice;
                }
            }

            decimal totalAmount = propertyStayAmount + eventPackagePrice;

            // Availability verification
            bool isAvailable = await _bookingRepo.IsSlotAvailableAsync(model.PropertyID, model.CheckInDate, model.CheckOutDate, model.BookingType, model.SlotType);
            if (!isAvailable)
            {
                ModelState.AddModelError("", "The selected dates/slots are already booked. Please pick another timeframe.");
                model.AvailableOccasions = await _propRepo.GetOccasionsAsync();
                model.AvailableEventPackages = await _eventPackageRepo.GetActivePackagesAsync();
                return View("Checkout", model);
            }

            int clientId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

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
                    ModelState.AddModelError("IdDocumentFile", $"Identity document upload error: {ex.Message}");
                    model.AvailableOccasions = await _propRepo.GetOccasionsAsync();
                    model.AvailableEventPackages = await _eventPackageRepo.GetActivePackagesAsync();
                    return View("Checkout", model);
                }
            }

            // Dynamic Commission calculation: Calculated on Property Stay Amount ONLY!
            // Event decoration fees belong to the Event Manager and are strictly segregated.
            var (rateSnapshot, platformCommission, ownerPayout) = await _commissionService.CalculateDynamicCommissionAsync(
                propertyStayAmount, property.CommissionRate, property.OwnerID);

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
                CustomEventNotes = selectedPackage != null ? model.CustomEventNotes : null,
                EventDate = selectedPackage != null ? model.CheckInDate : null,
                SetupStatus = selectedPackage != null ? "Pending" : null,
                IdempotencyKey = Guid.NewGuid().ToString("N")
            };

            int bookingId = await _bookingRepo.CreateBookingAsync(booking);

            // Redirect to Dedicated Payment Page
            return RedirectToAction("Payment", new { id = bookingId });
        }

        [Authorize]
        [HttpGet]
        public async Task<IActionResult> Payment(int id)
        {
            var booking = await _bookingRepo.GetByIdAsync(id);
            if (booking == null) return NotFound();

            var currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            if (booking.ClientID != currentUserId && !User.IsInRole("Admin"))
            {
                return Forbid();
            }

            var property = await _propRepo.GetByIdAsync(booking.PropertyID);
            var client = await _userRepo.GetByIdAsync(booking.ClientID);
            var order = await _paymentService.CreateOrderAsync(booking.TotalAmount, $"booking_{booking.BookingID}");

            var vm = new PaymentPageViewModel
            {
                BookingID = booking.BookingID,
                PropertyTitle = property?.Title ?? "Property Stay",
                Location = property?.Location ?? "",
                Amount = booking.TotalAmount,
                Currency = order.Currency,
                OrderId = order.OrderId,
                KeyId = order.KeyId,
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

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ProcessPayment(int bookingId, string orderId, string paymentMethod)
        {
            var booking = await _bookingRepo.GetByIdAsync(bookingId);
            if (booking == null) return NotFound();

            var paymentId = "pay_" + Guid.NewGuid().ToString("N")[..14];

            // Record successful payment transaction
            await _bookingRepo.RecordPaymentAsync(new Payment
            {
                BookingID = bookingId,
                GatewayTransactionID = $"{paymentMethod.ToUpper()}_{paymentId}",
                Amount = booking.TotalAmount,
                PaymentStatus = "Success"
            });

            // Update booking status to Confirmed
            await _bookingRepo.UpdateBookingStatusAsync(bookingId, "Confirmed");

            // Dispatch Notifications across Client, Owner, and Event Manager
            var client = await _userRepo.GetByIdAsync(booking.ClientID);
            var property = await _propRepo.GetByIdAsync(booking.PropertyID);

            if (client != null && property != null)
            {
                await DispatchConfirmedBookingAlertsAsync(booking, property, client, paymentId, paymentMethod);
            }

            TempData["Success"] = "Payment completed successfully! Booking confirmed.";
            return RedirectToAction("Confirmation", new { id = bookingId });
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> VerifyPayment(int bookingId, string razorpay_order_id, string razorpay_payment_id, string razorpay_signature)
        {
            var booking = await _bookingRepo.GetByIdAsync(bookingId);
            if (booking == null) return NotFound();

            bool isValid = await _paymentService.VerifyPaymentSignatureAsync(razorpay_order_id, razorpay_payment_id, razorpay_signature);
            if (!isValid)
            {
                TempData["Error"] = "Payment signature verification failed. Please try again or reach our 24/7 helpdesk.";
                return RedirectToAction("Payment", new { id = bookingId });
            }

            // Record Razorpay payment transaction
            await _bookingRepo.RecordPaymentAsync(new Payment
            {
                BookingID = bookingId,
                GatewayTransactionID = $"RAZORPAY_{razorpay_payment_id}",
                Amount = booking.TotalAmount,
                PaymentStatus = "Success"
            });

            // Update booking status to Confirmed
            await _bookingRepo.UpdateBookingStatusAsync(bookingId, "Confirmed");

            // Dispatch Notifications across Client, Owner, and Event Manager
            var client = await _userRepo.GetByIdAsync(booking.ClientID);
            var property = await _propRepo.GetByIdAsync(booking.PropertyID);

            if (client != null && property != null)
            {
                await DispatchConfirmedBookingAlertsAsync(booking, property, client, razorpay_payment_id, "Razorpay");
            }

            TempData["Success"] = "Razorpay payment verified successfully! Your booking is confirmed.";
            return RedirectToAction("Confirmation", new { id = bookingId });
        }

        private async Task DispatchConfirmedBookingAlertsAsync(Booking booking, Property property, User client, string transactionId, string paymentMethod)
        {
            decimal propertyStayGross = booking.TotalAmount - booking.EventPackagePriceSnapshot;
            decimal hostPayout = booking.OwnerPayoutAmount > 0 ? booking.OwnerPayoutAmount : Math.Round(propertyStayGross * 0.90m, 2);

            // 1. WhatsApp & Email to Guest / Client
            if (!string.IsNullOrEmpty(client.Phone))
            {
                _ = _whatsAppService.SendBookingConfirmationAsync(client.Phone, client.FullName, property.Title, booking.BookingID.ToString(), booking.TotalAmount);
            }

            string clientEmailBody = $@"
                <div style='font-family: Arial, sans-serif; line-height: 1.6; color: #333; max-width: 600px;'>
                    <h2 style='color: #059669;'>Payment Received &amp; Booking Confirmed!</h2>
                    <p>Dear {client.FullName},</p>
                    <p>Your payment of <strong>₹{booking.TotalAmount:N2}</strong> via {paymentMethod} for <strong>{property.Title}</strong> was successful.</p>
                    <div style='background: #f8fafc; border: 1px solid #e2e8f0; border-radius: 8px; padding: 15px; margin: 15px 0;'>
                        <p style='margin: 4px 0;'><strong>Booking Reference:</strong> #{booking.BookingID}</p>
                        <p style='margin: 4px 0;'><strong>Transaction ID:</strong> {transactionId}</p>
                        <p style='margin: 4px 0;'><strong>Stay Dates:</strong> {booking.CheckInDate:dd MMM yyyy} to {booking.CheckOutDate:dd MMM yyyy}</p>
                        <p style='margin: 4px 0;'><strong>Property Stay Amount:</strong> ₹{propertyStayGross:N2}</p>
                        {(booking.IsEventBooking && booking.EventPackagePriceSnapshot > 0 ? $"<p style='margin: 4px 0; color: #0284c7;'><strong>Décor Package:</strong> ₹{booking.EventPackagePriceSnapshot:N2}</p>" : "")}
                        <p style='margin: 4px 0; font-size: 1.1em; font-weight: bold;'><strong>Total Paid:</strong> ₹{booking.TotalAmount:N2}</p>
                    </div>
                    <p>Check-in details and digital access pass have been generated in your BookMyFun dashboard.</p>
                </div>";
            _ = _emailService.SendEmailAsync(client.Email, $"Booking & Payment Confirmation #{booking.BookingID} - BookMyFun", clientEmailBody);

            // 2. WhatsApp & Email to Property Owner
            var owner = await _userRepo.GetByIdAsync(property.OwnerID);
            if (owner != null)
            {
                if (!string.IsNullOrEmpty(owner.Phone))
                {
                    _ = _whatsAppService.SendOwnerBookingAlertAsync(
                        owner.Phone, 
                        owner.FullName, 
                        property.Title, 
                        booking.BookingID.ToString(), 
                        propertyStayGross, 
                        hostPayout, 
                        client.FullName, 
                        booking.CheckInDate, 
                        booking.CheckOutDate, 
                        booking.IsEventBooking, 
                        booking.EventPackagePriceSnapshot);
                }

                string ownerEmailBody = $@"
                    <div style='font-family: Arial, sans-serif; line-height: 1.6; color: #333; max-width: 600px;'>
                        <h2 style='color: #0284c7;'>New Confirmed Reservation #{booking.BookingID}</h2>
                        <p>Dear {owner.FullName},</p>
                        <p>A new guest has confirmed a reservation for your property <strong>{property.Title}</strong>.</p>
                        <div style='background: #f8fafc; border: 1px solid #e2e8f0; border-radius: 8px; padding: 15px; margin: 15px 0;'>
                            <h4 style='margin-top: 0; color: #0f172a;'>Financial Breakdown for Host</h4>
                            <p style='margin: 4px 0;'><strong>Guest Name:</strong> {client.FullName}</p>
                            <p style='margin: 4px 0;'><strong>Stay Dates:</strong> {booking.CheckInDate:dd MMM yyyy} to {booking.CheckOutDate:dd MMM yyyy}</p>
                            <p style='margin: 4px 0;'><strong>Property Stay (Host Gross):</strong> ₹{propertyStayGross:N2}</p>
                            <p style='margin: 4px 0;'><strong>Platform Fee ({booking.CommissionRateSnapshot:0.#}%):</strong> -₹{(propertyStayGross - hostPayout):N2}</p>
                            <p style='margin: 4px 0; font-size: 1.15em; font-weight: bold; color: #059669;'><strong>Your Net Host Payout:</strong> ₹{hostPayout:N2}</p>
                            {(booking.IsEventBooking && booking.EventPackagePriceSnapshot > 0 ? $@"
                            <hr style='border: none; border-top: 1px dashed #cbd5e1; margin: 12px 0;'/>
                            <p style='margin: 4px 0; color: #64748b; font-size: 0.9em;'>
                                <em>Client also added a themed décor package for ₹{booking.EventPackagePriceSnapshot:N2}. This fee is allocated directly to the Event Decorator Studio and does not impact your host payout.</em>
                            </p>" : "")}
                        </div>
                        <p>Sign in to your Host Dashboard to view guest contact details and prepare for arrival.</p>
                    </div>";
                _ = _emailService.SendEmailAsync(owner.Email, $"New Booking Alert #{booking.BookingID} - {property.Title}", ownerEmailBody);
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
                                <p style='margin: 4px 0; font-size: 1.15em; font-weight: bold; color: #b45309;'><strong>Allocated Décor Revenue:</strong> ₹{booking.EventPackagePriceSnapshot:N2}</p>
                            </div>
                            <p>Sign in to your Event Manager portal to accept this setup order and dispatch staging crew.</p>
                        </div>";
                    _ = _emailService.SendEmailAsync(eventManager.Email, $"New Event Setup Order #{booking.BookingID} - {pkgName}", emEmailBody);
                }
            }
        }

        [Authorize]
        [HttpGet]
        public async Task<IActionResult> GetCancellationQuote(int bookingId)
        {
            var booking = await _bookingRepo.GetByIdAsync(bookingId);
            if (booking == null) return NotFound(new { success = false, message = "Booking not found." });

            var currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            if (booking.ClientID != currentUserId && !User.IsInRole("Admin") && !User.IsInRole("Employee"))
            {
                return Forbid();
            }

            var quote = _refundEngine.CalculateCancellationQuote(booking);
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
            var userRole = User.FindFirstValue(ClaimTypes.Role) ?? "Client";

            if (booking.ClientID != currentUserId && !User.IsInRole("Admin") && !User.IsInRole("Employee"))
            {
                return Forbid();
            }

            if (booking.Status == "Cancelled")
            {
                TempData["Error"] = "This booking is already cancelled.";
                return RedirectToAction("Index", "Client");
            }

            var refund = await _refundEngine.ProcessCancellationAndRefundAsync(bookingId, currentUserId, userRole, reason, forceOverride);

            // Dispatch cancellation notices
            var client = await _userRepo.GetByIdAsync(booking.ClientID);
            if (client != null)
            {
                string msg = $"Your booking #{bookingId} has been cancelled. Net refund amount: ₹{refund.NetRefundAmount:N2} (Deductions: Fee ₹{refund.CancellationFeeDeducted:N2}, Gateway ₹{refund.GatewayFeeDeducted:N2}).";
                if (!string.IsNullOrEmpty(client.Phone))
                {
                    _ = _whatsAppService.SendWhatsAppMessageAsync(client.Phone, msg);
                }
                _ = _emailService.SendEmailAsync(client.Email, $"Booking Cancellation #{bookingId} - BookMyFun", $"<p>{msg}</p>");
            }

            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return Json(new { success = true, refund });
            }

            TempData["Success"] = $"Booking #{bookingId} has been cancelled successfully. Net refund: ₹{refund.NetRefundAmount:N2}";
            return RedirectToAction("Index", "Client");
        }

        [Authorize]
        [HttpGet]
        public async Task<IActionResult> GetPaymentAndRefundDetails(int bookingId)
        {
            var booking = await _bookingRepo.GetBookingByIdAsync(bookingId);
            if (booking == null) return NotFound(new { success = false, message = "Booking not found." });

            var currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var role = User.FindFirstValue(ClaimTypes.Role) ?? "Client";

            if (role == "Client" && booking.ClientID != currentUserId)
            {
                return Forbid();
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

        public async Task<IActionResult> Confirmation(int id)
        {
            var booking = await _bookingRepo.GetByIdAsync(id);
            if (booking == null) return NotFound();
            return View(booking);
        }
    }
}
