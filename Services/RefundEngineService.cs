using BookMyFun.Models.Entities;
using BookMyFun.Models.ViewModels;
using BookMyFun.Repositories;
using BookMyFun.Services.Exceptions;

namespace BookMyFun.Services
{
    public interface IRefundEngineService
    {
        CancellationQuoteViewModel CalculateCancellationQuote(Booking booking, DateTime? checkTime = null);
        Task<BookingRefund> ProcessCancellationAndRefundAsync(int bookingId, int requestingUserId, string userRole, string? reason, bool forceOverride = false);
        Task<List<BookingRefund>> GetRefundsQueueAsync(string? status = null);
        Task<bool> OverrideRefundStatusAsync(int refundId, string newStatus, int staffUserId, string? notes);
    }

    public class RefundEngineService : IRefundEngineService
    {
        private readonly IBookingRepository _bookingRepo;
        private readonly IPropertyRepository _propertyRepo;
        private readonly IRefundRepository _refundRepo;
        private readonly IConfigRepository _configRepo;
        private readonly ILogger<RefundEngineService> _logger;

        public RefundEngineService(
            IBookingRepository bookingRepo,
            IPropertyRepository propertyRepo,
            IRefundRepository refundRepo,
            IConfigRepository configRepo,
            ILogger<RefundEngineService> logger)
        {
            _bookingRepo = bookingRepo;
            _propertyRepo = propertyRepo;
            _refundRepo = refundRepo;
            _configRepo = configRepo;
            _logger = logger;
        }

        public CancellationQuoteViewModel CalculateCancellationQuote(Booking booking, DateTime? checkTime = null)
        {
            var now = checkTime ?? DateTime.UtcNow;
            var checkInDateTime = booking.CheckInDate;

            // In case booking has slot time or specific check-in time
            if (booking.SlotStartTime.HasValue)
            {
                checkInDateTime = booking.CheckInDate.Date + booking.SlotStartTime.Value;
            }

            double hoursRemaining = (checkInDateTime - now).TotalHours;

            // Default gateway fee is 2.0%
            decimal gatewayRate = 0.02m;
            decimal gatewayFeeDeducted = Math.Round(booking.TotalAmount * gatewayRate, 2);

            int freeWindow = booking.FreeCancellationWindowHoursSnapshot > 0 ? booking.FreeCancellationWindowHoursSnapshot : 48;
            int cutoff = booking.CancellationCutoffHoursSnapshot > 0 ? booking.CancellationCutoffHoursSnapshot : 24;

            decimal cancellationFeeDeducted = 0.00m;
            decimal netRefund = 0.00m;
            bool isEligible = false;
            string tier = "NonRefundable";
            string explanation = "";

            if (hoursRemaining >= freeWindow)
            {
                // Full Refund minus gateway fee
                tier = "FullRefund";
                isEligible = true;
                cancellationFeeDeducted = 0.00m;
                netRefund = Math.Max(0, booking.TotalAmount - gatewayFeeDeducted);
                explanation = $"Cancelled {hoursRemaining:F1} hours prior to check-in (free window: {freeWindow}h). Full refund issued minus nominal payment gateway processing charges (2%).";
            }
            else if (hoursRemaining >= cutoff)
            {
                // Partial Refund: Total minus cancellation fee and gateway fee
                tier = "PartialRefund";
                isEligible = true;

                if (string.Equals(booking.CancellationFeeTypeSnapshot, "fixed", StringComparison.OrdinalIgnoreCase))
                {
                    cancellationFeeDeducted = Math.Min(booking.CancellationFeeValueSnapshot, booking.TotalAmount);
                }
                else
                {
                    decimal pct = booking.CancellationFeeValueSnapshot > 0 ? booking.CancellationFeeValueSnapshot : 10.0m;
                    cancellationFeeDeducted = Math.Round(booking.TotalAmount * (pct / 100m), 2);
                }

                netRefund = Math.Max(0, booking.TotalAmount - cancellationFeeDeducted - gatewayFeeDeducted);
                explanation = $"Cancelled {hoursRemaining:F1} hours prior to check-in (between {cutoff}h and {freeWindow}h). Policy cancellation fee and gateway charges applied.";
            }
            else
            {
                // Non-refundable
                tier = "NonRefundable";
                isEligible = false;
                cancellationFeeDeducted = booking.TotalAmount;
                netRefund = 0.00m;
                explanation = $"Cancellation attempted within strict cutoff window ({cutoff} hours before check-in or post check-in). Standard booking amount is non-refundable per host policy.";
            }

            return new CancellationQuoteViewModel
            {
                BookingID = booking.BookingID,
                PropertyTitle = booking.PropertyTitle ?? $"Property #{booking.PropertyID}",
                CheckInDate = booking.CheckInDate,
                TotalAmount = booking.TotalAmount,
                HoursUntilCheckIn = Math.Round(hoursRemaining, 1),
                FreeCancellationWindowHours = freeWindow,
                CancellationCutoffHours = cutoff,
                CancellationFeeType = booking.CancellationFeeTypeSnapshot,
                CancellationFeeValue = booking.CancellationFeeValueSnapshot,
                GatewayFeeDeducted = gatewayFeeDeducted,
                CancellationFeeDeducted = cancellationFeeDeducted,
                NetRefundAmount = netRefund,
                IsEligibleForRefund = isEligible,
                PolicyTier = tier,
                PolicyExplanation = explanation
            };
        }

        public async Task<BookingRefund> ProcessCancellationAndRefundAsync(int bookingId, int requestingUserId, string userRole, string? reason, bool forceOverride = false)
        {
            var booking = await _bookingRepo.GetBookingByIdAsync(bookingId);
            if (booking == null)
                throw new NotFoundException("Booking", bookingId);

            if (booking.Status.Equals("Cancelled", StringComparison.OrdinalIgnoreCase))
                throw new ConflictException("Booking has already been cancelled.");

            // IDOR Protection: Clients can only cancel their own bookings; Admin and Employee can cancel any
            if (userRole.Equals("Client", StringComparison.OrdinalIgnoreCase) && booking.ClientID != requestingUserId)
            {
                throw new ForbiddenException("You cannot cancel another client's booking.");
            }

            var quote = CalculateCancellationQuote(booking);

            if (!quote.IsEligibleForRefund && !forceOverride && !userRole.Equals("Admin", StringComparison.OrdinalIgnoreCase) && !userRole.Equals("Employee", StringComparison.OrdinalIgnoreCase))
            {
                // If client tries to cancel a non-refundable booking, it cancels with 0 refund
                _logger.LogInformation("Client cancelled booking #{BookingId} with zero refund due to policy cutoff.", bookingId);
            }

            decimal finalNetRefund = quote.NetRefundAmount;
            decimal finalCancelFee = quote.CancellationFeeDeducted;
            decimal finalGatewayFee = quote.GatewayFeeDeducted;

            if (forceOverride && (userRole.Equals("Admin", StringComparison.OrdinalIgnoreCase) || userRole.Equals("Employee", StringComparison.OrdinalIgnoreCase)))
            {
                // Staff override gives full refund minus gateway
                finalCancelFee = 0.00m;
                finalNetRefund = Math.Max(0, booking.TotalAmount - finalGatewayFee);
                _logger.LogWarning("Staff user #{UserId} forced override refund for booking #{BookingId}", requestingUserId, bookingId);
            }

            // 1. Update Booking Status to Cancelled & release security deposit if held
            await _bookingRepo.CancelBookingAsync(bookingId);
            if (booking.SecurityDepositAmount > 0 && booking.SecurityDepositStatus == "Held")
            {
                await _bookingRepo.UpdateSecurityDepositStatusAsync(bookingId, "Released");
            }

            // 2. Insert into BookingRefunds
            var refund = new BookingRefund
            {
                BookingID = bookingId,
                GrossBookingAmount = booking.TotalAmount,
                CancellationFeeDeducted = finalCancelFee,
                GatewayFeeDeducted = finalGatewayFee,
                NetRefundAmount = finalNetRefund,
                RefundReason = reason ?? "User requested cancellation",
                Status = finalNetRefund > 0 ? "Completed" : "Completed", // Mock instant simulated gateway refund
                GatewayRefundID = finalNetRefund > 0 ? $"rfnd_{Guid.NewGuid().ToString("N")[..12]}" : null,
                ProcessedByUserID = requestingUserId,
                ProcessedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            };

            int refundId = await _refundRepo.InsertRefundAsync(refund);
            refund.RefundID = refundId;

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
    }
}
