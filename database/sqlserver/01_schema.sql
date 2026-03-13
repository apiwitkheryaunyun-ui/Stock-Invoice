SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID('dbo.stock_movements', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.stock_movements (
        id INT IDENTITY(1,1) PRIMARY KEY,
        product_id INT NOT NULL,
        movement_type NVARCHAR(10) NOT NULL,
        ref_type NVARCHAR(20) NOT NULL,
        ref_id INT NULL,
        movement_date DATE NOT NULL,
        qty_change DECIMAL(18,2) NOT NULL,
        unit_cost DECIMAL(18,2) NOT NULL,
        note NVARCHAR(255) NULL,
        created_at DATETIME2(0) NOT NULL CONSTRAINT DF_stock_movements_created_at DEFAULT SYSUTCDATETIME(),
        updated_at DATETIME2(0) NOT NULL CONSTRAINT DF_stock_movements_updated_at DEFAULT SYSUTCDATETIME(),
        CONSTRAINT CK_stock_movements_type CHECK (movement_type IN ('in', 'out', 'adjust')),
        CONSTRAINT CK_stock_movements_ref_type CHECK (ref_type IN ('opening_balance', 'purchase', 'sale', 'adjustment')),
        CONSTRAINT CK_stock_movements_qty_nonzero CHECK (qty_change <> 0),
        CONSTRAINT CK_stock_movements_unit_cost CHECK (unit_cost >= 0),
        CONSTRAINT CK_stock_movements_qty_sign CHECK (
            (movement_type = 'in' AND qty_change > 0)
            OR (movement_type = 'out' AND qty_change < 0)
            OR (movement_type = 'adjust')
        )
    );
END;
GO

IF OBJECT_ID('dbo.products', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.products (
        id INT IDENTITY(1,1) PRIMARY KEY,
        sku NVARCHAR(50) NOT NULL UNIQUE,
        name NVARCHAR(200) NOT NULL,
        unit NVARCHAR(20) NOT NULL CONSTRAINT DF_products_unit DEFAULT 'pcs',
        sell_price DECIMAL(18,2) NOT NULL,
        cost_price DECIMAL(18,2) NOT NULL,
        tax_rate DECIMAL(5,2) NOT NULL CONSTRAINT DF_products_tax DEFAULT 7,
        stock_qty DECIMAL(18,2) NOT NULL CONSTRAINT DF_products_stock DEFAULT 0,
        is_active BIT NOT NULL CONSTRAINT DF_products_active DEFAULT 1,
        created_at DATETIME2(0) NOT NULL CONSTRAINT DF_products_created_at DEFAULT SYSUTCDATETIME(),
        updated_at DATETIME2(0) NOT NULL CONSTRAINT DF_products_updated_at DEFAULT SYSUTCDATETIME(),
        CONSTRAINT CK_products_sell_price CHECK (sell_price >= 0),
        CONSTRAINT CK_products_cost_price CHECK (cost_price >= 0),
        CONSTRAINT CK_products_tax_rate CHECK (tax_rate >= 0 AND tax_rate <= 100),
        CONSTRAINT CK_products_stock_qty CHECK (stock_qty >= 0)
    );
END;
GO

IF OBJECT_ID('dbo.customers', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.customers (
        id INT IDENTITY(1,1) PRIMARY KEY,
        code NVARCHAR(30) NOT NULL UNIQUE,
        name NVARCHAR(200) NOT NULL,
        tax_id NVARCHAR(20) NULL,
        phone NVARCHAR(30) NULL,
        email NVARCHAR(120) NULL,
        address NVARCHAR(400) NULL,
        is_active BIT NOT NULL CONSTRAINT DF_customers_active DEFAULT 1,
        created_at DATETIME2(0) NOT NULL CONSTRAINT DF_customers_created_at DEFAULT SYSUTCDATETIME(),
        updated_at DATETIME2(0) NOT NULL CONSTRAINT DF_customers_updated_at DEFAULT SYSUTCDATETIME(),
        CONSTRAINT CK_customers_tax_id_len CHECK (tax_id IS NULL OR LEN(REPLACE(tax_id, '-', '')) BETWEEN 10 AND 13),
        CONSTRAINT CK_customers_phone_len CHECK (phone IS NULL OR LEN(REPLACE(REPLACE(phone, '-', ''), ' ', '')) BETWEEN 9 AND 15)
    );
END;
GO

IF OBJECT_ID('dbo.invoices', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.invoices (
        id INT IDENTITY(1,1) PRIMARY KEY,
        invoice_no NVARCHAR(50) NOT NULL UNIQUE,
        customer_id INT NOT NULL,
        invoice_date DATE NOT NULL,
        due_date DATE NULL,
        subtotal DECIMAL(18,2) NOT NULL CONSTRAINT DF_invoices_subtotal DEFAULT 0,
        tax_total DECIMAL(18,2) NOT NULL CONSTRAINT DF_invoices_tax_total DEFAULT 0,
        grand_total DECIMAL(18,2) NOT NULL CONSTRAINT DF_invoices_grand_total DEFAULT 0,
        status NVARCHAR(20) NOT NULL CONSTRAINT DF_invoices_status DEFAULT 'issued',
        notes NVARCHAR(400) NULL,
        created_at DATETIME2(0) NOT NULL CONSTRAINT DF_invoices_created_at DEFAULT SYSUTCDATETIME(),
        updated_at DATETIME2(0) NOT NULL CONSTRAINT DF_invoices_updated_at DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_invoices_customers FOREIGN KEY (customer_id) REFERENCES dbo.customers(id),
        CONSTRAINT CK_invoices_subtotal CHECK (subtotal >= 0),
        CONSTRAINT CK_invoices_tax_total CHECK (tax_total >= 0),
        CONSTRAINT CK_invoices_grand_total CHECK (grand_total >= 0),
        CONSTRAINT CK_invoices_status CHECK (status IN ('draft', 'issued', 'paid', 'cancelled'))
    );
END;
GO

IF OBJECT_ID('dbo.invoice_items', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.invoice_items (
        id INT IDENTITY(1,1) PRIMARY KEY,
        invoice_id INT NOT NULL,
        line_no INT NOT NULL,
        product_id INT NOT NULL,
        qty DECIMAL(18,2) NOT NULL,
        unit_price DECIMAL(18,2) NOT NULL,
        discount DECIMAL(18,2) NOT NULL CONSTRAINT DF_invoice_items_discount DEFAULT 0,
        line_total DECIMAL(18,2) NOT NULL,
        created_at DATETIME2(0) NOT NULL CONSTRAINT DF_invoice_items_created_at DEFAULT SYSUTCDATETIME(),
        updated_at DATETIME2(0) NOT NULL CONSTRAINT DF_invoice_items_updated_at DEFAULT SYSUTCDATETIME(),
        CONSTRAINT UQ_invoice_items_invoice_line UNIQUE (invoice_id, line_no),
        CONSTRAINT FK_invoice_items_invoices FOREIGN KEY (invoice_id) REFERENCES dbo.invoices(id) ON DELETE CASCADE,
        CONSTRAINT FK_invoice_items_products FOREIGN KEY (product_id) REFERENCES dbo.products(id),
        CONSTRAINT CK_invoice_items_qty CHECK (qty > 0),
        CONSTRAINT CK_invoice_items_unit_price CHECK (unit_price >= 0),
        CONSTRAINT CK_invoice_items_discount CHECK (discount >= 0),
        CONSTRAINT CK_invoice_items_line_total CHECK (line_total >= 0)
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_stock_movements_products')
BEGIN
    ALTER TABLE dbo.stock_movements
    ADD CONSTRAINT FK_stock_movements_products FOREIGN KEY (product_id) REFERENCES dbo.products(id);
END;
GO

CREATE INDEX IX_products_name ON dbo.products(name);
CREATE INDEX IX_products_active ON dbo.products(is_active);
CREATE INDEX IX_customers_name ON dbo.customers(name);
CREATE INDEX IX_invoices_date ON dbo.invoices(invoice_date);
CREATE INDEX IX_invoices_customer ON dbo.invoices(customer_id);
CREATE INDEX IX_invoices_status ON dbo.invoices(status);
CREATE INDEX IX_invoice_items_invoice ON dbo.invoice_items(invoice_id);
CREATE INDEX IX_invoice_items_product ON dbo.invoice_items(product_id);
CREATE INDEX IX_stock_movements_product_date ON dbo.stock_movements(product_id, movement_date);
GO

CREATE OR ALTER TRIGGER dbo.trg_products_updated_at
ON dbo.products
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE p
    SET updated_at = SYSUTCDATETIME()
    FROM dbo.products p
    INNER JOIN inserted i ON p.id = i.id;
END;
GO

CREATE OR ALTER TRIGGER dbo.trg_customers_updated_at
ON dbo.customers
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE c
    SET updated_at = SYSUTCDATETIME()
    FROM dbo.customers c
    INNER JOIN inserted i ON c.id = i.id;
END;
GO

CREATE OR ALTER TRIGGER dbo.trg_invoices_updated_at
ON dbo.invoices
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE inv
    SET updated_at = SYSUTCDATETIME()
    FROM dbo.invoices inv
    INNER JOIN inserted i ON inv.id = i.id;
END;
GO

CREATE OR ALTER TRIGGER dbo.trg_invoice_items_updated_at
ON dbo.invoice_items
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE ii
    SET updated_at = SYSUTCDATETIME()
    FROM dbo.invoice_items ii
    INNER JOIN inserted i ON ii.id = i.id;
END;
GO

CREATE OR ALTER TRIGGER dbo.trg_stock_movements_updated_at
ON dbo.stock_movements
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE sm
    SET updated_at = SYSUTCDATETIME()
    FROM dbo.stock_movements sm
    INNER JOIN inserted i ON sm.id = i.id;
END;
GO
