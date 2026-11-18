-- ==============================================================================
-- BookMyFun - Link Missing Property Occasions Migration Script
-- 
-- DEVELOPER CHECKLIST & RUN INSTRUCTIONS:
-- 1. Ensure you have a full database backup before running this script in production.
-- 2. Verify you are connected to the correct BookMyFun database.
-- 3. Run this script manually using SQL Server Management Studio (SSMS), Azure Data Studio, or sqlcmd.
-- 4. Idempotency Guarantee: This script only inserts missing PropertyOccasions links
--    for properties that currently have NO linked occasions. It does not delete, update,
--    drop, or rename any existing records or tables.
-- ==============================================================================

SET NOCOUNT ON;

PRINT 'Checking for properties with missing occasion links...';

-- Insert default active occasions for any property that currently has no linked occasions in PropertyOccasions
INSERT INTO dbo.PropertyOccasions (PropertyID, OccasionID)
SELECT p.PropertyID, o.OccasionID
FROM dbo.Properties p
CROSS JOIN dbo.Occasions o
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.PropertyOccasions po WHERE po.PropertyID = p.PropertyID
)
AND NOT EXISTS (
    SELECT 1 FROM dbo.PropertyOccasions po WHERE po.PropertyID = p.PropertyID AND po.OccasionID = o.OccasionID
);

PRINT 'Missing property occasions link check and population completed.';
