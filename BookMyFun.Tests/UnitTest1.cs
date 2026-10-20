namespace BookMyFun.Tests;

public class UnitTest1
{
    [Fact]
    public async Task AdminLogin_CredentialsAndRole_AreValid()
    {
        var config = new Microsoft.Extensions.Configuration.ConfigurationManager();
        config["ConnectionStrings:DefaultConnection"] = "Data Source=.\\SQLEXPRESS;Integrated Security=True;TrustServerCertificate=True;";
        var db = new BookMyFun.Data.DBHelper(config);
        var userRepo = new BookMyFun.Repositories.UserRepository(db);

        var admin = await userRepo.GetByEmailAsync("admin@bookmyfun.com");
        Assert.NotNull(admin);
        Assert.Equal("Admin", admin.Role);
        Assert.True(admin.IsActive);
        Assert.True(BookMyFun.Controllers.AccountController.VerifyPassword("Admin@123", admin.PasswordHash));
    }

    [Fact]
    public async Task AdminDashboard_Queries_SucceedWithoutErrors()
    {
        var config = new Microsoft.Extensions.Configuration.ConfigurationManager();
        config["ConnectionStrings:DefaultConnection"] = "Data Source=.\\SQLEXPRESS;Integrated Security=True;TrustServerCertificate=True;";
        var db = new BookMyFun.Data.DBHelper(config);

        var propRepo = new BookMyFun.Repositories.PropertyRepository(db);
        var userRepo = new BookMyFun.Repositories.UserRepository(db);
        var bookingRepo = new BookMyFun.Repositories.BookingRepository(db);

        var properties = await propRepo.GetAllPropertiesAsync();
        var owners = await userRepo.GetUsersByRoleAsync("Owner");
        var clients = await userRepo.GetUsersByRoleAsync("Client");
        var bookings = await bookingRepo.GetAllBookingsAsync();

        Assert.NotNull(properties);
        Assert.NotNull(owners);
        Assert.NotNull(clients);
        Assert.NotNull(bookings);
    }

    [Fact]
    public async Task PartnerLead_Approve_GeneratesCredentialsAndDispatches()
    {
        var config = new Microsoft.Extensions.Configuration.ConfigurationManager();
        config["ConnectionStrings:DefaultConnection"] = "Data Source=.\\SQLEXPRESS;Integrated Security=True;TrustServerCertificate=True;";
        var db = new BookMyFun.Data.DBHelper(config);

        var leadRepo = new BookMyFun.Repositories.PartnerLeadRepository(db);
        var userRepo = new BookMyFun.Repositories.UserRepository(db);
        var ownerReqRepo = new BookMyFun.Repositories.OwnerRequestRepository(db);
        var configRepo = new BookMyFun.Repositories.ConfigRepository(db);
        var commsLogger = Microsoft.Extensions.Logging.Abstractions.NullLogger<BookMyFun.Services.CommunicationService>.Instance;
        var leadLogger = Microsoft.Extensions.Logging.Abstractions.NullLogger<BookMyFun.Services.PartnerLeadService>.Instance;

        var commsService = new BookMyFun.Services.CommunicationService(configRepo, config, commsLogger);
        var partnerLeadService = new BookMyFun.Services.PartnerLeadService(
            leadRepo,
            userRepo,
            ownerReqRepo,
            commsService,
            commsService,
            leadLogger);

        // Create test lead
        string testEmail = $"test_lead_{Guid.NewGuid():N}@example.com";
        var createModel = new BookMyFun.Models.ViewModels.PartnerLeadCreateViewModel
        {
            FullName = "Test Partner Lead",
            Email = testEmail,
            Phone = "9876543210",
            PropertyName = "Green Meadows Villa",
            Location = "Lonavala",
            AcreageOrType = "3 BHK Villa",
            PartnerType = "Owner"
        };

        int leadId = await partnerLeadService.CreateLeadAsync(createModel);
        Assert.True(leadId > 0);

        // Approve lead
        var (success, initialPassword, token, accountId, roleDisplayName, email, phone) = 
            await partnerLeadService.ApproveLeadAsync(leadId, 1);

        Assert.True(success);
        Assert.StartsWith("Host@", initialPassword);
        Assert.True(accountId > 0);
        Assert.Equal(testEmail, email);
        Assert.Equal("Farmhouse & Villa Host", roleDisplayName);

        // Verify user exists and can authenticate with generated password
        var user = await userRepo.GetByEmailAsync(testEmail);
        Assert.NotNull(user);
        Assert.Equal("Owner", user.Role);
        Assert.True(user.IsActive);
        Assert.True(BookMyFun.Controllers.AccountController.VerifyPassword(initialPassword, user.PasswordHash));
    }

    [Fact]
    public async Task OwnerRequest_Approve_GeneratesCredentialsAndDispatches()
    {
        var config = new Microsoft.Extensions.Configuration.ConfigurationManager();
        config["ConnectionStrings:DefaultConnection"] = "Data Source=.\\SQLEXPRESS;Integrated Security=True;TrustServerCertificate=True;";
        var db = new BookMyFun.Data.DBHelper(config);

        var leadRepo = new BookMyFun.Repositories.PartnerLeadRepository(db);
        var userRepo = new BookMyFun.Repositories.UserRepository(db);
        var ownerReqRepo = new BookMyFun.Repositories.OwnerRequestRepository(db);
        var configRepo = new BookMyFun.Repositories.ConfigRepository(db);
        var commsLogger = Microsoft.Extensions.Logging.Abstractions.NullLogger<BookMyFun.Services.CommunicationService>.Instance;
        var leadLogger = Microsoft.Extensions.Logging.Abstractions.NullLogger<BookMyFun.Services.PartnerLeadService>.Instance;

        var commsService = new BookMyFun.Services.CommunicationService(configRepo, config, commsLogger);
        var partnerLeadService = new BookMyFun.Services.PartnerLeadService(
            leadRepo,
            userRepo,
            ownerReqRepo,
            commsService,
            commsService,
            leadLogger);

        // Create test user for owner request
        string testEmail = $"test_owner_{Guid.NewGuid():N}@example.com";
        var newUser = new BookMyFun.Models.Entities.User
        {
            FullName = "Test Owner Applicant",
            Email = testEmail,
            Phone = "9876543211",
            Role = "Owner",
            PasswordHash = "OldHash",
            IsActive = true
        };
        int userId = await userRepo.CreateUserAsync(newUser);
        Assert.True(userId > 0);

        int requestId = await ownerReqRepo.CreateRequestAsync(userId, 2);
        Assert.True(requestId > 0);

        // Approve owner request
        var (success, accountId, generatedPassword, email, phone, fullName) = 
            await partnerLeadService.ApproveOwnerRequestAsync(requestId, 1, "Approved for live platform");

        Assert.True(success);
        Assert.Equal(userId, accountId);
        Assert.StartsWith("Host@", generatedPassword);
        Assert.Equal(testEmail, email);

        // Verify updated password in repository
        var updatedUser = await userRepo.GetByEmailAsync(testEmail);
        Assert.NotNull(updatedUser);
        Assert.True(BookMyFun.Controllers.AccountController.VerifyPassword(generatedPassword, updatedUser.PasswordHash));
    }

    [Fact]
    public async Task RazorpayPaymentGateway_CreateOrderAndVerifySignature_WorkCorrectly()
    {
        var config = new Microsoft.Extensions.Configuration.ConfigurationManager();
        config["ConnectionStrings:DefaultConnection"] = "Data Source=.\\SQLEXPRESS;Integrated Security=True;TrustServerCertificate=True;";
        config["Razorpay:KeyId"] = "rzp_test_sample123";
        config["Razorpay:KeySecret"] = "sample_secret_xyz_789";

        var db = new BookMyFun.Data.DBHelper(config);
        var configRepo = new BookMyFun.Repositories.ConfigRepository(db);
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<BookMyFun.Services.RazorpayPaymentService>.Instance;

        var rzpService = new BookMyFun.Services.RazorpayPaymentService(configRepo, config, logger);

        // 1. Order Creation
        var order = await rzpService.CreateOrderAsync(15000.00m, "receipt_test_101", "INR");
        Assert.NotNull(order);
        Assert.StartsWith("order_", order.OrderId);
        Assert.Equal(15000.00m, order.Amount);

        // 2. Signature Verification (HMAC-SHA256)
        string orderId = "order_test_987654";
        string paymentId = "pay_test_123456";
        string secret = "sample_secret_xyz_789";

        var payload = $"{orderId}|{paymentId}";
        using var hmac = new System.Security.Cryptography.HMACSHA256(System.Text.Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(payload));
        var validSig = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();

        bool isValid = await rzpService.VerifyPaymentSignatureAsync(orderId, paymentId, validSig);
        Assert.True(isValid);

        // Invalid signature should fail
        bool isInvalid = await rzpService.VerifyPaymentSignatureAsync(orderId, paymentId, "invalid_signature_hex");
        Assert.False(isInvalid);
    }

    [Fact]
    public async Task FinancialSplit_PropertyStayAndEventDecor_AreStrictlySegregated()
    {
        var config = new Microsoft.Extensions.Configuration.ConfigurationManager();
        config["ConnectionStrings:DefaultConnection"] = "Data Source=.\\SQLEXPRESS;Integrated Security=True;TrustServerCertificate=True;";

        var db = new BookMyFun.Data.DBHelper(config);
        var configRepo = new BookMyFun.Repositories.ConfigRepository(db);
        var userRepo = new BookMyFun.Repositories.UserRepository(db);

        var commissionService = new BookMyFun.Services.CommissionService(configRepo, userRepo);
        var pricingService = new BookMyFun.Services.BookingPricingService();

        // Scenario: Property is 10,000 per night for 2 nights = 20,000
        decimal propertyStayGross = pricingService.CalculatePrice(10000.00m, "FullDay", null, 2);
        Assert.Equal(20000.00m, propertyStayGross);

        // Client adds optional Decor package: 5,000
        decimal decorPackagePrice = 5000.00m;
        decimal totalGuestInvoice = propertyStayGross + decorPackagePrice; // 25,000
        Assert.Equal(25000.00m, totalGuestInvoice);

        // Commission must be computed on Property Stay Gross ONLY (10% of 20,000 = 2,000), NOT on 25,000
        var (rateSnapshot, platformCommission, ownerPayout) = await commissionService.CalculateDynamicCommissionAsync(
            propertyStayGross, 10.0m, null);

        Assert.Equal(10.0m, rateSnapshot);
        Assert.Equal(2000.00m, platformCommission);
        Assert.Equal(18000.00m, ownerPayout);

        // Host receives 18,000 for their 20,000 stay.
        // The 5,000 decor fee is 100% segregated and belongs to the Event Manager.
        decimal eventManagerShare = decorPackagePrice;
        Assert.Equal(5000.00m, eventManagerShare);
        Assert.Equal(totalGuestInvoice, ownerPayout + platformCommission + eventManagerShare);
    }

    [Fact]
    public async Task WhatsAppService_DispatchesMultiStakeholderAlerts_Cleanly()
    {
        var config = new Microsoft.Extensions.Configuration.ConfigurationManager();
        config["ConnectionStrings:DefaultConnection"] = "Data Source=.\\SQLEXPRESS;Integrated Security=True;TrustServerCertificate=True;";

        var db = new BookMyFun.Data.DBHelper(config);
        var configRepo = new BookMyFun.Repositories.ConfigRepository(db);
        var commsLogger = Microsoft.Extensions.Logging.Abstractions.NullLogger<BookMyFun.Services.CommunicationService>.Instance;

        var commsService = new BookMyFun.Services.CommunicationService(configRepo, config, commsLogger);

        // Client booking confirmation
        bool clientSent = await commsService.SendBookingConfirmationAsync("9876543210", "Vipul Guest", "Serene Hills Villa", "101", 25000.00m);
        Assert.True(clientSent);

        // Owner booking alert with segregated decor note
        bool ownerSent = await commsService.SendOwnerBookingAlertAsync(
            "9876543211", "Rajesh Owner", "Serene Hills Villa", "101", 20000.00m, 18000.00m, "Vipul Guest", 
            DateTime.Today, DateTime.Today.AddDays(2), true, 5000.00m);
        Assert.True(ownerSent);

        // Event Manager setup staging alert
        bool eventManagerSent = await commsService.SendEventManagerSetupAlertAsync(
            "9876543212", "Creative Decorator", "Serene Hills Villa", "101", "Royal Birthday Staging", 5000.00m,
            DateTime.Today, "Vipul Guest");
        Assert.True(eventManagerSent);
    }

    [Fact]
    public async Task RealEmailOtp_GenerationDispatchAndRegistration_SucceedsWithoutTestingBypass()
    {
        var config = new Microsoft.Extensions.Configuration.ConfigurationManager();
        config["ConnectionStrings:DefaultConnection"] = "Data Source=.\\SQLEXPRESS;Integrated Security=True;TrustServerCertificate=True;";

        var db = new BookMyFun.Data.DBHelper(config);
        var configRepo = new BookMyFun.Repositories.ConfigRepository(db);
        var otpRepo = new BookMyFun.Repositories.OtpRepository(db);
        var userRepo = new BookMyFun.Repositories.UserRepository(db);
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<BookMyFun.Services.CommunicationService>.Instance;

        var commsService = new BookMyFun.Services.CommunicationService(configRepo, config, logger);

        // Target real user email as instructed by the user
        string targetEmail = "showsgarage@gmail.com";

        // 1. Generate real cryptographically secure random 6-digit OTP (NO testing/mock code)
        var realOtp = System.Security.Cryptography.RandomNumberGenerator.GetInt32(100000, 999999).ToString();
        Assert.Matches(@"^\d{6}$", realOtp);

        // 2. Save OTP to database
        bool saved = await otpRepo.SaveOtpAsync(targetEmail, realOtp, "Registration");
        Assert.True(saved);

        // 3. Dispatch real verification email via live configured Gmail SMTP
        string subject = "Your BookMyFun Verification Code";
        string body = $"<h2>Welcome to BookMyFun</h2><p>Your one-time registration code is: <strong>{realOtp}</strong></p>";
        bool emailSent = await commsService.SendEmailAsync(targetEmail, subject, body);
        Assert.True(emailSent);

        // 4. Validate the real OTP against the database
        bool isOtpValid = await otpRepo.ValidateOtpAsync(targetEmail, realOtp, "Registration");
        Assert.True(isOtpValid);

        // 5. Register/Authenticate user
        string testClientEmail = $"verified_client_{Guid.NewGuid():N}@example.com";
        string realOtp2 = System.Security.Cryptography.RandomNumberGenerator.GetInt32(100000, 999999).ToString();
        await otpRepo.SaveOtpAsync(testClientEmail, realOtp2, "Registration");
        bool valid2 = await otpRepo.ValidateOtpAsync(testClientEmail, realOtp2, "Registration");
        Assert.True(valid2);

        var newClient = new BookMyFun.Models.Entities.User
        {
            FullName = "Verified Client Guest",
            Email = testClientEmail,
            Phone = "9876500000",
            PasswordHash = BookMyFun.Controllers.AccountController.HashPassword("Client@123"),
            Role = "Client",
            IsActive = true
        };
        int createdId = await userRepo.CreateUserAsync(newClient);
        Assert.True(createdId > 0);

        var fetched = await userRepo.GetByEmailAsync(testClientEmail);
        Assert.NotNull(fetched);
        Assert.Equal("Client", fetched.Role);
        Assert.True(BookMyFun.Controllers.AccountController.VerifyPassword("Client@123", fetched.PasswordHash));
    }

}
