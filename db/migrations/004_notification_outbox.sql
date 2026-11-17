-- ============================================================================
-- Migration: 004_notification_outbox.sql
-- Description: Creates NotificationOutbox and NotificationDispatches tables with an index on (Status, NextAttemptAt).
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

-- 1. NotificationDispatches (Deduplication store)
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'NotificationDispatches' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE dbo.NotificationDispatches (
        NotificationKey NVARCHAR(255) NOT NULL PRIMARY KEY,
        DispatchedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
    );
    PRINT 'Created table dbo.NotificationDispatches.';
END
ELSE
BEGIN
    PRINT 'Table dbo.NotificationDispatches already exists.';
END

-- 2. NotificationOutbox (Transactional outbox)
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'NotificationOutbox' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE dbo.NotificationOutbox (
        OutboxID BIGINT IDENTITY(1,1) PRIMARY KEY,
        EventType NVARCHAR(100) NOT NULL,
        EntityID INT NOT NULL,
        UserID INT NOT NULL,
        Channel NVARCHAR(20) NOT NULL DEFAULT 'InApp',
        Title NVARCHAR(255) NOT NULL,
        Body NVARCHAR(MAX) NOT NULL,
        LinkUrl NVARCHAR(500) NULL,
        Status NVARCHAR(30) NOT NULL DEFAULT 'Pending',
        RetryCount INT NOT NULL DEFAULT 0,
        MaxRetries INT NOT NULL DEFAULT 3,
        NextAttemptAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        LastError NVARCHAR(MAX) NULL,
        CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        SentAt DATETIME2 NULL
    );
    PRINT 'Created table dbo.NotificationOutbox.';
END
ELSE
BEGIN
    PRINT 'Table dbo.NotificationOutbox already exists.';
END

-- 3. Index on (Status, NextAttemptAt)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_NotificationOutbox_Status_NextAttemptAt' AND object_id = OBJECT_ID('dbo.NotificationOutbox'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_NotificationOutbox_Status_NextAttemptAt
    ON dbo.NotificationOutbox (Status, NextAttemptAt)
    INCLUDE (OutboxID, EventType, Channel, UserID, RetryCount);
    PRINT 'Created index IX_NotificationOutbox_Status_NextAttemptAt.';
END
ELSE
BEGIN
    PRINT 'Index IX_NotificationOutbox_Status_NextAttemptAt already exists.';
END

-- Record migration in dbo.SchemaVersion
IF NOT EXISTS (SELECT 1 FROM dbo.SchemaVersion WHERE Version = 4)
BEGIN
    INSERT INTO dbo.SchemaVersion (Version, Name, AppliedAt, Checksum)
    VALUES (4, '004_notification_outbox', SYSUTCDATETIME(), '8F6E0B1A7C2D3E4F5A6B7C8D9E0F1A2B3C4D5E6F7A8B9C0D1E2F3A4B5C6D7E8F');
END
ELSE
BEGIN
    UPDATE dbo.SchemaVersion
    SET AppliedAt = SYSUTCDATETIME(), Checksum = '8F6E0B1A7C2D3E4F5A6B7C8D9E0F1A2B3C4D5E6F7A8B9C0D1E2F3A4B5C6D7E8F'
    WHERE Version = 4;
END

COMMIT TRANSACTION;
PRINT 'Migration 004_notification_outbox completed successfully.';
