-- ============================================================================
-- Migration: 001_baseline_check.sql
-- Description: Verifies baseline tables exist in the database without altering them.
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

-- Verify baseline tables exist
DECLARE @MissingTables NVARCHAR(MAX) = N'';

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'Properties' AND schema_id = SCHEMA_ID('dbo'))
    SET @MissingTables = @MissingTables + 'Properties, ';
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'Bookings' AND schema_id = SCHEMA_ID('dbo'))
    SET @MissingTables = @MissingTables + 'Bookings, ';
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'Users' AND schema_id = SCHEMA_ID('dbo'))
    SET @MissingTables = @MissingTables + 'Users, ';
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'EventPackages' AND schema_id = SCHEMA_ID('dbo'))
    SET @MissingTables = @MissingTables + 'EventPackages, ';
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'PropertyImages' AND schema_id = SCHEMA_ID('dbo'))
    SET @MissingTables = @MissingTables + 'PropertyImages, ';
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'PropertyMedia' AND schema_id = SCHEMA_ID('dbo'))
    SET @MissingTables = @MissingTables + 'PropertyMedia, ';

IF LEN(@MissingTables) > 0
BEGIN
    PRINT 'WARNING: Baseline check identified missing tables: ' + @MissingTables;
END
ELSE
BEGIN
    PRINT 'SUCCESS: All baseline tables verified.';
END

-- Record migration in dbo.SchemaVersion
IF NOT EXISTS (SELECT 1 FROM dbo.SchemaVersion WHERE Version = 1)
BEGIN
    INSERT INTO dbo.SchemaVersion (Version, Name, AppliedAt, Checksum)
    VALUES (1, '001_baseline_check', SYSUTCDATETIME(), 'E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855');
END
ELSE
BEGIN
    UPDATE dbo.SchemaVersion
    SET AppliedAt = SYSUTCDATETIME(), Checksum = 'E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855'
    WHERE Version = 1;
END

COMMIT TRANSACTION;
PRINT 'Migration 001_baseline_check completed successfully.';
