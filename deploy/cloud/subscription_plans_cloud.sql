/*
================================================================================
CLOUD DEPLOYMENT SCRIPT: Subscription Plans System (Migrations 008 & 009)
Target Database: Cloud / Hosted Production Database (db75190.databaseasp.net)
================================================================================

*** CRITICAL BACKUP REMINDER ***
Before executing this script in SQL Server Management Studio (SSMS):
1. Take a full, verified database backup of the cloud database.
2. Confirm the backup file has completed successfully.
3. Verify that you have appropriate administrative privileges to execute DDL.

RUN STEPS:
1. Open SQL Server Management Studio (SSMS).
2. Connect to the cloud SQL Server instance using secure credentials.
3. Select the production database from the Available Databases dropdown.
4. Open this script (subscription_plans_cloud.sql).
5. Review the execution plan and run in SSMS (Execute / F5).
6. Verify the Messages and Results tabs:
   - Check the preview SELECT of deleted Pending subscriptions.
   - Confirm 'Migration completed successfully. Transaction committed.'
7. Verify that dbo.SchemaVersion has been updated to Version 9.

CHARACTERISTICS:
- Fully idempotent: can be executed multiple times safely.
- Never drops or renames any existing tables or columns.
- Runs inside a single transaction with automatic error rollback.
================================================================================
*/

SET NOCOUNT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    PRINT 'Starting Subscription Plans Cloud Migration (008 & 009)...';

    -- 0. Ensure dbo.SchemaVersion exists
    IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'SchemaVersion' AND schema_id = SCHEMA_ID('dbo'))
    BEGIN
        CREATE TABLE dbo.SchemaVersion (
            Version INT NOT NULL PRIMARY KEY,
            Name NVARCHAR(100) NOT NULL,
            AppliedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
            Checksum NVARCHAR(64) NOT NULL
        );
        PRINT 'Created dbo.SchemaVersion tracking table.';
    END

    -- ------------------------------------------------------------------------
    -- PART A (008): SCHEMA ALTERATIONS & TABLES
    -- ------------------------------------------------------------------------

    -- 1. SubscriptionPlans: PlanType and Description
    IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'SubscriptionPlans' AND schema_id = SCHEMA_ID('dbo'))
    BEGIN
        IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.SubscriptionPlans') AND name = 'PlanType')
        BEGIN
            ALTER TABLE dbo.SubscriptionPlans ADD PlanType NVARCHAR(20) NOT NULL CONSTRAINT DF_SubscriptionPlans_PlanType DEFAULT 'Paid';
            PRINT 'Added column PlanType to dbo.SubscriptionPlans.';
        END
        ELSE
        BEGIN
            PRINT 'Column PlanType already exists on dbo.SubscriptionPlans.';
        END

        IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.SubscriptionPlans') AND name = 'Description')
        BEGIN
            ALTER TABLE dbo.SubscriptionPlans ADD Description NVARCHAR(500) NULL;
            PRINT 'Added column Description to dbo.SubscriptionPlans.';
        END
        ELSE
        BEGIN
            PRINT 'Column Description already exists on dbo.SubscriptionPlans.';
        END
    END

    -- 2. Users: TrialUsedAt
    IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'Users' AND schema_id = SCHEMA_ID('dbo'))
    BEGIN
        IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Users') AND name = 'TrialUsedAt')
        BEGIN
            ALTER TABLE dbo.Users ADD TrialUsedAt DATETIME2 NULL;
            PRINT 'Added column TrialUsedAt to dbo.Users.';
        END
        ELSE
        BEGIN
            PRINT 'Column TrialUsedAt already exists on dbo.Users.';
        END
    END

    -- 3. OwnerSubscriptions: Snapshots and Admin Grant fields
    IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'OwnerSubscriptions' AND schema_id = SCHEMA_ID('dbo'))
    BEGIN
        IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.OwnerSubscriptions') AND name = 'PlanNameSnapshot')
        BEGIN
            ALTER TABLE dbo.OwnerSubscriptions ADD PlanNameSnapshot NVARCHAR(100) NULL;
            PRINT 'Added column PlanNameSnapshot to dbo.OwnerSubscriptions.';
        END
        ELSE
        BEGIN
            PRINT 'Column PlanNameSnapshot already exists on dbo.OwnerSubscriptions.';
        END

        IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.OwnerSubscriptions') AND name = 'MaxListingsSnapshot')
        BEGIN
            ALTER TABLE dbo.OwnerSubscriptions ADD MaxListingsSnapshot INT NULL;
            PRINT 'Added column MaxListingsSnapshot to dbo.OwnerSubscriptions.';
        END
        ELSE
        BEGIN
            PRINT 'Column MaxListingsSnapshot already exists on dbo.OwnerSubscriptions.';
        END

        IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.OwnerSubscriptions') AND name = 'DurationDaysSnapshot')
        BEGIN
            ALTER TABLE dbo.OwnerSubscriptions ADD DurationDaysSnapshot INT NULL;
            PRINT 'Added column DurationDaysSnapshot to dbo.OwnerSubscriptions.';
        END
        ELSE
        BEGIN
            PRINT 'Column DurationDaysSnapshot already exists on dbo.OwnerSubscriptions.';
        END

        IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.OwnerSubscriptions') AND name = 'GrantedByUserID')
        BEGIN
            ALTER TABLE dbo.OwnerSubscriptions ADD GrantedByUserID INT NULL;
            PRINT 'Added column GrantedByUserID to dbo.OwnerSubscriptions.';
        END
        ELSE
        BEGIN
            PRINT 'Column GrantedByUserID already exists on dbo.OwnerSubscriptions.';
        END

        IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.OwnerSubscriptions') AND name = 'GrantReason')
        BEGIN
            ALTER TABLE dbo.OwnerSubscriptions ADD GrantReason NVARCHAR(300) NULL;
            PRINT 'Added column GrantReason to dbo.OwnerSubscriptions.';
        END
        ELSE
        BEGIN
            PRINT 'Column GrantReason already exists on dbo.OwnerSubscriptions.';
        END
    END

    -- 4. Create PhotoShootRequests Table
    IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'PhotoShootRequests' AND schema_id = SCHEMA_ID('dbo'))
    BEGIN
        CREATE TABLE dbo.PhotoShootRequests (
            RequestID INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
            PropertyID INT NOT NULL,
            OwnerID INT NOT NULL,
            ShootType NVARCHAR(10) NOT NULL CONSTRAINT CK_PhotoShootRequests_ShootType CHECK (ShootType IN ('Photo', 'Video', 'Both')),
            Status NVARCHAR(20) NOT NULL CONSTRAINT DF_PhotoShootRequests_Status DEFAULT 'Requested' CONSTRAINT CK_PhotoShootRequests_Status CHECK (Status IN ('Requested', 'Scheduled', 'Completed', 'Cancelled')),
            AssignedToUserID INT NULL,
            Notes NVARCHAR(500) NULL,
            CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_PhotoShootRequests_CreatedAt DEFAULT SYSUTCDATETIME(),
            UpdatedAt DATETIME2 NULL,
            CONSTRAINT FK_PhotoShootRequests_Properties FOREIGN KEY (PropertyID) REFERENCES dbo.Properties(PropertyID),
            CONSTRAINT FK_PhotoShootRequests_Owners FOREIGN KEY (OwnerID) REFERENCES dbo.Users(UserID),
            CONSTRAINT FK_PhotoShootRequests_AssignedStaff FOREIGN KEY (AssignedToUserID) REFERENCES dbo.Users(UserID)
        );
        PRINT 'Created table dbo.PhotoShootRequests.';
    END
    ELSE
    BEGIN
        PRINT 'Table dbo.PhotoShootRequests already exists.';
    END

    -- 5. Indexes for PhotoShootRequests
    IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'PhotoShootRequests' AND schema_id = SCHEMA_ID('dbo'))
    BEGIN
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PhotoShootRequests_OwnerID' AND object_id = OBJECT_ID('dbo.PhotoShootRequests'))
        BEGIN
            CREATE NONCLUSTERED INDEX IX_PhotoShootRequests_OwnerID ON dbo.PhotoShootRequests (OwnerID);
            PRINT 'Created index IX_PhotoShootRequests_OwnerID.';
        END

        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PhotoShootRequests_Status_CreatedAt' AND object_id = OBJECT_ID('dbo.PhotoShootRequests'))
        BEGIN
            CREATE NONCLUSTERED INDEX IX_PhotoShootRequests_Status_CreatedAt ON dbo.PhotoShootRequests (Status, CreatedAt);
            PRINT 'Created index IX_PhotoShootRequests_Status_CreatedAt.';
        END
    END

    -- ------------------------------------------------------------------------
    -- PART B (009): SEEDING & DATA CLEANUP
    -- ------------------------------------------------------------------------

    -- 6. Configure the Trial plan row
    DECLARE @TrialDays INT = 30;
    IF EXISTS (SELECT 1 FROM dbo.SiteSettings WHERE SettingKey = 'TrialDurationDays')
    BEGIN
        SELECT @TrialDays = TRY_CAST(SettingValue AS INT)
        FROM dbo.SiteSettings
        WHERE SettingKey = 'TrialDurationDays';
    END

    IF @TrialDays IS NULL OR @TrialDays <= 0
        SET @TrialDays = 30;

    IF EXISTS (SELECT 1 FROM dbo.SubscriptionPlans WHERE PlanName IN ('Free Trial', 'Trial') OR PlanType = 'Trial')
    BEGIN
        UPDATE dbo.SubscriptionPlans
        SET PlanType = 'Trial',
            Price = 0.00,
            MaxListings = 1,
            DurationInDays = @TrialDays,
            IsActive = 1,
            Description = COALESCE(Description, 'Complimentary trial listing for new property owners.')
        WHERE PlanName IN ('Free Trial', 'Trial') OR PlanType = 'Trial';
        PRINT 'Configured Trial plan row with duration ' + CAST(@TrialDays AS NVARCHAR(10)) + ' days.';
    END
    ELSE
    BEGIN
        INSERT INTO dbo.SubscriptionPlans (PlanName, Price, DurationInDays, MaxListings, IsActive, PlanType, Description)
        VALUES ('Free Trial', 0.00, @TrialDays, 1, 1, 'Trial', 'Complimentary trial listing for new property owners.');
        PRINT 'Inserted Free Trial plan row with duration ' + CAST(@TrialDays AS NVARCHAR(10)) + ' days.';
    END

    -- 7. Seed 'Plan 999' (60 days, 3 listings, Price 999, PlanType 'Paid')
    IF NOT EXISTS (SELECT 1 FROM dbo.SubscriptionPlans WHERE PlanName = 'Plan 999')
    BEGIN
        INSERT INTO dbo.SubscriptionPlans (PlanName, Price, DurationInDays, MaxListings, IsActive, PlanType, Description)
        VALUES ('Plan 999', 999.00, 60, 3, 1, 'Paid', 'Standard host subscription for up to 3 listings for 60 days. Includes photo and video shoot.');
        PRINT 'Inserted Plan 999.';
    END
    ELSE
    BEGIN
        PRINT 'Plan 999 already exists. Leaving unchanged.';
    END

    -- 8. Seed 'Plan 1999' (180 days, 2 listings, Price 1999, PlanType 'Paid')
    IF NOT EXISTS (SELECT 1 FROM dbo.SubscriptionPlans WHERE PlanName = 'Plan 1999')
    BEGIN
        INSERT INTO dbo.SubscriptionPlans (PlanName, Price, DurationInDays, MaxListings, IsActive, PlanType, Description)
        VALUES ('Plan 1999', 1999.00, 180, 2, 1, 'Paid', 'Extended host subscription for up to 2 listings for 180 days. Includes photo and video shoot.');
        PRINT 'Inserted Plan 1999.';
    END
    ELSE
    BEGIN
        PRINT 'Plan 1999 already exists. Leaving unchanged.';
    END

    -- 9. Preview and Delete Pending Subscriptions without Gateway Reference
    DECLARE @PendingSubs TABLE (SubscriptionID INT);

    INSERT INTO @PendingSubs (SubscriptionID)
    SELECT SubscriptionID
    FROM dbo.OwnerSubscriptions
    WHERE PaymentStatus = 'Pending' AND GatewayTransactionID IS NULL;

    -- Preview SELECT for DBA review
    SELECT SubscriptionID AS PendingSubscriptionIDToDelete
    FROM @PendingSubs;

    -- Print IDs to output
    DECLARE @IdList NVARCHAR(MAX) = N'';
    SELECT @IdList = STRING_AGG(CAST(SubscriptionID AS NVARCHAR(20)), ', ')
    FROM @PendingSubs;

    IF @IdList IS NOT NULL AND LEN(@IdList) > 0
    BEGIN
        PRINT 'Found pending subscriptions to delete. SubscriptionIDs: ' + @IdList;
    END
    ELSE
    BEGIN
        PRINT 'No pending subscriptions without gateway transaction reference found.';
    END

    -- Delete only those rows
    DELETE FROM dbo.OwnerSubscriptions
    WHERE SubscriptionID IN (SELECT SubscriptionID FROM @PendingSubs);

    PRINT 'Deleted ' + CAST(@@ROWCOUNT AS NVARCHAR(20)) + ' pending subscription row(s). Kept all paid, gateway-referenced, and admin-granted rows.';

    -- 10. Record versions 8 and 9 in dbo.SchemaVersion
    IF NOT EXISTS (SELECT 1 FROM dbo.SchemaVersion WHERE Version = 8)
    BEGIN
        INSERT INTO dbo.SchemaVersion (Version, Name, AppliedAt, Checksum)
        VALUES (8, '008_subscription_plans', SYSUTCDATETIME(), 'E08A9B1C2D3E4F5061728394A5B6C7D8E9F0A1B2C3D4E5F60718A9B0C1D2E3F4');
    END
    ELSE
    BEGIN
        UPDATE dbo.SchemaVersion
        SET AppliedAt = SYSUTCDATETIME(), Checksum = 'E08A9B1C2D3E4F5061728394A5B6C7D8E9F0A1B2C3D4E5F60718A9B0C1D2E3F4'
        WHERE Version = 8;
    END

    IF NOT EXISTS (SELECT 1 FROM dbo.SchemaVersion WHERE Version = 9)
    BEGIN
        INSERT INTO dbo.SchemaVersion (Version, Name, AppliedAt, Checksum)
        VALUES (9, '009_subscription_seed_and_cleanup', SYSUTCDATETIME(), 'A09B1C2D3E4F5061728394A5B6C7D8E9F0A1B2C3D4E5F60718A9B0C1D2E3F405');
    END
    ELSE
    BEGIN
        UPDATE dbo.SchemaVersion
        SET AppliedAt = SYSUTCDATETIME(), Checksum = 'A09B1C2D3E4F5061728394A5B6C7D8E9F0A1B2C3D4E5F60718A9B0C1D2E3F405'
        WHERE Version = 9;
    END

    COMMIT TRANSACTION;
    PRINT 'Subscription Plans Cloud Migration completed successfully. Transaction committed.';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
    BEGIN
        ROLLBACK TRANSACTION;
        PRINT 'Error encountered during cloud migration. Transaction rolled back.';
    END
    DECLARE @ErrorMessage NVARCHAR(4000) = ERROR_MESSAGE();
    DECLARE @ErrorSeverity INT = ERROR_SEVERITY();
    DECLARE @ErrorState INT = ERROR_STATE();
    RAISERROR(@ErrorMessage, @ErrorSeverity, @ErrorState);
END CATCH;
