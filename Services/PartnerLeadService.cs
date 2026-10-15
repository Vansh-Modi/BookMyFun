using BookMyFun.Models.Entities;
using BookMyFun.Models.ViewModels;
using BookMyFun.Repositories;
using BookMyFun.Services.Exceptions;
using System.Security.Cryptography;
using System.Text;

namespace BookMyFun.Services
{
    public interface IPartnerLeadService
    {
        Task<int> CreateLeadAsync(PartnerLeadCreateViewModel model);
        Task<List<PartnerLead>> GetAllLeadsAsync(string? status = null);
        Task<PartnerLead> GetLeadByIdAsync(int leadId);
        Task<(bool success, string activationToken)> ApproveLeadAsync(int leadId, int staffUserId);
        Task<bool> RejectLeadAsync(int leadId, int staffUserId);
        Task<bool> ActivateOwnerAccountAsync(ActivateOwnerViewModel model);
    }

    public class PartnerLeadService : IPartnerLeadService
    {
        private readonly IPartnerLeadRepository _leadRepo;
        private readonly IUserRepository _userRepo;
        private readonly IEmailService _emailService;
        private readonly ILogger<PartnerLeadService> _logger;

        public PartnerLeadService(
            IPartnerLeadRepository leadRepo,
            IUserRepository userRepo,
            IEmailService emailService,
            ILogger<PartnerLeadService> logger)
        {
            _leadRepo = leadRepo;
            _userRepo = userRepo;
            _emailService = emailService;
            _logger = logger;
        }

        public async Task<int> CreateLeadAsync(PartnerLeadCreateViewModel model)
        {
            var lead = new PartnerLead
            {
                FullName = model.FullName.Trim(),
                Phone = model.Phone.Trim(),
                Email = model.Email.Trim().ToLowerInvariant(),
                PropertyName = model.PropertyName.Trim(),
                Location = model.Location.Trim(),
                AcreageOrType = model.AcreageOrType.Trim(),
                Message = model.Message?.Trim(),
                Status = "New",
                CreatedAt = DateTime.UtcNow
            };

            int leadId = await _leadRepo.InsertLeadAsync(lead);
            _logger.LogInformation("Partner host lead #{LeadId} received from {Email}", leadId, lead.Email);
            return leadId;
        }

        public async Task<List<PartnerLead>> GetAllLeadsAsync(string? status = null)
        {
            return await _leadRepo.GetAllLeadsAsync(status);
        }

        public async Task<PartnerLead> GetLeadByIdAsync(int leadId)
        {
            var lead = await _leadRepo.GetLeadByIdAsync(leadId);
            if (lead == null) throw new NotFoundException("Partner Lead", leadId);
            return lead;
        }

        public async Task<(bool success, string activationToken)> ApproveLeadAsync(int leadId, int staffUserId)
        {
            var lead = await _leadRepo.GetLeadByIdAsync(leadId);
            if (lead == null) throw new NotFoundException("Partner Lead", leadId);

            if (lead.Status == "Approved")
                throw new ConflictException("Lead has already been approved.");

            // Generate cryptographically random token
            string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
            var expiresAt = DateTime.UtcNow.AddHours(48); // 48-hour expiration

            // Check if user already exists
            var existingUser = await _userRepo.GetByEmailAsync(lead.Email);
            if (existingUser == null)
            {
                var newUser = new User
                {
                    FullName = lead.FullName,
                    Email = lead.Email,
                    Phone = lead.Phone,
                    Role = "Owner",
                    PasswordHash = HashPassword(Guid.NewGuid().ToString()), // Temporary random hash until activation
                    SubscriptionTier = "Free",
                    CreatedAt = DateTime.UtcNow
                };
                await _userRepo.CreateUserAsync(newUser);
            }

            bool updated = await _leadRepo.UpdateLeadStatusAsync(leadId, "Approved", staffUserId, token, expiresAt);
            if (!updated) return (false, string.Empty);

            // Send activation notification
            try
            {
                string activationLink = $"/Account/ActivateHost?token={token}&email={Uri.EscapeDataString(lead.Email)}";
                string emailBody = $@"
                    <h2>Partner Host Application Approved!</h2>
                    <p>Dear {lead.FullName},</p>
                    <p>Congratulations! Your application to partner with BookMyFun has been approved by our operations team.</p>
                    <p>Please activate your host dashboard using the link below (valid for 48 hours):</p>
                    <p><a href='{activationLink}'>Activate Your Host Account Now</a></p>
                    <p>Welcome aboard!<br/>The BookMyFun Team</p>";

                _ = _emailService.SendEmailAsync(
                    lead.Email,
                    "Partner Host Application Approved - Activate Your BookMyFun Host Account",
                    emailBody);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send activation email to {Email}", lead.Email);
            }

            return (true, token);
        }

        public async Task<bool> RejectLeadAsync(int leadId, int staffUserId)
        {
            var lead = await _leadRepo.GetLeadByIdAsync(leadId);
            if (lead == null) throw new NotFoundException("Partner Lead", leadId);

            return await _leadRepo.UpdateLeadStatusAsync(leadId, "Rejected", staffUserId);
        }

        public async Task<bool> ActivateOwnerAccountAsync(ActivateOwnerViewModel model)
        {
            var lead = await _leadRepo.GetLeadByTokenAsync(model.Token);
            if (lead == null)
                throw new ValidationException("Token", "Invalid activation token.");

            if (!string.Equals(lead.Email, model.Email, StringComparison.OrdinalIgnoreCase))
                throw new ValidationException("Email", "The email address does not match this activation token.");

            if (lead.TokenExpiresAt.HasValue && DateTime.UtcNow > lead.TokenExpiresAt.Value)
                throw new ValidationException("Token", "Activation token has expired. Please contact support or request a new invite.");

            var user = await _userRepo.GetByEmailAsync(model.Email);
            if (user == null)
            {
                // Create user if not created yet
                user = new User
                {
                    FullName = lead.FullName,
                    Email = lead.Email,
                    Phone = lead.Phone,
                    Role = "Owner",
                    PasswordHash = HashPassword(model.Password),
                    SubscriptionTier = "Free"
                };
                await _userRepo.CreateUserAsync(user);
            }
            else
            {
                string newHash = HashPassword(model.Password);
                await _userRepo.ResetPasswordAsync(user.Email, newHash);
            }

            await _leadRepo.MarkTokenUsedAsync(lead.LeadID);
            _logger.LogInformation("Host account {Email} activated successfully.", model.Email);
            return true;
        }

        private static string HashPassword(string password)
        {
            using var sha = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(password);
            var hash = sha.ComputeHash(bytes);
            return Convert.ToBase64String(hash);
        }
    }
}
