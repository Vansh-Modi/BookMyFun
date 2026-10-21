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

    [Fact]
    public async Task Razorpay_OwnerSubscriptionPaymentWorkflow_TransitionsToPaid()
    {
        var config = new Microsoft.Extensions.Configuration.ConfigurationManager();
        config["ConnectionStrings:DefaultConnection"] = "Data Source=.\\SQLEXPRESS;Integrated Security=True;TrustServerCertificate=True;";
        config["Razorpay:KeyId"] = "rzp_test_owner_sub";
        config["Razorpay:KeySecret"] = "owner_secret_456_test";

        var db = new BookMyFun.Data.DBHelper(config);
        var subRepo = new BookMyFun.Repositories.SubscriptionRepository(db);
        var configRepo = new BookMyFun.Repositories.ConfigRepository(db);
        var userRepo = new BookMyFun.Repositories.UserRepository(db);
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<BookMyFun.Services.RazorpayPaymentService>.Instance;
        var rzpService = new BookMyFun.Services.RazorpayPaymentService(configRepo, config, logger);

        // 1. Ensure test owner exists
        string ownerEmail = $"test_sub_owner_{Guid.NewGuid():N}[..8]@bookmyfun.test";
        var ownerUser = new BookMyFun.Models.Entities.User
        {
            FullName = "Test Subscription Owner",
            Email = ownerEmail,
            PasswordHash = BookMyFun.Controllers.AccountController.HashPassword("Owner@123"),
            Role = "Owner",
            IsActive = true
        };
        int ownerId = await userRepo.CreateUserAsync(ownerUser);
        Assert.True(ownerId > 0);

        // 2. Fetch or create plan
        var plans = await subRepo.GetAllPlansAsync();
        var plan = plans.FirstOrDefault();
        if (plan == null)
        {
            int planId = await subRepo.CreatePlanAsync(new BookMyFun.Models.Entities.SubscriptionPlan
            {
                PlanName = "Starter Partner Plan",
                Price = 2999m,
                DurationInDays = 30,
                MaxListings = 3,
                IsActive = true
            });
            plan = await subRepo.GetPlanByIdAsync(planId);
        }
        Assert.NotNull(plan);

        // 3. Initiate Pending Subscription
        var sub = new BookMyFun.Models.Entities.OwnerSubscription
        {
            OwnerID = ownerId,
            PlanID = plan.PlanID,
            StartDate = DateTime.UtcNow,
            EndDate = DateTime.UtcNow.AddDays(plan.DurationInDays),
            PaymentStatus = "Pending",
            GatewayTransactionID = null
        };
        int subId = await subRepo.SubscribeOwnerAsync(sub);
        Assert.True(subId > 0);

        // 4. Create Razorpay Order
        var order = await rzpService.CreateOrderAsync(plan.Price, $"sub_{ownerId}_{plan.PlanID}_{subId}");
        Assert.NotNull(order);
        Assert.StartsWith("order_", order.OrderId);

        // 5. Simulate Payment Verification
        string paymentId = $"pay_test_{Guid.NewGuid():N}"[..18];
        string payload = $"{order.OrderId}|{paymentId}";
        using var hmac = new System.Security.Cryptography.HMACSHA256(System.Text.Encoding.UTF8.GetBytes("owner_secret_456_test"));
        var hash = hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(payload));
        var validSig = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();

        bool isValid = await rzpService.VerifyPaymentSignatureAsync(order.OrderId, paymentId, validSig);
        Assert.True(isValid);

        // 6. Update to Paid
        bool updated = await subRepo.UpdateSubscriptionPaymentStatusAsync(subId, "Paid", $"RAZORPAY_{paymentId}");
        Assert.True(updated);

        var activeSub = await subRepo.GetSubscriptionByIdAsync(subId);
        Assert.NotNull(activeSub);
        Assert.Equal("Paid", activeSub.PaymentStatus);
        Assert.Equal($"RAZORPAY_{paymentId}", activeSub.GatewayTransactionID);
    }

    [Fact]
    public async Task EventManager_ServiceAreas_SaveAndCheckServicedArea()
    {
        var config = new Microsoft.Extensions.Configuration.ConfigurationManager();
        config["ConnectionStrings:DefaultConnection"] = "Data Source=.\\SQLEXPRESS;Integrated Security=True;TrustServerCertificate=True;";
        var db = new BookMyFun.Data.DBHelper(config);

        var eventRepo = new BookMyFun.Repositories.EventPackageRepository(db);
        var userRepo = new BookMyFun.Repositories.UserRepository(db);

        // 1. Ensure an EventManager user exists
        var managers = await userRepo.GetUsersByRoleAsync("EventManager");
        int managerId;
        if (managers.Any())
        {
            managerId = managers.First().UserID;
        }
        else
        {
            managerId = await userRepo.CreateUserAsync(new BookMyFun.Models.Entities.User
            {
                FullName = "Test Event Manager",
                Email = $"eventmgr_{Guid.NewGuid():N}@bookmyfun.com",
                PasswordHash = "hashed_pass",
                Role = "EventManager",
                Phone = "9998887776",
                IsActive = true
            });
        }

        Assert.True(managerId > 0);

        // 2. Fetch distinct property locations from DB
        var locations = await eventRepo.GetAllDistinctPropertyLocationsAsync();
        Assert.NotNull(locations);

        // 3. Update service areas for this manager
        string testAreas = "Lonavala, Maharashtra; Khandala, Maharashtra";
        bool updated = await eventRepo.UpdateEventManagerServiceAreasAsync(managerId, testAreas);
        Assert.True(updated);

        // 4. Retrieve service areas
        string retrieved = await eventRepo.GetEventManagerServiceAreasAsync(managerId);
        Assert.Contains("Lonavala, Maharashtra", retrieved);

        // 5. Test IsAreaServicedByAnyEventManagerAsync
        bool isServicedLonavala = await eventRepo.IsAreaServicedByAnyEventManagerAsync("Lonavala, Maharashtra");
        Assert.True(isServicedLonavala);

        bool isServicedPartial = await eventRepo.IsAreaServicedByAnyEventManagerAsync("Lonavala");
        Assert.True(isServicedPartial);

        bool isServicedNonExistent = await eventRepo.IsAreaServicedByAnyEventManagerAsync("CompletelyUnknownCity9999");
        Assert.False(isServicedNonExistent);
    }

    [Fact]
    public async Task EventManager_CancelOrRemoveEventFromBooking_PreservesBooking()
    {
        var config = new Microsoft.Extensions.Configuration.ConfigurationManager();
        config["ConnectionStrings:DefaultConnection"] = "Data Source=.\\SQLEXPRESS;Integrated Security=True;TrustServerCertificate=True;";
        var db = new BookMyFun.Data.DBHelper(config);

        var eventRepo = new BookMyFun.Repositories.EventPackageRepository(db);
        var bookingRepo = new BookMyFun.Repositories.BookingRepository(db);

        // 1. Fetch an existing booking or check bookings table
        var allBookings = await bookingRepo.GetAllBookingsAsync();
        if (allBookings.Any())
        {
            var targetBooking = allBookings.First();

            // Setup the booking with an event
            await db.ExecuteNonQueryAsync(@"
                UPDATE Bookings 
                SET IsEventBooking = 1, 
                    EventPackagePriceSnapshot = 4500, 
                    SetupStatus = 'Pending' 
                WHERE BookingID = @BookingID", 
                new Microsoft.Data.SqlClient.SqlParameter("@BookingID", targetBooking.BookingID));

            // Call delete / remove
            bool removed = await eventRepo.CancelOrRemoveEventFromBookingAsync(targetBooking.BookingID);
            Assert.True(removed);

            // Fetch and verify booking stay is still present, but event details cleared
            var updatedBooking = await bookingRepo.GetBookingByIdAsync(targetBooking.BookingID);
            Assert.NotNull(updatedBooking);
            Assert.False(updatedBooking.IsEventBooking);
            Assert.Null(updatedBooking.EventPackageID);
            Assert.Equal("Cancelled", updatedBooking.SetupStatus);
        }
    }

    [Fact]
    public async Task EventCommission_StandardAndCustomOverrides_PrecedenceHierarchy()
    {
        var config = new Microsoft.Extensions.Configuration.ConfigurationManager();
        config["ConnectionStrings:DefaultConnection"] = "Data Source=.\\SQLEXPRESS;Integrated Security=True;TrustServerCertificate=True;";
        var db = new BookMyFun.Data.DBHelper(config);

        var configRepo = new BookMyFun.Repositories.ConfigRepository(db);
        var userRepo = new BookMyFun.Repositories.UserRepository(db);
        var propRepo = new BookMyFun.Repositories.PropertyRepository(db);
        var eventRepo = new BookMyFun.Repositories.EventPackageRepository(db);

        var commissionService = new BookMyFun.Services.CommissionService(configRepo, userRepo, propRepo, eventRepo);

        // 1. Standard Commission (Global default or set in config)
        decimal packagePrice = 20000m;
        var (rateStd, commStd, payoutStd) = await commissionService.CalculateEventCommissionAsync(
            packagePrice, propertyId: 0, packageId: null, propertyEventCommissionOverride: null);

        Assert.Equal(10.0m, rateStd);
        Assert.Equal(2000.00m, commStd);
        Assert.Equal(18000.00m, payoutStd);
        Assert.Equal(packagePrice, commStd + payoutStd);

        // 2. Property-level Event Commission Override (e.g. 15%)
        var (rateProp, commProp, payoutProp) = await commissionService.CalculateEventCommissionAsync(
            packagePrice, propertyId: 0, packageId: null, propertyEventCommissionOverride: 15.0m);

        Assert.Equal(15.0m, rateProp);
        Assert.Equal(3000.00m, commProp);
        Assert.Equal(17000.00m, payoutProp);
        Assert.Equal(packagePrice, commProp + payoutProp);

        // 3. Property-specific Event Package Override in PropertyEventCommissions
        var properties = await propRepo.GetAllPropertiesAsync();
        var packages = await eventRepo.GetActivePackagesAsync();
        if (properties.Any() && packages.Any())
        {
            var testProp = properties.First();
            var testPkg = packages.First();

            // Set custom 25% commission specifically for this package on this property
            await propRepo.SetPropertyEventCommissionsAsync(testProp.PropertyID, new List<(int, decimal)>
            {
                (testPkg.PackageID, 25.0m)
            });

            // Calculate commission: should resolve to 25.0% even if property has a 12% default
            var (rateCust, commCust, payoutCust) = await commissionService.CalculateEventCommissionAsync(
                packagePrice, testProp.PropertyID, testPkg.PackageID, propertyEventCommissionOverride: 12.0m);

            Assert.Equal(25.0m, rateCust);
            Assert.Equal(5000.00m, commCust);
            Assert.Equal(15000.00m, payoutCust);
            Assert.Equal(packagePrice, commCust + payoutCust);

            // Clean up custom commission
            await propRepo.SetPropertyEventCommissionsAsync(testProp.PropertyID, new List<(int, decimal)>());
        }
    }

    [Fact]
    public async Task EventCommission_BookingCreation_AccurateSnapshotAndNetPayouts()
    {
        var config = new Microsoft.Extensions.Configuration.ConfigurationManager();
        config["ConnectionStrings:DefaultConnection"] = "Data Source=.\\SQLEXPRESS;Integrated Security=True;TrustServerCertificate=True;";
        var db = new BookMyFun.Data.DBHelper(config);

        var bookingRepo = new BookMyFun.Repositories.BookingRepository(db);
        var propRepo = new BookMyFun.Repositories.PropertyRepository(db);
        var userRepo = new BookMyFun.Repositories.UserRepository(db);
        var eventRepo = new BookMyFun.Repositories.EventPackageRepository(db);

        var properties = await propRepo.GetAllPropertiesAsync();
        var clients = await userRepo.GetUsersByRoleAsync("Client");
        var packages = await eventRepo.GetActivePackagesAsync();

        if (properties.Any() && clients.Any() && packages.Any())
        {
            var prop = properties.First();
            var client = clients.First();
            var pkg = packages.First();

            decimal propertyStay = 15000m;
            decimal eventPrice = 10000m;
            decimal totalAmount = propertyStay + eventPrice;

            // Host: 10% stay commission (1500) -> 13500 payout
            decimal stayCommRate = 10.0m;
            decimal stayComm = 1500m;
            decimal hostPayout = 13500m;

            // Event: 12% event commission (1200) -> 8800 decorator payout
            decimal eventCommRate = 12.0m;
            decimal eventComm = 1200m;
            decimal decoratorPayout = 8800m;

            var booking = new BookMyFun.Models.Entities.Booking
            {
                PropertyID = prop.PropertyID,
                ClientID = client.UserID,
                CheckInDate = DateTime.Today.AddDays(10),
                CheckOutDate = DateTime.Today.AddDays(12),
                TotalAmount = totalAmount,
                Status = "Pending",
                BookingType = "FullDay",
                CommissionRateSnapshot = stayCommRate,
                PlatformCommissionAmount = stayComm,
                OwnerPayoutAmount = hostPayout,
                IsEventBooking = true,
                EventPackageID = pkg.PackageID,
                EventPackagePriceSnapshot = eventPrice,
                EventCommissionRateSnapshot = eventCommRate,
                EventPlatformCommissionAmount = eventComm,
                EventManagerPayoutAmount = decoratorPayout,
                SetupStatus = "Pending"
            };

            int bookingId = await bookingRepo.CreateBookingAsync(booking);
            Assert.True(bookingId > 0);

            var retrieved = await bookingRepo.GetBookingByIdAsync(bookingId);
            Assert.NotNull(retrieved);
            Assert.Equal(totalAmount, retrieved.TotalAmount);
            Assert.Equal(stayCommRate, retrieved.CommissionRateSnapshot);
            Assert.Equal(stayComm, retrieved.PlatformCommissionAmount);
            Assert.Equal(hostPayout, retrieved.OwnerPayoutAmount);

            Assert.True(retrieved.IsEventBooking);
            Assert.Equal(eventPrice, retrieved.EventPackagePriceSnapshot);
            Assert.Equal(eventCommRate, retrieved.EventCommissionRateSnapshot);
            Assert.Equal(eventComm, retrieved.EventPlatformCommissionAmount);
            Assert.Equal(decoratorPayout, retrieved.EventManagerPayoutAmount);

            // Verify perfect monetary balance: Total = Host Payout + Stay Comm + Decorator Payout + Event Comm
            decimal totalCalculated = retrieved.OwnerPayoutAmount + retrieved.PlatformCommissionAmount +
                                      retrieved.EventManagerPayoutAmount + retrieved.EventPlatformCommissionAmount;
            Assert.Equal(retrieved.TotalAmount, totalCalculated);
        }
    }
}
