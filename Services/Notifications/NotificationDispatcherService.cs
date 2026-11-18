using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BookMyFun.Data;
using BookMyFun.Models.Entities;
using BookMyFun.Repositories;
using BookMyFun.Services.Common;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace BookMyFun.Services.Notifications
{
    public class NotificationDispatcherService : INotificationDispatcherService
    {
        private readonly INotificationOutboxRepository _outboxRepo;
        private readonly IUserNotificationRepository _userNotificationRepo;
        private readonly IUserRepository _userRepo;
        private readonly IBookingRepository _bookingRepo;
        private readonly IPropertyRepository _propRepo;
        private readonly IEventPackageRepository? _eventPackageRepo;
        private readonly DBHelper _db;
        private readonly ILogger<NotificationDispatcherService> _logger;

        public NotificationDispatcherService(
            INotificationOutboxRepository outboxRepo,
            IUserNotificationRepository userNotificationRepo,
            IUserRepository userRepo,
            IBookingRepository bookingRepo,
            IPropertyRepository propRepo,
            DBHelper db,
            ILogger<NotificationDispatcherService> logger,
            IEventPackageRepository? eventPackageRepo = null)
        {
            _outboxRepo = outboxRepo;
            _userNotificationRepo = userNotificationRepo;
            _userRepo = userRepo;
            _bookingRepo = bookingRepo;
            _propRepo = propRepo;
            _db = db;
            _logger = logger;
            _eventPackageRepo = eventPackageRepo;
        }

        // --- Helper to enqueue based on WEA / EA / A code ---
        private async Task EnqueueForUserAsync(string eventType, int entityId, int userId, string code, string title, string body, string? linkUrl = null)
        {
            if (userId <= 0 || string.IsNullOrWhiteSpace(code) || code == "—") return;

            // InApp is always sent if A, EA, or WEA
            if (code.Contains("A", StringComparison.OrdinalIgnoreCase))
            {
                await _outboxRepo.EnqueueAsync(new NotificationOutbox
                {
                    EventType = eventType,
                    EntityID = entityId,
                    UserID = userId,
                    Channel = "InApp",
                    Title = title,
                    Body = body,
                    LinkUrl = linkUrl
                });
            }

            // Email is sent if E or WEA
            if (code.Contains("E", StringComparison.OrdinalIgnoreCase))
            {
                await _outboxRepo.EnqueueAsync(new NotificationOutbox
                {
                    EventType = eventType,
                    EntityID = entityId,
                    UserID = userId,
                    Channel = "Email",
                    Title = title,
                    Body = body,
                    LinkUrl = linkUrl
                });
            }

            // WhatsApp is sent if W
            if (code.Contains("W", StringComparison.OrdinalIgnoreCase))
            {
                await _outboxRepo.EnqueueAsync(new NotificationOutbox
                {
                    EventType = eventType,
                    EntityID = entityId,
                    UserID = userId,
                    Channel = "WhatsApp",
                    Title = title,
                    Body = body,
                    LinkUrl = linkUrl
                });
            }
        }

        // --- Helper for staff notifications following matrix rules ---
        private async Task EnqueueStaffAsync(string eventType, int entityId, string employeeCode, string adminCode, string title, string body, string? linkUrl = null)
        {
            try
            {
                var employees = await _userRepo.GetUsersByRoleAsync("Employee");
                var admins = await _userRepo.GetUsersByRoleAsync("Admin");

                if (!string.Equals(employeeCode, "—", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var emp in employees)
                    {
                        await EnqueueForUserAsync(eventType, entityId, emp.UserID, employeeCode, title, body, linkUrl);
                    }
                }

                if (!string.Equals(adminCode, "—", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var adm in admins)
                    {
                        await EnqueueForUserAsync(eventType, entityId, adm.UserID, adminCode, title, body, linkUrl);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to dispatch staff notifications for {EventType} #{EntityId}", eventType, entityId);
            }
        }

        // 1 & 2. Booking confirmed (stay) & Booking confirmed with event stay
        public async Task DispatchBookingConfirmedAsync(int bookingId)
        {
            try
            {
                var booking = await _bookingRepo.GetByIdAsync(bookingId);
                if (booking == null) return;
                var property = await _propRepo.GetByIdAsync(booking.PropertyID);
                string propTitle = property?.Title ?? "BookMyFun Farm Stay";
                string dates = $"{booking.CheckInDate:dd MMM yyyy} to {booking.CheckOutDate:dd MMM yyyy}";

                bool isEventStay = booking.IsEventBooking || (booking.DecoratorAssignedID.HasValue && booking.DecoratorAssignedID.Value > 0) || (booking.EventPackageID.HasValue && booking.EventPackageID.Value > 0);
                string decoratorName = "BookMyFun Certified Décor Team";
                if (isEventStay && booking.DecoratorAssignedID.HasValue)
                {
                    var decorUser = await _userRepo.GetByIdAsync(booking.DecoratorAssignedID.Value);
                    if (decorUser != null && !string.IsNullOrWhiteSpace(decorUser.FullName))
                    {
                        decoratorName = decorUser.FullName;
                    }
                }

                // 1 / 2. Client: WEA
                string clientTitle = isEventStay
                    ? $"Booking & Celebration Confirmed: {propTitle}"
                    : $"Booking Confirmed: {propTitle}";
                string clientBody = isEventStay
                    ? $"Your reservation #{booking.BookingID} for {propTitle} ({dates}) with Stay & Celebrate décor by {decoratorName} is confirmed! Total paid: ₹{booking.TotalAmount:N0}."
                    : $"Your reservation #{booking.BookingID} for {propTitle} ({dates}) is confirmed! Total paid: ₹{booking.TotalAmount:N0}.";

                await EnqueueForUserAsync("BookingConfirmed", booking.BookingID, booking.ClientID, "WEA", clientTitle, clientBody, "/Client/Index");

                // Owner: WEA
                int ownerId = property?.OwnerID ?? 0;
                if (ownerId > 0)
                {
                    string ownerTitle = isEventStay
                        ? $"New Stay & Event Booking: {propTitle} (#{booking.BookingID})"
                        : $"New Stay Booking: {propTitle} (#{booking.BookingID})";
                    string ownerBody = $"New booking #{booking.BookingID} confirmed for {propTitle} ({dates}). Payout: ₹{booking.OwnerPayoutAmount:N0}.";
                    await EnqueueForUserAsync("BookingConfirmed", booking.BookingID, ownerId, "WEA", ownerTitle, ownerBody, "/Owner/Index");
                }

                // Event Manager: WEA only if assigned! (Otherwise "—")
                if (isEventStay && booking.DecoratorAssignedID.HasValue && booking.DecoratorAssignedID.Value > 0)
                {
                    int decoratorId = booking.DecoratorAssignedID.Value;
                    string emTitle = $"New Event Setup Assigned: Booking #{booking.BookingID}";
                    string emBody = $"Event setup assigned for {propTitle} on {booking.CheckInDate:dd MMM yyyy}. Décor payout: ₹{booking.EventManagerPayoutAmount:N0}.";
                    await EnqueueForUserAsync("BookingConfirmedWithEvent", booking.BookingID, decoratorId, "WEA", emTitle, emBody, "/EventManager/Index");
                }

                // Staff: Employee EA | Admin EA
                string staffTitle = isEventStay
                    ? $"[Action Needed] Event Booking Confirmed #{booking.BookingID}"
                    : $"[Action Needed] Stay Booking Confirmed #{booking.BookingID}";
                string staffBody = $"Booking #{booking.BookingID} for {propTitle} confirmed. Amount: ₹{booking.TotalAmount:N0}.";
                await EnqueueStaffAsync("BookingConfirmed", booking.BookingID, "EA", "EA", staffTitle, staffBody, "/Admin/ManageBookings");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error dispatching BookingConfirmed for #{BookingId}", bookingId);
            }
        }

        // 3. Booking cancelled by guest: WEA | WEA | WEA if assigned | EA | EA
        public async Task DispatchBookingCancelledAsync(int bookingId, string? reason)
        {
            try
            {
                var booking = await _bookingRepo.GetByIdAsync(bookingId);
                if (booking == null) return;
                var property = await _propRepo.GetByIdAsync(booking.PropertyID);
                string propTitle = property?.Title ?? "Property";

                // Client: WEA
                string cTitle = $"Booking Cancelled: #{booking.BookingID}";
                string cBody = $"Your reservation #{booking.BookingID} for {propTitle} has been cancelled. Reason: {reason ?? "Requested by guest"}.";
                await EnqueueForUserAsync("BookingCancelled", booking.BookingID, booking.ClientID, "WEA", cTitle, cBody, "/Client/Index");

                // Owner: WEA
                int ownerId = property?.OwnerID ?? 0;
                if (ownerId > 0)
                {
                    string oTitle = $"Booking Cancelled by Guest: #{booking.BookingID}";
                    string oBody = $"Reservation #{booking.BookingID} for {propTitle} was cancelled by guest. Reason: {reason ?? "Guest cancellation"}.";
                    await EnqueueForUserAsync("BookingCancelled", booking.BookingID, ownerId, "WEA", oTitle, oBody, "/Owner/Index");
                }

                // Event Manager: WEA if assigned
                if (booking.DecoratorAssignedID.HasValue && booking.DecoratorAssignedID.Value > 0)
                {
                    string emTitle = $"Event Assignment Cancelled: Booking #{booking.BookingID}";
                    string emBody = $"Event setup assignment for {propTitle} has been cancelled due to booking cancellation.";
                    await EnqueueForUserAsync("BookingCancelled", booking.BookingID, booking.DecoratorAssignedID.Value, "WEA", emTitle, emBody, "/EventManager/Index");
                }

                // Employee: EA | Admin: EA
                string staffTitle = $"[Action Needed] Booking Cancelled #{booking.BookingID}";
                string staffBody = $"Booking #{booking.BookingID} cancelled by guest. Reason: {reason}. Review refund status.";
                await EnqueueStaffAsync("BookingCancelled", booking.BookingID, "EA", "EA", staffTitle, staffBody, "/Admin/RefundsQueue");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error dispatching BookingCancelled for #{BookingId}", bookingId);
            }
        }

        // 4. Payment received: WEA (receipt) | WEA | WEA if event | EA | EA
        public async Task DispatchReceiptAvailableAsync(int bookingId, string receiptNumber)
        {
            try
            {
                var booking = await _bookingRepo.GetByIdAsync(bookingId);
                if (booking == null) return;
                var property = await _propRepo.GetByIdAsync(booking.PropertyID);
                string propTitle = property?.Title ?? "Property";

                // Client: WEA
                string cTitle = $"Payment Received: Receipt #{receiptNumber}";
                string cBody = $"Payment of ₹{booking.TotalAmount:N0} received for booking #{booking.BookingID} ({propTitle}). Receipt #{receiptNumber}.";
                await EnqueueForUserAsync("PaymentReceived", booking.BookingID, booking.ClientID, "WEA", cTitle, cBody, $"/Booking/Receipt/{booking.BookingID}");

                // Owner: WEA
                int ownerId = property?.OwnerID ?? 0;
                if (ownerId > 0)
                {
                    string oTitle = $"Payment Received: Booking #{booking.BookingID}";
                    string oBody = $"Payment confirmed for booking #{booking.BookingID} on {propTitle}. Expected payout: ₹{booking.OwnerPayoutAmount:N0}.";
                    await EnqueueForUserAsync("PaymentReceived", booking.BookingID, ownerId, "WEA", oTitle, oBody, "/Owner/Index");
                }

                // Event Manager: WEA if event
                bool isEvent = booking.IsEventBooking || (booking.DecoratorAssignedID.HasValue && booking.DecoratorAssignedID.Value > 0) || (booking.EventPackageID.HasValue && booking.EventPackageID.Value > 0);
                if (isEvent && booking.DecoratorAssignedID.HasValue && booking.DecoratorAssignedID.Value > 0)
                {
                    string emTitle = $"Event Payment Received: Booking #{booking.BookingID}";
                    string emBody = $"Event décor payment secured for booking #{booking.BookingID}. Setup payout: ₹{booking.EventManagerPayoutAmount:N0}.";
                    await EnqueueForUserAsync("PaymentReceived", booking.BookingID, booking.DecoratorAssignedID.Value, "WEA", emTitle, emBody, "/EventManager/Index");
                }

                // Employee: EA | Admin: EA
                string sTitle = $"[Action Needed] Payment Received #{booking.BookingID}";
                string sBody = $"Payment receipt #{receiptNumber} generated for booking #{booking.BookingID} (₹{booking.TotalAmount:N0}).";
                await EnqueueStaffAsync("PaymentReceived", booking.BookingID, "EA", "EA", sTitle, sBody, "/Admin/ManageBookings");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error dispatching ReceiptAvailable for #{BookingId}", bookingId);
            }
        }

        // 5. Refund processed: WEA | WEA | WEA if event | EA | EA
        public async Task DispatchRefundProcessedAsync(int bookingId, decimal refundAmount)
        {
            try
            {
                var booking = await _bookingRepo.GetByIdAsync(bookingId);
                if (booking == null) return;
                var property = await _propRepo.GetByIdAsync(booking.PropertyID);
                string propTitle = property?.Title ?? "Property";

                // Client: WEA
                string cTitle = $"Refund Processed: ₹{refundAmount:N0}";
                string cBody = $"Refund of ₹{refundAmount:N0} processed for booking #{booking.BookingID} ({propTitle}). Credit will reflect in 5-7 business days.";
                await EnqueueForUserAsync("RefundProcessed", booking.BookingID, booking.ClientID, "WEA", cTitle, cBody, "/Client/Index");

                // Owner: WEA
                int ownerId = property?.OwnerID ?? 0;
                if (ownerId > 0)
                {
                    string oTitle = $"Booking Refund Processed: #{booking.BookingID}";
                    string oBody = $"A refund of ₹{refundAmount:N0} was processed for booking #{booking.BookingID} on {propTitle}.";
                    await EnqueueForUserAsync("RefundProcessed", booking.BookingID, ownerId, "WEA", oTitle, oBody, "/Owner/Index");
                }

                // Event Manager: WEA if event
                if (booking.DecoratorAssignedID.HasValue && booking.DecoratorAssignedID.Value > 0)
                {
                    string emTitle = $"Event Booking Refund Processed: #{booking.BookingID}";
                    string emBody = $"Refund processed for cancelled event booking #{booking.BookingID}.";
                    await EnqueueForUserAsync("RefundProcessed", booking.BookingID, booking.DecoratorAssignedID.Value, "WEA", emTitle, emBody, "/EventManager/Index");
                }

                // Employee: EA | Admin: EA
                string sTitle = $"[Action Needed] Refund Processed #{booking.BookingID}";
                string sBody = $"Refund of ₹{refundAmount:N0} recorded for booking #{booking.BookingID}.";
                await EnqueueStaffAsync("RefundProcessed", booking.BookingID, "EA", "EA", sTitle, sBody, "/Admin/RefundsQueue");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error dispatching RefundProcessed for #{BookingId}", bookingId);
            }
        }

        // 6. Owner payout dispatched: — | WEA | WEA if event | A | EA
        public async Task DispatchPayoutDispatchedAsync(int bookingId, int recipientUserId, decimal payoutAmount, string role)
        {
            try
            {
                bool isEvent = string.Equals(role, "EventManager", StringComparison.OrdinalIgnoreCase);
                string title = isEvent ? $"Décor Payout Dispatched: ₹{payoutAmount:N0}" : $"Host Payout Dispatched: ₹{payoutAmount:N0}";
                string body = $"Payout of ₹{payoutAmount:N0} for booking #{bookingId} has been dispatched to your bank account.";

                await EnqueueForUserAsync("PayoutDispatched", bookingId, recipientUserId, "WEA", title, body, isEvent ? "/EventManager/Index" : "/Owner/Index");

                // Employee: A | Admin: EA
                string sTitle = $"Payout Dispatched #{bookingId}";
                string sBody = $"Payout of ₹{payoutAmount:N0} dispatched to User #{recipientUserId} ({role}) for booking #{bookingId}.";
                await EnqueueStaffAsync("PayoutDispatched", bookingId, "A", "EA", sTitle, sBody, "/Admin/Reports");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error dispatching PayoutDispatched for #{BookingId}", bookingId);
            }
        }

        // 7. Event setup status changed: WEA | WEA | WEA | A | A
        public async Task DispatchEventStatusChangedAsync(int bookingId, string newStatus)
        {
            try
            {
                var booking = await _bookingRepo.GetByIdAsync(bookingId);
                if (booking == null) return;
                var property = await _propRepo.GetByIdAsync(booking.PropertyID);
                string propTitle = property?.Title ?? "Property";

                string title = $"Event Décor Setup: {newStatus}";
                string body = $"Event setup status for {propTitle} (Booking #{booking.BookingID}) is now: {newStatus}.";

                // Client: WEA
                await EnqueueForUserAsync("EventStatusChanged", booking.BookingID, booking.ClientID, "WEA", title, body, "/Client/Index");

                // Owner: WEA
                int ownerId = property?.OwnerID ?? 0;
                if (ownerId > 0)
                {
                    await EnqueueForUserAsync("EventStatusChanged", booking.BookingID, ownerId, "WEA", title, body, "/Owner/Index");
                }

                // Event Manager: WEA
                if (booking.DecoratorAssignedID.HasValue && booking.DecoratorAssignedID.Value > 0)
                {
                    await EnqueueForUserAsync("EventStatusChanged", booking.BookingID, booking.DecoratorAssignedID.Value, "WEA", title, body, "/EventManager/Index");
                }

                // Employee: A | Admin: A
                await EnqueueStaffAsync("EventStatusChanged", booking.BookingID, "A", "A", title, body, "/Admin/ManageBookings");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error dispatching EventStatusChanged for #{BookingId}", bookingId);
            }
        }

        // 8. Check-in reminder, the day before: WEA | WEA | — | — | —
        public async Task DispatchCheckInRemindersAsync()
        {
            try
            {
                const string sql = @"
                    SELECT b.BookingID, b.ClientID, b.PropertyID, p.Title AS PropertyTitle, b.CheckInDate, b.CheckOutDate
                    FROM Bookings b
                    JOIN Properties p ON b.PropertyID = p.PropertyID
                    WHERE b.Status = 'Confirmed'
                      AND CAST(b.CheckInDate AS DATE) = CAST(DATEADD(DAY, 1, GETDATE()) AS DATE);";

                var upcoming = await _db.ExecuteReaderListAsync(sql, reader => new
                {
                    BookingID = reader.GetInt32(reader.GetOrdinal("BookingID")),
                    ClientID = reader.GetInt32(reader.GetOrdinal("ClientID")),
                    PropertyID = reader.GetInt32(reader.GetOrdinal("PropertyID")),
                    PropertyTitle = reader.GetString(reader.GetOrdinal("PropertyTitle")),
                    CheckInDate = reader.GetDateTime(reader.GetOrdinal("CheckInDate")),
                    CheckOutDate = reader.GetDateTime(reader.GetOrdinal("CheckOutDate"))
                });

                foreach (var b in upcoming)
                {
                    var prop = await _propRepo.GetByIdAsync(b.PropertyID);

                    // Client: WEA
                    string cTitle = $"Check-in Tomorrow: {b.PropertyTitle}";
                    string cBody = $"Get ready! Your stay at {b.PropertyTitle} starts tomorrow, {b.CheckInDate:dd MMM yyyy}.";
                    await EnqueueForUserAsync("CheckInReminder", b.BookingID, b.ClientID, "WEA", cTitle, cBody, "/Client/Index");

                    // Owner: WEA
                    int ownerId = prop?.OwnerID ?? 0;
                    if (ownerId > 0)
                    {
                        string oTitle = $"Guest Arriving Tomorrow: {b.PropertyTitle}";
                        string oBody = $"Reminder: Guest check-in scheduled tomorrow for reservation #{b.BookingID}.";
                        await EnqueueForUserAsync("CheckInReminder", b.BookingID, ownerId, "WEA", oTitle, oBody, "/Owner/Index");
                    }
                }
            }
            catch (Exception ex)
            {
                if (BackgroundServiceResilienceHelper.IsMissingTableException(ex) || BackgroundServiceResilienceHelper.IsDatabaseUnreachableException(ex))
                {
                    throw;
                }
                _logger.LogError(ex, "Error during DispatchCheckInRemindersAsync run.");
            }
        }

        // 9. Damage claim raised: A | WEA | — | EA | EA
        public async Task DispatchDamageClaimRaisedAsync(int claimId, int bookingId)
        {
            try
            {
                var booking = await _bookingRepo.GetByIdAsync(bookingId);
                if (booking == null) return;
                var property = await _propRepo.GetByIdAsync(booking.PropertyID);
                int ownerId = property?.OwnerID ?? 0;

                // Client: A
                string cTitle = $"Damage Claim Raised: Booking #{bookingId}";
                string cBody = $"Host has submitted a security deposit claim for booking #{bookingId}. Staff review is underway.";
                await EnqueueForUserAsync("DamageClaimRaised", claimId, booking.ClientID, "A", cTitle, cBody, "/Client/Index");

                // Owner: WEA
                if (ownerId > 0)
                {
                    string oTitle = $"Damage Claim Submitted: #{claimId}";
                    string oBody = $"Your damage claim #{claimId} for booking #{bookingId} has been submitted for platform review.";
                    await EnqueueForUserAsync("DamageClaimRaised", claimId, ownerId, "WEA", oTitle, oBody, "/Owner/DamageClaims");
                }

                // Staff: Employee EA | Admin EA ("action needed")
                string sTitle = $"[Action Needed] Damage Claim Raised #{claimId}";
                string sBody = $"New damage claim #{claimId} submitted for booking #{bookingId}. Requires adjudication.";
                await EnqueueStaffAsync("DamageClaimRaised", claimId, "EA", "EA", sTitle, sBody, "/Admin/DamageClaims");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error dispatching DamageClaimRaised for claim #{ClaimId}", claimId);
            }
        }

        // 10. Damage claim resolved: WEA | WEA | — | A | EA
        public async Task DispatchDamageClaimUpdateAsync(int claimId, int bookingId, string status)
        {
            try
            {
                var booking = await _bookingRepo.GetByIdAsync(bookingId);
                if (booking == null) return;
                var property = await _propRepo.GetByIdAsync(booking.PropertyID);
                int ownerId = property?.OwnerID ?? 0;

                string title = $"Damage Claim Resolved: {status}";
                string body = $"Claim #{claimId} for booking #{bookingId} has been resolved: {status}.";

                // Client: WEA
                await EnqueueForUserAsync("DamageClaimResolved", claimId, booking.ClientID, "WEA", title, body, "/Client/Index");

                // Owner: WEA
                if (ownerId > 0)
                {
                    await EnqueueForUserAsync("DamageClaimResolved", claimId, ownerId, "WEA", title, body, "/Owner/DamageClaims");
                }

                // Employee: A | Admin: EA
                await EnqueueStaffAsync("DamageClaimResolved", claimId, "A", "EA", title, body, "/Admin/DamageClaims");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error dispatching DamageClaimResolved for claim #{ClaimId}", claimId);
            }
        }

        // 11. Review submitted by guest: A | WEA | WEA if event | A | A
        public async Task DispatchReviewSubmittedAsync(int reviewId, int bookingId)
        {
            try
            {
                var booking = await _bookingRepo.GetByIdAsync(bookingId);
                if (booking == null) return;
                var property = await _propRepo.GetByIdAsync(booking.PropertyID);
                int ownerId = property?.OwnerID ?? 0;

                // Client: A
                await EnqueueForUserAsync("ReviewSubmitted", reviewId, booking.ClientID, "A",
                    "Review Submitted", "Thank you for sharing your review with BookMyFun!", "/Client/Index");

                // Owner: WEA
                if (ownerId > 0)
                {
                    await EnqueueForUserAsync("ReviewSubmitted", reviewId, ownerId, "WEA",
                        $"New Review on {property?.Title}", $"A guest posted a review for booking #{bookingId}.", "/Owner/Index");
                }

                // Event Manager: WEA if event
                if (booking.DecoratorAssignedID.HasValue && booking.DecoratorAssignedID.Value > 0)
                {
                    await EnqueueForUserAsync("ReviewSubmitted", reviewId, booking.DecoratorAssignedID.Value, "WEA",
                        "New Review Received", $"Guest posted a review for event booking #{bookingId}.", "/EventManager/Index");
                }

                // Employee: A | Admin: A
                await EnqueueStaffAsync("ReviewSubmitted", reviewId, "A", "A",
                    "New Review Posted", $"New review #{reviewId} posted on booking #{bookingId}.", "/Admin/Index");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error dispatching ReviewSubmitted for review #{ReviewId}", reviewId);
            }
        }

        // 12. New property submitted for approval: — | WEA | — | EA | EA
        public async Task DispatchPropertyWaitingVerificationStaffAlertAsync(int propertyId, string propertyTitle, int ownerId)
        {
            try
            {
                // Owner: WEA
                string oTitle = $"Property Submitted for Verification: {propertyTitle}";
                string oBody = $"Your property '{propertyTitle}' was submitted and is in the review queue.";
                await EnqueueForUserAsync("PropertySubmitted", propertyId, ownerId, "WEA", oTitle, oBody, "/Owner/Index");

                // Employee: EA | Admin: EA ("action needed")
                string sTitle = $"[Action Needed] New Property Submitted #{propertyId}";
                string sBody = $"Property #{propertyId} ('{propertyTitle}') submitted by Owner #{ownerId}. Review required.";
                await EnqueueStaffAsync("PropertySubmitted", propertyId, "EA", "EA", sTitle, sBody, "/Admin/ManageProperties");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error dispatching PropertyWaitingVerification for property #{PropertyId}", propertyId);
            }
        }

        // 13. Property approved: — | WEA | — | A | A
        public async Task DispatchPropertyApprovedAsync(int propertyId, string propertyTitle, int ownerId)
        {
            try
            {
                // Owner: WEA
                string oTitle = $"Property Approved: {propertyTitle}";
                string oBody = $"Congratulations! '{propertyTitle}' is approved and ready for publishing.";
                await EnqueueForUserAsync("PropertyApproved", propertyId, ownerId, "WEA", oTitle, oBody, "/Owner/Index");

                // Employee: A | Admin: A
                await EnqueueStaffAsync("PropertyApproved", propertyId, "A", "A",
                    $"Property Approved: {propertyTitle}", $"Property #{propertyId} ({propertyTitle}) approved.", "/Admin/ManageProperties");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error dispatching PropertyApproved for #{PropertyId}", propertyId);
            }
        }

        // 14. Property rejected: — | WEA | — | A | A
        public async Task DispatchPropertyRejectedAsync(int propertyId, string propertyTitle, int ownerId, string reason)
        {
            try
            {
                // Owner: WEA
                string oTitle = $"Property Listing Rejected: {propertyTitle}";
                string oBody = $"Property '{propertyTitle}' was rejected. Reason: {reason}.";
                await EnqueueForUserAsync("PropertyRejected", propertyId, ownerId, "WEA", oTitle, oBody, "/Owner/Index");

                // Employee: A | Admin: A
                await EnqueueStaffAsync("PropertyRejected", propertyId, "A", "A",
                    $"Property Rejected: {propertyTitle}", $"Property #{propertyId} rejected. Reason: {reason}", "/Admin/ManageProperties");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error dispatching PropertyRejected for #{PropertyId}", propertyId);
            }
        }

        public async Task DispatchPropertyPublishedAsync(int propertyId, string propertyTitle, int ownerId)
        {
            await EnqueueForUserAsync("PropertyPublished", propertyId, ownerId, "WEA",
                $"Property Live: {propertyTitle}", $"Your listing '{propertyTitle}' is now live on BookMyFun!", $"/Properties/Details/{propertyId}");
            await EnqueueStaffAsync("PropertyPublished", propertyId, "A", "A",
                $"Property Published: {propertyTitle}", $"Owner published property #{propertyId}.", "/Admin/ManageProperties");
        }

        public async Task DispatchPropertyUnpublishedAsync(int propertyId, string propertyTitle, int ownerId, string reason)
        {
            await EnqueueForUserAsync("PropertyUnpublished", propertyId, ownerId, "WEA",
                $"Property Unpublished: {propertyTitle}", $"Your listing '{propertyTitle}' was unpublished. Reason: {reason}", "/Owner/Index");
            await EnqueueStaffAsync("PropertyUnpublished", propertyId, "A", "A",
                $"Property Unpublished: {propertyTitle}", $"Property #{propertyId} was unpublished.", "/Admin/ManageProperties");
        }

        public Task DispatchPropertyApprovalStatusAsync(int propertyId, bool isApproved, string? notes)
        {
            return isApproved
                ? DispatchPropertyApprovedAsync(propertyId, "Property", 0)
                : DispatchPropertyRejectedAsync(propertyId, "Property", 0, notes ?? "Did not meet platform guidelines.");
        }

        // 15. Owner subscription expiring in 7 days: — | WEA | — | — | —
        public async Task DispatchSubscriptionExpiringWarningAsync(int ownerId, string planName, DateTime endDate)
        {
            int dateEntityId = int.Parse(endDate.ToString("yyyyMMdd"));
            string title = $"Subscription Expiring in 7 Days: {planName}";
            string body = $"Your '{planName}' plan expires on {endDate:dd MMM yyyy}. Renew now to keep listings active.";
            await EnqueueForUserAsync("SubscriptionExpiringWarning", dateEntityId, ownerId, "WEA", title, body, "/Owner/Subscriptions");
        }

        public Task DispatchTrialExpiringWarningAsync(int ownerId, string planName, DateTime endDate)
            => DispatchSubscriptionExpiringWarningAsync(ownerId, planName, endDate);

        // 16. Owner subscription expired: — | WEA | — | A | EA
        public async Task DispatchSubscriptionExpiredAsync(int ownerId, string planName, int subscriptionId = 0, int unpublishedCount = 0)
        {
            int entityId = subscriptionId > 0 ? subscriptionId : int.Parse(DateTime.UtcNow.ToString("yyyyMMdd"));
            string title = $"Subscription Expired: {planName}";
            string body = unpublishedCount == 1
                ? $"1 property has been unpublished from public search because your plan ended. Your property is still safely stored in your account, and you can republish it once a plan is active."
                : $"{unpublishedCount} properties have been unpublished from public search because your plan ended. Your properties are still safely stored in your account, and you can choose which to republish once a plan is active.";

            await EnqueueForUserAsync("SubscriptionExpired", entityId, ownerId, "WEA", title, body, "/Owner/Index#needs-attention");

            // Employee: A | Admin: EA
            await EnqueueStaffAsync("SubscriptionExpired", entityId, "A", "EA",
                $"Owner Subscription Expired: User #{ownerId}", $"Owner #{ownerId} subscription '{planName}' expired. {unpublishedCount} properties unpublished.", "/Admin/ManageOwners");
        }

        public async Task DispatchPostPaymentPublishPromptAsync(int ownerId, int subscriptionId, string planName, List<string> unpublishedProperties, int maxListings)
        {
            int entityId = subscriptionId > 0 ? subscriptionId : int.Parse(DateTime.UtcNow.ToString("yyyyMMdd"));
            string title = $"Plan Activated: {planName}";
            string body;
            if (unpublishedProperties != null && unpublishedProperties.Any())
            {
                var names = string.Join(", ", unpublishedProperties.Take(3));
                var extra = unpublishedProperties.Count > 3 ? $" and {unpublishedProperties.Count - 3} more" : "";
                body = $"Your {planName} is now active (allows up to {maxListings} live listings). You have {unpublishedProperties.Count} unpublished property(ies) ({names}{extra}) still in your account. Please go to your dashboard to choose which properties to publish.";
            }
            else
            {
                body = $"Your subscription '{planName}' is now active (allows up to {maxListings} live listings). You can now publish or create listings from your dashboard.";
            }

            await EnqueueForUserAsync("SubscriptionPaid_ChooseProperties", entityId, ownerId, "WEA", title, body, "/Owner/Index");
        }

        public async Task DispatchSubscriptionActivatedAsync(int ownerId, string planName, DateTime endDate, List<string>? republishedProperties = null)
        {
            int dateEntityId = int.Parse(endDate.ToString("yyyyMMdd"));
            string title = $"Subscription Active: {planName}";
            string body = $"Your subscription '{planName}' is active until {endDate:dd MMM yyyy}.";
            await EnqueueForUserAsync("SubscriptionActivated", dateEntityId, ownerId, "WEA", title, body, "/Owner/Index");
        }

        public async Task DispatchPlanPurchaseConfirmedAsync(int ownerId, string planName, decimal amount, bool isScheduled, DateTime startDate)
        {
            int dateEntityId = int.Parse(startDate.ToString("yyyyMMdd"));
            string title = $"Plan Confirmed: {planName}";
            string body = $"Payment of ₹{amount:N0} confirmed for '{planName}'.";
            await EnqueueForUserAsync("PlanPurchaseConfirmed", dateEntityId, ownerId, "WEA", title, body, "/Owner/Subscriptions");
        }

        // 17. Event manager registered: — | — | WEA | EA | EA
        public async Task DispatchEventManagerRegisteredAsync(int managerId)
        {
            var mgr = await _userRepo.GetByIdAsync(managerId);
            string name = mgr?.FullName ?? "Event Manager";

            // Event Manager: WEA
            await EnqueueForUserAsync("EventManagerRegistered", managerId, managerId, "WEA",
                "Welcome to BookMyFun Events", "Your event manager profile has been registered and submitted for review.", "/EventManager/Index");

            // Employee: EA | Admin: EA ("action needed")
            string sTitle = $"[Action Needed] New Event Manager Registered #{managerId}";
            string sBody = $"Event manager #{managerId} ({name}) registered. Review credentials & portfolio.";
            await EnqueueStaffAsync("EventManagerRegistered", managerId, "EA", "EA", sTitle, sBody, "/Admin/EventManagerVerifications");
        }

        // 18. Event manager verified: — | — | WEA | A | A
        public async Task DispatchEventManagerVerifiedAsync(int managerId)
        {
            var mgr = await _userRepo.GetByIdAsync(managerId);
            string name = mgr?.FullName ?? "Event Manager";

            await EnqueueForUserAsync("EventManagerVerified", managerId, managerId, "WEA",
                "Profile Verified!", "Congratulations! Your event manager profile is verified and live for checkout booking.", "/EventManager/Index");

            await EnqueueStaffAsync("EventManagerVerified", managerId, "A", "A",
                $"Event Manager Verified: {name}", $"Manager #{managerId} ({name}) was verified.", "/Admin/EventManagerVerifications");
        }

        // 19. Event manager rejected: — | — | WEA | A | A
        public async Task DispatchEventManagerRejectedAsync(int managerId, string reason)
        {
            var mgr = await _userRepo.GetByIdAsync(managerId);
            string name = mgr?.FullName ?? "Event Manager";

            await EnqueueForUserAsync("EventManagerRejected", managerId, managerId, "WEA",
                "Verification Notice", $"Your event manager verification was rejected. Reason: {reason}", "/EventManager/Index");

            await EnqueueStaffAsync("EventManagerRejected", managerId, "A", "A",
                $"Event Manager Rejected: {name}", $"Manager #{managerId} rejected. Reason: {reason}", "/Admin/EventManagerVerifications");
        }

        // 20. Portfolio item submitted: — | — | A | EA | EA
        public async Task DispatchPortfolioItemSubmittedAsync(int portfolioId, int managerId, string title)
        {
            // Event Manager: A
            await EnqueueForUserAsync("PortfolioSubmitted", portfolioId, managerId, "A",
                "Portfolio Item Submitted", $"Showcase item '{title}' submitted for moderation.", "/EventManager/Index");

            // Employee: EA | Admin: EA ("action needed")
            await EnqueueStaffAsync("PortfolioSubmitted", portfolioId, "EA", "EA",
                $"[Action Needed] Portfolio Item Submitted #{portfolioId}", $"Manager #{managerId} submitted portfolio item '{title}'. Review required.", "/Admin/EventManagerVerifications");
        }

        // 21. Portfolio item approved: — | — | WEA | A | A
        public async Task DispatchPortfolioItemApprovedAsync(int portfolioId, int managerId, string title)
        {
            await EnqueueForUserAsync("PortfolioApproved", portfolioId, managerId, "WEA",
                "Portfolio Item Approved", $"Your showcase item '{title}' has been approved and is now public!", "/EventManager/Index");

            await EnqueueStaffAsync("PortfolioApproved", portfolioId, "A", "A",
                $"Portfolio Item Approved: {title}", $"Portfolio #{portfolioId} approved for public showcase.", "/Admin/EventManagerVerifications");
        }

        // 22. Portfolio item rejected: — | — | WEA | A | A
        public async Task DispatchPortfolioItemRejectedAsync(int portfolioId, int managerId, string title, string reason)
        {
            await EnqueueForUserAsync("PortfolioRejected", portfolioId, managerId, "WEA",
                "Portfolio Item Moderated", $"Your showcase item '{title}' was rejected. Reason: {reason}", "/EventManager/Index");

            await EnqueueStaffAsync("PortfolioRejected", portfolioId, "A", "A",
                $"Portfolio Item Rejected: {title}", $"Portfolio #{portfolioId} rejected. Reason: {reason}", "/Admin/EventManagerVerifications");
        }

        // 23. KYC submitted: A | — | — | EA | EA
        public async Task DispatchClientDocumentSubmittedAsync(int verificationId, int clientId, string docType)
        {
            // Client: A
            await EnqueueForUserAsync("KycSubmitted", verificationId, clientId, "A",
                "KYC Document Submitted", $"Your government {docType} was submitted and is pending verification.", "/Client/Index");

            // Employee: EA | Admin: EA ("action needed")
            await EnqueueStaffAsync("KycSubmitted", verificationId, "EA", "EA",
                $"[Action Needed] Guest KYC Submitted #{verificationId}", $"Guest #{clientId} submitted {docType} for identity verification.", "/Employee/IdVerifications");
        }

        // 24. KYC approved: WEA | — | — | A | A
        public async Task DispatchKycApprovedAsync(int verificationId, int clientId)
        {
            await EnqueueForUserAsync("KycApproved", verificationId, clientId, "WEA",
                "Identity Verified!", "Your government ID has been verified. You can now make confirmed bookings.", "/Client/Index");

            await EnqueueStaffAsync("KycApproved", verificationId, "A", "A",
                "Guest KYC Approved", $"Guest #{clientId} identity verified (KYC #{verificationId}).", "/Employee/IdVerifications");
        }

        // 25. KYC rejected: WEA | — | — | A | A
        public async Task DispatchKycRejectedAsync(int verificationId, int clientId, string reason)
        {
            await EnqueueForUserAsync("KycRejected", verificationId, clientId, "WEA",
                "Identity Verification Notice", $"Your ID verification was not approved. Reason: {reason}. Please resubmit valid proof.", "/Client/Index");

            await EnqueueStaffAsync("KycRejected", verificationId, "A", "A",
                "Guest KYC Rejected", $"Guest #{clientId} verification rejected. Reason: {reason}", "/Employee/IdVerifications");
        }

        // 26. Contact detail shared in chat or inquiry:
        // Client: A (warning) | Owner: A (warning) | Event Manager: A (warning) | Employee: — | Admin: A (flagged)
        // Rule: "Contact-detail warnings are in-app only, and employees never receive them."
        public async Task DispatchContactDetailAttemptFlaggedAsync(int attemptId, int senderUserId, int? recipientUserId, string channel)
        {
            try
            {
                string warnTitle = "Policy Warning: Contact Information Sharing";
                string warnBody = "Sharing phone numbers, email addresses, or off-platform contact details violates BookMyFun Terms of Service. All messaging must remain on-platform.";

                // Sender: A (warning)
                if (senderUserId > 0)
                {
                    await EnqueueForUserAsync("ContactDetailWarning", attemptId, senderUserId, "A", warnTitle, warnBody, null);
                }

                // Recipient (if applicable): A (warning)
                if (recipientUserId.HasValue && recipientUserId.Value > 0)
                {
                    await EnqueueForUserAsync("ContactDetailWarning", attemptId, recipientUserId.Value, "A", warnTitle, warnBody, null);
                }

                // Admin: A (flagged alert). Employees never receive them!
                var admins = await _userRepo.GetUsersByRoleAsync("Admin");
                foreach (var adm in admins)
                {
                    await EnqueueForUserAsync("ContactDetailFlagged", attemptId, adm.UserID, "A",
                        $"Contact Attempt Flagged #{attemptId}",
                        $"User #{senderUserId} attempted to share off-platform contact details via {channel}.",
                        "/Admin/FlaggedContacts");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error dispatching ContactDetailAttemptFlagged for attempt #{AttemptId}", attemptId);
            }
        }

        public async Task DispatchDecoratorAssignedAsync(int bookingId, int decoratorUserId)
        {
            var booking = await _bookingRepo.GetByIdAsync(bookingId);
            var property = booking != null ? await _propRepo.GetByIdAsync(booking.PropertyID) : null;
            string title = $"Event Setup Assigned: Booking #{bookingId}";
            string body = $"You have been assigned to event décor on {property?.Title ?? "Property"}.";
            await EnqueueForUserAsync("DecoratorAssigned", bookingId, decoratorUserId, "WEA", title, body, "/EventManager/Index");
        }

        public async Task DispatchPartnerLeadSubmittedAsync(int leadId, string leadName, string serviceType)
        {
            await EnqueueStaffAsync("PartnerLeadSubmitted", leadId, "EA", "EA",
                $"[Action Needed] New Partner Lead: {serviceType}",
                $"Partner application received from {leadName} ({serviceType}).",
                "/Admin/PartnerLeads");
        }

        public async Task DispatchBookingCreatedStaffAlertAsync(int bookingId, int clientId, int propertyId)
        {
            var property = await _propRepo.GetByIdAsync(propertyId);
            await EnqueueStaffAsync("BookingCreated", bookingId, "EA", "EA",
                $"New Booking Created #{bookingId}",
                $"Reservation #{bookingId} created by Client #{clientId} for {property?.Title ?? "Property"}.",
                "/Admin/ManageBookings");
        }
    }
}
