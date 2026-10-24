using BookMyFun.Models.Entities;
using BookMyFun.Models.ViewModels;
using BookMyFun.Repositories;
using BookMyFun.Services;
using BookMyFun.Services.Exceptions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BookMyFun.Tests
{
    public class RefundEngineAndCancellationTests
    {
        private class TestBookingRepo : IBookingRepository
        {
            public Dictionary<int, Booking> Bookings = new();
            public Dictionary<int, List<Payment>> Payments = new();
            public bool CancelCalled = false;
            public decimal LastUpdatedOwnerPayout = -1;

            public Task<Booking?> GetByIdAsync(int bookingId)
            {
                Bookings.TryGetValue(bookingId, out var b);
                return Task.FromResult(b);
            }

            public Task<Booking?> GetBookingByIdAsync(int bookingId) => GetByIdAsync(bookingId);
            public Task<Booking?> GetByIdempotencyKeyAsync(string idempotencyKey) => Task.FromResult<Booking?>(null);
            public Task<List<Booking>> GetByClientIdAsync(int clientId) => Task.FromResult(new List<Booking>());
            public Task<List<Booking>> GetByOwnerIdAsync(int ownerId) => Task.FromResult(new List<Booking>());
            public Task<List<Booking>> GetAllBookingsAsync() => Task.FromResult(new List<Booking>());
            public Task<int> CreateBookingAsync(Booking booking) => Task.FromResult(1);
            public Task<bool> UpdateBookingStatusAsync(int bookingId, string status) => Task.FromResult(true);

            public Task<bool> CancelBookingAsync(int bookingId)
            {
                CancelCalled = true;
                if (Bookings.TryGetValue(bookingId, out var b))
                {
                    b.Status = "Cancelled";
                }
                return Task.FromResult(true);
            }

            public Task<bool> UpdateOwnerPayoutAsync(int bookingId, decimal newOwnerPayout)
            {
                LastUpdatedOwnerPayout = newOwnerPayout;
                if (Bookings.TryGetValue(bookingId, out var b))
                {
                    b.OwnerPayoutAmount = newOwnerPayout;
                }
                return Task.FromResult(true);
            }

            public Task<bool> UpdateSecurityDepositStatusAsync(int bookingId, string status) => Task.FromResult(true);
            public Task<bool> IsSlotAvailableAsync(int propertyId, DateTime checkInDate, DateTime checkOutDate, string bookingType, string? slotType) => Task.FromResult(true);
            public Task<int> RecordPaymentAsync(Payment payment) => Task.FromResult(1);

            public Task<List<Payment>> GetPaymentsByBookingIdAsync(int bookingId)
            {
                if (Payments.TryGetValue(bookingId, out var p)) return Task.FromResult(p);
                return Task.FromResult(new List<Payment>());
            }

            public Task<List<CustomerReview>> GetReviewsByPropertyIdAsync(int propertyId) => Task.FromResult(new List<CustomerReview>());
            public Task<int> AddReviewAsync(CustomerReview review) => Task.FromResult(1);
            public Task<bool> UpdateBookingEventCommissionAsync(int bookingId, decimal newEventCommissionRate) => Task.FromResult(true);
        }

        private class TestPropertyRepo : IPropertyRepository
        {
            public Dictionary<int, Property> Properties = new();

            public Task<Property?> GetByIdAsync(int propertyId)
            {
                Properties.TryGetValue(propertyId, out var p);
                return Task.FromResult(p);
            }

            public Task<List<Property>> GetAllPropertiesAsync(bool onlyApproved = false) => Task.FromResult(Properties.Values.ToList());
            public Task<List<Property>> GetPropertiesByOwnerIdAsync(int ownerId) => Task.FromResult(Properties.Values.Where(p => p.OwnerID == ownerId).ToList());
            public Task<List<Property>> SearchPropertiesAsync(string? location, int? propertyTypeId, int? occasionId, decimal? minPrice, decimal? maxPrice, int? guests) => Task.FromResult(new List<Property>());
            public Task<int> CreatePropertyAsync(Property property) => Task.FromResult(1);
            public Task<bool> UpdatePropertyAsync(Property property) => Task.FromResult(true);
            public Task<bool> DeletePropertyAsync(int propertyId) => PermanentlyDeletePropertyAsync(propertyId);
            public Task<bool> PermanentlyDeletePropertyAsync(int propertyId) => Task.FromResult(Properties.Remove(propertyId));
            public Task<bool> DeactivatePropertyAsync(int propertyId, bool deactivate = true) => Task.FromResult(true);
            public Task<bool> ApprovePropertyAsync(int propertyId, bool isApproved, string? targetStatus = null) => Task.FromResult(true);
            public Task<List<PropertyType>> GetPropertyTypesAsync() => Task.FromResult(new List<PropertyType>());
            public Task<List<Occasion>> GetOccasionsAsync() => Task.FromResult(new List<Occasion>());
            public Task<List<AmenityMaster>> GetAmenitiesMasterAsync() => Task.FromResult(new List<AmenityMaster>());
            public Task<bool> UpdatePropertyMapUrlAsync(int propertyId, int ownerId, string? mapUrl) => Task.FromResult(true);
            public Task<int> GetOrCreatePropertyTypeByNameAsync(string name) => Task.FromResult(1);
            public Task<bool> UpdatePropertyCategoryAsync(int propertyId, int ownerId, int propertyTypeId) => Task.FromResult(true);
            public Task<PropertyEventCommission?> GetEventCommissionByPropertyIdAsync(int propertyId) => Task.FromResult<PropertyEventCommission?>(null);
            public Task<bool> SetPropertyEventCommissionAsync(int propertyId, decimal? commissionRate) => Task.FromResult(true);
            public Task<List<PropertyEventCommission>> GetAllPropertyEventCommissionsAsync() => Task.FromResult(new List<PropertyEventCommission>());
            public Task<bool> ResetPropertyEventCommissionAsync(int propertyId) => Task.FromResult(true);
            public Task<bool> SetPropertyOccasionsAsync(int propertyId, List<int> occasionIds) => Task.FromResult(true);
            public Task<List<int>> GetPropertyOccasionIdsAsync(int propertyId) => Task.FromResult(new List<int>());
            public Task<int> AddPropertyImageAsync(int propertyId, string imageUrl, bool isPrimary) => Task.FromResult(1);
            public Task<List<PropertyImage>> GetPropertyImagesAsync(int propertyId) => Task.FromResult(new List<PropertyImage>());
            public Task<int> AddPropertyMediaAsync(PropertyMedia media) => Task.FromResult(1);
            public Task<List<PropertyMedia>> GetPropertyMediaAsync(int propertyId) => Task.FromResult(new List<PropertyMedia>());
            public Task<bool> DeletePropertyMediaAsync(int mediaId) => Task.FromResult(true);
            public Task<List<AmenityMaster>> GetAllAmenitiesAsync() => Task.FromResult(new List<AmenityMaster>());
            public Task<List<int>> GetPropertyAmenityIdsAsync(int propertyId) => Task.FromResult(new List<int>());
            public Task<List<AmenityMaster>> GetPropertyAmenitiesAsync(int propertyId) => Task.FromResult(new List<AmenityMaster>());
            public Task<bool> SetPropertyAmenitiesAsync(int propertyId, List<int> amenityIds) => Task.FromResult(true);
            public Task<int> GetOrCreateAmenityByNameAsync(string name) => Task.FromResult(1);
            public Task<int> GetOrCreateOccasionByNameAsync(string name) => Task.FromResult(1);
            public Task<List<PropertyEventCommission>> GetPropertyEventCommissionsAsync(int propertyId) => Task.FromResult(new List<PropertyEventCommission>());
            public Task<decimal?> GetPropertyEventCommissionRateAsync(int propertyId, int packageId) => Task.FromResult<decimal?>(null);
            public Task<bool> SetPropertyEventCommissionsAsync(int propertyId, List<(int packageId, decimal commissionRate)> commissions) => Task.FromResult(true);
        }

        private class TestRefundRepo : IRefundRepository
        {
            public List<BookingRefund> Refunds = new();
            private int _nextId = 1;

            public Task<int> InsertRefundAsync(BookingRefund refund)
            {
                refund.RefundID = _nextId++;
                Refunds.Add(refund);
                return Task.FromResult(refund.RefundID);
            }

            public Task<BookingRefund?> GetRefundByIdAsync(int refundId)
            {
                var r = Refunds.FirstOrDefault(x => x.RefundID == refundId);
                return Task.FromResult(r);
            }

            public Task<BookingRefund?> GetRefundByBookingIdAsync(int bookingId)
            {
                var r = Refunds.FirstOrDefault(x => x.BookingID == bookingId);
                return Task.FromResult(r);
            }

            public Task<List<BookingRefund>> GetRefundsAsync(string? status = null)
            {
                var list = string.IsNullOrEmpty(status)
                    ? Refunds
                    : Refunds.Where(r => r.Status.Equals(status, StringComparison.OrdinalIgnoreCase)).ToList();
                return Task.FromResult(list);
            }

            public Task<bool> UpdateRefundStatusAsync(int refundId, string status, int processedByUserId, string? notes)
            {
                var r = Refunds.FirstOrDefault(x => x.RefundID == refundId);
                if (r != null)
                {
                    r.Status = status;
                    r.ProcessedByUserID = processedByUserId;
                    r.ProcessedAt = DateTime.UtcNow;
                    if (!string.IsNullOrEmpty(notes))
                    {
                        r.RefundReason = $"{r.RefundReason} [Note: {notes}]";
                    }
                    return Task.FromResult(true);
                }
                return Task.FromResult(false);
            }
        }

        private class TestConfigRepo : IConfigRepository
        {
            public Dictionary<string, string> Settings = new();

            public Task<ApiConfiguration?> GetActiveConfigByProviderAsync(string provider) => Task.FromResult<ApiConfiguration?>(null);
            public Task<List<ApiConfiguration>> GetAllConfigsAsync() => Task.FromResult(new List<ApiConfiguration>());
            public Task<bool> SaveOrUpdateConfigAsync(ApiConfiguration config) => Task.FromResult(true);

            public Task<string?> GetSettingValueAsync(string key)
            {
                Settings.TryGetValue(key, out var val);
                return Task.FromResult<string?>(val);
            }

            public Task<bool> SetSettingValueAsync(string key, string value, bool isEncrypted = false)
            {
                Settings[key] = value;
                return Task.FromResult(true);
            }

            public Task<List<SiteSetting>> GetAllSettingsAsync() => Task.FromResult(new List<SiteSetting>());
            public Task LogAuditAsync(int performedByUserId, string action, string entityType, int? entityId, string? details) => Task.CompletedTask;
        }

        private class TestUserRepo : IUserRepository
        {
            public Dictionary<int, User> Users = new();
            public Task<User?> GetByIdAsync(int userId)
            {
                Users.TryGetValue(userId, out var u);
                return Task.FromResult(u);
            }
            public Task<User?> GetByEmailAsync(string email) => Task.FromResult(Users.Values.FirstOrDefault(u => u.Email.Equals(email, StringComparison.OrdinalIgnoreCase)));
            public Task<User?> GetByPhoneAsync(string phone) => Task.FromResult(Users.Values.FirstOrDefault(u => u.Phone == phone));
            public Task<User?> GetByAuthProviderKeyAsync(string provider, string providerKey) => Task.FromResult<User?>(null);
            public Task<int> CreateUserAsync(User user) => Task.FromResult(1);
            public Task<bool> UpdateUserAsync(User user) => Task.FromResult(true);
            public Task<bool> DeleteUserAsync(int userId) => Task.FromResult(true);
            public Task<List<User>> GetUsersByRoleAsync(string role) => Task.FromResult(Users.Values.Where(u => u.Role == role).ToList());
            public Task<List<User>> GetAllUsersAsync() => Task.FromResult(Users.Values.ToList());
            public Task<bool> ResetPasswordAsync(string email, string newPasswordHash) => Task.FromResult(true);
            public Task<bool> UpdateUserTierAndCommissionAsync(int userId, string subscriptionTier, decimal? customCommissionRate) => Task.FromResult(true);
            public Task<bool> UpdateUserCommissionRateAsync(int userId, decimal? commissionRate) => Task.FromResult(true);
            public Task<bool> LinkAuthProviderAsync(int userId, string provider, string providerKey, string? profilePic = null) => Task.FromResult(true);
        }

        private class TestFinancialRepo : IFinancialRepository
        {
            public List<FinancialEntry> Entries = new();
            public Task<int> AddEntryAsync(FinancialEntry entry)
            {
                Entries.Add(entry);
                return Task.FromResult(Entries.Count);
            }
            public Task<List<FinancialEntry>> GetAllEntriesAsync() => Task.FromResult(Entries);
            public Task<(decimal totalIncome, decimal totalExpense, decimal netProfit)> GetFinancialSummaryAsync() => Task.FromResult((0m, 0m, 0m));
            public Task<List<(string Month, decimal Income, decimal Expense)>> GetMonthlyFinancialTrendsAsync() => Task.FromResult(new List<(string, decimal, decimal)>());
        }

        private class TestEmailService : IEmailService
        {
            public int CustomerNotifCount = 0;
            public int OwnerNotifCount = 0;
            public decimal LastCustomerRefundAmount = 0;
            public int LastRuleApplied = 0;

            public Task<bool> SendEmailAsync(string toEmail, string subject, string bodyHtml) => Task.FromResult(true);
            public Task<bool> SendOtpEmailAsync(string toEmail, string otpCode, string purpose, string? recipientName = null) => Task.FromResult(true);
            public Task EnqueueEmailAsync(string toEmail, string subject, string bodyHtml, string messageType = "Notification") => Task.CompletedTask;

            public Task<bool> SendCancellationCustomerNotificationAsync(
                int clientId,
                string clientEmail,
                string? clientPhone,
                bool whatsappOptIn,
                int bookingId,
                string propertyTitle,
                int ruleApplied,
                decimal refundAmount,
                string timeline = "5 to 7 business days")
            {
                CustomerNotifCount++;
                LastCustomerRefundAmount = refundAmount;
                LastRuleApplied = ruleApplied;
                return Task.FromResult(true);
            }

            public Task<bool> SendCancellationOwnerNotificationAsync(
                int ownerId,
                string ownerEmail,
                int bookingId,
                string propertyTitle,
                decimal amountReceivedOrLost,
                bool isReceived,
                string reason)
            {
                OwnerNotifCount++;
                return Task.FromResult(true);
            }
        }

        [Fact]
        public async Task Scenario1_CustomerCancels7PlusDaysBeforeCheckIn_FullRefundMinusGatewayFee()
        {
            // Arrange
            var bookingRepo = new TestBookingRepo();
            var propRepo = new TestPropertyRepo();
            var refundRepo = new TestRefundRepo();
            var configRepo = new TestConfigRepo();
            var userRepo = new TestUserRepo();
            var finRepo = new TestFinancialRepo();
            var emailService = new TestEmailService();

            configRepo.Settings["Refund:GatewayFeePercentage"] = "2.0";
            configRepo.Settings["Refund:ExpectedTimeline"] = "5 to 7 business days";

            var booking = new Booking
            {
                BookingID = 101,
                PropertyID = 10,
                ClientID = 5,
                TotalAmount = 10000.00m,
                OwnerPayoutAmount = 9000.00m,
                CommissionRateSnapshot = 10.0m,
                CheckInDate = DateTime.UtcNow.AddDays(10), // 10 days away (240h >= 168h)
                Status = "Confirmed"
            };
            bookingRepo.Bookings[101] = booking;

            var property = new Property { PropertyID = 10, Title = "Sunny Meadows Farm", OwnerID = 20 };
            propRepo.Properties[10] = property;
            userRepo.Users[5] = new User { UserID = 5, FullName = "Client Guest", Email = "guest@example.com", Phone = "9876543210" };
            userRepo.Users[20] = new User { UserID = 20, FullName = "Property Host", Email = "host@example.com" };

            var service = new RefundEngineService(
                bookingRepo, propRepo, refundRepo, configRepo,
                NullLogger<RefundEngineService>.Instance,
                gatewayService: null,
                emailService: emailService,
                userRepo: userRepo,
                financialRepo: finRepo);

            // Act
            var quote = await service.CalculateCancellationQuoteAsync(booking);
            var refund = await service.ProcessCancellationAndRefundAsync(101, 5, "Client", "Change of travel plans");

            // Assert
            Assert.Equal(1, quote.RuleApplied);
            Assert.Equal(200.00m, quote.GatewayFeeDeducted); // 2% of 10000
            Assert.Equal(9800.00m, quote.NetRefundAmount);
            Assert.Equal(0.00m, quote.OwnerShareAmount);
            Assert.Equal(0.00m, quote.CommissionAmount);

            Assert.Equal(1, refund.RuleApplied);
            Assert.Equal(9800.00m, refund.NetRefundAmount);
            Assert.Equal(200.00m, refund.GatewayFeeDeducted);
            Assert.Equal(0.00m, refund.OwnerShareAmount);
            Assert.Equal(0.00m, bookingRepo.LastUpdatedOwnerPayout); // Held owner payout reduced to 0

            Assert.Equal(1, emailService.CustomerNotifCount);
            Assert.Equal(9800.00m, emailService.LastCustomerRefundAmount);
            Assert.Equal(1, emailService.OwnerNotifCount);
        }

        [Fact]
        public async Task Scenario2_CustomerCancels3To7DaysBeforeCheckIn_50PercentRefundAndCommissionSettingD1()
        {
            // Arrange
            var bookingRepo = new TestBookingRepo();
            var propRepo = new TestPropertyRepo();
            var refundRepo = new TestRefundRepo();
            var configRepo = new TestConfigRepo();
            var userRepo = new TestUserRepo();
            var finRepo = new TestFinancialRepo();
            var emailService = new TestEmailService();

            // PENDING DECISION D1: Commission setting on 50% partial cancellation (placeholder: keep commission on the 50%)
            configRepo.Settings["Refund:D1_PartialCancellationCommissionPolicy"] = "KeepOnPaidHalf";

            var booking = new Booking
            {
                BookingID = 102,
                PropertyID = 10,
                ClientID = 5,
                TotalAmount = 10000.00m,
                OwnerPayoutAmount = 9000.00m,
                CommissionRateSnapshot = 10.0m,
                CheckInDate = DateTime.UtcNow.AddDays(5), // 5 days away (120h: between 72h and 168h)
                Status = "Confirmed"
            };
            bookingRepo.Bookings[102] = booking;

            var property = new Property { PropertyID = 10, Title = "Sunny Meadows Farm", OwnerID = 20 };
            propRepo.Properties[10] = property;
            userRepo.Users[5] = new User { UserID = 5, FullName = "Client Guest", Email = "guest@example.com", Phone = "9876543210" };
            userRepo.Users[20] = new User { UserID = 20, FullName = "Property Host", Email = "host@example.com" };

            var service = new RefundEngineService(
                bookingRepo, propRepo, refundRepo, configRepo,
                NullLogger<RefundEngineService>.Instance,
                gatewayService: null,
                emailService: emailService,
                userRepo: userRepo,
                financialRepo: finRepo);

            // Act
            var quote = await service.CalculateCancellationQuoteAsync(booking);
            var refund = await service.ProcessCancellationAndRefundAsync(102, 5, "Client", "Trip postponed");

            // Assert
            Assert.Equal(2, quote.RuleApplied);
            Assert.Equal(5000.00m, quote.NetRefundAmount); // 50% of 10000
            Assert.Equal(0.00m, quote.GatewayFeeDeducted); // Gateway fee not deducted in row 2
            Assert.Equal(5000.00m, quote.OwnerShareAmount); // 50% to owner
            Assert.Equal(500.00m, quote.CommissionAmount); // 10% commission on the 5000 per D1 placeholder

            Assert.Equal(2, refund.RuleApplied);
            Assert.Equal(5000.00m, refund.NetRefundAmount);
            Assert.Equal(5000.00m, refund.OwnerShareAmount);
            Assert.Equal(4500.00m, bookingRepo.LastUpdatedOwnerPayout); // 5000 - 500 comm = 4500
        }

        [Fact]
        public async Task Scenario3_CustomerCancelsUnder72Hours_ZeroRefundAndOwnerPolicyD2()
        {
            // Arrange
            var bookingRepo = new TestBookingRepo();
            var propRepo = new TestPropertyRepo();
            var refundRepo = new TestRefundRepo();
            var configRepo = new TestConfigRepo();
            var userRepo = new TestUserRepo();
            var finRepo = new TestFinancialRepo();
            var emailService = new TestEmailService();

            // PENDING DECISION D2: Under 72 hours cancellation owner policy (placeholder: no refund for all owners)
            configRepo.Settings["Refund:D2_Under72hOwnerPolicyOverride"] = "NoRefundAll";

            var booking = new Booking
            {
                BookingID = 103,
                PropertyID = 10,
                ClientID = 5,
                TotalAmount = 10000.00m,
                OwnerPayoutAmount = 9000.00m,
                CommissionRateSnapshot = 10.0m,
                CheckInDate = DateTime.UtcNow.AddHours(40), // 40 hours away (< 72h)
                Status = "Confirmed"
            };
            bookingRepo.Bookings[103] = booking;

            var property = new Property { PropertyID = 10, Title = "Sunny Meadows Farm", OwnerID = 20 };
            propRepo.Properties[10] = property;
            userRepo.Users[5] = new User { UserID = 5, FullName = "Client Guest", Email = "guest@example.com", Phone = "9876543210" };
            userRepo.Users[20] = new User { UserID = 20, FullName = "Property Host", Email = "host@example.com" };

            var service = new RefundEngineService(
                bookingRepo, propRepo, refundRepo, configRepo,
                NullLogger<RefundEngineService>.Instance,
                gatewayService: null,
                emailService: emailService,
                userRepo: userRepo,
                financialRepo: finRepo);

            // Act
            var quote = await service.CalculateCancellationQuoteAsync(booking);
            var refund = await service.ProcessCancellationAndRefundAsync(103, 5, "Client", "Emergency last-minute cancellation");

            // Assert
            Assert.Equal(3, quote.RuleApplied);
            Assert.Equal(0.00m, quote.NetRefundAmount);
            Assert.Equal(10000.00m, quote.OwnerShareAmount);
            Assert.Equal(1000.00m, quote.CommissionAmount);

            Assert.Equal(3, refund.RuleApplied);
            Assert.Equal(0.00m, refund.NetRefundAmount);
            Assert.Equal(9000.00m, bookingRepo.LastUpdatedOwnerPayout); // Full booking amount minus full commission (10000 - 1000 = 9000)
        }

        [Fact]
        public async Task Scenario4_OwnerCancelsAtAnyTime_FullRefundAndPenaltyD3()
        {
            // Arrange
            var bookingRepo = new TestBookingRepo();
            var propRepo = new TestPropertyRepo();
            var refundRepo = new TestRefundRepo();
            var configRepo = new TestConfigRepo();
            var userRepo = new TestUserRepo();
            var finRepo = new TestFinancialRepo();
            var emailService = new TestEmailService();

            // PENDING DECISION D3: Owner cancellation penalty amount (placeholder: fixed amount stored in SiteSettings)
            configRepo.Settings["Refund:D3_OwnerCancellationPenaltyAmount"] = "500.00";

            var booking = new Booking
            {
                BookingID = 104,
                PropertyID = 10,
                ClientID = 5,
                TotalAmount = 10000.00m,
                OwnerPayoutAmount = 0.00m, // No held payout yet
                CommissionRateSnapshot = 10.0m,
                CheckInDate = DateTime.UtcNow.AddDays(2),
                Status = "Confirmed"
            };
            bookingRepo.Bookings[104] = booking;

            var property = new Property { PropertyID = 10, Title = "Sunny Meadows Farm", OwnerID = 20 };
            propRepo.Properties[10] = property;
            userRepo.Users[5] = new User { UserID = 5, FullName = "Client Guest", Email = "guest@example.com", Phone = "9876543210" };
            userRepo.Users[20] = new User { UserID = 20, FullName = "Property Host", Email = "host@example.com" };

            var service = new RefundEngineService(
                bookingRepo, propRepo, refundRepo, configRepo,
                NullLogger<RefundEngineService>.Instance,
                gatewayService: null,
                emailService: emailService,
                userRepo: userRepo,
                financialRepo: finRepo);

            // Act
            var quote = await service.CalculateCancellationQuoteAsync(booking, isOwner: true);
            var refund = await service.ProcessCancellationAndRefundAsync(104, 20, "Owner", "Property maintenance issue");

            // Assert
            Assert.Equal(4, quote.RuleApplied);
            Assert.Equal(10000.00m, quote.NetRefundAmount); // Full refund to customer
            Assert.Equal(0.00m, quote.GatewayFeeDeducted); // No gateway fee deducted
            Assert.Equal(0.00m, quote.CommissionAmount); // Commission refunded in full

            Assert.Equal(4, refund.RuleApplied);
            Assert.Equal(10000.00m, refund.NetRefundAmount);
            Assert.Equal(500.00m, refund.ShortfallAmount); // Penalty of 500 exceeded 0 held payout -> recorded as shortfall
            Assert.Single(finRepo.Entries);
            Assert.Equal("OwnerShortfall", finRepo.Entries[0].Category);
            Assert.Equal(500.00m, finRepo.Entries[0].Amount);
        }

        [Fact]
        public async Task DoubleRefundPrevention_ThrowsConflictException()
        {
            // Arrange
            var bookingRepo = new TestBookingRepo();
            var propRepo = new TestPropertyRepo();
            var refundRepo = new TestRefundRepo();
            var configRepo = new TestConfigRepo();

            var booking = new Booking
            {
                BookingID = 105,
                PropertyID = 10,
                ClientID = 5,
                TotalAmount = 5000.00m,
                CheckInDate = DateTime.UtcNow.AddDays(10),
                Status = "Confirmed"
            };
            bookingRepo.Bookings[105] = booking;

            var service = new RefundEngineService(
                bookingRepo, propRepo, refundRepo, configRepo,
                NullLogger<RefundEngineService>.Instance);

            // Process first cancellation
            await service.ProcessCancellationAndRefundAsync(105, 5, "Client", "First cancellation request");

            // Act & Assert duplicate call
            await Assert.ThrowsAsync<ConflictException>(async () =>
            {
                await service.ProcessCancellationAndRefundAsync(105, 5, "Client", "Second cancellation click");
            });
        }

        [Fact]
        public async Task AdminAdjudication_Scenario5_ListingMismatch_UpdatesAmountsAndNotifies()
        {
            // Arrange
            var bookingRepo = new TestBookingRepo();
            var propRepo = new TestPropertyRepo();
            var refundRepo = new TestRefundRepo();
            var configRepo = new TestConfigRepo();
            var userRepo = new TestUserRepo();
            var emailService = new TestEmailService();

            var booking = new Booking
            {
                BookingID = 106,
                PropertyID = 10,
                ClientID = 5,
                TotalAmount = 8000.00m,
                OwnerPayoutAmount = 7200.00m,
                CheckInDate = DateTime.UtcNow.AddDays(1),
                Status = "Cancelled"
            };
            bookingRepo.Bookings[106] = booking;

            var property = new Property { PropertyID = 10, Title = "Hilltop Villa", OwnerID = 20 };
            propRepo.Properties[10] = property;
            userRepo.Users[5] = new User { UserID = 5, FullName = "Client Guest", Email = "guest@example.com" };
            userRepo.Users[20] = new User { UserID = 20, FullName = "Property Host", Email = "host@example.com" };

            // Pre-seed a Pending refund for row 5
            var initialRefund = new BookingRefund
            {
                BookingID = 106,
                GrossBookingAmount = 8000.00m,
                RuleApplied = 5,
                Status = "Pending",
                NetRefundAmount = 0.00m,
                RefundReason = "Listing mismatch claim"
            };
            int refundId = await refundRepo.InsertRefundAsync(initialRefund);

            var service = new RefundEngineService(
                bookingRepo, propRepo, refundRepo, configRepo,
                NullLogger<RefundEngineService>.Instance,
                gatewayService: null,
                emailService: emailService,
                userRepo: userRepo);

            // Act: Admin approves a partial refund of ₹4,000 with mandatory reason
            var updated = await service.AdjudicateAdminRefundAsync(
                refundId,
                adminUserId: 1,
                action: "Approve",
                refundAmount: 4000.00m,
                reason: "Confirmed missing swimming pool amenity advertised in listing");

            // Assert
            Assert.Equal("Completed", updated.Status);
            Assert.Equal(4000.00m, updated.NetRefundAmount);
            Assert.Equal(4000.00m, updated.CancellationFeeDeducted);
            Assert.Equal(1, emailService.CustomerNotifCount);
            Assert.Equal(4000.00m, emailService.LastCustomerRefundAmount);
            Assert.Equal(1, emailService.OwnerNotifCount);
        }
    }
}
