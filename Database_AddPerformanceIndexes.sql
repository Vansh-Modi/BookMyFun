-- ============================================================================
-- BookMyFun — Database Performance & Query Optimization Migration Script
-- Adds Non-Clustered Indexes on Foreign Keys and Search/Filter Columns
-- Designed for Microsoft SQL Server (MSSQL / MonsterASP Web SQL / SSMS)
-- Idempotent: Uses IF NOT EXISTS checks so it can be safely re-run.
-- DO NOT RUN AUTOMATICALLY: Review and apply during maintenance window.
-- ============================================================================

-- ============================================================================
-- 1. FOREIGN KEY INDEXES: BookingID
-- ============================================================================

-- 1.1 Payments(BookingID)
-- Accelerates payment transaction lookups per reservation and joins on confirmation/receipt screens.
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes 
    WHERE name = 'IX_Payments_BookingID' 
      AND object_id = OBJECT_ID('Payments')
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_Payments_BookingID 
    ON Payments (BookingID);
END;

-- 1.2 CustomerReviews(BookingID)
-- Prevents duplicate review checks and accelerates booking-to-review verification.
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes 
    WHERE name = 'IX_CustomerReviews_BookingID' 
      AND object_id = OBJECT_ID('CustomerReviews')
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_CustomerReviews_BookingID 
    ON CustomerReviews (BookingID);
END;

-- 1.3 BookingRefunds(BookingID)
-- Speeds up refund quote calculations, cancellation status lookups, and admin refund queues.
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes 
    WHERE name = 'IX_BookingRefunds_BookingID' 
      AND object_id = OBJECT_ID('BookingRefunds')
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_BookingRefunds_BookingID 
    ON BookingRefunds (BookingID);
END;

-- 1.4 DamageClaims(BookingID)
-- Speeds up security deposit damage claim lookups associated with a reservation.
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes 
    WHERE name = 'IX_DamageClaims_BookingID' 
      AND object_id = OBJECT_ID('DamageClaims')
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_DamageClaims_BookingID 
    ON DamageClaims (BookingID);
END;


-- ============================================================================
-- 2. FOREIGN KEY INDEXES: PropertyID
-- ============================================================================

-- 2.1 Bookings(PropertyID)
-- Critical index: powers availability checks, host property reservations, and calendar queries.
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes 
    WHERE name = 'IX_Bookings_PropertyID' 
      AND object_id = OBJECT_ID('Bookings')
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_Bookings_PropertyID 
    ON Bookings (PropertyID);
END;

-- 2.2 PropertyMedia(PropertyID)
-- Essential for rendering photos/videos on property cards, gallery carousels, and detail views.
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes 
    WHERE name = 'IX_PropertyMedia_PropertyID' 
      AND object_id = OBJECT_ID('PropertyMedia')
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_PropertyMedia_PropertyID 
    ON PropertyMedia (PropertyID)
    INCLUDE (IsPrimary, SortOrder, MediaType, StorageUrl);
END;

-- 2.3 PropertyImages(PropertyID)
-- Speeds up legacy photo gallery retrieval for properties.
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes 
    WHERE name = 'IX_PropertyImages_PropertyID' 
      AND object_id = OBJECT_ID('PropertyImages')
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_PropertyImages_PropertyID 
    ON PropertyImages (PropertyID);
END;

-- 2.4 PropertyOccasions(PropertyID)
-- Speeds up occasion tag retrieval and occasion-based property searches.
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes 
    WHERE name = 'IX_PropertyOccasions_PropertyID' 
      AND object_id = OBJECT_ID('PropertyOccasions')
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_PropertyOccasions_PropertyID 
    ON PropertyOccasions (PropertyID);
END;

-- 2.5 PropertyAmenitiesMapping(PropertyID)
-- Accelerates amenity badges and feature filtering for properties.
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes 
    WHERE name = 'IX_PropertyAmenitiesMapping_PropertyID' 
      AND object_id = OBJECT_ID('PropertyAmenitiesMapping')
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_PropertyAmenitiesMapping_PropertyID 
    ON PropertyAmenitiesMapping (PropertyID);
END;

-- 2.6 CustomerReviews(PropertyID)
-- Speeds up review listings and average star rating calculations on property detail pages.
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes 
    WHERE name = 'IX_CustomerReviews_PropertyID' 
      AND object_id = OBJECT_ID('CustomerReviews')
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_CustomerReviews_PropertyID 
    ON CustomerReviews (PropertyID)
    INCLUDE (Rating);
END;

-- 2.7 DamageClaims(PropertyID)
-- Speeds up property damage history and security deposit claim reviews.
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes 
    WHERE name = 'IX_DamageClaims_PropertyID' 
      AND object_id = OBJECT_ID('DamageClaims')
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_DamageClaims_PropertyID 
    ON DamageClaims (PropertyID);
END;

-- 2.8 PropertyEventCommissions(PropertyID)
-- Speeds up custom decorator commission lookups for properties during event checkout.
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes 
    WHERE name = 'IX_PropertyEventCommissions_PropertyID' 
      AND object_id = OBJECT_ID('PropertyEventCommissions')
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_PropertyEventCommissions_PropertyID 
    ON PropertyEventCommissions (PropertyID);
END;

-- 2.9 PropertyListingPayments(PropertyID)
-- Speeds up listing payment and activation status history per property.
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes 
    WHERE name = 'IX_PropertyListingPayments_PropertyID' 
      AND object_id = OBJECT_ID('PropertyListingPayments')
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_PropertyListingPayments_PropertyID 
    ON PropertyListingPayments (PropertyID);
END;


-- ============================================================================
-- 3. FOREIGN KEY INDEXES: ClientID
-- ============================================================================

-- 3.1 Bookings(ClientID)
-- Primary index for Traveler Hub (Client Dashboard): lists all trips booked by a guest.
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes 
    WHERE name = 'IX_Bookings_ClientID' 
      AND object_id = OBJECT_ID('Bookings')
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_Bookings_ClientID 
    ON Bookings (ClientID);
END;

-- 3.2 CustomerReviews(ClientID)
-- Speeds up review history and verification of feedback left by a client.
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes 
    WHERE name = 'IX_CustomerReviews_ClientID' 
      AND object_id = OBJECT_ID('CustomerReviews')
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_CustomerReviews_ClientID 
    ON CustomerReviews (ClientID);
END;

-- 3.3 ClientIdVerifications(ClientID)
-- Speeds up KYC verification status lookups during checkout and traveler hub loads.
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes 
    WHERE name = 'IX_ClientIdVerifications_ClientID' 
      AND object_id = OBJECT_ID('ClientIdVerifications')
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_ClientIdVerifications_ClientID 
    ON ClientIdVerifications (ClientID);
END;


-- ============================================================================
-- 4. FOREIGN KEY INDEXES: OwnerID
-- ============================================================================

-- 4.1 Properties(OwnerID)
-- Primary index for Host Dashboard: retrieves all farm listings owned by a host.
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes 
    WHERE name = 'IX_Properties_OwnerID' 
      AND object_id = OBJECT_ID('Properties')
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_Properties_OwnerID 
    ON Properties (OwnerID);
END;

-- 4.2 OwnerSubscriptions(OwnerID)
-- Speeds up host subscription plan validation, tier checks, and active listing quota enforcement.
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes 
    WHERE name = 'IX_OwnerSubscriptions_OwnerID' 
      AND object_id = OBJECT_ID('OwnerSubscriptions')
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_OwnerSubscriptions_OwnerID 
    ON OwnerSubscriptions (OwnerID);
END;

-- 4.3 DamageClaims(OwnerID)
-- Speeds up claim tracking for property hosts in the damage claims dashboard.
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes 
    WHERE name = 'IX_DamageClaims_OwnerID' 
      AND object_id = OBJECT_ID('DamageClaims')
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_DamageClaims_OwnerID 
    ON DamageClaims (OwnerID);
END;


-- ============================================================================
-- 5. SEARCH & FILTER COLUMN INDEXES: Location & ListingStatus (Properties)
-- ============================================================================

-- 5.1 Properties(Location)
-- Powers marketplace search by destination/city (e.g. Hyderabad, Moinabad, Shamshabad).
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes 
    WHERE name = 'IX_Properties_Location' 
      AND object_id = OBJECT_ID('Properties')
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_Properties_Location 
    ON Properties (Location);
END;

-- 5.2 Properties(ListingStatus, IsApproved)
-- Critical index for public catalog: 100% of guest queries filter for Active & Approved properties.
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes 
    WHERE name = 'IX_Properties_ListingStatus_IsApproved' 
      AND object_id = OBJECT_ID('Properties')
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_Properties_ListingStatus_IsApproved 
    ON Properties (ListingStatus, IsApproved)
    INCLUDE (PropertyID, OwnerID, PropertyTypeID, Title, Location, PricePerNight, MaxGuests, IsFeatured);
END;


-- ============================================================================
-- 6. SEARCH & FILTER COLUMN INDEXES: CheckInDate & CheckOutDate (Bookings)
-- ============================================================================

-- 6.1 Bookings(CheckInDate)
-- Speeds up financial date-range reporting and calendar arrival queries.
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes 
    WHERE name = 'IX_Bookings_CheckInDate' 
      AND object_id = OBJECT_ID('Bookings')
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_Bookings_CheckInDate 
    ON Bookings (CheckInDate);
END;

-- 6.2 Bookings(CheckOutDate)
-- Speeds up checkout schedules and departure processing.
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes 
    WHERE name = 'IX_Bookings_CheckOutDate' 
      AND object_id = OBJECT_ID('Bookings')
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_Bookings_CheckOutDate 
    ON Bookings (CheckOutDate);
END;

-- 6.3 Bookings(PropertyID, CheckInDate, CheckOutDate, Status)
-- High-impact composite availability index: powers the reservation overlap check in IsSlotAvailableAsync.
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes 
    WHERE name = 'IX_Bookings_Availability_Composite' 
      AND object_id = OBJECT_ID('Bookings')
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_Bookings_Availability_Composite 
    ON Bookings (PropertyID, Status, CheckInDate, CheckOutDate)
    INCLUDE (BookingType, SlotType);
END;
