-- ============================================================================
-- Migration: 005_query_indexes.sql
-- Description: Creates performance query indexes for marketplace search, media,
--              bookings, event managers, portfolios, and subscriptions.
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

-- 1. Property search & listings index
IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'Properties' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Properties_Search' AND object_id = OBJECT_ID('dbo.Properties'))
    BEGIN
        CREATE NONCLUSTERED INDEX IX_Properties_Search
        ON dbo.Properties (IsApproved, ListingStatus)
        INCLUDE (PropertyID, OwnerID, Title, Location, City, State, PricePerNight, MaxGuests, CreatedAt);
        PRINT 'Created index IX_Properties_Search.';
    END

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Properties_OwnerID' AND object_id = OBJECT_ID('dbo.Properties'))
    BEGIN
        CREATE NONCLUSTERED INDEX IX_Properties_OwnerID
        ON dbo.Properties (OwnerID, CreatedAt DESC);
        PRINT 'Created index IX_Properties_OwnerID.';
    END
END

-- 2. Property media gallery index
IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'PropertyMedia' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PropertyMedia_Property_Sort' AND object_id = OBJECT_ID('dbo.PropertyMedia'))
    BEGIN
        CREATE NONCLUSTERED INDEX IX_PropertyMedia_Property_Sort
        ON dbo.PropertyMedia (PropertyID, SortOrder ASC, MediaID ASC)
        INCLUDE (MediaType, StorageUrl, IsPrimary);
        PRINT 'Created index IX_PropertyMedia_Property_Sort.';
    END
END

-- 3. Bookings queries index
IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'Bookings' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Bookings_Property_Dates_Status' AND object_id = OBJECT_ID('dbo.Bookings'))
    BEGIN
        CREATE NONCLUSTERED INDEX IX_Bookings_Property_Dates_Status
        ON dbo.Bookings (PropertyID, CheckInDate, CheckOutDate, Status);
        PRINT 'Created index IX_Bookings_Property_Dates_Status.';
    END

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Bookings_ClientID_CreatedAt' AND object_id = OBJECT_ID('dbo.Bookings'))
    BEGIN
        CREATE NONCLUSTERED INDEX IX_Bookings_ClientID_CreatedAt
        ON dbo.Bookings (ClientID, CreatedAt DESC);
        PRINT 'Created index IX_Bookings_ClientID_CreatedAt.';
    END
END

-- 4. Users role & verification index
IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'Users' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Users_Role_Status' AND object_id = OBJECT_ID('dbo.Users'))
    BEGIN
        CREATE NONCLUSTERED INDEX IX_Users_Role_Status
        ON dbo.Users (Role, VerificationStatus, IsActive)
        INCLUDE (UserID, FullName, Email, ServiceAreas);
        PRINT 'Created index IX_Users_Role_Status.';
    END
END

-- 5. Event packages index
IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'EventPackages' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_EventPackages_User_Active' AND object_id = OBJECT_ID('dbo.EventPackages'))
    BEGIN
        CREATE NONCLUSTERED INDEX IX_EventPackages_User_Active
        ON dbo.EventPackages (CreatedByUserID, IsActive)
        INCLUDE (PackageID, Name, BasePrice, Category);
        PRINT 'Created index IX_EventPackages_User_Active.';
    END
END

-- 6. Event manager portfolios index
IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'EventManagerPortfolios' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_EventManagerPortfolios_User_Approved' AND object_id = OBJECT_ID('dbo.EventManagerPortfolios'))
    BEGIN
        CREATE NONCLUSTERED INDEX IX_EventManagerPortfolios_User_Approved
        ON dbo.EventManagerPortfolios (UserID, IsApproved)
        INCLUDE (PortfolioID, Title, ImageUrl);
        PRINT 'Created index IX_EventManagerPortfolios_User_Approved.';
    END
END

-- 7. Owner subscriptions index
IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'OwnerSubscriptions' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_OwnerSubscriptions_Owner_Status' AND object_id = OBJECT_ID('dbo.OwnerSubscriptions'))
    BEGIN
        CREATE NONCLUSTERED INDEX IX_OwnerSubscriptions_Owner_Status
        ON dbo.OwnerSubscriptions (OwnerID, Status, EndDate DESC);
        PRINT 'Created index IX_OwnerSubscriptions_Owner_Status.';
    END
END

-- Record migration in dbo.SchemaVersion
IF NOT EXISTS (SELECT 1 FROM dbo.SchemaVersion WHERE Version = 5)
BEGIN
    INSERT INTO dbo.SchemaVersion (Version, Name, AppliedAt, Checksum)
    VALUES (5, '005_query_indexes', SYSUTCDATETIME(), 'A1B2C3D4E5F60718293A4B5C6D7E8F90123456789ABCDEF0123456789ABCDEF0');
END
ELSE
BEGIN
    UPDATE dbo.SchemaVersion
    SET AppliedAt = SYSUTCDATETIME(), Checksum = 'A1B2C3D4E5F60718293A4B5C6D7E8F90123456789ABCDEF0123456789ABCDEF0'
    WHERE Version = 5;
END

COMMIT TRANSACTION;
PRINT 'Migration 005_query_indexes completed successfully.';
