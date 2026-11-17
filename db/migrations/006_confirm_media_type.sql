-- ============================================================================
-- Migration: 006_confirm_media_type.sql
-- Description: Verifies that MediaType column exists on PropertyMedia with default 'image'.
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

IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'PropertyMedia' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    -- 1. Add MediaType column if it does not exist
    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.PropertyMedia') AND name = 'MediaType')
    BEGIN
        ALTER TABLE dbo.PropertyMedia ADD MediaType NVARCHAR(20) NOT NULL DEFAULT 'image' WITH VALUES;
        PRINT 'Added column MediaType to dbo.PropertyMedia.';
    END
    ELSE
    BEGIN
        PRINT 'Column MediaType already exists on dbo.PropertyMedia.';
    END

    -- 2. Drop any legacy check constraint that might disallow 'image'
    DECLARE @ConstraintName NVARCHAR(200);
    SELECT @ConstraintName = name FROM sys.check_constraints
    WHERE parent_object_id = OBJECT_ID('dbo.PropertyMedia') AND definition LIKE '%MediaType%';

    IF @ConstraintName IS NOT NULL
    BEGIN
        DECLARE @DropSql NVARCHAR(500) = N'ALTER TABLE dbo.PropertyMedia DROP CONSTRAINT ' + QUOTENAME(@ConstraintName);
        EXEC sp_executesql @DropSql;
        PRINT 'Dropped legacy MediaType check constraint.';
    END

    -- 3. Add updated constraint that accepts 'image', 'video', 'photo'
    IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID('dbo.PropertyMedia') AND name = 'CK_PropertyMedia_MediaType')
    BEGIN
        ALTER TABLE dbo.PropertyMedia ADD CONSTRAINT CK_PropertyMedia_MediaType CHECK (MediaType IN ('image', 'video', 'photo'));
        PRINT 'Added CK_PropertyMedia_MediaType constraint.';
    END
END
ELSE
BEGIN
    PRINT 'PropertyMedia table does not exist; skipping column verification.';
END

-- Record migration in dbo.SchemaVersion
IF NOT EXISTS (SELECT 1 FROM dbo.SchemaVersion WHERE Version = 6)
BEGIN
    INSERT INTO dbo.SchemaVersion (Version, Name, AppliedAt, Checksum)
    VALUES (6, '006_confirm_media_type', SYSUTCDATETIME(), 'D4E5F6A7B8C9D0E1F2A3B4C5D6E7F8A9B0C1D2E3F4A5B6C7D8E9F0A1B2C3D4E5');
END
ELSE
BEGIN
    UPDATE dbo.SchemaVersion
    SET AppliedAt = SYSUTCDATETIME(), Checksum = 'D4E5F6A7B8C9D0E1F2A3B4C5D6E7F8A9B0C1D2E3F4A5B6C7D8E9F0A1B2C3D4E5'
    WHERE Version = 6;
END

COMMIT TRANSACTION;
PRINT 'Migration 006_confirm_media_type completed successfully.';
