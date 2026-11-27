-- Migration 012: Partner KYC Admin Review Columns & Audit Logs
-- Idempotent migration; does not drop or alter existing columns or tables.

IF COL_LENGTH('dbo.PartnerKycBankDetails', 'RejectionReason') IS NULL
BEGIN
    ALTER TABLE dbo.PartnerKycBankDetails ADD RejectionReason NVARCHAR(500) NULL;
END
GO

IF COL_LENGTH('dbo.PartnerKycBankDetails', 'ReviewedByUserId') IS NULL
BEGIN
    ALTER TABLE dbo.PartnerKycBankDetails ADD ReviewedByUserId INT NULL;
END
GO

IF COL_LENGTH('dbo.PartnerKycBankDetails', 'ReviewedAt') IS NULL
BEGIN
    ALTER TABLE dbo.PartnerKycBankDetails ADD ReviewedAt DATETIME2 NULL;
END
GO

IF OBJECT_ID('dbo.PartnerKycAuditLogs', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.PartnerKycAuditLogs (
        AuditId       INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        AdminUserId   INT NOT NULL,
        PartnerUserId INT NOT NULL,
        Action        NVARCHAR(40) NOT NULL,
        DocumentType  NVARCHAR(50) NULL,
        Reason        NVARCHAR(500) NULL,
        ClientIp      NVARCHAR(45) NULL,
        CreatedAt     DATETIME2 NOT NULL CONSTRAINT DF_PartnerKycAuditLogs_CreatedAt DEFAULT GETUTCDATE(),
        CONSTRAINT FK_PartnerKycAuditLogs_Admin   FOREIGN KEY (AdminUserId)   REFERENCES dbo.Users(UserID),
        CONSTRAINT FK_PartnerKycAuditLogs_Partner FOREIGN KEY (PartnerUserId) REFERENCES dbo.Users(UserID)
    );
END
GO
