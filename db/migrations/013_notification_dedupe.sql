-- Migration 013: Extend NotificationOutbox for Email Deduplication, Attempts/Error Tracking, and PasswordResetTokens
-- Target: BookMyFun SQL Server database (run manually after taking a backup)
-- Idempotent and safe to re-run. Never drops or renames any table or column.

SET NOCOUNT ON;

-- 1. Extend dbo.NotificationOutbox with COL_LENGTH guards (reuse existing RetryCount / EventType / LastError if present)
IF OBJECT_ID('dbo.NotificationOutbox', 'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.NotificationOutbox', 'DedupeKey') IS NULL
    BEGIN
        ALTER TABLE dbo.NotificationOutbox ADD DedupeKey NVARCHAR(200) NULL;
        PRINT 'Added DedupeKey NVARCHAR(200) NULL to dbo.NotificationOutbox.';
    END
    ELSE
    BEGIN
        PRINT 'DedupeKey column already exists on dbo.NotificationOutbox. Skipping.';
    END

    IF COL_LENGTH('dbo.NotificationOutbox', 'EventType') IS NULL
    BEGIN
        ALTER TABLE dbo.NotificationOutbox ADD EventType NVARCHAR(60) NULL;
        PRINT 'Added EventType NVARCHAR(60) NULL to dbo.NotificationOutbox.';
    END
    ELSE
    BEGIN
        PRINT 'EventType column already exists on dbo.NotificationOutbox. Skipping.';
    END

    IF COL_LENGTH('dbo.NotificationOutbox', 'RetryCount') IS NULL
       AND COL_LENGTH('dbo.NotificationOutbox', 'Attempts') IS NULL
    BEGIN
        ALTER TABLE dbo.NotificationOutbox ADD Attempts INT NOT NULL CONSTRAINT DF_NotificationOutbox_Attempts DEFAULT 0;
        PRINT 'Added Attempts INT NOT NULL DEFAULT 0 to dbo.NotificationOutbox.';
    END
    ELSE
    BEGIN
        PRINT 'Attempt counter column (RetryCount/Attempts) already exists on dbo.NotificationOutbox. Skipping.';
    END

    IF COL_LENGTH('dbo.NotificationOutbox', 'LastError') IS NULL
    BEGIN
        ALTER TABLE dbo.NotificationOutbox ADD LastError NVARCHAR(500) NULL;
        PRINT 'Added LastError NVARCHAR(500) NULL to dbo.NotificationOutbox.';
    END
    ELSE
    BEGIN
        PRINT 'LastError column already exists on dbo.NotificationOutbox. Skipping.';
    END
END
GO

-- 2. Unique filtered index on DedupeKey WHERE DedupeKey IS NOT NULL
IF OBJECT_ID('dbo.NotificationOutbox', 'U') IS NOT NULL
   AND COL_LENGTH('dbo.NotificationOutbox', 'DedupeKey') IS NOT NULL
   AND NOT EXISTS (
       SELECT 1
       FROM sys.indexes
       WHERE name = 'IX_NotificationOutbox_DedupeKey'
         AND object_id = OBJECT_ID('dbo.NotificationOutbox')
   )
BEGIN
    CREATE UNIQUE NONCLUSTERED INDEX IX_NotificationOutbox_DedupeKey
        ON dbo.NotificationOutbox (DedupeKey)
        WHERE DedupeKey IS NOT NULL;
    PRINT 'Created unique filtered index IX_NotificationOutbox_DedupeKey on dbo.NotificationOutbox.';
END
ELSE
BEGIN
    PRINT 'Index IX_NotificationOutbox_DedupeKey already exists or table/column unavailable. Skipping.';
END
GO

-- 3. Single-use PasswordResetTokens table (stores SHA-256 TokenHash only, never plaintext tokens)
IF OBJECT_ID('dbo.PasswordResetTokens', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.PasswordResetTokens (
        TokenId     INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        UserId      INT NOT NULL,
        TokenHash   NVARCHAR(64) NOT NULL,
        ExpiresAt   DATETIME2 NOT NULL,
        UsedAt      DATETIME2 NULL,
        CreatedAt   DATETIME2 NOT NULL CONSTRAINT DF_PasswordResetTokens_CreatedAt DEFAULT GETUTCDATE(),
        CONSTRAINT FK_PasswordResetTokens_Users FOREIGN KEY (UserId) REFERENCES dbo.Users(UserID)
    );
    PRINT 'Created table dbo.PasswordResetTokens.';
END
ELSE
BEGIN
    PRINT 'Table dbo.PasswordResetTokens already exists. Skipping.';
END
GO

IF OBJECT_ID('dbo.PasswordResetTokens', 'U') IS NOT NULL
   AND NOT EXISTS (
       SELECT 1
       FROM sys.indexes
       WHERE name = 'IX_PasswordResetTokens_TokenHash'
         AND object_id = OBJECT_ID('dbo.PasswordResetTokens')
   )
BEGIN
    CREATE UNIQUE NONCLUSTERED INDEX IX_PasswordResetTokens_TokenHash
        ON dbo.PasswordResetTokens (TokenHash);
    PRINT 'Created index IX_PasswordResetTokens_TokenHash.';
END
GO

-- 4. Record version 13 in dbo.SchemaVersion
IF OBJECT_ID('dbo.SchemaVersion', 'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.SchemaVersion WHERE Version = 13)
    BEGIN
        INSERT INTO dbo.SchemaVersion (Version, Name, AppliedAt, Checksum)
        VALUES (13, '013_notification_dedupe', SYSUTCDATETIME(), '013_NOTIFICATION_DEDUPE_V1');
        PRINT 'Recorded SchemaVersion 13 (013_notification_dedupe).';
    END
    ELSE
    BEGIN
        PRINT 'SchemaVersion 13 already recorded. Skipping.';
    END
END
GO
