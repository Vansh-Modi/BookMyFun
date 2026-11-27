-- Migration 011: Partner KYC & Bank Details (Owners and Event Managers)
-- Idempotent migration; does not drop any tables or columns.

IF OBJECT_ID('dbo.PartnerKycBankDetails', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.PartnerKycBankDetails (
        KycId                       INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        UserId                      INT NOT NULL,
        AadhaarDocumentKey          NVARCHAR(500) NULL,
        PanDocumentKey              NVARCHAR(500) NULL,
        BankProofDocumentKey        NVARCHAR(500) NULL,
        AccountHolderName           NVARCHAR(200) NULL,
        BankAccountNumberEncrypted  NVARCHAR(MAX) NULL,
        BankAccountLast4            NVARCHAR(4) NULL,
        IfscCode                    NVARCHAR(20) NULL,
        KycStatus                   NVARCHAR(40) NOT NULL CONSTRAINT DF_PartnerKycBankDetails_Status DEFAULT 'Incomplete',
        FirstPopupShownAt           DATETIME2 NULL,
        SubmittedAt                 DATETIME2 NULL,
        UpdatedAt                   DATETIME2 NOT NULL CONSTRAINT DF_PartnerKycBankDetails_UpdatedAt DEFAULT GETUTCDATE(),
        CONSTRAINT UQ_PartnerKycBankDetails_UserId UNIQUE (UserId),
        CONSTRAINT FK_PartnerKycBankDetails_Users FOREIGN KEY (UserId) REFERENCES dbo.Users(UserID)
    );
END
GO
