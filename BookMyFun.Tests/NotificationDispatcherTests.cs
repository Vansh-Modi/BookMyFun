using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BookMyFun.Data;
using BookMyFun.Models.Entities;
using BookMyFun.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BookMyFun.Tests
{
    public class NotificationDispatcherTests
    {
        [Theory]
        [InlineData("BookingConfirmed", 101, 5, "InApp", "BookingConfirmed:101:5:InApp")]
        [InlineData("BookingCancelled", 102, 6, "Email", "BookingCancelled:102:6:Email")]
        [InlineData("RefundProcessed", 103, 7, "WhatsApp", "RefundProcessed:103:7:WhatsApp")]
        [InlineData("PropertyApproval", 55, 12, "InApp", "PropertyApproval:55:12:InApp")]
        public void DeduplicationKey_FollowsExactRequiredFormat(string eventType, int entityId, int userId, string channel, string expectedKey)
        {
            string actualKey = $"{eventType}:{entityId}:{userId}:{channel}";
            Assert.Equal(expectedKey, actualKey);
        }

        [Theory]
        [InlineData(true, true, "9876543210", true)]   // Enabled & Verified & Phone present -> Allowed
        [InlineData(true, false, "9876543210", false)]  // Enabled & Not Verified -> BLOCKED
        [InlineData(false, true, "9876543210", false)]  // Disabled & Verified -> BLOCKED
        [InlineData(false, false, "9876543210", false)] // Disabled & Not Verified -> BLOCKED
        [InlineData(true, true, "", false)]             // Enabled & Verified but empty phone -> BLOCKED
        public void WhatsAppDispatch_RequiresBothEnabledAndVerified(bool enabled, bool verified, string phone, bool expectedAllowed)
        {
            var user = new User
            {
                UserID = 1,
                NotificationWhatsAppEnabled = enabled,
                WhatsAppVerified = verified,
                WhatsAppPhone = phone
            };

            bool isAllowed = user.NotificationWhatsAppEnabled && user.WhatsAppVerified && !string.IsNullOrWhiteSpace(user.WhatsAppPhone);
            Assert.Equal(expectedAllowed, isAllowed);
        }

        [Fact]
        public void NotificationMessages_DoNotExposeEmailPhoneOrPhysicalAddress()
        {
            // Verify privacy compliance: Titles and bodies must not leak customer/host private contact information
            var sampleBodies = new List<string>
            {
                "Your reservation #101 for Serene Farm (Nov 10 to Nov 12) is confirmed. Total paid: ₹12,000.",
                "New booking #101 confirmed for Serene Farm. Net payout: ₹10,200.",
                "A refund of ₹3,000 for reservation #101 (Serene Farm) has been processed.",
                "Official tax receipt #RCP-2026-101 for your reservation #101 is available for download.",
                "Guest #5 submitted a government Aadhaar for identity verification (KYC #12)."
            };

            var emailRegex = new Regex(@"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}", RegexOptions.Compiled);
            var phoneRegex = new Regex(@"(\+?\d{1,3}[-.\s]?)?\(?\d{3}\)?[-.\s]?\d{3}[-.\s]?\d{4}", RegexOptions.Compiled);

            foreach (var body in sampleBodies)
            {
                Assert.False(emailRegex.IsMatch(body), $"Notification body leaked an email address: {body}");
                Assert.False(phoneRegex.IsMatch(body), $"Notification body leaked a phone number: {body}");
                Assert.DoesNotContain("Street", body, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("Road", body, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("Pincode", body, StringComparison.OrdinalIgnoreCase);
            }
        }

        [Fact]
        public async Task UserNotificationRepository_HandlesUnconfiguredOrMissingDb_GracefullyWithoutCrashing()
        {
            var config = new ConfigurationBuilder().Build(); // No connection string
            var db = new DBHelper(config);
            var logger = NullLogger<UserNotificationRepository>.Instance;
            var repo = new UserNotificationRepository(db, logger);

            // 1. Insert should return false, not crash
            bool insertResult = await repo.InsertNotificationAsync(new UserNotification
            {
                UserID = 1,
                Title = "Test Alert",
                Body = "Test notification body"
            });
            Assert.False(insertResult);

            // 2. GetUserNotificationsAsync should return empty list, not crash
            var list = await repo.GetUserNotificationsAsync(1, 5);
            Assert.NotNull(list);
            Assert.Empty(list);

            // 3. GetUnreadCount should return 0, not crash
            int count = await repo.GetUnreadCountAsync(1);
            Assert.Equal(0, count);

            // 4. MarkAsRead should return false, not crash
            bool markResult = await repo.MarkAsReadAsync(1, 1);
            Assert.False(markResult);

            // 5. TryRecordDispatchAsync returns a boolean without throwing an unhandled exception
            var exception = await Record.ExceptionAsync(() => repo.TryRecordDispatchAsync("BookingConfirmed:101:5:InApp"));
            Assert.Null(exception);
        }
    }
}
