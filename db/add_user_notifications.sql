-- =====================================================================
-- Migration: Add UserNotifications Table and Index for In-App Alerts
-- Idempotent script for BookMyFun production database
-- =====================================================================

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'UserNotifications' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE dbo.UserNotifications (
        NotificationID INT IDENTITY(1,1) PRIMARY KEY,
        UserID INT NOT NULL,
        Title NVARCHAR(200) NOT NULL,
        Body NVARCHAR(1000) NOT NULL,
        LinkUrl NVARCHAR(500) NULL,
        IsRead BIT NOT NULL DEFAULT 0,
        CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE()
    );
END
GO

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_UserNotifications_UserID_IsRead' AND object_id = OBJECT_ID('dbo.UserNotifications'))
BEGIN
    CREATE INDEX IX_UserNotifications_UserID_IsRead ON dbo.UserNotifications (UserID, IsRead);
END
GO
