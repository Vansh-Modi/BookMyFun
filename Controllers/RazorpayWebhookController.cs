using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BookMyFun.Models.Entities;
using BookMyFun.Models.ViewModels;
using BookMyFun.Repositories;
using BookMyFun.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookMyFun.Controllers
{
    [ApiController]
    [Route("api/webhooks/razorpay")]
    [Route("webhook/razorpay")]
    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    public class RazorpayWebhookController : ControllerBase
    {
        private readonly IConfiguration _configuration;
        private readonly IConfigRepository _configRepo;
        private readonly IBookingRepository _bookingRepo;
        private readonly IRefundRepository _refundRepo;
        private readonly IWebhookEventRepository _webhookEventRepo;
        private readonly IUserRepository? _userRepo;
        private readonly IPropertyRepository? _propRepo;
        private readonly IEventPackageRepository? _eventPackageRepo;
        private readonly IEmailService? _emailService;
        private readonly IWhatsAppService? _whatsAppService;
        private readonly ILogger<RazorpayWebhookController> _logger;

        public RazorpayWebhookController(
            IConfiguration configuration,
            IConfigRepository configRepo,
            IBookingRepository bookingRepo,
            IRefundRepository refundRepo,
            IWebhookEventRepository webhookEventRepo,
            ILogger<RazorpayWebhookController> logger,
            IUserRepository? userRepo = null,
            IPropertyRepository? propRepo = null,
            IEventPackageRepository? eventPackageRepo = null,
            IEmailService? emailService = null,
            IWhatsAppService? whatsAppService = null)
        {
            _configuration = configuration;
            _configRepo = configRepo;
            _bookingRepo = bookingRepo;
            _refundRepo = refundRepo;
            _webhookEventRepo = webhookEventRepo;
            _logger = logger;
            _userRepo = userRepo;
            _propRepo = propRepo;
            _eventPackageRepo = eventPackageRepo;
            _emailService = emailService;
            _whatsAppService = whatsAppService;
        }

        [HttpPost]
        public async Task<IActionResult> HandleWebhook()
        {
            // 1. Read raw request body
            string rawBody;
            using (var reader = new StreamReader(Request.Body, Encoding.UTF8))
            {
                rawBody = await reader.ReadToEndAsync();
            }

            if (string.IsNullOrWhiteSpace(rawBody))
            {
                _logger.LogWarning("Razorpay webhook received with empty request body.");
                return BadRequest(new { error = "Request body cannot be empty." });
            }

            // 2. Resolve Razorpay Webhook Secret from configuration
            string? webhookSecret = _configuration["Razorpay:WebhookSecret"];
            if (string.IsNullOrWhiteSpace(webhookSecret))
            {
                try
                {
                    var config = await _configRepo.GetActiveConfigByProviderAsync("Razorpay");
                    if (config != null)
                    {
                        if (!string.IsNullOrWhiteSpace(config.ExtraParams))
                        {
                            try
                            {
                                using var doc = JsonDocument.Parse(config.ExtraParams);
                                if (doc.RootElement.TryGetProperty("WebhookSecret", out var wsProp) && !string.IsNullOrWhiteSpace(wsProp.GetString()))
                                {
                                    webhookSecret = wsProp.GetString();
                                }
                            }
                            catch { }
                        }

                        if (string.IsNullOrWhiteSpace(webhookSecret) && !string.IsNullOrWhiteSpace(config.ApiSecret))
                        {
                            webhookSecret = config.ApiSecret;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to load Razorpay webhook secret from database.");
                }
            }

            if (string.IsNullOrWhiteSpace(webhookSecret))
            {
                webhookSecret = _configuration["Razorpay:KeySecret"];
            }

            if (string.IsNullOrWhiteSpace(webhookSecret))
            {
                _logger.LogError("Razorpay webhook secret is not configured.");
                return BadRequest(new { error = "Webhook secret not configured." });
            }

            // 3. Verify X-Razorpay-Signature Header
            if (!Request.Headers.TryGetValue("X-Razorpay-Signature", out var signatureHeader) || 
                string.IsNullOrWhiteSpace(signatureHeader.FirstOrDefault()))
            {
                _logger.LogWarning("Razorpay webhook rejected: Missing X-Razorpay-Signature header.");
                return BadRequest(new { error = "Missing X-Razorpay-Signature header." });
            }

            string providedSignature = signatureHeader.FirstOrDefault()!.Trim().ToLowerInvariant();

            try
            {
                using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(webhookSecret.Trim()));
                var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(rawBody));
                var computedSignature = Convert.ToHexString(hash).ToLowerInvariant();

                byte[] expectedBytes = Encoding.UTF8.GetBytes(computedSignature);
                byte[] providedBytes = Encoding.UTF8.GetBytes(providedSignature);

                if (!CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes))
                {
                    _logger.LogWarning("Razorpay webhook rejected: Invalid signature.");
                    return BadRequest(new { error = "Invalid signature." });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception while validating Razorpay webhook signature.");
                return BadRequest(new { error = "Signature verification failed." });
            }

            // 4. Parse Webhook Event JSON
            JsonDocument jsonDoc;
            try
            {
                jsonDoc = JsonDocument.Parse(rawBody);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Invalid JSON payload in Razorpay webhook.");
                return BadRequest(new { error = "Invalid JSON payload." });
            }

            using (jsonDoc)
            {
                var root = jsonDoc.RootElement;

                // Extract Event Type
                string? eventType = root.TryGetProperty("event", out var evProp) ? evProp.GetString() : null;
                if (string.IsNullOrWhiteSpace(eventType))
                {
                    return BadRequest(new { error = "Missing 'event' property in payload." });
                }

                // Extract Event ID (X-Razorpay-Event-Id header or payload id)
                string? eventId = Request.Headers["X-Razorpay-Event-Id"].FirstOrDefault();
                if (string.IsNullOrWhiteSpace(eventId))
                {
                    if (root.TryGetProperty("id", out var idProp) || root.TryGetProperty("event_id", out idProp))
                    {
                        eventId = idProp.GetString();
                    }
                }

                if (string.IsNullOrWhiteSpace(eventId))
                {
                    _logger.LogWarning("Razorpay webhook rejected: Missing event identifier.");
                    return BadRequest(new { error = "Missing event identifier." });
                }

                // Secure Logging: Log ONLY event type and ID, NEVER personal customer data
                _logger.LogInformation("Processing Razorpay webhook event {EventType} with ID {EventId}", eventType, eventId);

                // 5. Idempotency Check: Ignore duplicate events
                if (await _webhookEventRepo.HasEventBeenProcessedAsync(eventId))
                {
                    _logger.LogInformation("Duplicate Razorpay webhook event {EventId} received. Skipping redundant processing.", eventId);
                    return Ok(new { status = "duplicate_ignored", eventId });
                }

                // 6. Handle Supported Events
                switch (eventType)
                {
                    case "payment.captured":
                        return await HandlePaymentCapturedAsync(root, eventId, eventType);

                    case "payment.failed":
                        return await HandlePaymentFailedAsync(root, eventId, eventType);

                    case "refund.processed":
                        return await HandleRefundProcessedAsync(root, eventId, eventType);

                    default:
                        _logger.LogInformation("Ignored unhandled Razorpay webhook event {EventType} with ID {EventId}", eventType, eventId);
                        await _webhookEventRepo.RecordEventProcessedAsync(eventId, eventType, "Ignored");
                        return Ok(new { status = "ignored", eventType });
                }
            }
        }

        private async Task<IActionResult> HandlePaymentCapturedAsync(JsonElement root, string eventId, string eventType)
        {
            if (!root.TryGetProperty("payload", out var payload) ||
                !payload.TryGetProperty("payment", out var payment) ||
                !payment.TryGetProperty("entity", out var paymentEntity))
            {
                return BadRequest(new { error = "Missing payment entity in payload." });
            }

            string? paymentId = paymentEntity.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
            if (string.IsNullOrWhiteSpace(paymentId))
            {
                return BadRequest(new { error = "Missing payment id in payload." });
            }

            long amountInPaise = paymentEntity.TryGetProperty("amount", out var amtProp) ? amtProp.GetInt64() : 0;
            decimal eventAmount = amountInPaise / 100m;

            int? bookingId = ExtractBookingId(root);
            if (!bookingId.HasValue)
            {
                _logger.LogWarning("Could not resolve booking ID from Razorpay event {EventId} (payment {PaymentId})", eventId, paymentId);
                return BadRequest(new { error = "Could not resolve booking ID from payment metadata." });
            }

            var booking = await _bookingRepo.GetByIdAsync(bookingId.Value);
            if (booking == null)
            {
                _logger.LogWarning("Booking {BookingId} not found for Razorpay event {EventId}", bookingId.Value, eventId);
                return BadRequest(new { error = $"Booking {bookingId.Value} not found." });
            }

            // Strict Amount Comparison: Compare event amount with server booking amount
            if (booking.TotalAmount != eventAmount)
            {
                _logger.LogWarning("Amount mismatch for Booking {BookingId}: expected ₹{ExpectedAmount}, received ₹{ReceivedAmount} in Razorpay event {EventId}",
                    booking.BookingID, booking.TotalAmount, eventAmount, eventId);
                await _webhookEventRepo.RecordEventProcessedAsync(eventId, eventType, "AmountMismatch", booking.BookingID);
                return BadRequest(new { error = "Amount mismatch.", expected = booking.TotalAmount, received = eventAmount });
            }

            // Update booking status exclusively from verified event
            if (booking.Status != "Confirmed")
            {
                await _bookingRepo.RecordPaymentAsync(new Payment
                {
                    BookingID = booking.BookingID,
                    GatewayTransactionID = $"RAZORPAY_{paymentId}",
                    Amount = booking.TotalAmount,
                    PaymentStatus = "Success"
                });

                await _bookingRepo.UpdateBookingStatusAsync(booking.BookingID, "Confirmed");

                // Dispatch multi-stakeholder alerts
                if (_userRepo != null && _propRepo != null)
                {
                    var client = await _userRepo.GetByIdAsync(booking.ClientID);
                    var property = await _propRepo.GetByIdAsync(booking.PropertyID);
                    var owner = property != null ? await _userRepo.GetByIdAsync(property.OwnerID) : null;
                    User? eventManager = null;
                    if (booking.IsEventBooking && booking.EventPackagePriceSnapshot > 0)
                    {
                        if (booking.DecoratorAssignedID.HasValue)
                        {
                            eventManager = await _userRepo.GetByIdAsync(booking.DecoratorAssignedID.Value);
                        }
                        if (eventManager == null && _eventPackageRepo != null)
                        {
                            var decorators = await _eventPackageRepo.GetDecoratorsAsync();
                            eventManager = decorators.FirstOrDefault();
                        }
                    }

                    if (client != null && property != null)
                    {
                        try
                        {
                            string baseUrl = Request != null && Request.Host.HasValue 
                                ? $"{Request.Scheme}://{Request.Host}" 
                                : "https://bookmyfun.in";

                            if (_emailService != null)
                            {
                                await _emailService.DispatchConfirmedBookingNotificationsAsync(booking, property, client, owner, eventManager, baseUrl);
                            }
                            await DispatchConfirmedBookingAlertsAsync(booking, property, client, paymentId, "Razorpay");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error dispatching alerts for confirmed Booking {BookingId}", booking.BookingID);
                        }
                    }
                }

                _logger.LogInformation("Booking {BookingId} marked Confirmed and paid from verified Razorpay event {EventId}", booking.BookingID, eventId);
            }

            await _webhookEventRepo.RecordEventProcessedAsync(eventId, eventType, "Success", booking.BookingID);
            return Ok(new { status = "success", bookingId = booking.BookingID, eventId });
        }

        private async Task<IActionResult> HandlePaymentFailedAsync(JsonElement root, string eventId, string eventType)
        {
            if (!root.TryGetProperty("payload", out var payload) ||
                !payload.TryGetProperty("payment", out var payment) ||
                !payment.TryGetProperty("entity", out var paymentEntity))
            {
                return BadRequest(new { error = "Missing payment entity in payload." });
            }

            string? paymentId = paymentEntity.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
            long amountInPaise = paymentEntity.TryGetProperty("amount", out var amtProp) ? amtProp.GetInt64() : 0;
            decimal eventAmount = amountInPaise / 100m;

            int? bookingId = ExtractBookingId(root);
            if (bookingId.HasValue)
            {
                var booking = await _bookingRepo.GetByIdAsync(bookingId.Value);
                if (booking != null)
                {
                    await _bookingRepo.RecordPaymentAsync(new Payment
                    {
                        BookingID = booking.BookingID,
                        GatewayTransactionID = paymentId != null ? $"RAZORPAY_{paymentId}" : "FAILED",
                        Amount = eventAmount > 0 ? eventAmount : booking.TotalAmount,
                        PaymentStatus = "Failed"
                    });

                    _logger.LogInformation("Recorded payment failure for Booking {BookingId} from Razorpay event {EventId}", booking.BookingID, eventId);
                }
            }

            await _webhookEventRepo.RecordEventProcessedAsync(eventId, eventType, "Failed", bookingId);
            return Ok(new { status = "payment_failed_recorded", eventId });
        }

        private async Task<IActionResult> HandleRefundProcessedAsync(JsonElement root, string eventId, string eventType)
        {
            string? refundId = null;

            if (root.TryGetProperty("payload", out var payload) &&
                payload.TryGetProperty("refund", out var refund) &&
                refund.TryGetProperty("entity", out var refundEntity))
            {
                refundId = refundEntity.TryGetProperty("id", out var rIdProp) ? rIdProp.GetString() : null;
            }

            int? bookingId = ExtractBookingId(root);
            if (bookingId.HasValue)
            {
                var booking = await _bookingRepo.GetByIdAsync(bookingId.Value);
                if (booking != null)
                {
                    if (booking.Status != "Cancelled")
                    {
                        await _bookingRepo.UpdateBookingStatusAsync(booking.BookingID, "Cancelled");
                    }

                    try
                    {
                        var refundRecord = await _refundRepo.GetRefundByBookingIdAsync(booking.BookingID);
                        if (refundRecord != null && refundRecord.Status != "Completed")
                        {
                            await _refundRepo.UpdateRefundStatusAsync(refundRecord.RefundID, "Completed", 0, $"Razorpay refund {refundId} processed successfully.");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to update internal refund record for Booking {BookingId}", booking.BookingID);
                    }

                    _logger.LogInformation("Processed refund for Booking {BookingId} from Razorpay event {EventId} (refund ID {RefundId})",
                        booking.BookingID, eventId, refundId);
                }
            }

            await _webhookEventRepo.RecordEventProcessedAsync(eventId, eventType, "RefundProcessed", bookingId);
            return Ok(new { status = "refund_processed", eventId });
        }

        private int? ExtractBookingId(JsonElement root)
        {
            if (root.TryGetProperty("payload", out var payload))
            {
                // 1. Try payment entity notes
                if (payload.TryGetProperty("payment", out var payment) &&
                    payment.TryGetProperty("entity", out var paymentEntity))
                {
                    if (paymentEntity.TryGetProperty("notes", out var notes))
                    {
                        if (notes.TryGetProperty("booking_id", out var bidProp) ||
                            notes.TryGetProperty("bookingId", out bidProp))
                        {
                            if (bidProp.ValueKind == JsonValueKind.Number && bidProp.TryGetInt32(out var numId))
                                return numId;
                            if (bidProp.ValueKind == JsonValueKind.String && int.TryParse(bidProp.GetString(), out var strId))
                                return strId;
                        }

                        if (notes.TryGetProperty("receipt", out var receiptProp))
                        {
                            var receipt = receiptProp.GetString();
                            if (!string.IsNullOrEmpty(receipt) && receipt.StartsWith("booking_") &&
                                int.TryParse(receipt["booking_".Length..], out var idFromReceipt))
                            {
                                return idFromReceipt;
                            }
                        }
                    }

                    if (paymentEntity.TryGetProperty("description", out var descProp))
                    {
                        var desc = descProp.GetString();
                        if (!string.IsNullOrEmpty(desc) && desc.StartsWith("booking_") &&
                            int.TryParse(desc["booking_".Length..], out var idFromDesc))
                        {
                            return idFromDesc;
                        }
                    }
                }

                // 2. Try refund entity notes
                if (payload.TryGetProperty("refund", out var refund) &&
                    refund.TryGetProperty("entity", out var refundEntity))
                {
                    if (refundEntity.TryGetProperty("notes", out var refundNotes))
                    {
                        if (refundNotes.TryGetProperty("booking_id", out var rbidProp) ||
                            refundNotes.TryGetProperty("bookingId", out rbidProp))
                        {
                            if (rbidProp.ValueKind == JsonValueKind.Number && rbidProp.TryGetInt32(out var rNumId))
                                return rNumId;
                            if (rbidProp.ValueKind == JsonValueKind.String && int.TryParse(rbidProp.GetString(), out var rStrId))
                                return rStrId;
                        }
                    }
                }
            }

            return null;
        }

        private async Task DispatchConfirmedBookingAlertsAsync(Booking booking, Property property, User client, string transactionId, string paymentMethod)
        {
            var owner = _userRepo != null ? await _userRepo.GetByIdAsync(property.OwnerID) : null;

            var receiptVm = new BookingReceiptViewModel
            {
                Booking = booking,
                Property = property,
                Client = client,
                Host = owner,
                PrimaryPayment = new Payment
                {
                    BookingID = booking.BookingID,
                    GatewayTransactionID = transactionId.StartsWith("RAZORPAY_") ? transactionId : $"RAZORPAY_{transactionId}",
                    Amount = booking.TotalAmount,
                    PaymentStatus = "Success"
                }
            };

            string baseUrl = Request != null && Request.Host.HasValue 
                ? $"{Request.Scheme}://{Request.Host}" 
                : "https://bookmyfun.in";

            string clientReceiptUrl = $"{baseUrl}/Booking/Receipt/{booking.BookingID}";
            string ownerReceiptUrl = $"{baseUrl}/Owner/BookingReceipt/{booking.BookingID}";

            // 1. Email & WhatsApp to Client
            if (_emailService != null)
            {
                await _emailService.SendBookingReceiptEmailAsync(client.Email, client.FullName, receiptVm, clientReceiptUrl);
            }

            if (!string.IsNullOrEmpty(client.Phone) && _whatsAppService != null)
            {
                await _whatsAppService.SendBookingReceiptWhatsAppAsync(
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

            // 2. Email & WhatsApp to Owner
            if (owner != null)
            {
                if (_emailService != null)
                {
                    await _emailService.SendOwnerSettlementReceiptEmailAsync(owner.Email, owner.FullName, receiptVm, ownerReceiptUrl);
                }

                if (!string.IsNullOrEmpty(owner.Phone) && _whatsAppService != null)
                {
                    await _whatsAppService.SendOwnerBookingReceiptWhatsAppAsync(
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

            // 3. WhatsApp to Event Manager if décor package was booked
            if (booking.IsEventBooking && booking.EventPackagePriceSnapshot > 0 && _eventPackageRepo != null)
            {
                var decorators = await _eventPackageRepo.GetDecoratorsAsync();
                var eventManager = decorators.FirstOrDefault();
                if (eventManager != null && !string.IsNullOrEmpty(eventManager.Phone) && _whatsAppService != null)
                {
                    string pkgName = booking.EventPackageName ?? "Selected Décor Package";
                    await _whatsAppService.SendEventManagerSetupAlertAsync(
                        eventManager.Phone,
                        eventManager.FullName,
                        property.Title,
                        booking.BookingID.ToString(),
                        pkgName,
                        booking.EventPackagePriceSnapshot,
                        booking.CheckInDate,
                        client.FullName);
                }
            }
        }
    }
}
