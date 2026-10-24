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
        Task<(bool success, string generatedPassword, string activationToken, int accountId, string roleDisplayName, string email, string? phone)> ApproveLeadAsync(int leadId, int staffUserId);
        Task<(bool success, int userId, string generatedPassword, string email, string? phone, string fullName)> ApproveOwnerRequestAsync(int requestId, int staffUserId, string? notes);
        Task<bool> RejectLeadAsync(int leadId, int staffUserId);
        Task<bool> ActivateOwnerAccountAsync(ActivateOwnerViewModel model);
    }

    public class PartnerLeadService : IPartnerLeadService
    {
        private readonly IPartnerLeadRepository _leadRepo;
        private readonly IUserRepository _userRepo;
        private readonly IOwnerRequestRepository _ownerReqRepo;
        private readonly IEmailService _emailService;
        private readonly IWhatsAppService _whatsAppService;
        private readonly ILogger<PartnerLeadService> _logger;
        private readonly IConfiguration? _configuration;

        public PartnerLeadService(
            IPartnerLeadRepository leadRepo,
            IUserRepository userRepo,
            IOwnerRequestRepository ownerReqRepo,
            IEmailService emailService,
            IWhatsAppService whatsAppService,
            ILogger<PartnerLeadService> logger,
            IConfiguration? configuration = null)
        {
            _leadRepo = leadRepo;
            _userRepo = userRepo;
            _ownerReqRepo = ownerReqRepo;
            _emailService = emailService;
            _whatsAppService = whatsAppService;
            _logger = logger;
            _configuration = configuration;
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

        public async Task<(bool success, string generatedPassword, string activationToken, int accountId, string roleDisplayName, string email, string? phone)> ApproveLeadAsync(int leadId, int staffUserId)
        {
            var lead = await _leadRepo.GetLeadByIdAsync(leadId);
            if (lead == null) throw new NotFoundException("Partner Lead", leadId);

            if (lead.Status == "Approved")
                throw new ConflictException("Lead has already been approved.");

            bool isEventManager = string.Equals(lead.PartnerType, "EventManager", StringComparison.OrdinalIgnoreCase);
            string role = isEventManager ? "EventManager" : "Owner";
            string roleDisplayName = isEventManager ? "Event Manager & Décor Studio" : "Farmhouse & Villa Host";

            // Generate cryptographically random single-use token valid for 48 hours
            string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
            var expiresAt = DateTime.UtcNow.AddHours(48); // 48-hour expiration
            string internalSecret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            string passwordHash = HashPassword(internalSecret);

            // Check if user already exists
            int accountId;
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
                accountId = await _userRepo.CreateUserAsync(newUser);
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
                accountId = existingUser.UserID;
            }

            bool updated = await _leadRepo.UpdateLeadStatusAsync(leadId, "Approved", staffUserId, token, expiresAt);
            if (!updated) return (false, string.Empty, string.Empty, 0, roleDisplayName, lead.Email, lead.Phone);

            string baseUrl = (_configuration?["AppUrl"] ?? "https://bookmyfun.in").TrimEnd('/');
            string setupUrl = $"{baseUrl}/Account/ActivateHost?token={token}&email={Uri.EscapeDataString(lead.Email)}";

            // Send activation and login details notification via Email (with 48-hour single-use password setup link)
            try
            {
                string headerColor = isEventManager ? "#d97706" : "#059669";
                string emailBody = $@"
                    <div style='font-family: -apple-system, BlinkMacSystemFont, ""Segoe UI"", Roboto, Helvetica, Arial, sans-serif; line-height: 1.6; color: #1e293b; max-width: 600px; margin: 0 auto; padding: 24px; border: 1px solid #e2e8f0; border-radius: 12px; background: #ffffff;'>
                        <div style='border-bottom: 2px solid {headerColor}; padding-bottom: 16px; margin-bottom: 20px;'>
                            <h2 style='color: {headerColor}; margin: 0 0 6px 0; font-size: 22px;'>Welcome to BookMyFun! Your {roleDisplayName} Account is Approved</h2>
                            <p style='margin: 0; color: #64748b; font-size: 14px;'>Partner Onboarding &amp; Verification</p>
                        </div>
                        <p style='font-size: 15px;'>Dear <strong>{lead.FullName}</strong>,</p>
                        <p style='font-size: 14px; color: #334155;'>Congratulations! Your partner application for <strong>'{lead.PropertyName}'</strong> has been reviewed and officially approved by our operations team.</p>
                        <div style='background-color: #f8fafc; border: 1px solid #cbd5e1; padding: 20px; border-radius: 8px; margin: 20px 0;'>
                            <h3 style='margin-top: 0; color: #0f172a; font-size: 16px;'>Your Partner Login Details</h3>
                            <p style='margin-bottom: 8px;'><strong>Account ID:</strong> <code>#{accountId}</code></p>
                            <p style='margin-bottom: 8px;'><strong>Registered Login ID / Email:</strong> <code>{lead.Email}</code></p>
                            <p style='margin-bottom: 16px;'><strong>Password Setup:</strong> For your security, no permanent or plaintext password is transmitted via email or message. Please choose your password using the secure one-time link below.</p>
                            <div style='text-align: center; margin: 24px 0;'>
                                <a href='{setupUrl}' style='display: inline-block; background-color: {headerColor}; color: #ffffff; text-decoration: none; padding: 12px 28px; font-weight: bold; border-radius: 6px; font-size: 15px;'>Set Your Password &amp; Activate Account</a>
                            </div>
                            <p style='font-size: 12px; color: #64748b; margin-bottom: 0;'>🔒 <strong>Security Notice:</strong> This single-use setup link is valid for <strong>48 hours</strong>. It will expire once used.</p>
                        </div>
                        <p style='font-size: 13px; color: #64748b;'>If the button above does not work, copy and paste this link in your browser:<br/><span style='word-break: break-all; color: #0284c7;'>{setupUrl}</span></p>
                        <hr style='border: none; border-top: 1px solid #e2e8f0; margin: 25px 0;' />
                        <p style='color: #64748b; font-size: 0.85em;'>The BookMyFun Operations &amp; Support Team</p>
                    </div>";

                await _emailService.EnqueueEmailAsync(
                    lead.Email,
                    $"Partner Application Approved - Your BookMyFun {roleDisplayName} Login Details",
                    emailBody,
                    "PartnerApprovalCredentials");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send partner credential email to {Email}", lead.Email);
            }

            // Send login details via WhatsApp with single-use setup link
            try
            {
                if (!string.IsNullOrWhiteSpace(lead.Phone))
                {
                    string waMessage = $"🎉 Congratulations {lead.FullName}!\n\n" +
                                       $"Your BookMyFun Partner account ({roleDisplayName}) for '{lead.PropertyName}' has been approved!\n\n" +
                                       $"📋 *Your Login Details:*\n" +
                                       $"• Account ID: #{accountId}\n" +
                                       $"• Login Email / ID: {lead.Email}\n\n" +
                                       $"🔐 *Set Your Password:*\n" +
                                       $"For security, no permanent password is sent in messages. Please set your password using this secure one-time link (valid for 48 hours):\n" +
                                       $"{setupUrl}\n\n" +
                                       $"Welcome to the BookMyFun Partner Network!";

                    await _whatsAppService.EnqueueWhatsAppAsync(lead.Phone, waMessage, "PartnerApprovalCredentials");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send credential WhatsApp to {Phone}", lead.Phone);
            }

            return (true, string.Empty, token, accountId, roleDisplayName, lead.Email, lead.Phone);
        }

        public async Task<(bool success, int userId, string generatedPassword, string email, string? phone, string fullName)> ApproveOwnerRequestAsync(int requestId, int staffUserId, string? notes)
        {
            var req = await _ownerReqRepo.GetRequestByIdAsync(requestId);
            if (req == null)
                throw new NotFoundException("Owner Request", requestId);

            if (req.Status == "Approved")
                throw new ConflictException("Owner request has already been approved.");

            // Generate secure temporary initial password for approved owner
            string generatedPassword = $"Host@{RandomNumberGenerator.GetInt32(100000, 999999)}";
            string passwordHash = HashPassword(generatedPassword);

            bool approved = await _ownerReqRepo.ApproveOwnerRequestAsync(requestId, staffUserId, notes, passwordHash);
            if (!approved)
                return (false, req.UserID, string.Empty, req.Email ?? string.Empty, req.Phone, req.FullName ?? "Owner");

            string userEmail = req.Email ?? string.Empty;
            string userPhone = req.Phone ?? string.Empty;
            string fullName = req.FullName ?? "Host Partner";
            int accountId = req.UserID;

            // Dispatch Credentials via Email
            if (!string.IsNullOrWhiteSpace(userEmail))
            {
                try
                {
                    string emailBody = $@"
                        <div style='font-family: Arial, sans-serif; line-height: 1.6; color: #333; max-width: 600px;'>
                            <h2 style='color: #059669;'>Welcome to BookMyFun! Your Owner Account is Approved</h2>
                            <p>Dear {fullName},</p>
                            <p>Congratulations! Your Host / Owner application has been reviewed and officially approved. Your property listings are now published and live on the platform.</p>
                            <div style='background-color: #f8fafc; border: 1px solid #cbd5e1; padding: 18px; border-radius: 8px; margin: 20px 0;'>
                                <h3 style='margin-top: 0; color: #0f172a;'>Your Host Account Credentials</h3>
                                <p style='margin-bottom: 8px;'><strong>Login Portal:</strong> <a href='https://bookmyfun.in/Account/Login'>Sign In to BookMyFun Portal</a></p>
                                <p style='margin-bottom: 8px;'><strong>Account ID:</strong> <code>#{accountId}</code></p>
                                <p style='margin-bottom: 8px;'><strong>Login Email / ID:</strong> <code>{userEmail}</code></p>
                                <p style='margin-bottom: 0;'><strong>Password:</strong> <code style='font-size: 1.1em; background: #e2e8f0; padding: 2px 6px; border-radius: 4px;'>{generatedPassword}</code></p>
                            </div>
                            <p>You can immediately sign in with your Account ID or Email to manage your properties, calendars, and guest bookings.</p>
                            <p style='color: #64748b; font-size: 0.9em;'>For security, please change your password after logging in via your Account Profile.</p>
                            <hr style='border: none; border-top: 1px solid #e2e8f0; margin: 25px 0;' />
                            <p style='color: #64748b; font-size: 0.85em;'>The BookMyFun Operations &amp; Support Team</p>
                        </div>";

                    await _emailService.EnqueueEmailAsync(
                        userEmail,
                        "Owner Application Approved - Your BookMyFun Host Credentials",
                        emailBody,
                        "OwnerApprovalCredentials");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to send owner credential email to {Email}", userEmail);
                }
            }

            // Dispatch Credentials via WhatsApp API
            if (!string.IsNullOrWhiteSpace(userPhone))
            {
                try
                {
                    string waMessage = $"🎉 Congratulations {fullName}!\n\n" +
                                       $"Your BookMyFun Owner / Host account application has been approved! Your property listings are now live.\n\n" +
                                       $"🔑 *Your Login Credentials:*\n" +
                                       $"• Portal: https://bookmyfun.in/Account/Login\n" +
                                       $"• Account ID: #{accountId}\n" +
                                       $"• Login Email / ID: {userEmail}\n" +
                                       $"• Password: {generatedPassword}\n\n" +
                                       $"Sign in now to manage your properties and bookings. Welcome to the BookMyFun Host Community!";

                    await _whatsAppService.EnqueueWhatsAppAsync(userPhone, waMessage, "OwnerApprovalCredentials");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to send owner credential WhatsApp to {Phone}", userPhone);
                }
            }

            return (true, accountId, generatedPassword, userEmail, userPhone, fullName);
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
                throw new ValidationException("Token", "Invalid activation token or link has already been used.");

            if (!string.Equals(lead.Email, model.Email, StringComparison.OrdinalIgnoreCase))
                throw new ValidationException("Email", "The email address does not match this activation token.");

            if (lead.TokenExpiresAt.HasValue && DateTime.UtcNow > lead.TokenExpiresAt.Value)
                throw new ValidationException("Token", "Activation link has expired (valid for 48 hours). Please contact support for a new activation link.");

            string role = string.Equals(lead.PartnerType, "EventManager", StringComparison.OrdinalIgnoreCase) ? "EventManager" : "Owner";

            var user = await _userRepo.GetByEmailAsync(model.Email);
            if (user == null)
            {
                // Create user if not created yet
                user = new User
                {
                    FullName = lead.FullName,
                    Email = lead.Email,
                    Phone = lead.Phone,
                    Role = role,
                    PasswordHash = HashPassword(model.Password),
                    SubscriptionTier = "Free",
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                };
                await _userRepo.CreateUserAsync(user);
            }
            else
            {
                string newHash = HashPassword(model.Password);
                user.Role = role;
                user.PasswordHash = newHash;
                user.IsActive = true;
                await _userRepo.ResetPasswordAsync(user.Email, newHash);
                await _userRepo.UpdateUserAsync(user);
            }

            await _leadRepo.MarkTokenUsedAsync(lead.LeadID);
            _logger.LogInformation("Partner account {Email} ({Role}) password set and activated successfully via single-use token.", model.Email, role);
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
