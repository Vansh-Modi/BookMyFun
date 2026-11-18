-- ==============================================================================
-- Idempotent Schema Script: Add IsFeatured column to Properties table
-- Database: BookMyFun
-- NOTE: Please take a full backup of the production database before executing
--       this script manually in SQL Server Management Studio or Azure Data Studio.
-- ==============================================================================

IF NOT EXISTS (
    SELECT 1 
    FROM sys.columns 
    WHERE object_id = OBJECT_ID(N'dbo.Properties') 
      AND name = N'IsFeatured'
)
BEGIN
    ALTER TABLE dbo.Properties 
    ADD IsFeatured BIT NOT NULL CONSTRAINT DF_Properties_IsFeatured DEFAULT 0;
    
    PRINT 'SUCCESS: Column IsFeatured added to Properties table with default value 0.';
END
ELSE
BEGIN
    PRINT 'NOTICE: Column IsFeatured already exists in Properties table. No action taken.';
END
GO
