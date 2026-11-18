-- =====================================================================================
-- BookMyFun Performance Optimization Indexes
-- Safe, Idempotent script for production execution
-- Designed for top pages: Home, Search, Property Details, Checkout, and Dashboards
-- =====================================================================================

-- 1. Properties: Active & Approved index for Search & Discovery
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Properties_Active_Approved_Price' AND object_id = OBJECT_ID('Properties'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_Properties_Active_Approved_Price 
    ON Properties (IsActive, IsApproved, PricePerNight)
    INCLUDE (Title, Location, City, State, MaxGuests, RatingAverage, ReviewCount, CreatedAt);
END
GO

-- 2. Properties: Tenancy index for Host / Owner Dashboard
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Properties_OwnerID' AND object_id = OBJECT_ID('Properties'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_Properties_OwnerID 
    ON Properties (OwnerID)
    INCLUDE (Title, IsActive, IsApproved, PricePerNight, CreatedAt);
END
GO

-- 3. Bookings: Client Tenancy & Dashboard Lookups
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Bookings_ClientID_Status' AND object_id = OBJECT_ID('Bookings'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_Bookings_ClientID_Status 
    ON Bookings (ClientID, Status)
    INCLUDE (PropertyID, CheckInDate, CheckOutDate, TotalAmount, CreatedAt);
END
GO

-- 4. Bookings: Calendar Overlap & Availability Checks
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Bookings_Property_Availability' AND object_id = OBJECT_ID('Bookings'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_Bookings_Property_Availability 
    ON Bookings (PropertyID, Status, CheckInDate, CheckOutDate);
END
GO

-- 5. Bookings: Event Manager Assigned Setups Lookups
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Bookings_Decorator_Status' AND object_id = OBJECT_ID('Bookings'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_Bookings_Decorator_Status 
    ON Bookings (DecoratorID, SetupStatus, EventDate)
    WHERE DecoratorID IS NOT NULL;
END
GO

-- 6. CustomerReviews: Property Reviews Lookup & Aggregations
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_CustomerReviews_PropertyID_Rating' AND object_id = OBJECT_ID('CustomerReviews'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_CustomerReviews_PropertyID_Rating 
    ON CustomerReviews (PropertyID, Rating, CreatedAt)
    INCLUDE (ClientID, Comment);
END
GO

-- 7. PropertyAmenitiesMapping: Fast Bi-directional Lookup
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PropertyAmenities_PropertyID_AmenityID' AND object_id = OBJECT_ID('PropertyAmenitiesMapping'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_PropertyAmenities_PropertyID_AmenityID 
    ON PropertyAmenitiesMapping (PropertyID, AmenityID);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PropertyAmenities_AmenityID_PropertyID' AND object_id = OBJECT_ID('PropertyAmenitiesMapping'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_PropertyAmenities_AmenityID_PropertyID 
    ON PropertyAmenitiesMapping (AmenityID, PropertyID);
END
GO

-- 8. EventPackages: Manager & Active Filtering
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_EventPackages_User_Active' AND object_id = OBJECT_ID('EventPackages'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_EventPackages_User_Active 
    ON EventPackages (CreatedByUserID, IsActive)
    INCLUDE (Name, Category, BasePrice);
END
GO

-- 9. EventManagerPortfolios: Approval & Display Lookups
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_EventManagerPortfolios_Manager_Approved' AND object_id = OBJECT_ID('EventManagerPortfolios'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_EventManagerPortfolios_Manager_Approved 
    ON EventManagerPortfolios (EventManagerID, IsApproved, DisplayOrder);
END
GO

-- 10. OtpVerifications: High-Throughput Verification Lookups
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_OtpVerifications_Lookup' AND object_id = OBJECT_ID('OtpVerifications'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_OtpVerifications_Lookup 
    ON OtpVerifications (Contact, Purpose, IsUsed, ExpiresAt);
END
GO

-- 11. FlaggedContactAttempts: Audit & Review Dashboard
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_FlaggedContactAttempts_User_Date' AND object_id = OBJECT_ID('FlaggedContactAttempts'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_FlaggedContactAttempts_User_Date 
    ON FlaggedContactAttempts (TargetUserID, CreatedAt);
END
GO
