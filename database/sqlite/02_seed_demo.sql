PRAGMA foreign_keys = ON;

BEGIN TRANSACTION;

WITH RECURSIVE seq(x) AS (
    SELECT 1
    UNION ALL
    SELECT x + 1 FROM seq WHERE x < 30
)
INSERT INTO products (
    sku, name, unit, sell_price, cost_price, tax_rate, stock_qty, is_active, created_at, updated_at
)
SELECT
    printf('SKU%04d', x),
    'Demo Product ' || x,
    CASE WHEN x % 3 = 0 THEN 'box' ELSE 'pcs' END,
    round(35 + (x * 4.25), 2),
    round(18 + (x * 2.10), 2),
    7,
    0,
    1,
    datetime('now'),
    datetime('now')
FROM seq;

WITH RECURSIVE seq(x) AS (
    SELECT 1
    UNION ALL
    SELECT x + 1 FROM seq WHERE x < 20
)
INSERT INTO customers (
    code, name, tax_id, phone, email, address, is_active, created_at, updated_at
)
SELECT
    printf('CUST%04d', x),
    'Demo Customer ' || x,
    printf('010555%07d', x),
    printf('081-555-%04d', x),
    'customer' || x || '@demo.local',
    'Demo Address Block ' || x,
    1,
    datetime('now'),
    datetime('now')
FROM seq;

WITH RECURSIVE seq(x) AS (
    SELECT 1
    UNION ALL
    SELECT x + 1 FROM seq WHERE x < 40
)
INSERT INTO invoices (
    invoice_no,
    customer_id,
    invoice_date,
    due_date,
    subtotal,
    tax_total,
    grand_total,
    status,
    notes,
    created_at,
    updated_at
)
SELECT
    'INV' || strftime('%Y%m', date('now', '-' || (x % 3) || ' month')) || printf('%04d', x),
    ((x - 1) % 20) + 1,
    date('now', '-' || (x * 2) || ' day'),
    date('now', '-' || ((x * 2) - 7) || ' day'),
    0,
    0,
    0,
    CASE WHEN x % 5 = 0 THEN 'paid' ELSE 'issued' END,
    'Demo invoice #' || x,
    datetime('now'),
    datetime('now')
FROM seq;

INSERT INTO invoice_items (
    invoice_id, line_no, product_id, qty, unit_price, discount, line_total, created_at, updated_at
)
SELECT
    i.id,
    1,
    ((i.id * 3) % 30) + 1,
    (i.id % 5) + 1,
    p.sell_price,
    0,
    round(((i.id % 5) + 1) * p.sell_price, 2),
    datetime('now'),
    datetime('now')
FROM invoices i
JOIN products p ON p.id = ((i.id * 3) % 30) + 1;

INSERT INTO invoice_items (
    invoice_id, line_no, product_id, qty, unit_price, discount, line_total, created_at, updated_at
)
SELECT
    i.id,
    2,
    ((i.id * 7) % 30) + 1,
    ((i.id + 1) % 4) + 1,
    p.sell_price,
    CASE WHEN i.id % 4 = 0 THEN 10 ELSE 0 END,
    round((((i.id + 1) % 4) + 1) * p.sell_price - (CASE WHEN i.id % 4 = 0 THEN 10 ELSE 0 END), 2),
    datetime('now'),
    datetime('now')
FROM invoices i
JOIN products p ON p.id = ((i.id * 7) % 30) + 1;

UPDATE invoices
SET subtotal = (
        SELECT IFNULL(round(SUM(ii.line_total), 2), 0)
        FROM invoice_items ii
        WHERE ii.invoice_id = invoices.id
    ),
    tax_total = (
        SELECT IFNULL(round(SUM(ii.line_total) * 0.07, 2), 0)
        FROM invoice_items ii
        WHERE ii.invoice_id = invoices.id
    ),
    grand_total = (
        SELECT IFNULL(round(SUM(ii.line_total) * 1.07, 2), 0)
        FROM invoice_items ii
        WHERE ii.invoice_id = invoices.id
    ),
    updated_at = datetime('now');

INSERT INTO stock_movements (
    product_id, movement_type, ref_type, ref_id, movement_date, qty_change, unit_cost, note, created_at, updated_at
)
SELECT
    p.id,
    'in',
    'opening_balance',
    NULL,
    date('now', '-120 day'),
    80 + (p.id % 20),
    p.cost_price,
    'Initial demo stock',
    datetime('now'),
    datetime('now')
FROM products p;

INSERT INTO stock_movements (
    product_id, movement_type, ref_type, ref_id, movement_date, qty_change, unit_cost, note, created_at, updated_at
)
SELECT
    ii.product_id,
    'out',
    'sale',
    ii.invoice_id,
    i.invoice_date,
    -ii.qty,
    p.cost_price,
    'Auto stock out from demo invoice',
    datetime('now'),
    datetime('now')
FROM invoice_items ii
JOIN invoices i ON i.id = ii.invoice_id
JOIN products p ON p.id = ii.product_id;

UPDATE products
SET stock_qty = (
    SELECT IFNULL(round(SUM(sm.qty_change), 2), 0)
    FROM stock_movements sm
    WHERE sm.product_id = products.id
),
updated_at = datetime('now');

COMMIT;
