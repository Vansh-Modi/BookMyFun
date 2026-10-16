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
        Task<(bool success, string generatedPassword, string activationToken)> ApproveLeadAsync(int leadId, int staffUserId);
        Task<bool> RejectLeadAsync(int leadId, int staffUserId);
        Task<bool> ActivateOwnerAccountAsync(ActivateOwnerViewModel model);
    }

    public class PartnerLeadService : IPartnerLeadService
    {
        private readonly IPartnerLeadRepository _leadRepo;
        private readonly IUserRepository _userRepo;
        private readonly IEmailService _emailService;
        private readonly IWhatsAppService _whatsAppService;
        private readonly ILogger<PartnerLeadService> _logger;

        public PartnerLeadService(
            IPartnerLeadRepository leadRepo,
            IUserRepository userRepo,
            IEmailService emailService,
            IWhatsAppService whatsAppService,
            ILogger<PartnerLeadService> logger)
        {
            _leadRepo = leadRepo;
            _userRepo = userRepo;
            _emailService = emailService;
            _whatsAppService = whatsAppService;
            _logger = logger;
        }

        public async Task<int> CreateLeadAsync(PartnerLeadCreateViewModel model)
        {
            string partnerType = string.Equals(model.PartnerType, "EventManager", StringComparison.OrdinalIgnoreCase) 
                ? "EventManager" 
                : "Owner";

            var lead = new PartnerLead
            {
                FullName = model.FullName.Trim(),
                Phone = model.Phone.Trim(),
                Email = model.Email.Trim().ToLowerInvariant(),
                PropertyName = model.PropertyName.Trim(),
                Location = model.Location.Trim(),
                AcreageOrType = model.AcreageOrType.Trim(),
                Message = model.Message?.Trim(),
                PartnerType = partnerType,
                Status = "New",
                CreatedAt = DateTime.UtcNow
            };

            int leadId = await _leadRepo.InsertLeadAsync(lead);
            _logger.LogInformation("Partner {Type} lead #{LeadId} received from {Email}", partnerType, leadId, lead.Email);
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

        public async Task<(bool success, string generatedPassword, string activationToken)> ApproveLeadAsync(int leadId, int staffUserId)
        {
            var lead = await _leadRepo.GetLeadByIdAsync(leadId);
            if (lead == null) throw new NotFoundException("Partner Lead", leadId);

            if (lead.Status == "Approved")
                throw new ConflictException("Lead has already been approved.");

            bool isEventManager = string.Equals(lead.PartnerType, "EventManager", StringComparison.OrdinalIgnoreCase);
            string role = isEventManager ? "EventManager" : "Owner";
            string roleDisplayName = isEventManager ? "Event Manager & Décor Studio" : "Farmhouse & Villa Host";

            // Generate cryptographically random token & temporary initial password
            string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
            var expiresAt = DateTime.UtcNow.AddHours(48); // 48-hour expiration
            string generatedPassword = isEventManager
                ? $"Event@{RandomNumberGenerator.GetInt32(100000, 999999)}"
                : $"Host@{RandomNumberGenerator.GetInt32(100000, 999999)}";
            string passwordHash = HashPassword(generatedPassword);

            // Check if user already exists
            var existingUser = await _userRepo.GetByEmailAsync(lead.Email);
            if (existingUser == null)
            {
                var newUser = new User
                {
                    FullName = lead.FullName,
                    Email = lead.Email,
                    Phone = lead.Phone,
                    Role = role,
                    PasswordHash = passwordHash,
                    SubscriptionTier = "Free",
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                };
                await _userRepo.CreateUserAsync(newUser);
            }
            else
            {
                existingUser.Role = role;
                existingUser.PasswordHash = passwordHash;
                existingUser.IsActive = true;
                if (!string.IsNullOrEmpty(lead.Phone) && string.IsNullOrEmpty(existingUser.Phone))
                {
                    existingUser.Phone = lead.Phone;
                }
                await _userRepo.UpdateUserAsync(existingUser);
            }

            bool updated = await _leadRepo.UpdateLeadStatusAsync(leadId, "Approved", staffUserId, token, expiresAt);
            if (!updated) return (false, string.Empty, string.Empty);

            // Send activation and credentials notification via Email
            try
            {
                string headerColor = isEventManager ? "#d97706" : "#059669";
                string emailBody = $@"
                    <div style='font-family: Arial, sans-serif; line-height: 1.6; color: #333; max-width: 600px;'>
                        <h2 style='color: {headerColor};'>Welcome to BookMyFun! Your {roleDisplayName} Account is Approved</h2>
                        <p>Dear {lead.FullName},</p>
                        <p>Congratulations! Your partner application for <strong>'{lead.PropertyName}'</strong> has been reviewed and approved by our operations team.</p>
                        <div style='background-color: #f8fafc; border: 1px solid #cbd5e1; padding: 18px; border-radius: 8px; margin: 20px 0;'>
                            <h3 style='margin-top: 0; color: #0f172a;'>Your Login Credentials</h3>
                            <p style='margin-bottom: 8px;'><strong>Login Portal:</strong> <a href='/Account/Login'>Sign In to BookMyFun Portal</a></p>
                            <p style='margin-bottom: 8px;'><strong>User ID / Email:</strong> <code>{lead.Email}</code></p>
                            <p style='margin-bottom: 0;'><strong>Temporary Password:</strong> <code style='font-size: 1.1em; background: #e2e8f0; padding: 2px 6px; border-radius: 4px;'>{generatedPassword}</code></p>
                        </div>
                        <p>You can immediately sign in to your dedicated dashboard, manage your offerings, and start connecting with guests!</p>
                        <p style='color: #64748b; font-size: 0.9em;'>For security, please change your password after your first login via your Profile page.</p>
                        <hr style='border: none; border-top: 1px solid #e2e8f0; margin: 25px 0;' />
                        <p style='color: #64748b; font-size: 0.85em;'>The BookMyFun Operations &amp; Support Team</p>
                    </div>";

                _ = _emailService.SendEmailAsync(
                    lead.Email,
                    $"Partner Application Approved - Your BookMyFun {roleDisplayName} Credentials",
                    emailBody);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send partner credential email to {Email}", lead.Email);
            }

            // Send credentials via WhatsApp / SMS message
            try
            {
                if (!string.IsNullOrWhiteSpace(lead.Phone))
                {
                    string waMessage = $"🎉 Congratulations {lead.FullName}!\n\n" +
                                       $"Your BookMyFun Partner account ({roleDisplayName}) for '{lead.PropertyName}' has been approved!\n\n" +
                                       $"🔑 *Your Login Credentials:*\n" +
                                       $"• Portal: https://bookmyfun.in/Account/Login\n" +
                                       $"• Login ID: {lead.Email}\n" +
                                       $"• Password: {generatedPassword}\n\n" +
                                       $"Sign in now to access your dashboard. Welcome to BookMyFun!";

                    _ = _whatsAppService.SendWhatsAppMessageAsync(lead.Phone, waMessage);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send credential WhatsApp to {Phone}", lead.Phone);
            }

            return (true, generatedPassword, token);
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
