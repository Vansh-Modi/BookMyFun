-- ============================================================================
-- Migration: 009_subscription_seed_and_cleanup.sql
-- Description:
--   1. Updates or inserts Trial plan row: PlanType 'Trial', Price 0, MaxListings 1,
--      DurationInDays from SiteSettings.TrialDurationDays (default 30).
--   2. Inserts 'Plan 999' (Paid, Price 999, 60 days, 3 listings) and
--      'Plan 1999' (Paid, Price 1999, 180 days, 2 listings) if not existing.
--   3. Leaves existing Starter, Gold, Platinum plans unchanged.
--   4. SELECTs and prints SubscriptionIDs where PaymentStatus = 'Pending'
--      AND GatewayTransactionID IS NULL, then deletes only those rows.
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

-- 1. Configure the Trial plan row
DECLARE @TrialDays INT = 30;
IF EXISTS (SELECT 1 FROM dbo.SiteSettings WHERE SettingKey = 'TrialDurationDays')
BEGIN
    SELECT @TrialDays = TRY_CAST(SettingValue AS INT)
    FROM dbo.SiteSettings
    WHERE SettingKey = 'TrialDurationDays';
END

IF @TrialDays IS NULL OR @TrialDays <= 0
    SET @TrialDays = 30;

IF EXISTS (SELECT 1 FROM dbo.SubscriptionPlans WHERE PlanName IN ('Free Trial', 'Trial') OR PlanType = 'Trial')
BEGIN
    UPDATE dbo.SubscriptionPlans
    SET PlanType = 'Trial',
        Price = 0.00,
        MaxListings = 1,
        DurationInDays = @TrialDays,
        IsActive = 1,
        Description = COALESCE(Description, 'Complimentary trial listing for new property owners.')
    WHERE PlanName IN ('Free Trial', 'Trial') OR PlanType = 'Trial';
    PRINT 'Configured Trial plan row with duration ' + CAST(@TrialDays AS NVARCHAR(10)) + ' days.';
END
ELSE
BEGIN
    INSERT INTO dbo.SubscriptionPlans (PlanName, Price, DurationInDays, MaxListings, IsActive, PlanType, Description)
    VALUES ('Free Trial', 0.00, @TrialDays, 1, 1, 'Trial', 'Complimentary trial listing for new property owners.');
    PRINT 'Inserted Free Trial plan row with duration ' + CAST(@TrialDays AS NVARCHAR(10)) + ' days.';
END

-- 2. Seed 'Plan 999' (60 days, 3 listings, Price 999, PlanType 'Paid')
IF NOT EXISTS (SELECT 1 FROM dbo.SubscriptionPlans WHERE PlanName = 'Plan 999')
BEGIN
    INSERT INTO dbo.SubscriptionPlans (PlanName, Price, DurationInDays, MaxListings, IsActive, PlanType, Description)
    VALUES ('Plan 999', 999.00, 60, 3, 1, 'Paid', 'Standard host subscription for up to 3 listings for 60 days. Includes photo and video shoot.');
    PRINT 'Inserted Plan 999.';
END
ELSE
BEGIN
    PRINT 'Plan 999 already exists. Leaving unchanged.';
END

-- 3. Seed 'Plan 1999' (180 days, 2 listings, Price 1999, PlanType 'Paid')
IF NOT EXISTS (SELECT 1 FROM dbo.SubscriptionPlans WHERE PlanName = 'Plan 1999')
BEGIN
    INSERT INTO dbo.SubscriptionPlans (PlanName, Price, DurationInDays, MaxListings, IsActive, PlanType, Description)
    VALUES ('Plan 1999', 1999.00, 180, 2, 1, 'Paid', 'Extended host subscription for up to 2 listings for 180 days. Includes photo and video shoot.');
    PRINT 'Inserted Plan 1999.';
END
ELSE
BEGIN
    PRINT 'Plan 1999 already exists. Leaving unchanged.';
END

-- 4. Preview and Delete Pending Subscriptions without Gateway Reference
DECLARE @PendingSubs TABLE (SubscriptionID INT);

INSERT INTO @PendingSubs (SubscriptionID)
SELECT SubscriptionID
FROM dbo.OwnerSubscriptions
WHERE PaymentStatus = 'Pending' AND GatewayTransactionID IS NULL;

-- Preview SELECT
SELECT SubscriptionID AS PendingSubscriptionIDToDelete
FROM @PendingSubs;

-- Print SubscriptionIDs
DECLARE @IdList NVARCHAR(MAX) = N'';
SELECT @IdList = STRING_AGG(CAST(SubscriptionID AS NVARCHAR(20)), ', ')
FROM @PendingSubs;

IF @IdList IS NOT NULL AND LEN(@IdList) > 0
BEGIN
    PRINT 'Found pending subscriptions to delete. SubscriptionIDs: ' + @IdList;
END
ELSE
BEGIN
    PRINT 'No pending subscriptions without gateway transaction reference found.';
END

-- Delete only those rows
DELETE FROM dbo.OwnerSubscriptions
WHERE SubscriptionID IN (SELECT SubscriptionID FROM @PendingSubs);

PRINT 'Deleted ' + CAST(@@ROWCOUNT AS NVARCHAR(20)) + ' pending subscription row(s). Kept all paid, gateway-referenced, and admin-granted rows.';

-- 5. Record version 9 in dbo.SchemaVersion
IF NOT EXISTS (SELECT 1 FROM dbo.SchemaVersion WHERE Version = 9)
BEGIN
    INSERT INTO dbo.SchemaVersion (Version, Name, AppliedAt, Checksum)
    VALUES (9, '009_subscription_seed_and_cleanup', SYSUTCDATETIME(), 'A09B1C2D3E4F5061728394A5B6C7D8E9F0A1B2C3D4E5F60718A9B0C1D2E3F405');
END
ELSE
BEGIN
    UPDATE dbo.SchemaVersion
    SET AppliedAt = SYSUTCDATETIME(), Checksum = 'A09B1C2D3E4F5061728394A5B6C7D8E9F0A1B2C3D4E5F60718A9B0C1D2E3F405'
    WHERE Version = 9;
END

COMMIT TRANSACTION;
PRINT 'Migration 009_subscription_seed_and_cleanup completed successfully.';
