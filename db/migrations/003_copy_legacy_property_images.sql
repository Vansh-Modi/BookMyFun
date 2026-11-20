-- ============================================================================
-- Migration: 003_copy_legacy_property_images.sql
-- Description: Copies legacy PropertyImages into PropertyMedia where no PropertyMedia row matches.
--              Sets MediaType = 'image', SourceType = 'legacy', StorageUrl = ImageURL,
--              IsPrimary = IsPrimary, SortOrder = ImageID. Never deletes PropertyImages rows.
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

-- Ensure PropertyMedia table exists
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'PropertyMedia' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE dbo.PropertyMedia (
        MediaID INT IDENTITY(1,1) PRIMARY KEY,
        PropertyID INT NOT NULL CONSTRAINT FK_PropertyMedia_Properties FOREIGN KEY REFERENCES dbo.Properties(PropertyID) ON DELETE CASCADE,
        MediaType NVARCHAR(20) NOT NULL DEFAULT 'image',
        SourceType NVARCHAR(20) NOT NULL DEFAULT 'legacy',
        StorageUrl NVARCHAR(MAX) NOT NULL,
        PublicId NVARCHAR(255) NULL,
        IsPrimary BIT NOT NULL DEFAULT 0,
        SortOrder INT NOT NULL DEFAULT 0,
        CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
    );
    PRINT 'Created table dbo.PropertyMedia.';
END

-- Ensure MediaType constraint allows 'image'
DECLARE @ConstraintName NVARCHAR(200);
SELECT @ConstraintName = name FROM sys.check_constraints
WHERE parent_object_id = OBJECT_ID('dbo.PropertyMedia') AND definition LIKE '%MediaType%';

IF @ConstraintName IS NOT NULL
BEGIN
    DECLARE @DropSql NVARCHAR(500) = N'ALTER TABLE dbo.PropertyMedia DROP CONSTRAINT ' + QUOTENAME(@ConstraintName);
    EXEC sp_executesql @DropSql;
    PRINT 'Dropped legacy check constraint on MediaType to allow ''image''.';
END

-- Add updated check constraint allowing 'image', 'video', 'photo'
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID('dbo.PropertyMedia') AND name = 'CK_PropertyMedia_MediaType')
BEGIN
    ALTER TABLE dbo.PropertyMedia ADD CONSTRAINT CK_PropertyMedia_MediaType CHECK (MediaType IN ('image', 'video', 'photo'));
    PRINT 'Added updated CK_PropertyMedia_MediaType check constraint.';
END

-- Ensure SourceType constraint allows 'legacy'
DECLARE @SourceTypeConstraint NVARCHAR(200);
SELECT @SourceTypeConstraint = name FROM sys.check_constraints
WHERE parent_object_id = OBJECT_ID('dbo.PropertyMedia') AND definition LIKE '%SourceType%';

IF @SourceTypeConstraint IS NOT NULL
BEGIN
    DECLARE @DropSourceTypeSql NVARCHAR(500) = N'ALTER TABLE dbo.PropertyMedia DROP CONSTRAINT ' + QUOTENAME(@SourceTypeConstraint);
    EXEC sp_executesql @DropSourceTypeSql;
    PRINT 'Dropped legacy check constraint on SourceType to allow ''legacy''.';
END

-- Add updated check constraint allowing 'file', 'url', 'legacy'
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID('dbo.PropertyMedia') AND name = 'CK_PropertyMedia_SourceType')
BEGIN
    ALTER TABLE dbo.PropertyMedia ADD CONSTRAINT CK_PropertyMedia_SourceType CHECK (SourceType IN ('file', 'url', 'legacy'));
    PRINT 'Added updated CK_PropertyMedia_SourceType check constraint.';
END

-- Copy legacy images where not already present in PropertyMedia
IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'PropertyImages' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    INSERT INTO dbo.PropertyMedia (PropertyID, MediaType, SourceType, StorageUrl, PublicId, IsPrimary, SortOrder, CreatedAt)
    SELECT pi.PropertyID,
           'image',
           'legacy',
           pi.ImageURL,
           NULL,
           pi.IsPrimary,
           pi.ImageID,
           SYSUTCDATETIME()
    FROM dbo.PropertyImages pi
    WHERE NOT EXISTS (
        SELECT 1 FROM dbo.PropertyMedia pm
        WHERE pm.PropertyID = pi.PropertyID AND pm.StorageUrl = pi.ImageURL
    );

    PRINT 'Legacy PropertyImages rows copied into PropertyMedia.';
END
ELSE
BEGIN
    PRINT 'Table PropertyImages not found; skipped image copy.';
END

-- Record migration in dbo.SchemaVersion
IF NOT EXISTS (SELECT 1 FROM dbo.SchemaVersion WHERE Version = 3)
BEGIN
    INSERT INTO dbo.SchemaVersion (Version, Name, AppliedAt, Checksum)
    VALUES (3, '003_copy_legacy_property_images', SYSUTCDATETIME(), '37BA82A7C4F98E109761D6E57E2F2E237C5BA5CD160B08E9D181D5E7958567A1');
END
ELSE
BEGIN
    UPDATE dbo.SchemaVersion
    SET AppliedAt = SYSUTCDATETIME(), Checksum = '37BA82A7C4F98E109761D6E57E2F2E237C5BA5CD160B08E9D181D5E7958567A1'
    WHERE Version = 3;
END

COMMIT TRANSACTION;
PRINT 'Migration 003_copy_legacy_property_images completed successfully.';
