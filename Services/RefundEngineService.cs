using System.Globalization;
using BookMyFun.Models.Entities;
using BookMyFun.Models.ViewModels;
using BookMyFun.Repositories;
using BookMyFun.Services.Exceptions;

namespace BookMyFun.Services
{
    public interface IRefundEngineService
    {
        CancellationQuoteViewModel CalculateCancellationQuote(Booking booking, DateTime? checkTime = null, bool isOwner = false);
        Task<CancellationQuoteViewModel> CalculateCancellationQuoteAsync(Booking booking, DateTime? checkTime = null, bool isOwner = false);
        Task<BookingRefund> ProcessCancellationAndRefundAsync(int bookingId, int requestingUserId, string userRole, string? reason, bool forceOverride = false, int? specificRule = null, decimal? customRefundAmount = null, bool issueCredit = false);
        Task<List<BookingRefund>> GetRefundsQueueAsync(string? status = null);
        Task<bool> OverrideRefundStatusAsync(int refundId, string newStatus, int staffUserId, string? notes);
        Task<BookingRefund> AdjudicateAdminRefundAsync(int refundId, int adminUserId, string action, decimal refundAmount, string reason, bool issueAsCredit = false);
        Task<BookingRefund?> GetRefundByIdAsync(int refundId) => Task.FromResult<BookingRefund?>(null);
    }

    public class RefundEngineService : IRefundEngineService
    {
        private readonly IBookingRepository _bookingRepo;
        private readonly IPropertyRepository _propertyRepo;
        private readonly IRefundRepository _refundRepo;
        private readonly IConfigRepository _configRepo;
        private readonly ILogger<RefundEngineService> _logger;
        private readonly IPaymentGatewayService? _gatewayService;
        private readonly IEmailService? _emailService;
        private readonly IUserRepository? _userRepo;
        private readonly IFinancialRepository? _financialRepo;

        public RefundEngineService(
            IBookingRepository bookingRepo,
            IPropertyRepository propertyRepo,
            IRefundRepository refundRepo,
            IConfigRepository configRepo,
            ILogger<RefundEngineService> logger,
            IPaymentGatewayService? gatewayService = null,
            IEmailService? emailService = null,
            IUserRepository? userRepo = null,
            IFinancialRepository? financialRepo = null)
        {
            _bookingRepo = bookingRepo;
            _propertyRepo = propertyRepo;
            _refundRepo = refundRepo;
            _configRepo = configRepo;
            _logger = logger;
            _gatewayService = gatewayService;
            _emailService = emailService;
            _userRepo = userRepo;
            _financialRepo = financialRepo;
        }

        public CancellationQuoteViewModel CalculateCancellationQuote(Booking booking, DateTime? checkTime = null, bool isOwner = false)
        {
            return CalculateCancellationQuoteInternal(booking, checkTime, isOwner, 2.0m, "5 to 7 business days", "KeepOnPaidHalf", "NoRefundAll", 500.00m);
        }

        public async Task<CancellationQuoteViewModel> CalculateCancellationQuoteAsync(Booking booking, DateTime? checkTime = null, bool isOwner = false)
        {
            decimal gatewayRate = await GetGatewayFeePercentageAsync();
            string timeline = await GetRefundTimelineSettingAsync();
            string d1Policy = await GetD1PolicySettingAsync();
            string d2Policy = await GetD2PolicySettingAsync();
            decimal d3Penalty = await GetD3PenaltySettingAsync();

            return CalculateCancellationQuoteInternal(booking, checkTime, isOwner, gatewayRate, timeline, d1Policy, d2Policy, d3Penalty);
        }

        private CancellationQuoteViewModel CalculateCancellationQuoteInternal(
            Booking booking,
            DateTime? checkTime,
            bool isOwner,
            decimal gatewayRate,
            string timeline,
            string d1Policy,
            string d2Policy,
            decimal d3Penalty)
        {
            var now = checkTime ?? DateTime.UtcNow;
            var checkInDateTime = booking.CheckInDate;

            if (booking.SlotStartTime.HasValue)
            {
                checkInDateTime = booking.CheckInDate.Date + booking.SlotStartTime.Value;
            }

            double hoursRemaining = (checkInDateTime - now).TotalHours;
            decimal amountPaid = booking.TotalAmount;
            decimal commRate = booking.CommissionRateSnapshot > 0 ? booking.CommissionRateSnapshot : 10.0m;

            int ruleApplied = 1;
            decimal gatewayFeeDeducted = 0.00m;
            decimal cancellationFeeDeducted = 0.00m;
            decimal netRefund = 0.00m;
            decimal ownerShare = 0.00m;
            decimal commission = 0.00m;
            bool isEligible = false;
            string tier = "NonRefundable";
            string explanation = "";

            if (isOwner)
            {
                // Scenario 4: Owner cancels at any time
                ruleApplied = 4;
                isEligible = true;
                tier = "FullRefund";
                gatewayFeeDeducted = 0.00m;
                cancellationFeeDeducted = 0.00m;
                netRefund = amountPaid;
                ownerShare = 0.00m;
                commission = 0.00m; // Commission refunded in full
                // PENDING DECISION D3: Owner cancellation penalty amount (placeholder: fixed amount stored in SiteSettings)
                explanation = $"Host-initiated cancellation. Full refund of ₹{netRefund:N2} issued to customer. Host is assessed cancellation penalty of ₹{d3Penalty:N2}. Expected timeline: {timeline}.";
            }
            else if (hoursRemaining >= 168.0) // 7+ days (168 hours)
            {
                // Scenario 1: Customer cancels 7+ days before check-in
                ruleApplied = 1;
                isEligible = true;
                tier = "FullRefund";
                gatewayFeeDeducted = Math.Round(amountPaid * (gatewayRate / 100m), 2);
                cancellationFeeDeducted = 0.00m;
                netRefund = Math.Max(0, amountPaid - gatewayFeeDeducted);
                ownerShare = 0.00m;
                commission = 0.00m;
                explanation = $"Cancelled {hoursRemaining:F1} hours prior to check-in (7+ days). Full refund issued minus nominal payment gateway processing fee ({gatewayRate:0.#}%). Expected timeline: {timeline}.";
            }
            else if (hoursRemaining >= 72.0) // 3 to 7 days (72h to 168h)
            {
                // Scenario 2: Customer cancels 3 to 7 days before check-in
                ruleApplied = 2;
                isEligible = true;
                tier = "PartialRefund";
                gatewayFeeDeducted = 0.00m; // Gateway fee not deducted in row 2 (row 1 only)
                netRefund = Math.Round(amountPaid * 0.50m, 2);
                cancellationFeeDeducted = amountPaid - netRefund;
                ownerShare = Math.Round(amountPaid * 0.50m, 2);

                // PENDING DECISION D1: Commission setting on 50% partial cancellation (placeholder: keep commission on the 50%)
                if (string.Equals(d1Policy, "KeepOnPaidHalf", StringComparison.OrdinalIgnoreCase))
                {
                    commission = Math.Round(ownerShare * (commRate / 100m), 2);
                }
                else
                {
                    commission = 0.00m;
                }

                explanation = $"Cancelled {hoursRemaining:F1} hours prior to check-in (3 to 7 days). 50% refund issued to customer; 50% allocated to host. Expected timeline: {timeline}.";
            }
            else // Under 72 hours
            {
                // Scenario 3: Customer cancels under 72 hours before check-in
                ruleApplied = 3;
                tier = "NonRefundable";
                gatewayFeeDeducted = 0.00m;
                cancellationFeeDeducted = amountPaid;
                
                // PENDING DECISION D2: Under 72 hours cancellation owner policy (placeholder: no refund for all owners)
                if (string.Equals(d2Policy, "NoRefundAll", StringComparison.OrdinalIgnoreCase))
                {
                    netRefund = 0.00m;
                    isEligible = false;
                    ownerShare = amountPaid;
                    commission = Math.Round(amountPaid * (commRate / 100m), 2);
                    explanation = $"Cancellation attempted under 72 hours before check-in ({hoursRemaining:F1}h remaining). Standard booking amount is non-refundable per host policy.";
                }
                else
                {
                    netRefund = 0.00m;
                    isEligible = false;
                    ownerShare = amountPaid;
                    commission = Math.Round(amountPaid * (commRate / 100m), 2);
                    explanation = $"Cancelled within strict 72-hour window. Non-refundable per current policy setting.";
                }
            }

            return new CancellationQuoteViewModel
            {
                BookingID = booking.BookingID,
                PropertyTitle = booking.PropertyTitle ?? $"Property #{booking.PropertyID}",
                CheckInDate = booking.CheckInDate,
                TotalAmount = booking.TotalAmount,
                HoursUntilCheckIn = Math.Round(hoursRemaining, 1),
                FreeCancellationWindowHours = 168,
                CancellationCutoffHours = 72,
                CancellationFeeType = "percentage",
                CancellationFeeValue = ruleApplied == 1 ? 0.0m : (ruleApplied == 2 ? 50.0m : 100.0m),
                GatewayFeeDeducted = gatewayFeeDeducted,
                CancellationFeeDeducted = cancellationFeeDeducted,
                NetRefundAmount = netRefund,
                IsEligibleForRefund = isEligible,
                PolicyTier = tier,
                PolicyExplanation = explanation,
                RuleApplied = ruleApplied,
                CustomerRefundAmount = netRefund,
                OwnerShareAmount = ownerShare,
                CommissionAmount = commission,
                AmountPaid = amountPaid,
                RefundTimeline = timeline
            };
        }

        public async Task<BookingRefund> ProcessCancellationAndRefundAsync(
            int bookingId,
            int requestingUserId,
            string userRole,
            string? reason,
            bool forceOverride = false,
            int? specificRule = null,
            decimal? customRefundAmount = null,
            bool issueCredit = false)
        {
            // 1. Double Refund Protection: A cancellation that already has a refund must not create another one
            var existingRefund = await _refundRepo.GetRefundByBookingIdAsync(bookingId);
            if (existingRefund != null)
            {
                _logger.LogWarning("Double refund prevented for Booking #{BookingId}. Existing refund #{RefundId} status: '{Status}'.", bookingId, existingRefund.RefundID, existingRefund.Status);
                throw new ConflictException($"A refund has already been registered or processed for booking #{bookingId} (Refund #{existingRefund.RefundID}). Duplicate refund request rejected.");
            }

            var booking = await _bookingRepo.GetByIdAsync(bookingId);
            if (booking == null)
                throw new NotFoundException("Booking", bookingId);

            if (booking.Status.Equals("Cancelled", StringComparison.OrdinalIgnoreCase))
                throw new ConflictException("Booking has already been cancelled.");

            var property = await _propertyRepo.GetByIdAsync(booking.PropertyID);
            bool isClient = booking.ClientID == requestingUserId;
            bool isOwner = (property != null && property.OwnerID == requestingUserId);
            bool isStaff = userRole.Equals("Admin", StringComparison.OrdinalIgnoreCase) || userRole.Equals("Employee", StringComparison.OrdinalIgnoreCase);

            // IDOR Protection: Clients can only cancel their own bookings
            if (userRole.Equals("Client", StringComparison.OrdinalIgnoreCase) && !isClient && !isStaff)
            {
                throw new ForbiddenException("You cannot cancel another client's booking.");
            }

            // Determine if owner cancellation
            bool isOwnerCancel = isOwner || userRole.Equals("Owner", StringComparison.OrdinalIgnoreCase);

            // 2. Compute Refund Quote based on actual amount paid
            decimal amountPaid = booking.TotalAmount;
            var payments = await _bookingRepo.GetPaymentsByBookingIdAsync(bookingId);
            if (payments != null && payments.Any(p => p.PaymentStatus == "Completed" || p.PaymentStatus == "Success"))
            {
                var sumPaid = payments.Where(p => p.PaymentStatus == "Completed" || p.PaymentStatus == "Success").Sum(p => p.Amount);
                if (sumPaid > 0) amountPaid = sumPaid;
            }

            var quote = await CalculateCancellationQuoteAsync(booking, DateTime.UtcNow, isOwnerCancel);
            quote.AmountPaid = amountPaid;

            int ruleApplied = specificRule ?? quote.RuleApplied;
            decimal finalNetRefund = quote.NetRefundAmount;
            decimal finalCancelFee = quote.CancellationFeeDeducted;
            decimal finalGatewayFee = quote.GatewayFeeDeducted;
            decimal ownerShareAmount = quote.OwnerShareAmount;
            decimal commissionAmount = quote.CommissionAmount;
            decimal commRate = booking.CommissionRateSnapshot > 0 ? booking.CommissionRateSnapshot : 10.0m;

            if (specificRule == 5)
            {
                // Scenario 5: Property does not match listing (confirmed by admin after review)
                ruleApplied = 5;
                finalNetRefund = customRefundAmount.HasValue ? Math.Min(customRefundAmount.Value, amountPaid) : amountPaid;
                finalCancelFee = Math.Max(0, amountPaid - finalNetRefund);
                finalGatewayFee = 0.00m;
                ownerShareAmount = Math.Max(0, amountPaid - finalNetRefund);
                commissionAmount = 0.00m; // Commission refunded in full
            }
            else if (specificRule == 6)
            {
                // Scenario 6: Force majeure (weather, government order), confirmed by admin
                ruleApplied = 6;
                finalNetRefund = amountPaid;
                finalCancelFee = 0.00m;
                finalGatewayFee = 0.00m;
                ownerShareAmount = 0.00m;
                commissionAmount = 0.00m;
            }
            else if (forceOverride && isStaff)
            {
                finalCancelFee = 0.00m;
                finalNetRefund = Math.Max(0, amountPaid - finalGatewayFee);
                _logger.LogWarning("Staff user #{UserId} forced override refund for booking #{BookingId}", requestingUserId, bookingId);
            }

            // 3. Held Owner Payout Adjustment & Shortfall Calculation
            decimal heldOwnerPayout = booking.OwnerPayoutAmount;
            decimal newOwnerPayout = 0.00m;
            decimal shortfall = 0.00m;

            if (ruleApplied == 1 || ruleApplied == 6)
            {
                newOwnerPayout = 0.00m;
            }
            else if (ruleApplied == 2)
            {
                // Owner gets 50% minus commission on that 50%
                decimal ownerGross = Math.Round(amountPaid * 0.50m, 2);
                // PENDING DECISION D1: Commission setting on 50% partial cancellation (placeholder: keep commission on the 50%)
                decimal comm = Math.Round(ownerGross * (commRate / 100m), 2);
                newOwnerPayout = Math.Max(0, ownerGross - comm);
            }
            else if (ruleApplied == 3)
            {
                // Owner gets full booking amount minus full commission
                decimal comm = Math.Round(amountPaid * (commRate / 100m), 2);
                newOwnerPayout = Math.Max(0, amountPaid - comm);
            }
            else if (ruleApplied == 4)
            {
                // Owner cancels: gets 0, charged penalty (D3)
                newOwnerPayout = 0.00m;
                // PENDING DECISION D3: Owner cancellation penalty amount (placeholder: fixed amount stored in SiteSettings)
                decimal penalty = await GetD3PenaltySettingAsync();
                if (heldOwnerPayout < penalty)
                {
                    shortfall = penalty - heldOwnerPayout;
                }
            }
            else if (ruleApplied == 5)
            {
                // Owner absorbs cost of refund
                if (heldOwnerPayout >= finalNetRefund)
                {
                    newOwnerPayout = heldOwnerPayout - finalNetRefund;
                }
                else
                {
                    shortfall = finalNetRefund - heldOwnerPayout;
                    newOwnerPayout = 0.00m;
                }
            }

            // Update held owner payout on the booking
            await _bookingRepo.UpdateOwnerPayoutAsync(bookingId, newOwnerPayout);

            // If shortfall occurred, record in FinancialLedger as an amount owed by owner in next payout
            if (shortfall > 0 && _financialRepo != null)
            {
                await _financialRepo.AddEntryAsync(new FinancialEntry
                {
                    TransactionType = "Expense",
                    Category = "OwnerShortfall",
                    Amount = shortfall,
                    Title = $"Owner Payout Shortfall - Booking #{bookingId}",
                    Details = $"Shortfall of ₹{shortfall:N2} due to refund #{bookingId} under Rule #{ruleApplied}. Amount owed by owner in subsequent payout.",
                    TransactionDate = DateTime.Today,
                    LoggedByUserID = requestingUserId
                });
            }

            // 4. Update Booking Status to Cancelled & release security deposit if held
            await _bookingRepo.CancelBookingAsync(bookingId);
            if (booking.SecurityDepositAmount > 0 && booking.SecurityDepositStatus == "Held")
            {
                await _bookingRepo.UpdateSecurityDepositStatusAsync(bookingId, "Released");
            }

            // 5. Create BookingRefund record BEFORE calling the gateway (initially Initiated)
            var refund = new BookingRefund
            {
                BookingID = bookingId,
                GrossBookingAmount = amountPaid,
                CancellationFeeDeducted = finalCancelFee,
                GatewayFeeDeducted = finalGatewayFee,
                NetRefundAmount = finalNetRefund,
                RefundReason = reason ?? $"Cancelled under Rule #{ruleApplied}",
                Status = "Initiated",
                GatewayRefundID = null,
                ProcessedByUserID = requestingUserId,
                ProcessedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                RuleApplied = ruleApplied,
                OwnerShareAmount = ownerShareAmount,
                CommissionAmount = commissionAmount,
                ShortfallAmount = shortfall
            };

            int refundId = await _refundRepo.InsertRefundAsync(refund);
            refund.RefundID = refundId;

            // 6. Call Gateway Refund if refund amount > 0 and not pure credit
            if ((ruleApplied == 5 || ruleApplied == 6) && !isStaff)
            {
                // Rows 5 and 6 require admin adjudication: include admin in refund queue with Pending status
                refund.Status = "Pending";
                await _refundRepo.UpdateRefundStatusAsync(refundId, "Pending", requestingUserId, $"Awaiting admin adjudication under Scenario #{ruleApplied}");
                _logger.LogInformation("Refund #{RefundId} for Booking #{BookingId} placed in Admin Refund Queue for Scenario #{RuleApplied}", refundId, bookingId, ruleApplied);
            }
            else if (finalNetRefund > 0 && !issueCredit && _gatewayService != null)
            {
                string paymentId = payments?.FirstOrDefault(p => p.PaymentStatus == "Completed" || p.PaymentStatus == "Success")?.GatewayTransactionID 
                    ?? $"PAY_BOOKING_{bookingId}";

                var gatewayResult = await _gatewayService.ProcessRefundAsync(paymentId, finalNetRefund, refund.RefundReason);
                if (gatewayResult.Success && !gatewayResult.IsPending)
                {
                    refund.Status = "Completed";
                    refund.GatewayRefundID = gatewayResult.RefundId;
                    await _refundRepo.UpdateRefundStatusAsync(refundId, "Completed", requestingUserId, $"Gateway refund completed: {gatewayResult.RefundId}");
                    _logger.LogInformation("Refund #{RefundId} for Booking #{BookingId} completed via gateway with ID {GatewayRefundId}", refundId, bookingId, gatewayResult.RefundId);
                }
                else
                {
                    // Mark as Pending for retry and show to admin
                    refund.Status = "Pending";
                    await _refundRepo.UpdateRefundStatusAsync(refundId, "Pending", requestingUserId, $"Gateway pending or failed retry needed: {gatewayResult.ErrorMessage ?? "Awaiting confirmation"}");
                    _logger.LogWarning("Gateway refund call for Booking #{BookingId} returned Pending or failed: {Error}", bookingId, gatewayResult.ErrorMessage);
                }
            }
            else
            {
                // Zero refund (e.g. Rule 3) or credit issued
                refund.Status = "Completed";
                await _refundRepo.UpdateRefundStatusAsync(refundId, "Completed", requestingUserId, issueCredit ? "Credit issued per force majeure" : "Zero refund eligible");
            }

            // 7. Multi-Stakeholder Notifications
            string timeline = await GetRefundTimelineSettingAsync();
            var client = _userRepo != null ? await _userRepo.GetByIdAsync(booking.ClientID) : null;
            var owner = property != null && _userRepo != null ? await _userRepo.GetByIdAsync(property.OwnerID) : null;
            User? eventManager = null;
            if (booking.IsEventBooking && booking.EventPackagePriceSnapshot > 0 && _userRepo != null)
            {
                if (booking.DecoratorAssignedID.HasValue)
                {
                    eventManager = await _userRepo.GetByIdAsync(booking.DecoratorAssignedID.Value);
                }
                if (eventManager == null)
                {
                    var ems = await _userRepo.GetUsersByRoleAsync("EventManager");
                    eventManager = ems.FirstOrDefault();
                }
            }

            if (_emailService != null && client != null && property != null)
            {
                decimal payoutAdjustment = heldOwnerPayout - newOwnerPayout;
                decimal heldBalanceChange = heldOwnerPayout - newOwnerPayout;
                decimal totalDeductions = finalCancelFee + finalGatewayFee;

                try
                {
                    await _emailService.DispatchCancellationNotificationsAsync(
                        booking,
                        property,
                        client,
                        owner,
                        eventManager,
                        ruleApplied,
                        amountPaid,
                        totalDeductions,
                        finalNetRefund,
                        timeline,
                        reason ?? $"Cancellation under Scenario #{ruleApplied}",
                        payoutAdjustment,
                        heldBalanceChange);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to dispatch cancellation notifications for Booking #{BookingId}", bookingId);
                }
            }

            if (client != null && _emailService != null)
            {
                await _emailService.SendCancellationCustomerNotificationAsync(
                    client.UserID, client.Email, client.Phone, !string.IsNullOrWhiteSpace(client.Phone),
                    bookingId, property?.Title ?? $"Property #{booking.PropertyID}",
                    ruleApplied, finalNetRefund, timeline);
            }

            if (property != null && _emailService != null && owner != null)
            {
                decimal hostImpact = ruleApplied == 3 ? ownerShareAmount : (ruleApplied == 2 ? ownerShareAmount : (ruleApplied == 4 ? await GetD3PenaltySettingAsync() : 0.00m));
                bool hostReceived = (ruleApplied == 2 || ruleApplied == 3);
                await _emailService.SendCancellationOwnerNotificationAsync(
                    owner.UserID, owner.Email, bookingId, property.Title,
                    hostImpact, hostReceived, reason ?? $"Cancellation under Rule #{ruleApplied}");
            }

            return refund;
        }

        public async Task<BookingRefund> AdjudicateAdminRefundAsync(
            int refundId,
            int adminUserId,
            string action,
            decimal refundAmount,
            string reason,
            bool issueAsCredit = false)
        {
            if (string.IsNullOrWhiteSpace(reason))
                throw new ArgumentException("A specific reason is required for administrative refund adjudication.", nameof(reason));

            var refund = await _refundRepo.GetRefundByIdAsync(refundId);
            if (refund == null)
                throw new NotFoundException("BookingRefund", refundId);

            if (action.Equals("Reject", StringComparison.OrdinalIgnoreCase))
            {
                await _refundRepo.UpdateRefundStatusAsync(refundId, "Rejected", adminUserId, reason);
                refund.Status = "Rejected";
                return refund;
            }

            var booking = await _bookingRepo.GetByIdAsync(refund.BookingID);
            if (booking == null)
                throw new NotFoundException("Booking", refund.BookingID);

            int rule = refund.RuleApplied ?? 5;
            decimal approvedRefund = Math.Min(refundAmount, booking.TotalAmount);

            // Execute gateway refund if not credit
            string? gatewayRefundId = null;
            string newStatus = "Completed";

            if (approvedRefund > 0 && !issueAsCredit && _gatewayService != null)
            {
                var payments = await _bookingRepo.GetPaymentsByBookingIdAsync(booking.BookingID);
                string paymentId = payments?.FirstOrDefault(p => p.PaymentStatus == "Completed" || p.PaymentStatus == "Success")?.GatewayTransactionID 
                    ?? $"PAY_BOOKING_{booking.BookingID}";

                var gw = await _gatewayService.ProcessRefundAsync(paymentId, approvedRefund, reason);
                if (gw.Success && !gw.IsPending)
                {
                    gatewayRefundId = gw.RefundId;
                    newStatus = "Completed";
                }
                else
                {
                    newStatus = "Pending";
                }
            }

            refund.NetRefundAmount = approvedRefund;
            refund.CancellationFeeDeducted = Math.Max(0, booking.TotalAmount - approvedRefund);
            refund.Status = newStatus;
            refund.GatewayRefundID = gatewayRefundId;
            refund.ProcessedAt = DateTime.UtcNow;
            refund.ProcessedByUserID = adminUserId;

            await _refundRepo.UpdateRefundStatusAsync(refundId, newStatus, adminUserId, $"Adjudicated by admin. Reason: {reason}");

            // Multi-stakeholder notifications
            var client = _userRepo != null ? await _userRepo.GetByIdAsync(booking.ClientID) : null;
            var property = await _propertyRepo.GetByIdAsync(booking.PropertyID);
            var owner = property != null && _userRepo != null ? await _userRepo.GetByIdAsync(property.OwnerID) : null;
            User? eventManager = null;
            if (booking.IsEventBooking && booking.EventPackagePriceSnapshot > 0 && _userRepo != null)
            {
                if (booking.DecoratorAssignedID.HasValue)
                {
                    eventManager = await _userRepo.GetByIdAsync(booking.DecoratorAssignedID.Value);
                }
                if (eventManager == null)
                {
                    var ems = await _userRepo.GetUsersByRoleAsync("EventManager");
                    eventManager = ems.FirstOrDefault();
                }
            }
            string timeline = await GetRefundTimelineSettingAsync();

            if (_emailService != null && client != null && property != null)
            {
                decimal totalDeductions = Math.Max(0, booking.TotalAmount - approvedRefund);
                try
                {
                    await _emailService.DispatchCancellationNotificationsAsync(
                        booking,
                        property,
                        client,
                        owner,
                        eventManager,
                        rule,
                        booking.TotalAmount,
                        totalDeductions,
                        approvedRefund,
                        timeline,
                        reason,
                        approvedRefund,
                        approvedRefund);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to dispatch cancellation notifications during admin adjudication for Booking #{BookingId}", booking.BookingID);
                }
            }

            if (client != null && _emailService != null)
            {
                await _emailService.SendCancellationCustomerNotificationAsync(
                    client.UserID, client.Email, client.Phone, !string.IsNullOrWhiteSpace(client.Phone),
                    booking.BookingID, property?.Title ?? $"Property #{booking.PropertyID}",
                    rule, approvedRefund, timeline);
            }

            if (property != null && _emailService != null && owner != null)
            {
                await _emailService.SendCancellationOwnerNotificationAsync(
                    owner.UserID, owner.Email, booking.BookingID, property.Title,
                    approvedRefund, isReceived: false, reason);
            }

            return refund;
        }

        public async Task<List<BookingRefund>> GetRefundsQueueAsync(string? status = null)
        {
            return await _refundRepo.GetRefundsAsync(status);
        }

        public async Task<bool> OverrideRefundStatusAsync(int refundId, string newStatus, int staffUserId, string? notes)
        {
            return await _refundRepo.UpdateRefundStatusAsync(refundId, newStatus, staffUserId, notes);
        }

        public async Task<BookingRefund?> GetRefundByIdAsync(int refundId)
        {
            return await _refundRepo.GetRefundByIdAsync(refundId);
        }

        private async Task<decimal> GetGatewayFeePercentageAsync()
        {
            try
            {
                string? val = await _configRepo.GetSettingValueAsync("Refund:GatewayFeePercentage");
                if (!string.IsNullOrWhiteSpace(val) && decimal.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal pct))
                {
                    return pct;
                }
            }
            catch { }
            return 2.0m;
        }

        private async Task<string> GetRefundTimelineSettingAsync()
        {
            try
            {
                string? val = await _configRepo.GetSettingValueAsync("Refund:ExpectedTimeline");
                if (!string.IsNullOrWhiteSpace(val)) return val.Trim();
            }
            catch { }
            return "5 to 7 business days";
        }

        private async Task<string> GetD1PolicySettingAsync()
        {
            try
            {
                // PENDING DECISION D1: Commission setting on 50% partial cancellation (placeholder: keep commission on the 50%)
                string? val = await _configRepo.GetSettingValueAsync("Refund:D1_PartialCancellationCommissionPolicy");
                if (!string.IsNullOrWhiteSpace(val)) return val.Trim();
            }
            catch { }
            return "KeepOnPaidHalf";
        }

        private async Task<string> GetD2PolicySettingAsync()
        {
            try
            {
                // PENDING DECISION D2: Under 72 hours cancellation owner policy (placeholder: no refund for all owners)
                string? val = await _configRepo.GetSettingValueAsync("Refund:D2_Under72hOwnerPolicyOverride");
                if (!string.IsNullOrWhiteSpace(val)) return val.Trim();
            }
            catch { }
            return "NoRefundAll";
        }

        private async Task<decimal> GetD3PenaltySettingAsync()
        {
            try
            {
                // PENDING DECISION D3: Owner cancellation penalty amount (placeholder: fixed amount stored in SiteSettings)
                string? val = await _configRepo.GetSettingValueAsync("Refund:D3_OwnerCancellationPenaltyAmount");
                if (!string.IsNullOrWhiteSpace(val) && decimal.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal amt))
                {
                    return amt;
                }
            }
            catch { }
            return 500.00m;
        }
    }
}
