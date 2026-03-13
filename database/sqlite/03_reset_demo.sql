PRAGMA foreign_keys = ON;

BEGIN TRANSACTION;

DELETE FROM stock_movements;
DELETE FROM invoice_items;
DELETE FROM invoices;
DELETE FROM customers;
DELETE FROM products;

DELETE FROM sqlite_sequence WHERE name IN (
    'stock_movements',
    'invoice_items',
    'invoices',
    'customers',
    'products'
);

COMMIT;

-- After reset, run:
--   database/sqlite/02_seed_demo.sql
