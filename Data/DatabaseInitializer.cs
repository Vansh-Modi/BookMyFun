using Microsoft.Data.SqlClient;

namespace BookMyFun.Data
{
    public class DatabaseInitializer
    {
        private readonly DBHelper _dbHelper;
        private readonly ILogger<DatabaseInitializer> _logger;

        public DatabaseInitializer(DBHelper dbHelper, ILogger<DatabaseInitializer> logger)
        {
            _dbHelper = dbHelper;
            _logger = logger;
        }

        public async Task InitializeAsync()
        {
            try
            {
                var script = @"
-- 1. Users Table (Supports 5 roles: Admin, Employee, Owner, Client, EventManager)
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Users')
BEGIN
    CREATE TABLE Users (
        UserID INT IDENTITY(1,1) PRIMARY KEY,
        FullName NVARCHAR(100) NOT NULL,
        Email NVARCHAR(100) UNIQUE NOT NULL,
        PasswordHash NVARCHAR(255) NOT NULL,
        Phone NVARCHAR(20),
        Role NVARCHAR(20) CHECK (Role IN ('Admin', 'Employee', 'Owner', 'Client', 'EventManager')) NOT NULL,
        IsActive BIT DEFAULT 1,
        CustomCommissionRate DECIMAL(5,2) NULL,
        SubscriptionTier NVARCHAR(30) DEFAULT 'Free',
        CreatedAt DATETIME2 DEFAULT GETDATE()
    );
END
ELSE
BEGIN
    -- Ensure columns exist
    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Users') AND name = 'IsActive')
    BEGIN
        ALTER TABLE Users ADD IsActive BIT NOT NULL DEFAULT 1 WITH VALUES;
    END

    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Users') AND name = 'CustomCommissionRate')
    BEGIN
        ALTER TABLE Users ADD CustomCommissionRate DECIMAL(5,2) NULL;
    END

    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Users') AND name = 'SubscriptionTier')
    BEGIN
        ALTER TABLE Users ADD SubscriptionTier NVARCHAR(30) DEFAULT 'Free';
    END

    -- Ensure Role CHECK constraint supports all 5 roles (Admin, Employee, Owner, Client, EventManager)
    DECLARE @RoleConstraint NVARCHAR(200);
    SELECT @RoleConstraint = cc.name 
    FROM sys.check_constraints cc
    JOIN sys.columns c ON cc.parent_object_id = c.object_id AND cc.parent_column_id = c.column_id
    WHERE cc.parent_object_id = OBJECT_ID('Users') AND c.name = 'Role';

    IF @RoleConstraint IS NOT NULL AND @RoleConstraint <> 'CK_Users_Role_V2'
    BEGIN
        EXEC('ALTER TABLE Users DROP CONSTRAINT ' + @RoleConstraint);
        ALTER TABLE Users ADD CONSTRAINT CK_Users_Role_V2 CHECK (Role IN ('Admin', 'Employee', 'Owner', 'Client', 'EventManager'));
    END
END

-- 2. SiteSettings Table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'SiteSettings')
BEGIN
    CREATE TABLE SiteSettings (
        SettingID INT IDENTITY(1,1) PRIMARY KEY,
        SettingKey NVARCHAR(100) UNIQUE NOT NULL,
        SettingValue NVARCHAR(MAX),
        IsEncrypted BIT DEFAULT 0,
        UpdatedAt DATETIME2 DEFAULT GETDATE()
    );
END

-- 3. ApiConfigurations Table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ApiConfigurations')
BEGIN
    CREATE TABLE ApiConfigurations (
        ConfigID INT IDENTITY(1,1) PRIMARY KEY,
        ServiceProvider NVARCHAR(50) CHECK (ServiceProvider IN ('SMTP', 'WhatsApp', 'Razorpay', 'Cashfree')) NOT NULL,
        ApiKey NVARCHAR(255) NOT NULL,
        ApiSecret NVARCHAR(255) NOT NULL,
        ExtraParams NVARCHAR(MAX),
        IsActive BIT DEFAULT 1
    );
END

-- 4. PropertyTypes Table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'PropertyTypes')
BEGIN
    CREATE TABLE PropertyTypes (
        PropertyTypeID INT IDENTITY(1,1) PRIMARY KEY,
        TypeName NVARCHAR(50) NOT NULL,
        Description NVARCHAR(MAX)
    );
END

-- 5. Occasions Table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Occasions')
BEGIN
    CREATE TABLE Occasions (
        OccasionID INT IDENTITY(1,1) PRIMARY KEY,
        OccasionName NVARCHAR(50) NOT NULL,
        Description NVARCHAR(MAX)
    );
END

-- 6. SubscriptionPlans Table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'SubscriptionPlans')
BEGIN
    CREATE TABLE SubscriptionPlans (
        PlanID INT IDENTITY(1,1) PRIMARY KEY,
        PlanName NVARCHAR(50) NOT NULL,
        Price DECIMAL(18,2) NOT NULL,
        DurationInDays INT NOT NULL,
        MaxListings INT NOT NULL,
        IsActive BIT DEFAULT 1
    );
END

-- 7. ContactUs Table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ContactUs')
BEGIN
    CREATE TABLE ContactUs (
        ContactID INT IDENTITY(1,1) PRIMARY KEY,
        Name NVARCHAR(100) NOT NULL,
        Email NVARCHAR(100) NOT NULL,
        Phone NVARCHAR(20),
        Subject NVARCHAR(255),
        Message NVARCHAR(MAX) NOT NULL,
        Status NVARCHAR(20) CHECK (Status IN ('New', 'Responded')) DEFAULT 'New',
        RespondedByUserID INT NULL FOREIGN KEY REFERENCES Users(UserID),
        ResponseNotes NVARCHAR(MAX) NULL,
        RespondedAt DATETIME2 NULL,
        CreatedAt DATETIME2 DEFAULT GETDATE()
    );
END
ELSE
BEGIN
    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('ContactUs') AND name = 'RespondedByUserID')
    BEGIN
        ALTER TABLE ContactUs ADD RespondedByUserID INT NULL FOREIGN KEY REFERENCES Users(UserID);
        ALTER TABLE ContactUs ADD ResponseNotes NVARCHAR(MAX) NULL;
        ALTER TABLE ContactUs ADD RespondedAt DATETIME2 NULL;
    END
END

-- 8. Properties Table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Properties')
BEGIN
    CREATE TABLE Properties (
        PropertyID INT IDENTITY(1,1) PRIMARY KEY,
        OwnerID INT NOT NULL FOREIGN KEY REFERENCES Users(UserID),
        PropertyTypeID INT NOT NULL FOREIGN KEY REFERENCES PropertyTypes(PropertyTypeID),
        Title NVARCHAR(150) NOT NULL,
        Description NVARCHAR(MAX),
        Location NVARCHAR(255) NOT NULL,
        FormattedAddress NVARCHAR(500) NULL,
        Latitude DECIMAL(9,6) NULL,
        Longitude DECIMAL(9,6) NULL,
        PricePerNight DECIMAL(18,2) NOT NULL,
        SecurityDepositAmount DECIMAL(18,2) DEFAULT 0.00,
        CommissionRate DECIMAL(5,2) NULL,
        FreeCancellationWindowHours INT DEFAULT 48,
        CancellationCutoffHours INT DEFAULT 24,
        CancellationFeeType NVARCHAR(20) DEFAULT 'percentage',
        CancellationFeeValue DECIMAL(18,2) DEFAULT 10.00,
        MaxGuests INT NOT NULL,
        IsFeatured BIT DEFAULT 0,
        ListingStatus NVARCHAR(20) CHECK (ListingStatus IN ('Draft', 'PendingPayment', 'Active', 'Expired')) DEFAULT 'Draft',
        IsApproved BIT DEFAULT 0,
        CreatedAt DATETIME2 DEFAULT GETDATE()
    );
END
ELSE
BEGIN
    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Properties') AND name = 'FreeCancellationWindowHours')
    BEGIN
        ALTER TABLE Properties ADD FreeCancellationWindowHours INT DEFAULT 48;
        ALTER TABLE Properties ADD CancellationCutoffHours INT DEFAULT 24;
        ALTER TABLE Properties ADD CancellationFeeType NVARCHAR(20) DEFAULT 'percentage';
        ALTER TABLE Properties ADD CancellationFeeValue DECIMAL(18,2) DEFAULT 10.00;
        ALTER TABLE Properties ADD SecurityDepositAmount DECIMAL(18,2) DEFAULT 0.00;
        ALTER TABLE Properties ADD CommissionRate DECIMAL(5,2) NULL;
        ALTER TABLE Properties ADD FormattedAddress NVARCHAR(500) NULL;
        ALTER TABLE Properties ADD Latitude DECIMAL(9,6) NULL;
        ALTER TABLE Properties ADD Longitude DECIMAL(9,6) NULL;
        ALTER TABLE Properties ADD IsFeatured BIT DEFAULT 0;
    END
END

-- 9. OwnerSubscriptions Table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'OwnerSubscriptions')
BEGIN
    CREATE TABLE OwnerSubscriptions (
        SubscriptionID INT IDENTITY(1,1) PRIMARY KEY,
        OwnerID INT NOT NULL FOREIGN KEY REFERENCES Users(UserID),
        PlanID INT NOT NULL FOREIGN KEY REFERENCES SubscriptionPlans(PlanID),
        StartDate DATETIME2 NOT NULL,
        EndDate DATETIME2 NOT NULL,
        PaymentStatus NVARCHAR(20) CHECK (PaymentStatus IN ('Paid', 'Pending', 'Expired')) DEFAULT 'Pending',
        GatewayTransactionID NVARCHAR(100)
    );
END

-- 10. PropertyOccasions Table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'PropertyOccasions')
BEGIN
    CREATE TABLE PropertyOccasions (
        PropertyID INT NOT NULL FOREIGN KEY REFERENCES Properties(PropertyID) ON DELETE CASCADE,
        OccasionID INT NOT NULL FOREIGN KEY REFERENCES Occasions(OccasionID) ON DELETE CASCADE,
        PRIMARY KEY (PropertyID, OccasionID)
    );
END

-- 11. PropertyImages Table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'PropertyImages')
BEGIN
    CREATE TABLE PropertyImages (
        ImageID INT IDENTITY(1,1) PRIMARY KEY,
        PropertyID INT NOT NULL FOREIGN KEY REFERENCES Properties(PropertyID) ON DELETE CASCADE,
        ImageURL NVARCHAR(MAX) NOT NULL,
        IsPrimary BIT DEFAULT 0
    );
END

-- 12. PropertyMedia Table (Dual-Mode Uploads)
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'PropertyMedia')
BEGIN
    CREATE TABLE PropertyMedia (
        MediaID INT IDENTITY(1,1) PRIMARY KEY,
        PropertyID INT NOT NULL FOREIGN KEY REFERENCES Properties(PropertyID) ON DELETE CASCADE,
        MediaType NVARCHAR(10) CHECK (MediaType IN ('photo', 'video')) NOT NULL,
        SourceType NVARCHAR(10) CHECK (SourceType IN ('file', 'url')) NOT NULL,
        StorageUrl NVARCHAR(MAX) NOT NULL,
        PublicId NVARCHAR(255) NULL,
        IsPrimary BIT DEFAULT 0,
        SortOrder INT DEFAULT 0,
        CreatedAt DATETIME2 DEFAULT GETDATE()
    );
END

-- 13. AmenitiesMaster Table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'AmenitiesMaster')
BEGIN
    CREATE TABLE AmenitiesMaster (
        AmenityID INT IDENTITY(1,1) PRIMARY KEY,
        AmenityName NVARCHAR(50) NOT NULL UNIQUE,
        IconClass NVARCHAR(50) NOT NULL,
        Category NVARCHAR(50) DEFAULT 'General'
    );

    INSERT INTO AmenitiesMaster (AmenityName, IconClass, Category) VALUES
    ('Swimming Pool', 'bi-water', 'Water & Pool'),
    ('Gazebo', 'bi-umbrella', 'Outdoor & Lawn'),
    ('Bonfire Pit', 'bi-fire', 'Outdoor & Lawn'),
    ('Barbecue (BBQ)', 'bi-egg-fried', 'Kitchen & Dining'),
    ('Power Backup / Generator', 'bi-lightning-charge', 'Power & Tech'),
    ('Spacious Lawn Area', 'bi-tree', 'Outdoor & Lawn'),
    ('Onsite Caretaker', 'bi-person-badge', 'Service'),
    ('Pet Friendly', 'bi-heart', 'General'),
    ('Acoustic Sound System', 'bi-music-note-beamed', 'Entertainment'),
    ('High-Speed WiFi', 'bi-wifi', 'Power & Tech'),
    ('Free Onsite Parking', 'bi-p-square', 'General'),
    ('Air Conditioning', 'bi-snow', 'General');
END

-- 14. PropertyAmenitiesMapping Table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'PropertyAmenitiesMapping')
BEGIN
    CREATE TABLE PropertyAmenitiesMapping (
        PropertyID INT NOT NULL FOREIGN KEY REFERENCES Properties(PropertyID) ON DELETE CASCADE,
        AmenityID INT NOT NULL FOREIGN KEY REFERENCES AmenitiesMaster(AmenityID) ON DELETE CASCADE,
        PRIMARY KEY (PropertyID, AmenityID)
    );
END

-- 15. EventPackages Table (5th Dashboard: Event Manager / Decorator)
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'EventPackages')
BEGIN
    CREATE TABLE EventPackages (
        PackageID INT IDENTITY(1,1) PRIMARY KEY,
        Name NVARCHAR(150) NOT NULL,
        Category NVARCHAR(50) NOT NULL, -- 'Birthday', 'Anniversary', 'Wedding', 'Corporate', 'Custom'
        BasePrice DECIMAL(18,2) NOT NULL,
        Description NVARCHAR(MAX) NULL,
        MediaUrl NVARCHAR(MAX) NULL,
        IncludedItems NVARCHAR(MAX) NULL,
        IsActive BIT DEFAULT 1,
        CreatedByUserID INT NULL FOREIGN KEY REFERENCES Users(UserID),
        CreatedAt DATETIME2 DEFAULT GETDATE()
    );

    INSERT INTO EventPackages (Name, Category, BasePrice, Description, MediaUrl, IncludedItems, IsActive) VALUES
    ('Royal Floral Wedding Canopy', 'Wedding', 45000.00, 'Exquisite real floral mandap, entrance archway, draped walkway, fairy light canopy, and stage backdrop.', 'https://images.unsplash.com/photo-1519741497674-611481863552', 'Floral Mandap, Stage Backdrop, Ambient Fairy Lights, Entrance Floral Archway, Guest Pathway Draping', 1),
    ('Neon DJ & Acoustic Sound Night', 'Birthday', 25000.00, 'High-intensity neon laser lighting, strobe rigs, dual subwoofer JBL sound system, DJ console, and glowing photo booth.', 'https://images.unsplash.com/photo-1492684223066-81342ee5ff30', 'JBL Dual Subwoofers, DJ Deck, Moving Head Lasers, Neon Photo Wall, LED Strobes', 1),
    ('Boho Chic Birthday Lawn Setup', 'Birthday', 18000.00, 'Aesthetic low-seating bohemian floor tables, macrame backdrop, teepee tent, warm fairy lights, and pastel balloon garlands.', 'https://images.unsplash.com/photo-1530103862676-de8c9debad1d', 'Teepee Photo Booth, Low Floor Tables, Boho Rugs & Cushions, Pastel Balloon Arch, Warm Fairy Lights', 1),
    ('Corporate Gala & Cocktail Lounge', 'Corporate', 35000.00, 'Sophisticated cocktail high-top tables, illuminated bar counter, sleek black trussing, spotlighting, and podium setup.', 'https://images.unsplash.com/photo-1511578314322-379afb476865', 'Illuminated Bar Counter, 8 Cocktail Tables, Truss Spotlights, Audio Podium, Cordless Microphones', 1);
END

-- 16. Bookings Table (With Policy, Commission Snapshots & Event Integration)
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Bookings')
BEGIN
    CREATE TABLE Bookings (
        BookingID INT IDENTITY(1,1) PRIMARY KEY,
        PropertyID INT NOT NULL FOREIGN KEY REFERENCES Properties(PropertyID),
        ClientID INT NOT NULL FOREIGN KEY REFERENCES Users(UserID),
        OccasionID INT FOREIGN KEY REFERENCES Occasions(OccasionID),
        CheckInDate DATE NOT NULL,
        CheckOutDate DATE NOT NULL,
        BookingType NVARCHAR(30) DEFAULT 'FullDay',
        SlotType NVARCHAR(30) NULL,
        SlotStartTime TIME NULL,
        SlotEndTime TIME NULL,
        TotalAmount DECIMAL(18,2) NOT NULL,
        Status NVARCHAR(20) CHECK (Status IN ('Pending', 'Confirmed', 'Cancelled', 'Completed')) DEFAULT 'Pending',
        CommissionRateSnapshot DECIMAL(5,2) DEFAULT 10.00,
        PlatformCommissionAmount DECIMAL(18,2) DEFAULT 0.00,
        OwnerPayoutAmount DECIMAL(18,2) DEFAULT 0.00,
        FreeCancellationWindowHoursSnapshot INT DEFAULT 48,
        CancellationCutoffHoursSnapshot INT DEFAULT 24,
        CancellationFeeTypeSnapshot NVARCHAR(20) DEFAULT 'percentage',
        CancellationFeeValueSnapshot DECIMAL(18,2) DEFAULT 10.00,
        SecurityDepositAmount DECIMAL(18,2) DEFAULT 0.00,
        SecurityDepositStatus NVARCHAR(30) DEFAULT 'Held',
        IdempotencyKey NVARCHAR(100) NULL,
        -- Event Integration
        IsEventBooking BIT DEFAULT 0,
        EventPackageID INT NULL FOREIGN KEY REFERENCES EventPackages(PackageID),
        CustomEventNotes NVARCHAR(MAX) NULL,
        EventDate DATE NULL,
        DecoratorAssignedID INT NULL FOREIGN KEY REFERENCES Users(UserID),
        SetupStatus NVARCHAR(30) DEFAULT 'Pending',
        SetupProofMediaUrls NVARCHAR(MAX) NULL,
        EventPackagePriceSnapshot DECIMAL(18,2) DEFAULT 0.00,
        CreatedAt DATETIME2 DEFAULT GETDATE()
    );
END
ELSE
BEGIN
    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Bookings') AND name = 'IsEventBooking')
    BEGIN
        ALTER TABLE Bookings ADD IsEventBooking BIT DEFAULT 0;
        ALTER TABLE Bookings ADD EventPackageID INT NULL FOREIGN KEY REFERENCES EventPackages(PackageID);
        ALTER TABLE Bookings ADD CustomEventNotes NVARCHAR(MAX) NULL;
        ALTER TABLE Bookings ADD EventDate DATE NULL;
        ALTER TABLE Bookings ADD DecoratorAssignedID INT NULL FOREIGN KEY REFERENCES Users(UserID);
        ALTER TABLE Bookings ADD SetupStatus NVARCHAR(30) DEFAULT 'Pending';
        ALTER TABLE Bookings ADD SetupProofMediaUrls NVARCHAR(MAX) NULL;
        ALTER TABLE Bookings ADD EventPackagePriceSnapshot DECIMAL(18,2) DEFAULT 0.00;
    END
END

-- 17. Payments Table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Payments')
BEGIN
    CREATE TABLE Payments (
        PaymentID INT IDENTITY(1,1) PRIMARY KEY,
        BookingID INT NOT NULL FOREIGN KEY REFERENCES Bookings(BookingID) ON DELETE CASCADE,
        GatewayTransactionID NVARCHAR(100),
        Amount DECIMAL(18,2) NOT NULL,
        PaymentStatus NVARCHAR(20) CHECK (PaymentStatus IN ('Success', 'Failed')) NOT NULL,
        PaymentDate DATETIME2 DEFAULT GETDATE()
    );
END

-- 18. CustomerReviews Table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'CustomerReviews')
BEGIN
    CREATE TABLE CustomerReviews (
        ReviewID INT IDENTITY(1,1) PRIMARY KEY,
        BookingID INT NOT NULL FOREIGN KEY REFERENCES Bookings(BookingID),
        ClientID INT NOT NULL FOREIGN KEY REFERENCES Users(UserID),
        PropertyID INT NOT NULL FOREIGN KEY REFERENCES Properties(PropertyID),
        Rating INT CHECK (Rating BETWEEN 1 AND 5) NOT NULL,
        Comment NVARCHAR(MAX),
        CreatedAt DATETIME2 DEFAULT GETDATE()
    );
END

-- 19. AuditLogs Table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'AuditLogs')
BEGIN
    CREATE TABLE AuditLogs (
        AuditID INT IDENTITY(1,1) PRIMARY KEY,
        PerformedByUserID INT NOT NULL FOREIGN KEY REFERENCES Users(UserID),
        Action NVARCHAR(100) NOT NULL,
        EntityType NVARCHAR(50) NOT NULL,
        EntityID INT NULL,
        Details NVARCHAR(MAX) NULL,
        Timestamp DATETIME2 DEFAULT GETDATE()
    );
END

-- 20. OtpVerifications Table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'OtpVerifications')
BEGIN
    CREATE TABLE OtpVerifications (
        OtpID INT IDENTITY(1,1) PRIMARY KEY,
        Contact NVARCHAR(100) NOT NULL,
        OtpCode NVARCHAR(10) NOT NULL,
        Purpose NVARCHAR(50) NOT NULL,
        ExpiresAt DATETIME2 NOT NULL,
        IsUsed BIT DEFAULT 0,
        CreatedAt DATETIME2 DEFAULT GETDATE()
    );
END

-- 21. OwnerRequests Table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'OwnerRequests')
BEGIN
    CREATE TABLE OwnerRequests (
        RequestID INT IDENTITY(1,1) PRIMARY KEY,
        UserID INT NOT NULL FOREIGN KEY REFERENCES Users(UserID) ON DELETE CASCADE,
        RequestedAt DATETIME2 DEFAULT GETDATE(),
        TrialDeadline DATETIME2 NOT NULL,
        Status NVARCHAR(30) CHECK (Status IN ('PendingEvaluation', 'Approved', 'Rejected')) DEFAULT 'PendingEvaluation',
        AdminNotes NVARCHAR(MAX) NULL,
        ReviewedByUserID INT NULL FOREIGN KEY REFERENCES Users(UserID),
        ReviewedAt DATETIME2 NULL
    );
END

-- 22. FinancialLedger Table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'FinancialLedger')
BEGIN
    CREATE TABLE FinancialLedger (
        LedgerID INT IDENTITY(1,1) PRIMARY KEY,
        TransactionType NVARCHAR(20) CHECK (TransactionType IN ('Income', 'Expense')) NOT NULL,
        Category NVARCHAR(50) NOT NULL,
        Amount DECIMAL(18,2) NOT NULL,
        Title NVARCHAR(150) NOT NULL,
        Details NVARCHAR(MAX) NULL,
        TransactionDate DATE NOT NULL,
        LoggedByUserID INT NULL FOREIGN KEY REFERENCES Users(UserID),
        CreatedAt DATETIME2 DEFAULT GETDATE()
    );
END

-- 23. PropertyListingPayments Table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'PropertyListingPayments')
BEGIN
    CREATE TABLE PropertyListingPayments (
        ListingPaymentID INT IDENTITY(1,1) PRIMARY KEY,
        PropertyID INT NOT NULL FOREIGN KEY REFERENCES Properties(PropertyID) ON DELETE CASCADE,
        OwnerID INT NOT NULL FOREIGN KEY REFERENCES Users(UserID),
        Amount DECIMAL(18,2) NOT NULL,
        PaymentStatus NVARCHAR(20) CHECK (PaymentStatus IN ('Success', 'Failed', 'Pending')) DEFAULT 'Pending',
        GatewayTransactionID NVARCHAR(100),
        PaymentDate DATETIME2 DEFAULT GETDATE()
    );
END

-- 24. PartnerLeads Table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'PartnerLeads')
BEGIN
    CREATE TABLE PartnerLeads (
        LeadID INT IDENTITY(1,1) PRIMARY KEY,
        FullName NVARCHAR(100) NOT NULL,
        Phone NVARCHAR(20) NOT NULL,
        Email NVARCHAR(100) NOT NULL,
        PropertyName NVARCHAR(150) NOT NULL,
        Location NVARCHAR(255) NOT NULL,
        AcreageOrType NVARCHAR(100) NOT NULL,
        Message NVARCHAR(MAX) NULL,
        Status NVARCHAR(30) CHECK (Status IN ('New', 'Contacted', 'Approved', 'Rejected')) DEFAULT 'New',
        ActivationToken NVARCHAR(255) NULL,
        TokenExpiresAt DATETIME2 NULL,
        ReviewedByUserID INT NULL FOREIGN KEY REFERENCES Users(UserID),
        PartnerType NVARCHAR(50) DEFAULT 'Owner',
        CreatedAt DATETIME2 DEFAULT GETDATE()
    );
END

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('PartnerLeads') AND name = 'PartnerType')
BEGIN
    ALTER TABLE PartnerLeads ADD PartnerType NVARCHAR(50) DEFAULT 'Owner';
END

-- 25. BookingRefunds Table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'BookingRefunds')
BEGIN
    CREATE TABLE BookingRefunds (
        RefundID INT IDENTITY(1,1) PRIMARY KEY,
        BookingID INT NOT NULL FOREIGN KEY REFERENCES Bookings(BookingID),
        GrossBookingAmount DECIMAL(18,2) NOT NULL,
        CancellationFeeDeducted DECIMAL(18,2) NOT NULL,
        GatewayFeeDeducted DECIMAL(18,2) NOT NULL,
        NetRefundAmount DECIMAL(18,2) NOT NULL,
        RefundReason NVARCHAR(500) NULL,
        Status NVARCHAR(30) CHECK (Status IN ('Initiated', 'Processing', 'Completed', 'Failed', 'Overridden')) DEFAULT 'Initiated',
        GatewayRefundID NVARCHAR(100) NULL,
        ProcessedByUserID INT NULL FOREIGN KEY REFERENCES Users(UserID),
        ProcessedAt DATETIME2 NULL,
        CreatedAt DATETIME2 DEFAULT GETDATE()
    );
END

-- 26. DamageClaims Table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'DamageClaims')
BEGIN
    CREATE TABLE DamageClaims (
        ClaimID INT IDENTITY(1,1) PRIMARY KEY,
        BookingID INT NOT NULL FOREIGN KEY REFERENCES Bookings(BookingID),
        PropertyID INT NOT NULL FOREIGN KEY REFERENCES Properties(PropertyID),
        OwnerID INT NOT NULL FOREIGN KEY REFERENCES Users(UserID),
        ClaimAmount DECIMAL(18,2) NOT NULL,
        ItemizedDescription NVARCHAR(MAX) NOT NULL,
        EvidenceMediaUrls NVARCHAR(MAX) NOT NULL,
        Status NVARCHAR(30) CHECK (Status IN ('UnderReview', 'Approved', 'Rejected', 'Settled')) DEFAULT 'UnderReview',
        AdjudicatedAmount DECIMAL(18,2) NULL,
        AdjudicatedByUserID INT NULL FOREIGN KEY REFERENCES Users(UserID),
        AdjudicationNotes NVARCHAR(MAX) NULL,
        AdjudicatedAt DATETIME2 NULL,
        CreatedAt DATETIME2 DEFAULT GETDATE()
    );
END

-- 27. ClientIdVerifications Table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ClientIdVerifications')
BEGIN
    CREATE TABLE ClientIdVerifications (
        VerificationID INT IDENTITY(1,1) PRIMARY KEY,
        ClientID INT NOT NULL FOREIGN KEY REFERENCES Users(UserID),
        DocumentType NVARCHAR(50) NOT NULL,
        DocumentStorageKey NVARCHAR(500) NOT NULL,
        MaskedIdentifier NVARCHAR(20) NULL,
        VerificationStatus NVARCHAR(30) CHECK (VerificationStatus IN ('PendingReview', 'Verified', 'Rejected')) DEFAULT 'PendingReview',
        RejectionReason NVARCHAR(255) NULL,
        VerifiedByUserID INT NULL FOREIGN KEY REFERENCES Users(UserID),
        VerifiedAt DATETIME2 NULL,
        CreatedAt DATETIME2 DEFAULT GETDATE()
    );
END
";

                await _dbHelper.ExecuteNonQueryAsync(script);
                _logger.LogInformation("Database tables and enhancements verified/initialized successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Database initialization warning (ensure SQL Server is running if creating tables for the first time).");
            }
        }
    }
}
