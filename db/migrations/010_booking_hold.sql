-- Migration 010: Booking Hold Status, UTC Expiry Column, and Stale Unpaid Cleanup
-- Target: BookMyFun SQL Server database (run manually after taking a backup)
-- Idempotent and safe to re-run. Never drops or renames any table or column.

SET NOCOUNT ON;

-- 1. Add HoldExpiresAt DATETIME2 NULL column to dbo.Bookings if it does not exist
IF COL_LENGTH('dbo.Bookings', 'HoldExpiresAt') IS NULL
BEGIN
    ALTER TABLE dbo.Bookings
        ADD HoldExpiresAt DATETIME2 NULL;
    PRINT 'Added HoldExpiresAt DATETIME2 NULL column to dbo.Bookings.';
END
ELSE
BEGIN
    PRINT 'HoldExpiresAt column already exists on dbo.Bookings. Skipping.';
END
GO

-- 2. Identify stale pending/unpaid bookings (> 15 minutes old) with no payment record or gateway reference
DECLARE @StaleBookings TABLE (BookingID INT);

INSERT INTO @StaleBookings (BookingID)
SELECT b.BookingID
FROM dbo.Bookings b
WHERE b.Status IN ('Pending', 'Hold')
  AND b.CreatedAt < DATEADD(MINUTE, -15, SYSUTCDATETIME())
  AND b.CreatedAt < DATEADD(MINUTE, -15, GETDATE())
  AND NOT EXISTS (
      SELECT 1
      FROM dbo.Payments p
      WHERE p.BookingID = b.BookingID
  )
  AND NOT EXISTS (
      SELECT 1
      FROM dbo.Payments p2
      WHERE p2.BookingID = b.BookingID
        AND p2.GatewayTransactionID IS NOT NULL
  );

DECLARE @ToExpireCount INT = (SELECT COUNT(*) FROM @StaleBookings);
PRINT 'Stale pending/unpaid booking rows to mark Expired: ' + CAST(@ToExpireCount AS NVARCHAR(20));

-- 3. Mark only those stale unpaid rows as Expired (never touch paid, payment-linked, or gateway-referenced bookings)
IF @ToExpireCount > 0
BEGIN
    UPDATE dbo.Bookings
    SET Status = 'Expired'
    WHERE BookingID IN (SELECT BookingID FROM @StaleBookings)
      AND Status IN ('Pending', 'Hold');

    PRINT 'Updated ' + CAST(@@ROWCOUNT AS NVARCHAR(20)) + ' booking row(s) to Status = ''Expired''.';
END
ELSE
BEGIN
    PRINT 'No stale pending/unpaid bookings found to expire.';
END
GO

-- 4. Record version 10 in dbo.SchemaVersion last
IF OBJECT_ID('dbo.SchemaVersion', 'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.SchemaVersion WHERE Version = 10)
    BEGIN
        INSERT INTO dbo.SchemaVersion (Version, Name, AppliedAt, Checksum)
        VALUES (10, '010_booking_hold', SYSUTCDATETIME(), 'B10C2D3E4F5061728394A5B6C7D8E9F0A1B2C3D4E5F60718A9B0C1D2E3F40610');
        PRINT 'Recorded SchemaVersion 10 (010_booking_hold).';
    END
    ELSE
    BEGIN
        PRINT 'SchemaVersion 10 already recorded. Skipping.';
    END
END
GO
