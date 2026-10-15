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
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Users')
BEGIN
    CREATE TABLE Users (
        UserID INT IDENTITY(1,1) PRIMARY KEY,
        FullName NVARCHAR(100) NOT NULL,
        Email NVARCHAR(100) UNIQUE NOT NULL,
        PasswordHash NVARCHAR(255) NOT NULL,
        Phone NVARCHAR(20),
        Role NVARCHAR(20) CHECK (Role IN ('Admin', 'Employee', 'Owner', 'Client')) NOT NULL,
        CreatedAt DATETIME2 DEFAULT GETDATE()
    );
END

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

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'PropertyTypes')
BEGIN
    CREATE TABLE PropertyTypes (
        PropertyTypeID INT IDENTITY(1,1) PRIMARY KEY,
        TypeName NVARCHAR(50) NOT NULL,
        Description NVARCHAR(MAX)
    );
END

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Occasions')
BEGIN
    CREATE TABLE Occasions (
        OccasionID INT IDENTITY(1,1) PRIMARY KEY,
        OccasionName NVARCHAR(50) NOT NULL,
        Description NVARCHAR(MAX)
    );
END

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
        CreatedAt DATETIME2 DEFAULT GETDATE()
    );
END

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Properties')
BEGIN
    CREATE TABLE Properties (
        PropertyID INT IDENTITY(1,1) PRIMARY KEY,
        OwnerID INT NOT NULL FOREIGN KEY REFERENCES Users(UserID),
        PropertyTypeID INT NOT NULL FOREIGN KEY REFERENCES PropertyTypes(PropertyTypeID),
        Title NVARCHAR(150) NOT NULL,
        Description NVARCHAR(MAX),
        Location NVARCHAR(255) NOT NULL,
        PricePerNight DECIMAL(18,2) NOT NULL,
        MaxGuests INT NOT NULL,
        ListingStatus NVARCHAR(20) CHECK (ListingStatus IN ('Draft', 'PendingPayment', 'Active', 'Expired')) DEFAULT 'Draft',
        IsApproved BIT DEFAULT 0,
        CreatedAt DATETIME2 DEFAULT GETDATE()
    );
END

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

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'PropertyOccasions')
BEGIN
    CREATE TABLE PropertyOccasions (
        PropertyID INT NOT NULL FOREIGN KEY REFERENCES Properties(PropertyID) ON DELETE CASCADE,
        OccasionID INT NOT NULL FOREIGN KEY REFERENCES Occasions(OccasionID) ON DELETE CASCADE,
        PRIMARY KEY (PropertyID, OccasionID)
    );
END

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'PropertyImages')
BEGIN
    CREATE TABLE PropertyImages (
        ImageID INT IDENTITY(1,1) PRIMARY KEY,
        PropertyID INT NOT NULL FOREIGN KEY REFERENCES Properties(PropertyID) ON DELETE CASCADE,
        ImageURL NVARCHAR(MAX) NOT NULL,
        IsPrimary BIT DEFAULT 0
    );
END

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

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Bookings')
BEGIN
    CREATE TABLE Bookings (
        BookingID INT IDENTITY(1,1) PRIMARY KEY,
        PropertyID INT NOT NULL FOREIGN KEY REFERENCES Properties(PropertyID),
        ClientID INT NOT NULL FOREIGN KEY REFERENCES Users(UserID),
        OccasionID INT FOREIGN KEY REFERENCES Occasions(OccasionID),
        CheckInDate DATE NOT NULL,
        CheckOutDate DATE NOT NULL,
        TotalAmount DECIMAL(18,2) NOT NULL,
        Status NVARCHAR(20) CHECK (Status IN ('Pending', 'Confirmed', 'Cancelled')) DEFAULT 'Pending',
        CreatedAt DATETIME2 DEFAULT GETDATE()
    );
END

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

-- Creative addition for flexible booking duration metadata (Backward compatible)
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Bookings') AND name = 'BookingType')
BEGIN
    ALTER TABLE Bookings ADD BookingType NVARCHAR(30) DEFAULT 'FullDay';
    ALTER TABLE Bookings ADD SlotType NVARCHAR(30) NULL;
    ALTER TABLE Bookings ADD SlotStartTime TIME NULL;
    ALTER TABLE Bookings ADD SlotEndTime TIME NULL;
END

-- Creative addition for Admin/Employee Audit Trail
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

-- Creative addition for OTP Verification (WhatsApp / Email)
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'OtpVerifications')
BEGIN
    CREATE TABLE OtpVerifications (
        OtpID INT IDENTITY(1,1) PRIMARY KEY,
        Contact NVARCHAR(100) NOT NULL, -- Email or Phone
        OtpCode NVARCHAR(10) NOT NULL,
        Purpose NVARCHAR(50) NOT NULL, -- 'Registration', 'Login', 'Booking'
        ExpiresAt DATETIME2 NOT NULL,
        IsUsed BIT DEFAULT 0,
        CreatedAt DATETIME2 DEFAULT GETDATE()
    );
END

-- Creative addition for Owner Request and Trial Listing Period
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'OwnerRequests')
BEGIN
    CREATE TABLE OwnerRequests (
        RequestID INT IDENTITY(1,1) PRIMARY KEY,
        UserID INT NOT NULL FOREIGN KEY REFERENCES Users(UserID) ON DELETE CASCADE,
        RequestedAt DATETIME2 DEFAULT GETDATE(),
        TrialDeadline DATETIME2 NOT NULL, -- 1-2 working days window
        Status NVARCHAR(30) CHECK (Status IN ('PendingEvaluation', 'Approved', 'Rejected')) DEFAULT 'PendingEvaluation',
        AdminNotes NVARCHAR(MAX) NULL,
        ReviewedByUserID INT NULL FOREIGN KEY REFERENCES Users(UserID),
        ReviewedAt DATETIME2 NULL
    );
END

-- Creative addition for Financial Ledger (Income & Expense tracking)
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'FinancialLedger')
BEGIN
    CREATE TABLE FinancialLedger (
        LedgerID INT IDENTITY(1,1) PRIMARY KEY,
        TransactionType NVARCHAR(20) CHECK (TransactionType IN ('Income', 'Expense')) NOT NULL,
        Category NVARCHAR(50) NOT NULL, -- 'BookingCommission', 'Subscription', 'Marketing', 'Maintenance', 'Server', 'Salaries', 'Other'
        Amount DECIMAL(18,2) NOT NULL,
        Title NVARCHAR(150) NOT NULL,
        Details NVARCHAR(MAX) NULL,
        TransactionDate DATE NOT NULL,
        LoggedByUserID INT NULL FOREIGN KEY REFERENCES Users(UserID),
        CreatedAt DATETIME2 DEFAULT GETDATE()
    );
END

-- 1. Property Schema Enhancements (Cancellation, Commission, Security Deposit, Coordinates)
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

-- 2. User Schema Enhancements (Custom Commission & Subscription Tier)
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Users') AND name = 'CustomCommissionRate')
BEGIN
    ALTER TABLE Users ADD CustomCommissionRate DECIMAL(5,2) NULL;
    ALTER TABLE Users ADD SubscriptionTier NVARCHAR(30) DEFAULT 'Free';
END

-- 3. Booking Schema Enhancements (Policy Snapshots, Commission Snapshots, Security Deposit, Idempotency)
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Bookings') AND name = 'FreeCancellationWindowHoursSnapshot')
BEGIN
    ALTER TABLE Bookings ADD FreeCancellationWindowHoursSnapshot INT DEFAULT 48;
    ALTER TABLE Bookings ADD CancellationCutoffHoursSnapshot INT DEFAULT 24;
    ALTER TABLE Bookings ADD CancellationFeeTypeSnapshot NVARCHAR(20) DEFAULT 'percentage';
    ALTER TABLE Bookings ADD CancellationFeeValueSnapshot DECIMAL(18,2) DEFAULT 10.00;
    ALTER TABLE Bookings ADD CommissionRateSnapshot DECIMAL(5,2) DEFAULT 10.00;
    ALTER TABLE Bookings ADD PlatformCommissionAmount DECIMAL(18,2) DEFAULT 0.00;
    ALTER TABLE Bookings ADD OwnerPayoutAmount DECIMAL(18,2) DEFAULT 0.00;
    ALTER TABLE Bookings ADD SecurityDepositAmount DECIMAL(18,2) DEFAULT 0.00;
    ALTER TABLE Bookings ADD SecurityDepositStatus NVARCHAR(30) DEFAULT 'Held';
    ALTER TABLE Bookings ADD IdempotencyKey NVARCHAR(100) NULL;
END

-- 4. Dual File/URL Property Media Architecture
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

-- 5. Amenities Master & Mapping Taxonomy
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

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'PropertyAmenitiesMapping')
BEGIN
    CREATE TABLE PropertyAmenitiesMapping (
        PropertyID INT NOT NULL FOREIGN KEY REFERENCES Properties(PropertyID) ON DELETE CASCADE,
        AmenityID INT NOT NULL FOREIGN KEY REFERENCES AmenitiesMaster(AmenityID) ON DELETE CASCADE,
        PRIMARY KEY (PropertyID, AmenityID)
    );
END

-- 6. Partner Host Intake Leads (Replaces open host registration)
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
        CreatedAt DATETIME2 DEFAULT GETDATE()
    );
END

-- 7. Automated Booking Refunds Tracking
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

-- 8. Damage Claims Adjudication (24h Window Post-Checkout)
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

-- 9. Client Government ID Verification & Privacy Governance
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ClientIdVerifications')
BEGIN
    CREATE TABLE ClientIdVerifications (
        VerificationID INT IDENTITY(1,1) PRIMARY KEY,
        ClientID INT NOT NULL FOREIGN KEY REFERENCES Users(UserID),
        DocumentType NVARCHAR(50) NOT NULL, -- 'Aadhaar', 'Passport', 'DrivingLicense'
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
