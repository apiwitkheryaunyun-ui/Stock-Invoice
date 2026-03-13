SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRANSACTION;

;WITH seq AS (
    SELECT 1 AS n
    UNION ALL
    SELECT n + 1 FROM seq WHERE n < 30
)
INSERT INTO dbo.products (sku, name, unit, sell_price, cost_price, tax_rate, stock_qty, is_active)
SELECT
    CONCAT('SKU', RIGHT(CONCAT('0000', n), 4)),
    CONCAT('Demo Product ', n),
    CASE WHEN n % 3 = 0 THEN 'box' ELSE 'pcs' END,
    CAST(35 + (n * 4.25) AS DECIMAL(18,2)),
    CAST(18 + (n * 2.10) AS DECIMAL(18,2)),
    7,
    0,
    1
FROM seq
WHERE NOT EXISTS (SELECT 1 FROM dbo.products p WHERE p.sku = CONCAT('SKU', RIGHT(CONCAT('0000', n), 4)))
OPTION (MAXRECURSION 0);

;WITH seq AS (
    SELECT 1 AS n
    UNION ALL
    SELECT n + 1 FROM seq WHERE n < 20
)
INSERT INTO dbo.customers (code, name, tax_id, phone, email, address, is_active)
SELECT
    CONCAT('CUST', RIGHT(CONCAT('0000', n), 4)),
    CONCAT('Demo Customer ', n),
    CONCAT('010555', RIGHT(CONCAT('0000000', n), 7)),
    CONCAT('081-555-', RIGHT(CONCAT('0000', n), 4)),
    CONCAT('customer', n, '@demo.local'),
    CONCAT('Demo Address Block ', n),
    1
FROM seq
WHERE NOT EXISTS (SELECT 1 FROM dbo.customers c WHERE c.code = CONCAT('CUST', RIGHT(CONCAT('0000', n), 4)))
OPTION (MAXRECURSION 0);

;WITH seq AS (
    SELECT 1 AS n
    UNION ALL
    SELECT n + 1 FROM seq WHERE n < 40
)
INSERT INTO dbo.invoices (
    invoice_no,
    customer_id,
    invoice_date,
    due_date,
    subtotal,
    tax_total,
    grand_total,
    status,
    notes
)
SELECT
    CONCAT(
        'INV',
        FORMAT(DATEADD(MONTH, -(n % 3), CAST(GETDATE() AS DATE)), 'yyyyMM'),
        RIGHT(CONCAT('0000', n), 4)
    ),
    ((n - 1) % 20) + 1,
    DATEADD(DAY, -(n * 2), CAST(GETDATE() AS DATE)),
    DATEADD(DAY, 7 - (n * 2), CAST(GETDATE() AS DATE)),
    0,
    0,
    0,
    CASE WHEN n % 5 = 0 THEN 'paid' ELSE 'issued' END,
    CONCAT('Demo invoice #', n)
FROM seq
WHERE NOT EXISTS (
    SELECT 1
    FROM dbo.invoices i
    WHERE i.invoice_no = CONCAT(
        'INV',
        FORMAT(DATEADD(MONTH, -(n % 3), CAST(GETDATE() AS DATE)), 'yyyyMM'),
        RIGHT(CONCAT('0000', n), 4)
    )
)
OPTION (MAXRECURSION 0);

INSERT INTO dbo.invoice_items (invoice_id, line_no, product_id, qty, unit_price, discount, line_total)
SELECT
    i.id,
    1,
    ((i.id * 3) % 30) + 1,
    (i.id % 5) + 1,
    p.sell_price,
    0,
    CAST(((i.id % 5) + 1) * p.sell_price AS DECIMAL(18,2))
FROM dbo.invoices i
JOIN dbo.products p ON p.id = ((i.id * 3) % 30) + 1
WHERE NOT EXISTS (SELECT 1 FROM dbo.invoice_items x WHERE x.invoice_id = i.id AND x.line_no = 1);

INSERT INTO dbo.invoice_items (invoice_id, line_no, product_id, qty, unit_price, discount, line_total)
SELECT
    i.id,
    2,
    ((i.id * 7) % 30) + 1,
    ((i.id + 1) % 4) + 1,
    p.sell_price,
    CASE WHEN i.id % 4 = 0 THEN 10 ELSE 0 END,
    CAST((((i.id + 1) % 4) + 1) * p.sell_price - (CASE WHEN i.id % 4 = 0 THEN 10 ELSE 0 END) AS DECIMAL(18,2))
FROM dbo.invoices i
JOIN dbo.products p ON p.id = ((i.id * 7) % 30) + 1
WHERE NOT EXISTS (SELECT 1 FROM dbo.invoice_items x WHERE x.invoice_id = i.id AND x.line_no = 2);

UPDATE inv
SET
    subtotal = x.subtotal,
    tax_total = x.tax_total,
    grand_total = x.grand_total
FROM dbo.invoices inv
CROSS APPLY (
    SELECT
        CAST(ISNULL(SUM(ii.line_total), 0) AS DECIMAL(18,2)) AS subtotal,
        CAST(ISNULL(SUM(ii.line_total), 0) * 0.07 AS DECIMAL(18,2)) AS tax_total,
        CAST(ISNULL(SUM(ii.line_total), 0) * 1.07 AS DECIMAL(18,2)) AS grand_total
    FROM dbo.invoice_items ii
    WHERE ii.invoice_id = inv.id
) x;

INSERT INTO dbo.stock_movements (product_id, movement_type, ref_type, ref_id, movement_date, qty_change, unit_cost, note)
SELECT
    p.id,
    'in',
    'opening_balance',
    NULL,
    DATEADD(DAY, -120, CAST(GETDATE() AS DATE)),
    CAST(80 + (p.id % 20) AS DECIMAL(18,2)),
    p.cost_price,
    'Initial demo stock'
FROM dbo.products p
WHERE NOT EXISTS (
    SELECT 1
    FROM dbo.stock_movements sm
    WHERE sm.product_id = p.id
      AND sm.ref_type = 'opening_balance'
);

INSERT INTO dbo.stock_movements (product_id, movement_type, ref_type, ref_id, movement_date, qty_change, unit_cost, note)
SELECT
    ii.product_id,
    'out',
    'sale',
    ii.invoice_id,
    i.invoice_date,
    CAST(-ii.qty AS DECIMAL(18,2)),
    p.cost_price,
    'Auto stock out from demo invoice'
FROM dbo.invoice_items ii
JOIN dbo.invoices i ON i.id = ii.invoice_id
JOIN dbo.products p ON p.id = ii.product_id
WHERE NOT EXISTS (
    SELECT 1
    FROM dbo.stock_movements sm
    WHERE sm.ref_type = 'sale'
      AND sm.ref_id = ii.invoice_id
      AND sm.product_id = ii.product_id
);

UPDATE p
SET stock_qty = x.total_qty
FROM dbo.products p
CROSS APPLY (
    SELECT CAST(ISNULL(SUM(sm.qty_change), 0) AS DECIMAL(18,2)) AS total_qty
    FROM dbo.stock_movements sm
    WHERE sm.product_id = p.id
) x;

COMMIT TRANSACTION;
