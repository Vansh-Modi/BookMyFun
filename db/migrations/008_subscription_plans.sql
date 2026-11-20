-- ============================================================================
-- Migration: 008_subscription_plans.sql
-- Description:
--   1. SubscriptionPlans: adds PlanType NVARCHAR(20) NOT NULL DEFAULT 'Paid'
--      and Description NVARCHAR(500) NULL.
--   2. Users: adds TrialUsedAt DATETIME2 NULL.
--   3. OwnerSubscriptions: adds PlanNameSnapshot NVARCHAR(100) NULL,
--      MaxListingsSnapshot INT NULL, DurationDaysSnapshot INT NULL,
--      GrantedByUserID INT NULL, and GrantReason NVARCHAR(300) NULL.
--   4. Creates PhotoShootRequests table with indexes on (OwnerID) and (Status, CreatedAt).
-- Idempotent: Yes
-- ============================================================================

SET NOCOUNT ON;

-- Ensure SchemaVersion tracking table exists
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'SchemaVersion' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE dbo.SchemaVersion (
        Version INT NOT NULL PRIMARY KEY,
        Name NVARCHAR(100) NOT NULL,
        AppliedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        Checksum NVARCHAR(64) NOT NULL
    );
END

BEGIN TRANSACTION;

-- 1. SubscriptionPlans: PlanType and Description
IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'SubscriptionPlans' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.SubscriptionPlans') AND name = 'PlanType')
    BEGIN
        ALTER TABLE dbo.SubscriptionPlans ADD PlanType NVARCHAR(20) NOT NULL CONSTRAINT DF_SubscriptionPlans_PlanType DEFAULT 'Paid';
        PRINT 'Added column PlanType to dbo.SubscriptionPlans.';
    END
    ELSE
    BEGIN
        PRINT 'Column PlanType already exists on dbo.SubscriptionPlans.';
    END

    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.SubscriptionPlans') AND name = 'Description')
    BEGIN
        ALTER TABLE dbo.SubscriptionPlans ADD Description NVARCHAR(500) NULL;
        PRINT 'Added column Description to dbo.SubscriptionPlans.';
    END
    ELSE
    BEGIN
        PRINT 'Column Description already exists on dbo.SubscriptionPlans.';
    END
END

-- 2. Users: TrialUsedAt
IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'Users' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Users') AND name = 'TrialUsedAt')
    BEGIN
        ALTER TABLE dbo.Users ADD TrialUsedAt DATETIME2 NULL;
        PRINT 'Added column TrialUsedAt to dbo.Users.';
    END
    ELSE
    BEGIN
        PRINT 'Column TrialUsedAt already exists on dbo.Users.';
    END
END

-- 3. OwnerSubscriptions: Snapshots and Admin Grant fields
IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'OwnerSubscriptions' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.OwnerSubscriptions') AND name = 'PlanNameSnapshot')
    BEGIN
        ALTER TABLE dbo.OwnerSubscriptions ADD PlanNameSnapshot NVARCHAR(100) NULL;
        PRINT 'Added column PlanNameSnapshot to dbo.OwnerSubscriptions.';
    END
    ELSE
    BEGIN
        PRINT 'Column PlanNameSnapshot already exists on dbo.OwnerSubscriptions.';
    END

    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.OwnerSubscriptions') AND name = 'MaxListingsSnapshot')
    BEGIN
        ALTER TABLE dbo.OwnerSubscriptions ADD MaxListingsSnapshot INT NULL;
        PRINT 'Added column MaxListingsSnapshot to dbo.OwnerSubscriptions.';
    END
    ELSE
    BEGIN
        PRINT 'Column MaxListingsSnapshot already exists on dbo.OwnerSubscriptions.';
    END

    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.OwnerSubscriptions') AND name = 'DurationDaysSnapshot')
    BEGIN
        ALTER TABLE dbo.OwnerSubscriptions ADD DurationDaysSnapshot INT NULL;
        PRINT 'Added column DurationDaysSnapshot to dbo.OwnerSubscriptions.';
    END
    ELSE
    BEGIN
        PRINT 'Column DurationDaysSnapshot already exists on dbo.OwnerSubscriptions.';
    END

    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.OwnerSubscriptions') AND name = 'GrantedByUserID')
    BEGIN
        ALTER TABLE dbo.OwnerSubscriptions ADD GrantedByUserID INT NULL;
        PRINT 'Added column GrantedByUserID to dbo.OwnerSubscriptions.';
    END
    ELSE
    BEGIN
        PRINT 'Column GrantedByUserID already exists on dbo.OwnerSubscriptions.';
    END

    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.OwnerSubscriptions') AND name = 'GrantReason')
    BEGIN
        ALTER TABLE dbo.OwnerSubscriptions ADD GrantReason NVARCHAR(300) NULL;
        PRINT 'Added column GrantReason to dbo.OwnerSubscriptions.';
    END
    ELSE
    BEGIN
        PRINT 'Column GrantReason already exists on dbo.OwnerSubscriptions.';
    END
END

-- 4. Create PhotoShootRequests table
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'PhotoShootRequests' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE dbo.PhotoShootRequests (
        RequestID INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        PropertyID INT NOT NULL,
        OwnerID INT NOT NULL,
        ShootType NVARCHAR(10) NOT NULL CONSTRAINT CK_PhotoShootRequests_ShootType CHECK (ShootType IN ('Photo', 'Video', 'Both')),
        Status NVARCHAR(20) NOT NULL CONSTRAINT DF_PhotoShootRequests_Status DEFAULT 'Requested' CONSTRAINT CK_PhotoShootRequests_Status CHECK (Status IN ('Requested', 'Scheduled', 'Completed', 'Cancelled')),
        AssignedToUserID INT NULL,
        Notes NVARCHAR(500) NULL,
        CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_PhotoShootRequests_CreatedAt DEFAULT SYSUTCDATETIME(),
        UpdatedAt DATETIME2 NULL,
        CONSTRAINT FK_PhotoShootRequests_Properties FOREIGN KEY (PropertyID) REFERENCES dbo.Properties(PropertyID),
        CONSTRAINT FK_PhotoShootRequests_Owners FOREIGN KEY (OwnerID) REFERENCES dbo.Users(UserID),
        CONSTRAINT FK_PhotoShootRequests_AssignedStaff FOREIGN KEY (AssignedToUserID) REFERENCES dbo.Users(UserID)
    );
    PRINT 'Created table dbo.PhotoShootRequests.';
END
ELSE
BEGIN
    PRINT 'Table dbo.PhotoShootRequests already exists.';
END

-- 5. Indexes for PhotoShootRequests
IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'PhotoShootRequests' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PhotoShootRequests_OwnerID' AND object_id = OBJECT_ID('dbo.PhotoShootRequests'))
    BEGIN
        CREATE NONCLUSTERED INDEX IX_PhotoShootRequests_OwnerID ON dbo.PhotoShootRequests (OwnerID);
        PRINT 'Created index IX_PhotoShootRequests_OwnerID.';
    END

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PhotoShootRequests_Status_CreatedAt' AND object_id = OBJECT_ID('dbo.PhotoShootRequests'))
    BEGIN
        CREATE NONCLUSTERED INDEX IX_PhotoShootRequests_Status_CreatedAt ON dbo.PhotoShootRequests (Status, CreatedAt);
        PRINT 'Created index IX_PhotoShootRequests_Status_CreatedAt.';
    END
END

-- 6. Record version 8 in dbo.SchemaVersion
IF NOT EXISTS (SELECT 1 FROM dbo.SchemaVersion WHERE Version = 8)
BEGIN
    INSERT INTO dbo.SchemaVersion (Version, Name, AppliedAt, Checksum)
    VALUES (8, '008_subscription_plans', SYSUTCDATETIME(), 'E08A9B1C2D3E4F5061728394A5B6C7D8E9F0A1B2C3D4E5F60718A9B0C1D2E3F4');
END
ELSE
BEGIN
    UPDATE dbo.SchemaVersion
    SET AppliedAt = SYSUTCDATETIME(), Checksum = 'E08A9B1C2D3E4F5061728394A5B6C7D8E9F0A1B2C3D4E5F60718A9B0C1D2E3F4'
    WHERE Version = 8;
END

COMMIT TRANSACTION;
PRINT 'Migration 008_subscription_plans completed successfully.';
