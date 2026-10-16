using BookMyFun.Models.Entities;
using BookMyFun.Models.ViewModels;
using BookMyFun.Repositories;
using BookMyFun.Services.Exceptions;
using System.Text.Json;

namespace BookMyFun.Services
{
    public interface IDamageClaimService
    {
        Task<int> CreateClaimAsync(DamageClaimCreateViewModel model, int hostUserId, bool isStaff = false);
        Task<List<DamageClaim>> GetAllClaimsAsync(string? status = null);
        Task<List<DamageClaim>> GetClaimsByOwnerAsync(int ownerId);
        Task<DamageClaim> GetClaimByIdAsync(int claimId);
        Task<bool> AdjudicateClaimAsync(DamageClaimAdjudicateViewModel model, int staffUserId);
    }

    public class DamageClaimService : IDamageClaimService
    {
        private readonly IDamageClaimRepository _claimRepo;
        private readonly IBookingRepository _bookingRepo;
        private readonly IPropertyRepository _propertyRepo;
        private readonly ILogger<DamageClaimService> _logger;

        public DamageClaimService(
            IDamageClaimRepository claimRepo,
            IBookingRepository bookingRepo,
            IPropertyRepository propertyRepo,
            ILogger<DamageClaimService> logger)
        {
            _claimRepo = claimRepo;
            _bookingRepo = bookingRepo;
            _propertyRepo = propertyRepo;
            _logger = logger;
        }

        public async Task<int> CreateClaimAsync(DamageClaimCreateViewModel model, int hostUserId, bool isStaff = false)
        {
            var booking = await _bookingRepo.GetBookingByIdAsync(model.BookingID);
            if (booking == null) throw new NotFoundException("Booking", model.BookingID);

            var property = await _propertyRepo.GetByIdAsync(booking.PropertyID);
            if (property == null) throw new NotFoundException("Property", booking.PropertyID);

            // Tenant isolation: Hosts can only raise damage claims for their own properties; Staff can log for any
            if (!isStaff && property.OwnerID != hostUserId)
            {
                throw new ForbiddenException("You are not authorized to submit a damage claim for this property.");
            }

            int targetOwnerId = isStaff ? property.OwnerID : hostUserId;

            if (model.ClaimAmount <= 0)
            {
                throw new ValidationException("ClaimAmount", "Please enter a valid claim amount greater than zero.");
            }

            var allEvidence = new List<string>(model.EvidenceUrls ?? new List<string>());
            if (!string.IsNullOrWhiteSpace(model.EvidenceUrlsInput))
            {
                var inputUrls = model.EvidenceUrlsInput.Split(new[] { '\r', '\n', ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var u in inputUrls)
                {
                    var trimmed = u.Trim();
                    if (!string.IsNullOrEmpty(trimmed) && !allEvidence.Contains(trimmed))
                    {
                        allEvidence.Add(trimmed);
                    }
                }
            }

            string evidenceJson = allEvidence.Any()
                ? JsonSerializer.Serialize(allEvidence)
                : "[]";

            var claim = new DamageClaim
            {
                BookingID = booking.BookingID,
                PropertyID = property.PropertyID,
                OwnerID = targetOwnerId,
                ClaimAmount = model.ClaimAmount,
                ItemizedDescription = model.ItemizedDescription.Trim(),
                EvidenceMediaUrls = evidenceJson,
                Status = "UnderReview",
                CreatedAt = DateTime.UtcNow
            };

            int claimId = await _claimRepo.InsertClaimAsync(claim);
            _logger.LogInformation("Damage claim #{ClaimId} filed for Booking #{BookingId} by Host #{HostId} for amount {Amount}", claimId, booking.BookingID, hostUserId, model.ClaimAmount);
            return claimId;
        }

        public async Task<List<DamageClaim>> GetAllClaimsAsync(string? status = null)
        {
            return await _claimRepo.GetAllClaimsAsync(status);
        }

        public async Task<List<DamageClaim>> GetClaimsByOwnerAsync(int ownerId)
        {
            return await _claimRepo.GetClaimsByOwnerIdAsync(ownerId);
        }

        public async Task<DamageClaim> GetClaimByIdAsync(int claimId)
        {
            var claim = await _claimRepo.GetClaimByIdAsync(claimId);
            if (claim == null) throw new NotFoundException("Damage Claim", claimId);
            return claim;
        }

        public async Task<bool> AdjudicateClaimAsync(DamageClaimAdjudicateViewModel model, int staffUserId)
        {
            var claim = await _claimRepo.GetClaimByIdAsync(model.ClaimID);
            if (claim == null) throw new NotFoundException("Damage Claim", model.ClaimID);

            var booking = await _bookingRepo.GetBookingByIdAsync(claim.BookingID);
            if (booking == null) throw new NotFoundException("Booking", claim.BookingID);

            string finalStatus;
            decimal? finalApprovedAmount = null;

            if (model.Action.Equals("Reject", StringComparison.OrdinalIgnoreCase))
            {
                finalStatus = "Rejected";
                finalApprovedAmount = 0.00m;
                // Release full security deposit back to client
                await _bookingRepo.UpdateSecurityDepositStatusAsync(booking.BookingID, "Released");
            }
            else if (model.Action.Equals("PartiallyApprove", StringComparison.OrdinalIgnoreCase))
            {
                finalStatus = "Approved";
                finalApprovedAmount = model.ApprovedAmount.HasValue && model.ApprovedAmount.Value > 0
                    ? Math.Min(model.ApprovedAmount.Value, claim.ClaimAmount)
                    : claim.ClaimAmount;

                string depositStatus = finalApprovedAmount >= booking.SecurityDepositAmount ? "FullyClaimed" : "PartiallyClaimed";
                await _bookingRepo.UpdateSecurityDepositStatusAsync(booking.BookingID, depositStatus);
            }
            else
            {
                // Full approval
                finalStatus = "Approved";
                finalApprovedAmount = claim.ClaimAmount;
                string depositStatus = finalApprovedAmount >= booking.SecurityDepositAmount ? "FullyClaimed" : "PartiallyClaimed";
                await _bookingRepo.UpdateSecurityDepositStatusAsync(booking.BookingID, depositStatus);
            }

            bool updated = await _claimRepo.AdjudicateClaimAsync(
                claim.ClaimID,
                finalStatus,
                finalApprovedAmount,
                staffUserId,
                model.AdjudicationNotes);

            _logger.LogInformation("Damage claim #{ClaimId} adjudicated with status {Status}, approved amount {ApprovedAmount}",
                claim.ClaimID, finalStatus, finalApprovedAmount);

            return updated;
        }
    }
}
