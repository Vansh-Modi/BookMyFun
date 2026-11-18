-- ==============================================================================
-- BookMyFun - Listing & Subscription Rules Database Script
-- File: /db/listing_subscription_rules.sql
-- 
-- DEVELOPER CHECKLIST & RUN INSTRUCTIONS:
-- 1. [CRITICAL] Take a full backup of the production database before executing.
-- 2. This script is fully IDEMPOTENT: it uses IF NOT EXISTS, COL_LENGTH, and sys.indexes.
-- 3. NO DROP TABLE, DROP COLUMN, or RENAME operations are executed.
-- 4. Safe to execute in SQL Server Management Studio (SSMS), Azure Data Studio, or sqlcmd.
-- ==============================================================================

SET NOCOUNT ON;

PRINT 'Starting execution of listing_subscription_rules.sql...';

-- ------------------------------------------------------------------------------
-- 1. Schema Enhancements: Properties Table
-- ------------------------------------------------------------------------------
IF COL_LENGTH('dbo.Properties', 'CreatedByUserID') IS NULL
BEGIN
    ALTER TABLE dbo.Properties ADD CreatedByUserID INT NULL;
    PRINT 'Added column CreatedByUserID to Properties table.';
END
ELSE
BEGIN
    PRINT 'Column CreatedByUserID already exists in Properties table.';
END
GO

IF COL_LENGTH('dbo.Properties', 'ApprovedAt') IS NULL
BEGIN
    ALTER TABLE dbo.Properties ADD ApprovedAt DATETIME2 NULL;
    PRINT 'Added column ApprovedAt to Properties table.';
END
ELSE
BEGIN
    PRINT 'Column ApprovedAt already exists in Properties table.';
END
GO

IF COL_LENGTH('dbo.Properties', 'ApprovedByUserID') IS NULL
BEGIN
    ALTER TABLE dbo.Properties ADD ApprovedByUserID INT NULL;
    PRINT 'Added column ApprovedByUserID to Properties table.';
END
ELSE
BEGIN
    PRINT 'Column ApprovedByUserID already exists in Properties table.';
END
GO

IF COL_LENGTH('dbo.Properties', 'PublishedAt') IS NULL
BEGIN
    ALTER TABLE dbo.Properties ADD PublishedAt DATETIME2 NULL;
    PRINT 'Added column PublishedAt to Properties table.';
END
ELSE
BEGIN
    PRINT 'Column PublishedAt already exists in Properties table.';
END
GO

IF COL_LENGTH('dbo.Properties', 'UnpublishReason') IS NULL
BEGIN
    ALTER TABLE dbo.Properties ADD UnpublishReason NVARCHAR(200) NULL;
    PRINT 'Added column UnpublishReason to Properties table.';
END
ELSE
BEGIN
    PRINT 'Column UnpublishReason already exists in Properties table.';
END
GO

IF COL_LENGTH('dbo.Properties', 'RejectionReason') IS NULL
BEGIN
    ALTER TABLE dbo.Properties ADD RejectionReason NVARCHAR(500) NULL;
    PRINT 'Added column RejectionReason to Properties table.';
END
ELSE
BEGIN
    PRINT 'Column RejectionReason already exists in Properties table.';
END
GO

-- ------------------------------------------------------------------------------
-- 2. Performance Index: Properties(OwnerID, ListingStatus)
-- ------------------------------------------------------------------------------
IF NOT EXISTS (
    SELECT 1 
    FROM sys.indexes 
    WHERE name = 'IX_Properties_OwnerID_ListingStatus' 
      AND object_id = OBJECT_ID('dbo.Properties')
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_Properties_OwnerID_ListingStatus 
    ON dbo.Properties (OwnerID, ListingStatus);
    PRINT 'Created index IX_Properties_OwnerID_ListingStatus on Properties(OwnerID, ListingStatus).';
END
ELSE
BEGIN
    PRINT 'Index IX_Properties_OwnerID_ListingStatus already exists.';
END
GO

-- ------------------------------------------------------------------------------
-- 3. SiteSettings: TrialDurationDays = 60
-- ------------------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM dbo.SiteSettings WHERE SettingKey = 'TrialDurationDays')
BEGIN
    INSERT INTO dbo.SiteSettings (SettingKey, SettingValue, IsEncrypted, UpdatedAt)
    VALUES ('TrialDurationDays', '60', 0, GETUTCDATE());
    PRINT 'Inserted SiteSetting TrialDurationDays = 60.';
END
ELSE
BEGIN
    PRINT 'SiteSetting TrialDurationDays already exists.';
END
GO

-- ------------------------------------------------------------------------------
-- 4. SubscriptionPlans: Free Trial (Price = 0, Duration = 60, MaxListings = 1)
-- ------------------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM dbo.SubscriptionPlans WHERE PlanName = 'Free Trial')
BEGIN
    INSERT INTO dbo.SubscriptionPlans (PlanName, Price, DurationInDays, MaxListings, IsActive)
    VALUES ('Free Trial', 0, 60, 1, 1);
    PRINT 'Inserted SubscriptionPlan "Free Trial".';
END
ELSE
BEGIN
    PRINT 'SubscriptionPlan "Free Trial" already exists.';
END
GO

-- ------------------------------------------------------------------------------
-- 5. OwnerSubscriptions: Ensure PaymentStatus constraint includes 'Trial'
-- ------------------------------------------------------------------------------
DECLARE @SubStatusConstraint NVARCHAR(200);
SELECT @SubStatusConstraint = cc.name 
FROM sys.check_constraints cc
JOIN sys.columns c ON cc.parent_object_id = c.object_id AND cc.parent_column_id = c.column_id
WHERE cc.parent_object_id = OBJECT_ID('dbo.OwnerSubscriptions') AND c.name = 'PaymentStatus';

IF @SubStatusConstraint IS NOT NULL AND @SubStatusConstraint <> 'CK_OwnerSubscriptions_PaymentStatus_V3'
BEGIN
    EXEC('ALTER TABLE dbo.OwnerSubscriptions DROP CONSTRAINT ' + @SubStatusConstraint);
    ALTER TABLE dbo.OwnerSubscriptions ADD CONSTRAINT CK_OwnerSubscriptions_PaymentStatus_V3 
    CHECK (PaymentStatus IN ('Trial', 'Paid', 'Pending', 'Expired', 'Scheduled', 'Cancelled'));
    PRINT 'Updated PaymentStatus check constraint on OwnerSubscriptions to CK_OwnerSubscriptions_PaymentStatus_V3.';
END
ELSE IF @SubStatusConstraint IS NULL
BEGIN
    ALTER TABLE dbo.OwnerSubscriptions ADD CONSTRAINT CK_OwnerSubscriptions_PaymentStatus_V3 
    CHECK (PaymentStatus IN ('Trial', 'Paid', 'Pending', 'Expired', 'Scheduled', 'Cancelled'));
    PRINT 'Created PaymentStatus check constraint CK_OwnerSubscriptions_PaymentStatus_V3 on OwnerSubscriptions.';
END
ELSE
BEGIN
    PRINT 'Constraint CK_OwnerSubscriptions_PaymentStatus_V3 already in place.';
END
GO

-- ------------------------------------------------------------------------------
-- 6. OwnerSubscriptions: Existing Owners Migration
--    Insert Free Trial for every owner who has properties and NO subscription row.
-- ------------------------------------------------------------------------------
DECLARE @FreeTrialPlanID INT;
SELECT TOP 1 @FreeTrialPlanID = PlanID 
FROM dbo.SubscriptionPlans 
WHERE PlanName = 'Free Trial';

IF @FreeTrialPlanID IS NOT NULL
BEGIN
    INSERT INTO dbo.OwnerSubscriptions (OwnerID, PlanID, StartDate, EndDate, PaymentStatus, GatewayTransactionID, PricePaid)
    SELECT DISTINCT p.OwnerID, @FreeTrialPlanID, SYSDATETIME(), DATEADD(DAY, 60, SYSDATETIME()), 'Trial', NULL, 0
    FROM dbo.Properties p
    WHERE NOT EXISTS (
        SELECT 1 
        FROM dbo.OwnerSubscriptions os 
        WHERE os.OwnerID = p.OwnerID
    );

    PRINT 'Migrated existing property owners without subscriptions to Free Trial (' + CAST(@@ROWCOUNT AS NVARCHAR(10)) + ' owners migrated).';
END
ELSE
BEGIN
    PRINT 'ERROR: Free Trial plan not found; migration aborted.';
END
GO

PRINT 'listing_subscription_rules.sql executed successfully.';
