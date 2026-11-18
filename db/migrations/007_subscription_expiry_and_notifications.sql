-- ============================================================================
-- Migration: 007_subscription_expiry_and_notifications.sql
-- Description: 
--   1. Adds ReadAt (DATETIME2 NULL) to dbo.UserNotifications for read tracking.
--   2. Updates ListingStatus check constraint on dbo.Properties to allow 'Live'
--      and 'Unpublished' states while retaining backward compatibility.
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

-- 1. Add ReadAt column to dbo.UserNotifications if it does not exist
IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'UserNotifications' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.UserNotifications') AND name = 'ReadAt')
    BEGIN
        ALTER TABLE dbo.UserNotifications ADD ReadAt DATETIME2 NULL;
        PRINT 'Added column ReadAt to dbo.UserNotifications.';
    END
    ELSE
    BEGIN
        PRINT 'Column ReadAt already exists on dbo.UserNotifications.';
    END
END

-- 2. Update ListingStatus CHECK constraint on dbo.Properties to accept 'Live' and 'Unpublished'
IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'Properties' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    DECLARE @ListingStatusConstraint NVARCHAR(200);
    SELECT @ListingStatusConstraint = cc.name 
    FROM sys.check_constraints cc
    JOIN sys.columns c ON cc.parent_object_id = c.object_id AND cc.parent_column_id = c.column_id
    WHERE cc.parent_object_id = OBJECT_ID('dbo.Properties') AND c.name = 'ListingStatus';

    IF @ListingStatusConstraint IS NOT NULL AND @ListingStatusConstraint <> 'CK_Properties_ListingStatus_V3'
    BEGIN
        DECLARE @DropListingStatusSql NVARCHAR(500) = N'ALTER TABLE dbo.Properties DROP CONSTRAINT ' + QUOTENAME(@ListingStatusConstraint);
        EXEC sp_executesql @DropListingStatusSql;
        ALTER TABLE dbo.Properties ADD CONSTRAINT CK_Properties_ListingStatus_V3 CHECK (ListingStatus IN ('Draft', 'PendingPayment', 'Active', 'Live', 'Expired', 'Deactivated', 'Unpublished', 'Rejected'));
        PRINT 'Updated ListingStatus check constraint on dbo.Properties to CK_Properties_ListingStatus_V3.';
    END
    ELSE IF @ListingStatusConstraint IS NULL
    BEGIN
        ALTER TABLE dbo.Properties ADD CONSTRAINT CK_Properties_ListingStatus_V3 CHECK (ListingStatus IN ('Draft', 'PendingPayment', 'Active', 'Live', 'Expired', 'Deactivated', 'Unpublished', 'Rejected'));
        PRINT 'Created ListingStatus check constraint CK_Properties_ListingStatus_V3 on dbo.Properties.';
    END
    ELSE
    BEGIN
        PRINT 'Constraint CK_Properties_ListingStatus_V3 already in place on dbo.Properties.';
    END
END

-- 3. Record migration in dbo.SchemaVersion
IF NOT EXISTS (SELECT 1 FROM dbo.SchemaVersion WHERE Version = 7)
BEGIN
    INSERT INTO dbo.SchemaVersion (Version, Name, AppliedAt, Checksum)
    VALUES (7, '007_subscription_expiry_and_notifications', SYSUTCDATETIME(), 'E7F8A9B0C1D2E3F405162738495061728394A5B6C7D8E9F0A1B2C3D4E5F60718');
END
ELSE
BEGIN
    UPDATE dbo.SchemaVersion
    SET AppliedAt = SYSUTCDATETIME(), Checksum = 'E7F8A9B0C1D2E3F405162738495061728394A5B6C7D8E9F0A1B2C3D4E5F60718'
    WHERE Version = 7;
END

COMMIT TRANSACTION;
PRINT 'Migration 007_subscription_expiry_and_notifications completed successfully.';
