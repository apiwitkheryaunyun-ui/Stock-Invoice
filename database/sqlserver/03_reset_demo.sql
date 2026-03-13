SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRANSACTION;

DELETE FROM dbo.stock_movements;
DELETE FROM dbo.invoice_items;
DELETE FROM dbo.invoices;
DELETE FROM dbo.customers;
DELETE FROM dbo.products;

DBCC CHECKIDENT ('dbo.stock_movements', RESEED, 0);
DBCC CHECKIDENT ('dbo.invoice_items', RESEED, 0);
DBCC CHECKIDENT ('dbo.invoices', RESEED, 0);
DBCC CHECKIDENT ('dbo.customers', RESEED, 0);
DBCC CHECKIDENT ('dbo.products', RESEED, 0);

COMMIT TRANSACTION;

-- After reset, run:
--   database/sqlserver/02_seed_demo.sql
