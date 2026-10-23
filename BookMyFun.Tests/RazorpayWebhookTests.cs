using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BookMyFun.Controllers;
using BookMyFun.Models.Entities;
using BookMyFun.Repositories;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BookMyFun.Tests
{
    public class RazorpayWebhookTests
    {
        private const string TestWebhookSecret = "whsec_test_secret_key_abcdef123456";

        #region In-Memory Test Doubles

        private class MockWebhookEventRepo : IWebhookEventRepository
        {
            public readonly HashSet<string> ProcessedEvents = new();
            public readonly Dictionary<string, string> EventStatuses = new();

            public Task<bool> HasEventBeenProcessedAsync(string eventId)
            {
                return Task.FromResult(ProcessedEvents.Contains(eventId));
            }

            public Task RecordEventProcessedAsync(string eventId, string eventType, string status, int? bookingId = null)
            {
                ProcessedEvents.Add(eventId);
                EventStatuses[eventId] = status;
                return Task.CompletedTask;
            }
        }

        private class MockBookingRepo : IBookingRepository
        {
            public readonly Dictionary<int, Booking> Bookings = new();
            public readonly List<Payment> Payments = new();

            public Task<Booking?> GetByIdAsync(int bookingId)
            {
                Bookings.TryGetValue(bookingId, out var b);
                return Task.FromResult(b);
            }

            public Task<Booking?> GetBookingByIdAsync(int bookingId) => GetByIdAsync(bookingId);

            public Task<bool> UpdateBookingStatusAsync(int bookingId, string status)
            {
                if (Bookings.TryGetValue(bookingId, out var b))
                {
                    b.Status = status;
                    return Task.FromResult(true);
                }
                return Task.FromResult(false);
            }

            public Task<int> RecordPaymentAsync(Payment payment)
            {
                payment.PaymentID = Payments.Count + 1;
                Payments.Add(payment);
                return Task.FromResult(payment.PaymentID);
            }

            public Task<List<Payment>> GetPaymentsByBookingIdAsync(int bookingId)
            {
                return Task.FromResult(Payments.Where(p => p.BookingID == bookingId).ToList());
            }

            public Task<Booking?> GetByIdempotencyKeyAsync(string idempotencyKey) => Task.FromResult<Booking?>(null);
            public Task<List<Booking>> GetByClientIdAsync(int clientId) => Task.FromResult(new List<Booking>());
            public Task<List<Booking>> GetByOwnerIdAsync(int ownerId) => Task.FromResult(new List<Booking>());
            public Task<List<Booking>> GetAllBookingsAsync() => Task.FromResult(Bookings.Values.ToList());
            public Task<int> CreateBookingAsync(Booking booking)
            {
                booking.BookingID = Bookings.Count + 1;
                Bookings[booking.BookingID] = booking;
                return Task.FromResult(booking.BookingID);
            }
            public Task<bool> CancelBookingAsync(int bookingId) => UpdateBookingStatusAsync(bookingId, "Cancelled");
            public Task<bool> UpdateSecurityDepositStatusAsync(int bookingId, string status) => Task.FromResult(true);
            public Task<bool> IsSlotAvailableAsync(int propertyId, DateTime checkInDate, DateTime checkOutDate, string bookingType, string? slotType) => Task.FromResult(true);
            public Task<List<CustomerReview>> GetReviewsByPropertyIdAsync(int propertyId) => Task.FromResult(new List<CustomerReview>());
            public Task<int> AddReviewAsync(CustomerReview review) => Task.FromResult(1);
            public Task<bool> UpdateBookingEventCommissionAsync(int bookingId, decimal newEventCommissionRate) => Task.FromResult(true);
        }

        private class MockRefundRepo : IRefundRepository
        {
            public readonly Dictionary<int, BookingRefund> Refunds = new();

            public Task<int> InsertRefundAsync(BookingRefund refund)
            {
                refund.RefundID = Refunds.Count + 1;
                Refunds[refund.BookingID] = refund;
                return Task.FromResult(refund.RefundID);
            }

            public Task<BookingRefund?> GetRefundByIdAsync(int refundId)
            {
                return Task.FromResult(Refunds.Values.FirstOrDefault(r => r.RefundID == refundId));
            }

            public Task<BookingRefund?> GetRefundByBookingIdAsync(int bookingId)
            {
                Refunds.TryGetValue(bookingId, out var r);
                return Task.FromResult(r);
            }

            public Task<List<BookingRefund>> GetRefundsAsync(string? status = null) => Task.FromResult(Refunds.Values.ToList());
            public Task<bool> UpdateRefundStatusAsync(int refundId, string status, int processedByUserId, string? notes)
            {
                var r = Refunds.Values.FirstOrDefault(x => x.RefundID == refundId);
                if (r != null)
                {
                    r.Status = status;
                    return Task.FromResult(true);
                }
                return Task.FromResult(false);
            }
        }

        private class MockConfigRepo : IConfigRepository
        {
            public Task<ApiConfiguration?> GetActiveConfigByProviderAsync(string provider) => Task.FromResult<ApiConfiguration?>(null);
            public Task<List<ApiConfiguration>> GetAllConfigsAsync() => Task.FromResult(new List<ApiConfiguration>());
            public Task<bool> SaveOrUpdateConfigAsync(ApiConfiguration config) => Task.FromResult(true);
            public Task<string?> GetSettingValueAsync(string key) => Task.FromResult<string?>(null);
            public Task<bool> SetSettingValueAsync(string key, string value, bool isEncrypted = false) => Task.FromResult(true);
            public Task<List<SiteSetting>> GetAllSettingsAsync() => Task.FromResult(new List<SiteSetting>());
            public Task LogAuditAsync(int performedByUserId, string action, string entityType, int? entityId, string? details) => Task.CompletedTask;
        }

        #endregion

        private static (RazorpayWebhookController controller, MockBookingRepo bookingRepo, MockWebhookEventRepo webhookRepo, MockRefundRepo refundRepo) CreateController()
        {
            var config = new ConfigurationManager();
            config["Razorpay:WebhookSecret"] = TestWebhookSecret;

            var bookingRepo = new MockBookingRepo();
            var refundRepo = new MockRefundRepo();
            var webhookRepo = new MockWebhookEventRepo();
            var configRepo = new MockConfigRepo();
            var logger = NullLogger<RazorpayWebhookController>.Instance;

            var controller = new RazorpayWebhookController(
                config,
                configRepo,
                bookingRepo,
                refundRepo,
                webhookRepo,
                logger);

            return (controller, bookingRepo, webhookRepo, refundRepo);
        }

        private static string ComputeHmacSha256(string payload, string secret)
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret.Trim()));
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        private static void SetupHttpContext(RazorpayWebhookController controller, string rawBody, string? signature, string? eventId = null)
        {
            var httpContext = new DefaultHttpContext();
            var stream = new MemoryStream(Encoding.UTF8.GetBytes(rawBody));
            httpContext.Request.Body = stream;
            httpContext.Request.ContentLength = stream.Length;
            httpContext.Request.ContentType = "application/json";

            if (!string.IsNullOrEmpty(signature))
            {
                httpContext.Request.Headers["X-Razorpay-Signature"] = signature;
            }

            if (!string.IsNullOrEmpty(eventId))
            {
                httpContext.Request.Headers["X-Razorpay-Event-Id"] = eventId;
            }

            controller.ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            };
        }

        [Fact]
        public async Task Webhook_ValidSignature_ProcessesPaymentCaptured_UpdatesBookingStatusToConfirmed()
        {
            // Arrange
            var (controller, bookingRepo, webhookRepo, _) = CreateController();
            int bookingId = 101;
            decimal bookingAmount = 15000.00m;
            bookingRepo.Bookings[bookingId] = new Booking
            {
                BookingID = bookingId,
                PropertyID = 1,
                ClientID = 5,
                TotalAmount = bookingAmount,
                Status = "Pending"
            };

            string eventId = "evt_capture_valid_999";
            string paymentId = "pay_test_capture_123";

            var payloadObj = new
            {
                entity = "event",
                account_id = "acc_test123",
                @event = "payment.captured",
                id = eventId,
                payload = new
                {
                    payment = new
                    {
                        entity = new
                        {
                            id = paymentId,
                            amount = 1500000, // 15,000 INR in paise
                            currency = "INR",
                            status = "captured",
                            notes = new Dictionary<string, string>
                            {
                                { "booking_id", bookingId.ToString() }
                            }
                        }
                    }
                }
            };

            string rawJson = JsonSerializer.Serialize(payloadObj);
            string validSignature = ComputeHmacSha256(rawJson, TestWebhookSecret);
            SetupHttpContext(controller, rawJson, validSignature, eventId);

            // Act
            var result = await controller.HandleWebhook();

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.NotNull(okResult.Value);

            // Verify booking state was transitioned to Confirmed exclusively by verified webhook
            var booking = await bookingRepo.GetByIdAsync(bookingId);
            Assert.NotNull(booking);
            Assert.Equal("Confirmed", booking.Status);

            // Verify payment record
            var payments = await bookingRepo.GetPaymentsByBookingIdAsync(bookingId);
            Assert.Single(payments);
            Assert.Equal("Success", payments[0].PaymentStatus);
            Assert.Equal(bookingAmount, payments[0].Amount);
            Assert.Equal($"RAZORPAY_{paymentId}", payments[0].GatewayTransactionID);

            // Verify event idempotency record
            bool isRecorded = await webhookRepo.HasEventBeenProcessedAsync(eventId);
            Assert.True(isRecorded);
        }

        [Fact]
        public async Task Webhook_InvalidSignature_RejectsWithBadRequest400()
        {
            // Arrange
            var (controller, bookingRepo, webhookRepo, _) = CreateController();
            int bookingId = 102;
            bookingRepo.Bookings[bookingId] = new Booking
            {
                BookingID = bookingId,
                TotalAmount = 5000.00m,
                Status = "Pending"
            };

            string eventId = "evt_invalid_sig_400";
            var payloadObj = new
            {
                entity = "event",
                @event = "payment.captured",
                id = eventId,
                payload = new
                {
                    payment = new
                    {
                        entity = new
                        {
                            id = "pay_bad_sig",
                            amount = 500000,
                            notes = new Dictionary<string, string> { { "booking_id", "102" } }
                        }
                    }
                }
            };

            string rawJson = JsonSerializer.Serialize(payloadObj);
            string forgedSignature = "bad_signature_digest_that_does_not_match_hmac_sha256";
            SetupHttpContext(controller, rawJson, forgedSignature, eventId);

            // Act
            var result = await controller.HandleWebhook();

            // Assert
            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.NotNull(badRequest.Value);

            // Verify booking remains unchanged in Pending
            var booking = await bookingRepo.GetByIdAsync(bookingId);
            Assert.NotNull(booking);
            Assert.Equal("Pending", booking.Status);
            Assert.Empty(bookingRepo.Payments);

            // Verify event is NOT marked processed
            bool isRecorded = await webhookRepo.HasEventBeenProcessedAsync(eventId);
            Assert.False(isRecorded);
        }

        [Fact]
        public async Task Webhook_DuplicateEvent_ReturnsOkAndSkipsDuplicateProcessing()
        {
            // Arrange
            var (controller, bookingRepo, webhookRepo, _) = CreateController();
            int bookingId = 103;
            bookingRepo.Bookings[bookingId] = new Booking
            {
                BookingID = bookingId,
                TotalAmount = 10000.00m,
                Status = "Pending"
            };

            string eventId = "evt_duplicate_idempotency_111";

            // Mark event as already processed
            await webhookRepo.RecordEventProcessedAsync(eventId, "payment.captured", "Success", bookingId);

            var payloadObj = new
            {
                entity = "event",
                @event = "payment.captured",
                id = eventId,
                payload = new
                {
                    payment = new
                    {
                        entity = new
                        {
                            id = "pay_dup_999",
                            amount = 1000000,
                            notes = new Dictionary<string, string> { { "booking_id", "103" } }
                        }
                    }
                }
            };

            string rawJson = JsonSerializer.Serialize(payloadObj);
            string validSignature = ComputeHmacSha256(rawJson, TestWebhookSecret);
            SetupHttpContext(controller, rawJson, validSignature, eventId);

            // Act
            var result = await controller.HandleWebhook();

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.NotNull(okResult.Value);

            // Booking remains Pending because duplicate event was safely skipped
            var booking = await bookingRepo.GetByIdAsync(bookingId);
            Assert.NotNull(booking);
            Assert.Equal("Pending", booking.Status);
            Assert.Empty(bookingRepo.Payments);
        }

        [Fact]
        public async Task Webhook_AmountMismatch_RejectsWithBadRequestAndLogsMismatch()
        {
            // Arrange
            var (controller, bookingRepo, webhookRepo, _) = CreateController();
            int bookingId = 104;
            decimal legitimateBookingAmount = 25000.00m;
            bookingRepo.Bookings[bookingId] = new Booking
            {
                BookingID = bookingId,
                TotalAmount = legitimateBookingAmount,
                Status = "Pending"
            };

            string eventId = "evt_amount_mismatch_555";
            // Razorpay event reports 10,000 INR (1,000,000 paise) instead of expected 25,000 INR
            var payloadObj = new
            {
                entity = "event",
                @event = "payment.captured",
                id = eventId,
                payload = new
                {
                    payment = new
                    {
                        entity = new
                        {
                            id = "pay_tampered_amount",
                            amount = 1000000, // 10,000 INR
                            notes = new Dictionary<string, string> { { "booking_id", bookingId.ToString() } }
                        }
                    }
                }
            };

            string rawJson = JsonSerializer.Serialize(payloadObj);
            string validSignature = ComputeHmacSha256(rawJson, TestWebhookSecret);
            SetupHttpContext(controller, rawJson, validSignature, eventId);

            // Act
            var result = await controller.HandleWebhook();

            // Assert
            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.NotNull(badRequest.Value);

            // Verify booking was NOT marked Confirmed or paid
            var booking = await bookingRepo.GetByIdAsync(bookingId);
            Assert.NotNull(booking);
            Assert.Equal("Pending", booking.Status);
            Assert.Empty(bookingRepo.Payments);

            // Verify event recorded with AmountMismatch status
            Assert.True(webhookRepo.EventStatuses.ContainsKey(eventId));
            Assert.Equal("AmountMismatch", webhookRepo.EventStatuses[eventId]);
        }

        [Fact]
        public async Task Webhook_RefundProcessed_CancelsBookingAndUpdatesRefundRecord()
        {
            // Arrange
            var (controller, bookingRepo, webhookRepo, refundRepo) = CreateController();
            int bookingId = 105;
            bookingRepo.Bookings[bookingId] = new Booking
            {
                BookingID = bookingId,
                TotalAmount = 8000.00m,
                Status = "Confirmed"
            };

            refundRepo.Refunds[bookingId] = new BookingRefund
            {
                RefundID = 1,
                BookingID = bookingId,
                NetRefundAmount = 7200.00m,
                Status = "Initiated"
            };

            string eventId = "evt_refund_processed_777";
            var payloadObj = new
            {
                entity = "event",
                @event = "refund.processed",
                id = eventId,
                payload = new
                {
                    refund = new
                    {
                        entity = new
                        {
                            id = "rfnd_test_abc123",
                            payment_id = "pay_orig_789",
                            amount = 720000,
                            notes = new Dictionary<string, string> { { "booking_id", "105" } }
                        }
                    }
                }
            };

            string rawJson = JsonSerializer.Serialize(payloadObj);
            string validSignature = ComputeHmacSha256(rawJson, TestWebhookSecret);
            SetupHttpContext(controller, rawJson, validSignature, eventId);

            // Act
            var result = await controller.HandleWebhook();

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.NotNull(okResult.Value);

            var booking = await bookingRepo.GetByIdAsync(bookingId);
            Assert.NotNull(booking);
            Assert.Equal("Cancelled", booking.Status);

            var refund = await refundRepo.GetRefundByBookingIdAsync(bookingId);
            Assert.NotNull(refund);
            Assert.Equal("Completed", refund.Status);
        }
    }
}
