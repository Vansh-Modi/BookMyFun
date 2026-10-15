using BookMyFun.Models.Entities;
using BookMyFun.Models.ViewModels;
using BookMyFun.Repositories;
using BookMyFun.Services.Exceptions;

namespace BookMyFun.Services
{
    public interface IIdVerificationService
    {
        Task<int> SubmitVerificationAsync(int clientId, string documentType, string storageKey, string? maskedIdentifier);
        Task<ClientIdVerification?> GetClientVerificationAsync(int clientId, int requestingUserId, string userRole);
        Task<List<ClientIdVerification>> GetAllVerificationsAsync(string? status = null);
        Task<bool> ReviewVerificationAsync(int verificationId, string action, int staffUserId, string? rejectionReason = null);
        Task<bool> IsClientVerifiedAsync(int clientId);
    }

    public class IdVerificationService : IIdVerificationService
    {
        private readonly IIdVerificationRepository _idRepo;
        private readonly IUserRepository _userRepo;
        private readonly ILogger<IdVerificationService> _logger;

        public IdVerificationService(
            IIdVerificationRepository idRepo,
            IUserRepository userRepo,
            ILogger<IdVerificationService> logger)
        {
            _idRepo = idRepo;
            _userRepo = userRepo;
            _logger = logger;
        }

        public async Task<int> SubmitVerificationAsync(int clientId, string documentType, string storageKey, string? maskedIdentifier)
        {
            var user = await _userRepo.GetByIdAsync(clientId);
            if (user == null) throw new NotFoundException("User", clientId);

            // Mask identifier if provided (retain only last 4 chars)
            string? safeMasked = null;
            if (!string.IsNullOrWhiteSpace(maskedIdentifier))
            {
                string clean = maskedIdentifier.Trim().Replace(" ", "").Replace("-", "");
                safeMasked = clean.Length > 4 ? $"•••• •••• {clean[^4..]}" : clean;
            }

            var verification = new ClientIdVerification
            {
                ClientID = clientId,
                DocumentType = documentType,
                DocumentStorageKey = storageKey,
                MaskedIdentifier = safeMasked,
                VerificationStatus = "PendingReview",
                CreatedAt = DateTime.UtcNow
            };

            int id = await _idRepo.InsertVerificationAsync(verification);
            _logger.LogInformation("Identity verification document submitted for Client #{ClientId}, VerificationID #{VerificationId}", clientId, id);
            return id;
        }

        public async Task<ClientIdVerification?> GetClientVerificationAsync(int clientId, int requestingUserId, string userRole)
        {
            // Owner role MUST NEVER view raw ID verifications (Strict Shield)
            if (userRole.Equals("Owner", StringComparison.OrdinalIgnoreCase))
            {
                throw new ForbiddenException("Property Hosts are restricted from accessing guest identity documents for data privacy compliance.");
            }

            // Client can only view their own verification
            if (userRole.Equals("Client", StringComparison.OrdinalIgnoreCase) && clientId != requestingUserId)
            {
                throw new ForbiddenException("You cannot view another guest's identity verification.");
            }

            return await _idRepo.GetVerificationByClientIdAsync(clientId);
        }

        public async Task<List<ClientIdVerification>> GetAllVerificationsAsync(string? status = null)
        {
            return await _idRepo.GetAllVerificationsAsync(status);
        }

        public async Task<bool> ReviewVerificationAsync(int verificationId, string action, int staffUserId, string? rejectionReason = null)
        {
            var verification = await _idRepo.GetVerificationByIdAsync(verificationId);
            if (verification == null) throw new NotFoundException("Verification", verificationId);

            string status = action.Equals("Verify", StringComparison.OrdinalIgnoreCase) ? "Verified" : "Rejected";
            bool updated = await _idRepo.ReviewVerificationAsync(verificationId, status, staffUserId, rejectionReason);

            _logger.LogInformation("ID verification #{VerificationId} reviewed by Staff #{StaffId}: {Status}", verificationId, staffUserId, status);
            return updated;
        }

        public async Task<bool> IsClientVerifiedAsync(int clientId)
        {
            var verification = await _idRepo.GetVerificationByClientIdAsync(clientId);
            return verification != null && verification.VerificationStatus == "Verified";
        }
    }
}
