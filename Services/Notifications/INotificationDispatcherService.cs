using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BookMyFun.Services.Notifications
{
    public interface INotificationDispatcherService
    {
        // 1 & 2. Booking Confirmed (Stay and Event Stay)
        Task DispatchBookingConfirmedAsync(int bookingId);

        // 3. Booking Cancelled by Guest
        Task DispatchBookingCancelledAsync(int bookingId, string? reason);

        // 4. Payment Received (Receipt)
        Task DispatchReceiptAvailableAsync(int bookingId, string receiptNumber);

        // 5. Refund Processed
        Task DispatchRefundProcessedAsync(int bookingId, decimal refundAmount);

        // 6. Owner Payout Dispatched
        Task DispatchPayoutDispatchedAsync(int bookingId, int recipientUserId, decimal payoutAmount, string role);

        // 7. Event Setup Status Changed
        Task DispatchEventStatusChangedAsync(int bookingId, string newStatus);

        // 8. Check-In Reminder (day before)
        Task DispatchCheckInRemindersAsync();

        // 9 & 10. Damage Claims
        Task DispatchDamageClaimRaisedAsync(int claimId, int bookingId) => Task.CompletedTask;
        Task DispatchDamageClaimUpdateAsync(int claimId, int bookingId, string status);

        // 11. Review Submitted by Guest
        Task DispatchReviewSubmittedAsync(int reviewId, int bookingId) => Task.CompletedTask;

        // 12, 13, 14. Property Lifecycle
        Task DispatchPropertyWaitingVerificationStaffAlertAsync(int propertyId, string propertyTitle, int ownerId);
        Task DispatchPropertyApprovedAsync(int propertyId, string propertyTitle, int ownerId) => Task.CompletedTask;
        Task DispatchPropertyRejectedAsync(int propertyId, string propertyTitle, int ownerId, string reason) => Task.CompletedTask;
        Task DispatchPropertyPublishedAsync(int propertyId, string propertyTitle, int ownerId) => Task.CompletedTask;
        Task DispatchPropertyUnpublishedAsync(int propertyId, string propertyTitle, int ownerId, string reason) => Task.CompletedTask;
        Task DispatchPropertyApprovalStatusAsync(int propertyId, bool isApproved, string? notes);

        // 15 & 16. Subscription Lifecycle
        Task DispatchTrialExpiringWarningAsync(int ownerId, string planName, DateTime endDate) => Task.CompletedTask;
        Task DispatchSubscriptionExpiringWarningAsync(int ownerId, string planName, DateTime endDate) => Task.CompletedTask;
        Task DispatchSubscriptionExpiredAsync(int ownerId, string planName, int subscriptionId = 0, int unpublishedCount = 0) => Task.CompletedTask;
        Task DispatchPostPaymentPublishPromptAsync(int ownerId, int subscriptionId, string planName, List<string> unpublishedProperties, int maxListings) => Task.CompletedTask;
        Task DispatchSubscriptionActivatedAsync(int ownerId, string planName, DateTime endDate, List<string>? republishedProperties = null) => Task.CompletedTask;
        Task DispatchPlanPurchaseConfirmedAsync(int ownerId, string planName, decimal amount, bool isScheduled, DateTime startDate) => Task.CompletedTask;

        // 17, 18, 19. Event Manager Verification Lifecycle
        Task DispatchEventManagerRegisteredAsync(int managerId) => Task.CompletedTask;
        Task DispatchEventManagerVerifiedAsync(int managerId) => Task.CompletedTask;
        Task DispatchEventManagerRejectedAsync(int managerId, string reason) => Task.CompletedTask;

        // 20, 21, 22. Portfolio Moderation
        Task DispatchPortfolioItemSubmittedAsync(int portfolioId, int managerId, string title) => Task.CompletedTask;
        Task DispatchPortfolioItemApprovedAsync(int portfolioId, int managerId, string title) => Task.CompletedTask;
        Task DispatchPortfolioItemRejectedAsync(int portfolioId, int managerId, string title, string reason) => Task.CompletedTask;

        // 23, 24, 25. Guest KYC
        Task DispatchClientDocumentSubmittedAsync(int verificationId, int clientId, string docType);
        Task DispatchKycApprovedAsync(int verificationId, int clientId) => Task.CompletedTask;
        Task DispatchKycRejectedAsync(int verificationId, int clientId, string reason) => Task.CompletedTask;

        // 26. Contact Detail Warnings & Flags
        Task DispatchContactDetailAttemptFlaggedAsync(int attemptId, int senderUserId, int? recipientUserId, string channel) => Task.CompletedTask;

        // Additional Platform Dispatches
        Task DispatchDecoratorAssignedAsync(int bookingId, int decoratorUserId);
        Task DispatchPartnerLeadSubmittedAsync(int leadId, string leadName, string serviceType);
        Task DispatchBookingCreatedStaffAlertAsync(int bookingId, int clientId, int propertyId);
    }
}
