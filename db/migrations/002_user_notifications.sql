-- ============================================================================
-- Migration: 002_user_notifications.sql
-- Description: Creates UserNotifications table and index on (UserID, IsRead, CreatedAt).
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

-- Create UserNotifications table if not exists
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'UserNotifications' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE dbo.UserNotifications (
        NotificationID INT IDENTITY(1,1) PRIMARY KEY,
        UserID INT NOT NULL,
        Title NVARCHAR(150) NOT NULL,
        Message NVARCHAR(MAX) NOT NULL,
        Link NVARCHAR(500) NULL,
        Category NVARCHAR(50) NOT NULL DEFAULT 'General',
        IsRead BIT NOT NULL DEFAULT 0,
        CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_UserNotifications_Users FOREIGN KEY (UserID) REFERENCES dbo.Users(UserID) ON DELETE CASCADE
    );
    PRINT 'Created table dbo.UserNotifications.';
END
ELSE
BEGIN
    PRINT 'Table dbo.UserNotifications already exists.';
END

-- Ensure index on (UserID, IsRead, CreatedAt)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_UserNotifications_UserID_IsRead_CreatedAt' AND object_id = OBJECT_ID('dbo.UserNotifications'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_UserNotifications_UserID_IsRead_CreatedAt
    ON dbo.UserNotifications (UserID, IsRead, CreatedAt DESC);
    PRINT 'Created index IX_UserNotifications_UserID_IsRead_CreatedAt.';
END
ELSE
BEGIN
    PRINT 'Index IX_UserNotifications_UserID_IsRead_CreatedAt already exists.';
END

-- Record migration in dbo.SchemaVersion
IF NOT EXISTS (SELECT 1 FROM dbo.SchemaVersion WHERE Version = 2)
BEGIN
    INSERT INTO dbo.SchemaVersion (Version, Name, AppliedAt, Checksum)
    VALUES (2, '002_user_notifications', SYSUTCDATETIME(), '9B29367F4F79F1722881D299E1CF3141F9357EEA4AE88A4F1D777420AE0DE457');
END
ELSE
BEGIN
    UPDATE dbo.SchemaVersion
    SET AppliedAt = SYSUTCDATETIME(), Checksum = '9B29367F4F79F1722881D299E1CF3141F9357EEA4AE88A4F1D777420AE0DE457'
    WHERE Version = 2;
END

COMMIT TRANSACTION;
PRINT 'Migration 002_user_notifications completed successfully.';
