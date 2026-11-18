-- =====================================================================================
-- BookMyFun Safe Schema Update: Allow 'PaymentFailed' in Bookings Status Constraint
-- Idempotent script for production execution
-- =====================================================================================

DECLARE @ConstraintName NVARCHAR(200);

SELECT TOP 1 @ConstraintName = name 
FROM sys.check_constraints 
WHERE parent_object_id = OBJECT_ID('Bookings') 
  AND definition LIKE '%Status%';

IF @ConstraintName IS NOT NULL
BEGIN
    DECLARE @DropSql NVARCHAR(500) = 'ALTER TABLE Bookings DROP CONSTRAINT ' + QUOTENAME(@ConstraintName);
    EXEC(@DropSql);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID('Bookings') AND name = 'CK_Bookings_Status')
BEGIN
    ALTER TABLE Bookings ADD CONSTRAINT CK_Bookings_Status 
    CHECK (Status IN ('Pending', 'Confirmed', 'Cancelled', 'Completed', 'PaymentFailed'));
END
GO
