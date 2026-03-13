using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using StockInvoiceApp.Models;

namespace StockInvoiceApp.Services;

public sealed class DatabaseService
{
    private readonly AppEnvironment _env;

    public DatabaseService(AppEnvironment env)
    {
        _env = env;
    }

    public string AppMode => _env.Settings.AppMode;
    public decimal TaxRatePercent => _env.Settings.TaxRatePercent;

    public void InitializeDatabase()
    {
        ExecuteSqlScript(Path.Combine(_env.WorkspaceRoot, "database", "sqlite", "01_schema.sql"));
        EnsureDynamicTables();

        if (string.Equals(_env.Settings.AppMode, "demo", StringComparison.OrdinalIgnoreCase) && _env.Settings.Seeding.EnableDemoSeed)
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(1) FROM invoices;";
            var invoiceCount = Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
            if (invoiceCount == 0)
            {
                ExecuteSqlScript(Path.Combine(_env.WorkspaceRoot, "database", "sqlite", "02_seed_demo.sql"));
            }
        }
    }

    public List<ProductLookup> GetProducts()
    {
        var result = new List<ProductLookup>();
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, sku, name, sell_price FROM products WHERE is_active = 1 ORDER BY name;";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new ProductLookup
            {
                Id = reader.GetInt32(0),
                Sku = reader.GetString(1),
                Name = reader.GetString(2),
                SellPrice = reader.GetDecimal(3)
            });
        }

        return result;
    }

    public List<CustomerLookup> GetCustomers()
    {
        var result = new List<CustomerLookup>();
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, code, name FROM customers WHERE is_active = 1 ORDER BY name;";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new CustomerLookup
            {
                Id = reader.GetInt32(0),
                Code = reader.GetString(1),
                Name = reader.GetString(2)
            });
        }

        return result;
    }

    public List<ProductManageRow> GetManageProducts()
    {
        var rows = new List<ProductManageRow>();
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT id, sku, name, unit, sell_price, cost_price, tax_rate, stock_qty, is_active
                            FROM products ORDER BY name;";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new ProductManageRow
            {
                Id = reader.GetInt32(0),
                Sku = reader.GetString(1),
                Name = reader.GetString(2),
                Unit = reader.GetString(3),
                SellPrice = reader.GetDecimal(4),
                CostPrice = reader.GetDecimal(5),
                TaxRate = reader.GetDecimal(6),
                StockQty = reader.GetDecimal(7),
                IsActive = reader.GetInt32(8) == 1
            });
        }

        return rows;
    }

    public List<CustomerManageRow> GetManageCustomers()
    {
        var rows = new List<CustomerManageRow>();
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT id, code, name, tax_id, phone, email, address, is_active
                            FROM customers ORDER BY name;";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new CustomerManageRow
            {
                Id = reader.GetInt32(0),
                Code = reader.GetString(1),
                Name = reader.GetString(2),
                TaxId = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                Phone = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                Email = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                Address = reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
                IsActive = reader.GetInt32(7) == 1
            });
        }

        return rows;
    }

    public int SaveProduct(ProductManageRow product)
    {
        if (string.IsNullOrWhiteSpace(product.Sku) || string.IsNullOrWhiteSpace(product.Name))
        {
            throw new InvalidOperationException("SKU and product name are required.");
        }

        using var conn = Open();
        using var cmd = conn.CreateCommand();
        if (product.Id == 0)
        {
            cmd.CommandText = @"INSERT INTO products (sku, name, unit, sell_price, cost_price, tax_rate, stock_qty, is_active, created_at, updated_at)
                                VALUES ($sku, $name, $unit, $sell, $cost, $tax, 0, $active, datetime('now'), datetime('now'));
                                SELECT last_insert_rowid();";
        }
        else
        {
            cmd.CommandText = @"UPDATE products
                                SET sku = $sku,
                                    name = $name,
                                    unit = $unit,
                                    sell_price = $sell,
                                    cost_price = $cost,
                                    tax_rate = $tax,
                                    is_active = $active,
                                    updated_at = datetime('now')
                                WHERE id = $id;
                                SELECT $id;";
            cmd.Parameters.AddWithValue("$id", product.Id);
        }

        cmd.Parameters.AddWithValue("$sku", product.Sku.Trim());
        cmd.Parameters.AddWithValue("$name", product.Name.Trim());
        cmd.Parameters.AddWithValue("$unit", string.IsNullOrWhiteSpace(product.Unit) ? "pcs" : product.Unit.Trim());
        cmd.Parameters.AddWithValue("$sell", product.SellPrice);
        cmd.Parameters.AddWithValue("$cost", product.CostPrice);
        cmd.Parameters.AddWithValue("$tax", product.TaxRate <= 0 ? TaxRatePercent : product.TaxRate);
        cmd.Parameters.AddWithValue("$active", product.IsActive ? 1 : 0);

        return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    public int SaveCustomer(CustomerManageRow customer)
    {
        if (string.IsNullOrWhiteSpace(customer.Code) || string.IsNullOrWhiteSpace(customer.Name))
        {
            throw new InvalidOperationException("Customer code and name are required.");
        }

        using var conn = Open();
        using var cmd = conn.CreateCommand();
        if (customer.Id == 0)
        {
            cmd.CommandText = @"INSERT INTO customers (code, name, tax_id, phone, email, address, is_active, created_at, updated_at)
                                VALUES ($code, $name, $taxId, $phone, $email, $address, $active, datetime('now'), datetime('now'));
                                SELECT last_insert_rowid();";
        }
        else
        {
            cmd.CommandText = @"UPDATE customers
                                SET code = $code,
                                    name = $name,
                                    tax_id = $taxId,
                                    phone = $phone,
                                    email = $email,
                                    address = $address,
                                    is_active = $active,
                                    updated_at = datetime('now')
                                WHERE id = $id;
                                SELECT $id;";
            cmd.Parameters.AddWithValue("$id", customer.Id);
        }

        cmd.Parameters.AddWithValue("$code", customer.Code.Trim());
        cmd.Parameters.AddWithValue("$name", customer.Name.Trim());
        cmd.Parameters.AddWithValue("$taxId", string.IsNullOrWhiteSpace(customer.TaxId) ? (object)DBNull.Value : customer.TaxId.Trim());
        cmd.Parameters.AddWithValue("$phone", string.IsNullOrWhiteSpace(customer.Phone) ? (object)DBNull.Value : customer.Phone.Trim());
        cmd.Parameters.AddWithValue("$email", string.IsNullOrWhiteSpace(customer.Email) ? (object)DBNull.Value : customer.Email.Trim());
        cmd.Parameters.AddWithValue("$address", string.IsNullOrWhiteSpace(customer.Address) ? (object)DBNull.Value : customer.Address.Trim());
        cmd.Parameters.AddWithValue("$active", customer.IsActive ? 1 : 0);

        return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    public void DeleteProduct(int productId)
    {
        using var conn = Open();
        using var check = conn.CreateCommand();
        check.CommandText = "SELECT COUNT(1) FROM invoice_items WHERE product_id = $id;";
        check.Parameters.AddWithValue("$id", productId);
        if (Convert.ToInt32(check.ExecuteScalar(), CultureInfo.InvariantCulture) > 0)
        {
            throw new InvalidOperationException("This product is already used in invoice history and cannot be deleted.");
        }

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM products WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", productId);
        cmd.ExecuteNonQuery();
    }

    public void DeleteCustomer(int customerId)
    {
        using var conn = Open();
        using var check = conn.CreateCommand();
        check.CommandText = "SELECT COUNT(1) FROM invoices WHERE customer_id = $id;";
        check.Parameters.AddWithValue("$id", customerId);
        if (Convert.ToInt32(check.ExecuteScalar(), CultureInfo.InvariantCulture) > 0)
        {
            throw new InvalidOperationException("This customer is already used in invoice history and cannot be deleted.");
        }

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM customers WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", customerId);
        cmd.ExecuteNonQuery();
    }

    public void AdjustStock(int productId, decimal qtyChange, decimal unitCost, string note)
    {
        if (qtyChange == 0)
        {
            throw new InvalidOperationException("Stock adjustment qty cannot be zero.");
        }

        using var conn = Open();
        using var tx = conn.BeginTransaction();

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"INSERT INTO stock_movements (product_id, movement_type, ref_type, ref_id, movement_date, qty_change, unit_cost, note, created_at, updated_at)
                                VALUES ($pid, $type, 'adjustment', NULL, date('now'), $qty, $cost, $note, datetime('now'), datetime('now'));";
            cmd.Parameters.AddWithValue("$pid", productId);
            cmd.Parameters.AddWithValue("$type", qtyChange > 0 ? "in" : "out");
            cmd.Parameters.AddWithValue("$qty", qtyChange);
            cmd.Parameters.AddWithValue("$cost", unitCost < 0 ? 0 : unitCost);
            cmd.Parameters.AddWithValue("$note", string.IsNullOrWhiteSpace(note) ? "Manual stock adjustment" : note.Trim());
            cmd.ExecuteNonQuery();
        }

        RecalculateProductsStock(conn, tx, new[] { productId });
        tx.Commit();
    }

    public List<InvoiceSummary> GetInvoiceSummaries()
    {
        var result = new List<InvoiceSummary>();
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT i.id, i.invoice_no, c.name, i.invoice_date, i.grand_total, i.status
                            FROM invoices i
                            JOIN customers c ON c.id = i.customer_id
                            ORDER BY i.invoice_date DESC, i.id DESC;";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new InvoiceSummary
            {
                Id = reader.GetInt32(0),
                InvoiceNo = reader.GetString(1),
                CustomerName = reader.GetString(2),
                InvoiceDate = DateTime.Parse(reader.GetString(3), CultureInfo.InvariantCulture),
                GrandTotal = reader.GetDecimal(4),
                Status = reader.GetString(5)
            });
        }

        return result;
    }

    public (InvoiceDetail invoice, ObservableCollection<InvoiceItemRow> items) GetInvoiceWithItems(int invoiceId)
    {
        using var conn = Open();
        InvoiceDetail invoice;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"SELECT id, invoice_no, customer_id, invoice_date, due_date, status, notes, subtotal, tax_total, grand_total
                                FROM invoices WHERE id = $id;";
            cmd.Parameters.AddWithValue("$id", invoiceId);
            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
            {
                throw new InvalidOperationException("Invoice not found.");
            }

            invoice = new InvoiceDetail
            {
                Id = reader.GetInt32(0),
                InvoiceNo = reader.GetString(1),
                CustomerId = reader.GetInt32(2),
                InvoiceDate = DateTime.Parse(reader.GetString(3), CultureInfo.InvariantCulture),
                DueDate = reader.IsDBNull(4) ? null : DateTime.Parse(reader.GetString(4), CultureInfo.InvariantCulture),
                Status = reader.GetString(5),
                Notes = reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
                Subtotal = reader.GetDecimal(7),
                TaxTotal = reader.GetDecimal(8),
                GrandTotal = reader.GetDecimal(9)
            };
        }

        var items = new ObservableCollection<InvoiceItemRow>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"SELECT ii.id, ii.product_id, p.sku, p.name, ii.qty, ii.unit_price, ii.discount
                                FROM invoice_items ii
                                JOIN products p ON p.id = ii.product_id
                                WHERE ii.invoice_id = $id
                                ORDER BY ii.line_no;";
            cmd.Parameters.AddWithValue("$id", invoiceId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                items.Add(new InvoiceItemRow
                {
                    Id = reader.GetInt32(0),
                    ProductId = reader.GetInt32(1),
                    ProductDisplay = $"{reader.GetString(2)} - {reader.GetString(3)}",
                    Qty = reader.GetDecimal(4),
                    UnitPrice = reader.GetDecimal(5),
                    Discount = reader.GetDecimal(6)
                });
            }
        }

        return (invoice, items);
    }

    public InvoicePrintData GetInvoicePrintData(int invoiceId)
    {
        using var conn = Open();
        var data = new InvoicePrintData();

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"SELECT i.id, i.invoice_no, i.invoice_date, i.due_date, i.status, i.notes, i.subtotal, i.tax_total, i.grand_total,
                                       c.id, c.code, c.name, c.tax_id, c.phone, c.email, c.address
                                FROM invoices i
                                JOIN customers c ON c.id = i.customer_id
                                WHERE i.id = $id;";
            cmd.Parameters.AddWithValue("$id", invoiceId);
            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
            {
                throw new InvalidOperationException("Invoice not found.");
            }

            data.InvoiceId = reader.GetInt32(0);
            data.InvoiceNo = reader.GetString(1);
            data.InvoiceDate = DateTime.Parse(reader.GetString(2), CultureInfo.InvariantCulture);
            data.DueDate = reader.IsDBNull(3) ? null : DateTime.Parse(reader.GetString(3), CultureInfo.InvariantCulture);
            data.Status = reader.GetString(4);
            data.Notes = reader.IsDBNull(5) ? string.Empty : reader.GetString(5);
            data.Subtotal = reader.GetDecimal(6);
            data.TaxTotal = reader.GetDecimal(7);
            data.GrandTotal = reader.GetDecimal(8);
            data.CustomerId = reader.GetInt32(9);
            data.CustomerCode = reader.GetString(10);
            data.CustomerName = reader.GetString(11);
            data.CustomerTaxId = reader.IsDBNull(12) ? string.Empty : reader.GetString(12);
            data.CustomerPhone = reader.IsDBNull(13) ? string.Empty : reader.GetString(13);
            data.CustomerEmail = reader.IsDBNull(14) ? string.Empty : reader.GetString(14);
            data.CustomerAddress = reader.IsDBNull(15) ? string.Empty : reader.GetString(15);
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"SELECT ii.line_no, p.sku, p.name, ii.qty, ii.unit_price, ii.discount, ii.line_total
                                FROM invoice_items ii
                                JOIN products p ON p.id = ii.product_id
                                WHERE ii.invoice_id = $id
                                ORDER BY ii.line_no;";
            cmd.Parameters.AddWithValue("$id", invoiceId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                data.Items.Add(new InvoicePrintItem
                {
                    LineNo = reader.GetInt32(0),
                    ProductSku = reader.GetString(1),
                    ProductName = reader.GetString(2),
                    Qty = reader.GetDecimal(3),
                    UnitPrice = reader.GetDecimal(4),
                    Discount = reader.GetDecimal(5),
                    LineTotal = reader.GetDecimal(6)
                });
            }
        }

        data.CustomerCustomFields = GetEntityFieldValuesForPdf("customer", data.CustomerId);
        data.InvoiceCustomFields = GetEntityFieldValuesForPdf("invoice", data.InvoiceId);

        return data;
    }

    public int SaveInvoice(InvoiceDetail invoice, IReadOnlyCollection<InvoiceItemRow> items)
    {
        if (items.Count == 0)
        {
            throw new InvalidOperationException("Invoice must contain at least one item.");
        }

        using var conn = Open();
        using var tx = conn.BeginTransaction();

        if (invoice.Id > 0)
        {
            using var check = conn.CreateCommand();
            check.Transaction = tx;
            check.CommandText = "SELECT status FROM invoices WHERE id = $id;";
            check.Parameters.AddWithValue("$id", invoice.Id);
            var status = check.ExecuteScalar()?.ToString() ?? string.Empty;
            if (string.Equals(status, "paid", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Paid invoice cannot be modified.");
            }
        }

        var subtotal = 0m;
        foreach (var item in items)
        {
            subtotal += item.LineTotal;
        }

        var tax = Math.Round(subtotal * (TaxRatePercent / 100m), 2);
        var grand = subtotal + tax;

        if (invoice.Id == 0)
        {
            using var insert = conn.CreateCommand();
            insert.Transaction = tx;
            insert.CommandText = @"INSERT INTO invoices (invoice_no, customer_id, invoice_date, due_date, subtotal, tax_total, grand_total, status, notes, created_at, updated_at)
                                   VALUES ($no, $cid, $date, $due, $sub, $tax, $grand, $status, $notes, datetime('now'), datetime('now'));
                                   SELECT last_insert_rowid();";
            insert.Parameters.AddWithValue("$no", invoice.InvoiceNo);
            insert.Parameters.AddWithValue("$cid", invoice.CustomerId);
            insert.Parameters.AddWithValue("$date", invoice.InvoiceDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            insert.Parameters.AddWithValue("$due", invoice.DueDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? (object)DBNull.Value);
            insert.Parameters.AddWithValue("$sub", subtotal);
            insert.Parameters.AddWithValue("$tax", tax);
            insert.Parameters.AddWithValue("$grand", grand);
            insert.Parameters.AddWithValue("$status", invoice.Status);
            insert.Parameters.AddWithValue("$notes", string.IsNullOrWhiteSpace(invoice.Notes) ? (object)DBNull.Value : invoice.Notes.Trim());
            invoice.Id = Convert.ToInt32(insert.ExecuteScalar(), CultureInfo.InvariantCulture);
        }
        else
        {
            using var update = conn.CreateCommand();
            update.Transaction = tx;
            update.CommandText = @"UPDATE invoices
                                   SET invoice_no = $no,
                                       customer_id = $cid,
                                       invoice_date = $date,
                                       due_date = $due,
                                       subtotal = $sub,
                                       tax_total = $tax,
                                       grand_total = $grand,
                                       status = $status,
                                       notes = $notes,
                                       updated_at = datetime('now')
                                   WHERE id = $id;";
            update.Parameters.AddWithValue("$id", invoice.Id);
            update.Parameters.AddWithValue("$no", invoice.InvoiceNo);
            update.Parameters.AddWithValue("$cid", invoice.CustomerId);
            update.Parameters.AddWithValue("$date", invoice.InvoiceDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            update.Parameters.AddWithValue("$due", invoice.DueDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? (object)DBNull.Value);
            update.Parameters.AddWithValue("$sub", subtotal);
            update.Parameters.AddWithValue("$tax", tax);
            update.Parameters.AddWithValue("$grand", grand);
            update.Parameters.AddWithValue("$status", invoice.Status);
            update.Parameters.AddWithValue("$notes", string.IsNullOrWhiteSpace(invoice.Notes) ? (object)DBNull.Value : invoice.Notes.Trim());
            update.ExecuteNonQuery();

            using var clearItems = conn.CreateCommand();
            clearItems.Transaction = tx;
            clearItems.CommandText = "DELETE FROM invoice_items WHERE invoice_id = $id;";
            clearItems.Parameters.AddWithValue("$id", invoice.Id);
            clearItems.ExecuteNonQuery();

            using var clearMoves = conn.CreateCommand();
            clearMoves.Transaction = tx;
            clearMoves.CommandText = "DELETE FROM stock_movements WHERE ref_type = 'sale' AND ref_id = $id;";
            clearMoves.Parameters.AddWithValue("$id", invoice.Id);
            clearMoves.ExecuteNonQuery();
        }

        var productIds = new HashSet<int>();
        var lineNo = 1;
        foreach (var item in items)
        {
            productIds.Add(item.ProductId);

            using var insertItem = conn.CreateCommand();
            insertItem.Transaction = tx;
            insertItem.CommandText = @"INSERT INTO invoice_items (invoice_id, line_no, product_id, qty, unit_price, discount, line_total, created_at, updated_at)
                                       VALUES ($inv, $line, $pid, $qty, $price, $disc, $total, datetime('now'), datetime('now'));";
            insertItem.Parameters.AddWithValue("$inv", invoice.Id);
            insertItem.Parameters.AddWithValue("$line", lineNo++);
            insertItem.Parameters.AddWithValue("$pid", item.ProductId);
            insertItem.Parameters.AddWithValue("$qty", item.Qty);
            insertItem.Parameters.AddWithValue("$price", item.UnitPrice);
            insertItem.Parameters.AddWithValue("$disc", item.Discount);
            insertItem.Parameters.AddWithValue("$total", item.LineTotal);
            insertItem.ExecuteNonQuery();

            using var stockOut = conn.CreateCommand();
            stockOut.Transaction = tx;
            stockOut.CommandText = @"INSERT INTO stock_movements (product_id, movement_type, ref_type, ref_id, movement_date, qty_change, unit_cost, note, created_at, updated_at)
                                     VALUES ($pid, 'out', 'sale', $refId, $mdate, $qtyChange, $unitCost, 'Invoice stock out', datetime('now'), datetime('now'));";
            stockOut.Parameters.AddWithValue("$pid", item.ProductId);
            stockOut.Parameters.AddWithValue("$refId", invoice.Id);
            stockOut.Parameters.AddWithValue("$mdate", invoice.InvoiceDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            stockOut.Parameters.AddWithValue("$qtyChange", -Math.Abs(item.Qty));
            stockOut.Parameters.AddWithValue("$unitCost", item.UnitPrice);
            stockOut.ExecuteNonQuery();
        }

        RecalculateProductsStock(conn, tx, productIds);
        tx.Commit();
        return invoice.Id;
    }

    public void DeleteInvoice(int invoiceId)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();

        using (var statusCmd = conn.CreateCommand())
        {
            statusCmd.Transaction = tx;
            statusCmd.CommandText = "SELECT status FROM invoices WHERE id = $id;";
            statusCmd.Parameters.AddWithValue("$id", invoiceId);
            var status = statusCmd.ExecuteScalar()?.ToString() ?? string.Empty;
            if (string.Equals(status, "paid", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Paid invoice cannot be deleted.");
            }
        }

        var productIds = new HashSet<int>();
        using (var idsCmd = conn.CreateCommand())
        {
            idsCmd.Transaction = tx;
            idsCmd.CommandText = "SELECT DISTINCT product_id FROM invoice_items WHERE invoice_id = $id;";
            idsCmd.Parameters.AddWithValue("$id", invoiceId);
            using var reader = idsCmd.ExecuteReader();
            while (reader.Read())
            {
                productIds.Add(reader.GetInt32(0));
            }
        }

        using (var deleteMoves = conn.CreateCommand())
        {
            deleteMoves.Transaction = tx;
            deleteMoves.CommandText = "DELETE FROM stock_movements WHERE ref_type = 'sale' AND ref_id = $id;";
            deleteMoves.Parameters.AddWithValue("$id", invoiceId);
            deleteMoves.ExecuteNonQuery();
        }

        using (var deleteItems = conn.CreateCommand())
        {
            deleteItems.Transaction = tx;
            deleteItems.CommandText = "DELETE FROM invoice_items WHERE invoice_id = $id;";
            deleteItems.Parameters.AddWithValue("$id", invoiceId);
            deleteItems.ExecuteNonQuery();
        }

        using (var deleteInvoice = conn.CreateCommand())
        {
            deleteInvoice.Transaction = tx;
            deleteInvoice.CommandText = "DELETE FROM invoices WHERE id = $id;";
            deleteInvoice.Parameters.AddWithValue("$id", invoiceId);
            deleteInvoice.ExecuteNonQuery();
        }

        using (var clearCustom = conn.CreateCommand())
        {
            clearCustom.Transaction = tx;
            clearCustom.CommandText = "DELETE FROM dynamic_field_values WHERE entity_type = 'invoice' AND entity_id = $id;";
            clearCustom.Parameters.AddWithValue("$id", invoiceId);
            clearCustom.ExecuteNonQuery();
        }

        RecalculateProductsStock(conn, tx, productIds);
        tx.Commit();
    }

    public int ImportProducts(IEnumerable<ProductCsvRow> rows)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        var count = 0;

        foreach (var row in rows)
        {
            using var upsert = conn.CreateCommand();
            upsert.Transaction = tx;
            upsert.CommandText = @"INSERT INTO products (sku, name, unit, sell_price, cost_price, tax_rate, stock_qty, is_active, created_at, updated_at)
                                   VALUES ($sku, $name, $unit, $sell, $cost, $tax, 0, 1, datetime('now'), datetime('now'))
                                   ON CONFLICT(sku) DO UPDATE SET
                                      name = excluded.name,
                                      unit = excluded.unit,
                                      sell_price = excluded.sell_price,
                                      cost_price = excluded.cost_price,
                                      tax_rate = excluded.tax_rate,
                                      updated_at = datetime('now');";
            upsert.Parameters.AddWithValue("$sku", row.Sku.Trim());
            upsert.Parameters.AddWithValue("$name", row.Name.Trim());
            upsert.Parameters.AddWithValue("$unit", string.IsNullOrWhiteSpace(row.Unit) ? "pcs" : row.Unit.Trim());
            upsert.Parameters.AddWithValue("$sell", row.SellPrice);
            upsert.Parameters.AddWithValue("$cost", row.CostPrice);
            upsert.Parameters.AddWithValue("$tax", row.TaxRate <= 0 ? TaxRatePercent : row.TaxRate);
            upsert.ExecuteNonQuery();
            count++;
        }

        tx.Commit();
        return count;
    }

    public int ImportCustomers(IEnumerable<CustomerCsvRow> rows)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        var count = 0;

        foreach (var row in rows)
        {
            using var upsert = conn.CreateCommand();
            upsert.Transaction = tx;
            upsert.CommandText = @"INSERT INTO customers (code, name, tax_id, phone, email, address, is_active, created_at, updated_at)
                                   VALUES ($code, $name, $taxId, $phone, $email, $address, 1, datetime('now'), datetime('now'))
                                   ON CONFLICT(code) DO UPDATE SET
                                      name = excluded.name,
                                      tax_id = excluded.tax_id,
                                      phone = excluded.phone,
                                      email = excluded.email,
                                      address = excluded.address,
                                      updated_at = datetime('now');";
            upsert.Parameters.AddWithValue("$code", row.Code.Trim());
            upsert.Parameters.AddWithValue("$name", row.Name.Trim());
            upsert.Parameters.AddWithValue("$taxId", string.IsNullOrWhiteSpace(row.TaxId) ? (object)DBNull.Value : row.TaxId.Trim());
            upsert.Parameters.AddWithValue("$phone", string.IsNullOrWhiteSpace(row.Phone) ? (object)DBNull.Value : row.Phone.Trim());
            upsert.Parameters.AddWithValue("$email", string.IsNullOrWhiteSpace(row.Email) ? (object)DBNull.Value : row.Email.Trim());
            upsert.Parameters.AddWithValue("$address", string.IsNullOrWhiteSpace(row.Address) ? (object)DBNull.Value : row.Address.Trim());
            upsert.ExecuteNonQuery();
            count++;
        }

        tx.Commit();
        return count;
    }

    public List<DynamicFieldDefinition> GetDynamicFieldDefinitions(string entityType)
    {
        var rows = new List<DynamicFieldDefinition>();
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT id, entity_type, field_key, label, data_type, is_required, is_visible, show_in_pdf, sort_order
                            FROM dynamic_field_defs
                            WHERE entity_type = $type
                            ORDER BY sort_order, id;";
        cmd.Parameters.AddWithValue("$type", entityType);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new DynamicFieldDefinition
            {
                Id = reader.GetInt32(0),
                EntityType = reader.GetString(1),
                FieldKey = reader.GetString(2),
                Label = reader.GetString(3),
                DataType = reader.GetString(4),
                IsRequired = reader.GetInt32(5) == 1,
                IsVisible = reader.GetInt32(6) == 1,
                ShowInPdf = reader.GetInt32(7) == 1,
                SortOrder = reader.GetInt32(8)
            });
        }

        return rows;
    }

    public int SaveDynamicFieldDefinition(DynamicFieldDefinition field)
    {
        if (string.IsNullOrWhiteSpace(field.EntityType) || string.IsNullOrWhiteSpace(field.FieldKey) || string.IsNullOrWhiteSpace(field.Label))
        {
            throw new InvalidOperationException("Entity type, field key and label are required.");
        }

        using var conn = Open();
        using var cmd = conn.CreateCommand();
        if (field.Id == 0)
        {
            cmd.CommandText = @"INSERT INTO dynamic_field_defs (entity_type, field_key, label, data_type, is_required, is_visible, show_in_pdf, sort_order, created_at, updated_at)
                                VALUES ($type, $key, $label, $dtype, $req, $visible, $pdf, $order, datetime('now'), datetime('now'));
                                SELECT last_insert_rowid();";
        }
        else
        {
            cmd.CommandText = @"UPDATE dynamic_field_defs
                                SET entity_type = $type,
                                    field_key = $key,
                                    label = $label,
                                    data_type = $dtype,
                                    is_required = $req,
                                    is_visible = $visible,
                                    show_in_pdf = $pdf,
                                    sort_order = $order,
                                    updated_at = datetime('now')
                                WHERE id = $id;
                                SELECT $id;";
            cmd.Parameters.AddWithValue("$id", field.Id);
        }

        cmd.Parameters.AddWithValue("$type", field.EntityType.Trim().ToLowerInvariant());
        cmd.Parameters.AddWithValue("$key", field.FieldKey.Trim().ToLowerInvariant());
        cmd.Parameters.AddWithValue("$label", field.Label.Trim());
        cmd.Parameters.AddWithValue("$dtype", string.IsNullOrWhiteSpace(field.DataType) ? "text" : field.DataType.Trim().ToLowerInvariant());
        cmd.Parameters.AddWithValue("$req", field.IsRequired ? 1 : 0);
        cmd.Parameters.AddWithValue("$visible", field.IsVisible ? 1 : 0);
        cmd.Parameters.AddWithValue("$pdf", field.ShowInPdf ? 1 : 0);
        cmd.Parameters.AddWithValue("$order", field.SortOrder);

        return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    public void DeleteDynamicFieldDefinition(int fieldId)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();

        string fieldKey;
        string entityType;
        using (var getCmd = conn.CreateCommand())
        {
            getCmd.Transaction = tx;
            getCmd.CommandText = "SELECT entity_type, field_key FROM dynamic_field_defs WHERE id = $id;";
            getCmd.Parameters.AddWithValue("$id", fieldId);
            using var reader = getCmd.ExecuteReader();
            if (!reader.Read())
            {
                tx.Rollback();
                return;
            }

            entityType = reader.GetString(0);
            fieldKey = reader.GetString(1);
        }

        using (var delVals = conn.CreateCommand())
        {
            delVals.Transaction = tx;
            delVals.CommandText = "DELETE FROM dynamic_field_values WHERE entity_type = $type AND field_key = $key;";
            delVals.Parameters.AddWithValue("$type", entityType);
            delVals.Parameters.AddWithValue("$key", fieldKey);
            delVals.ExecuteNonQuery();
        }

        using (var delDef = conn.CreateCommand())
        {
            delDef.Transaction = tx;
            delDef.CommandText = "DELETE FROM dynamic_field_defs WHERE id = $id;";
            delDef.Parameters.AddWithValue("$id", fieldId);
            delDef.ExecuteNonQuery();
        }

        tx.Commit();
    }

    public Dictionary<string, string> GetEntityFieldValues(string entityType, int entityId)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT field_key, field_value
                            FROM dynamic_field_values
                            WHERE entity_type = $type AND entity_id = $id;";
        cmd.Parameters.AddWithValue("$type", entityType);
        cmd.Parameters.AddWithValue("$id", entityId);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            map[reader.GetString(0)] = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
        }

        return map;
    }

    public void SaveEntityFieldValues(string entityType, int entityId, Dictionary<string, string> values)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();

        using (var del = conn.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = "DELETE FROM dynamic_field_values WHERE entity_type = $type AND entity_id = $id;";
            del.Parameters.AddWithValue("$type", entityType);
            del.Parameters.AddWithValue("$id", entityId);
            del.ExecuteNonQuery();
        }

        foreach (var item in values)
        {
            using var ins = conn.CreateCommand();
            ins.Transaction = tx;
            ins.CommandText = @"INSERT INTO dynamic_field_values (entity_type, entity_id, field_key, field_value, created_at, updated_at)
                                VALUES ($type, $id, $key, $value, datetime('now'), datetime('now'));";
            ins.Parameters.AddWithValue("$type", entityType);
            ins.Parameters.AddWithValue("$id", entityId);
            ins.Parameters.AddWithValue("$key", item.Key);
            ins.Parameters.AddWithValue("$value", item.Value ?? string.Empty);
            ins.ExecuteNonQuery();
        }

        tx.Commit();
    }

    public void ExportBackup(string outputPath)
    {
        var payload = BuildBackupPayload();
        File.WriteAllText(outputPath, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
    }

    public void ImportBackup(string inputPath)
    {
        if (!File.Exists(inputPath))
        {
            throw new FileNotFoundException("Backup file not found.", inputPath);
        }

        var json = File.ReadAllText(inputPath);
        var payload = JsonSerializer.Deserialize<BackupPayload>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (payload is null)
        {
            throw new InvalidOperationException("Backup file format is invalid.");
        }

        using var conn = Open();
        using var tx = conn.BeginTransaction();

        ExecuteDelete(conn, tx, "DELETE FROM stock_movements;");
        ExecuteDelete(conn, tx, "DELETE FROM invoice_items;");
        ExecuteDelete(conn, tx, "DELETE FROM invoices;");
        ExecuteDelete(conn, tx, "DELETE FROM dynamic_field_values;");
        ExecuteDelete(conn, tx, "DELETE FROM dynamic_field_defs;");
        ExecuteDelete(conn, tx, "DELETE FROM customers;");
        ExecuteDelete(conn, tx, "DELETE FROM products;");

        foreach (var p in payload.Products)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"INSERT INTO products (id, sku, name, unit, sell_price, cost_price, tax_rate, stock_qty, is_active, created_at, updated_at)
                                VALUES ($id, $sku, $name, $unit, $sell, $cost, $tax, $stock, $active, datetime('now'), datetime('now'));";
            cmd.Parameters.AddWithValue("$id", p.Id);
            cmd.Parameters.AddWithValue("$sku", p.Sku);
            cmd.Parameters.AddWithValue("$name", p.Name);
            cmd.Parameters.AddWithValue("$unit", p.Unit);
            cmd.Parameters.AddWithValue("$sell", p.SellPrice);
            cmd.Parameters.AddWithValue("$cost", p.CostPrice);
            cmd.Parameters.AddWithValue("$tax", p.TaxRate);
            cmd.Parameters.AddWithValue("$stock", p.StockQty);
            cmd.Parameters.AddWithValue("$active", p.IsActive ? 1 : 0);
            cmd.ExecuteNonQuery();
        }

        foreach (var c in payload.Customers)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"INSERT INTO customers (id, code, name, tax_id, phone, email, address, is_active, created_at, updated_at)
                                VALUES ($id, $code, $name, $taxId, $phone, $email, $address, $active, datetime('now'), datetime('now'));";
            cmd.Parameters.AddWithValue("$id", c.Id);
            cmd.Parameters.AddWithValue("$code", c.Code);
            cmd.Parameters.AddWithValue("$name", c.Name);
            cmd.Parameters.AddWithValue("$taxId", string.IsNullOrWhiteSpace(c.TaxId) ? (object)DBNull.Value : c.TaxId);
            cmd.Parameters.AddWithValue("$phone", string.IsNullOrWhiteSpace(c.Phone) ? (object)DBNull.Value : c.Phone);
            cmd.Parameters.AddWithValue("$email", string.IsNullOrWhiteSpace(c.Email) ? (object)DBNull.Value : c.Email);
            cmd.Parameters.AddWithValue("$address", string.IsNullOrWhiteSpace(c.Address) ? (object)DBNull.Value : c.Address);
            cmd.Parameters.AddWithValue("$active", c.IsActive ? 1 : 0);
            cmd.ExecuteNonQuery();
        }

        foreach (var inv in payload.Invoices)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"INSERT INTO invoices (id, invoice_no, customer_id, invoice_date, due_date, subtotal, tax_total, grand_total, status, notes, created_at, updated_at)
                                VALUES ($id, $no, $cid, $date, $due, $sub, $tax, $grand, $status, $notes, datetime('now'), datetime('now'));";
            cmd.Parameters.AddWithValue("$id", inv.Id);
            cmd.Parameters.AddWithValue("$no", inv.InvoiceNo);
            cmd.Parameters.AddWithValue("$cid", inv.CustomerId);
            cmd.Parameters.AddWithValue("$date", inv.InvoiceDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("$due", inv.DueDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$sub", inv.Subtotal);
            cmd.Parameters.AddWithValue("$tax", inv.TaxTotal);
            cmd.Parameters.AddWithValue("$grand", inv.GrandTotal);
            cmd.Parameters.AddWithValue("$status", inv.Status);
            cmd.Parameters.AddWithValue("$notes", string.IsNullOrWhiteSpace(inv.Notes) ? (object)DBNull.Value : inv.Notes);
            cmd.ExecuteNonQuery();
        }

        foreach (var item in payload.InvoiceItems)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"INSERT INTO invoice_items (invoice_id, line_no, product_id, qty, unit_price, discount, line_total, created_at, updated_at)
                                VALUES ($invoiceId, $lineNo, $productId, $qty, $price, $discount, $lineTotal, datetime('now'), datetime('now'));";
            cmd.Parameters.AddWithValue("$invoiceId", item.InvoiceId);
            cmd.Parameters.AddWithValue("$lineNo", item.LineNo);
            cmd.Parameters.AddWithValue("$productId", item.ProductId);
            cmd.Parameters.AddWithValue("$qty", item.Qty);
            cmd.Parameters.AddWithValue("$price", item.UnitPrice);
            cmd.Parameters.AddWithValue("$discount", item.Discount);
            cmd.Parameters.AddWithValue("$lineTotal", item.LineTotal);
            cmd.ExecuteNonQuery();
        }

        foreach (var sm in payload.StockMovements)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"INSERT INTO stock_movements (id, product_id, movement_type, ref_type, ref_id, movement_date, qty_change, unit_cost, note, created_at, updated_at)
                                VALUES ($id, $pid, $type, $refType, $refId, $mdate, $qty, $cost, $note, datetime('now'), datetime('now'));";
            cmd.Parameters.AddWithValue("$id", sm.Id);
            cmd.Parameters.AddWithValue("$pid", sm.ProductId);
            cmd.Parameters.AddWithValue("$type", sm.MovementType);
            cmd.Parameters.AddWithValue("$refType", sm.RefType);
            cmd.Parameters.AddWithValue("$refId", sm.RefId ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$mdate", sm.MovementDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("$qty", sm.QtyChange);
            cmd.Parameters.AddWithValue("$cost", sm.UnitCost);
            cmd.Parameters.AddWithValue("$note", sm.Note);
            cmd.ExecuteNonQuery();
        }

        foreach (var d in payload.DynamicFields)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"INSERT INTO dynamic_field_defs (id, entity_type, field_key, label, data_type, is_required, is_visible, show_in_pdf, sort_order, created_at, updated_at)
                                VALUES ($id, $entityType, $fieldKey, $label, $dataType, $isRequired, $isVisible, $showInPdf, $sortOrder, datetime('now'), datetime('now'));";
            cmd.Parameters.AddWithValue("$id", d.Id);
            cmd.Parameters.AddWithValue("$entityType", d.EntityType);
            cmd.Parameters.AddWithValue("$fieldKey", d.FieldKey);
            cmd.Parameters.AddWithValue("$label", d.Label);
            cmd.Parameters.AddWithValue("$dataType", d.DataType);
            cmd.Parameters.AddWithValue("$isRequired", d.IsRequired ? 1 : 0);
            cmd.Parameters.AddWithValue("$isVisible", d.IsVisible ? 1 : 0);
            cmd.Parameters.AddWithValue("$showInPdf", d.ShowInPdf ? 1 : 0);
            cmd.Parameters.AddWithValue("$sortOrder", d.SortOrder);
            cmd.ExecuteNonQuery();
        }

        foreach (var v in payload.DynamicValues)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"INSERT INTO dynamic_field_values (id, entity_type, entity_id, field_key, field_value, created_at, updated_at)
                                VALUES ($id, $entityType, $entityId, $fieldKey, $fieldValue, datetime('now'), datetime('now'));";
            cmd.Parameters.AddWithValue("$id", v.Id);
            cmd.Parameters.AddWithValue("$entityType", v.EntityType);
            cmd.Parameters.AddWithValue("$entityId", v.EntityId);
            cmd.Parameters.AddWithValue("$fieldKey", v.FieldKey);
            cmd.Parameters.AddWithValue("$fieldValue", v.FieldValue);
            cmd.ExecuteNonQuery();
        }

        var productIds = payload.Products.ConvertAll(x => x.Id);
        RecalculateProductsStock(conn, tx, productIds);
        tx.Commit();
    }

    public void HardResetBusinessData()
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();

        ExecuteDelete(conn, tx, "DELETE FROM stock_movements;");
        ExecuteDelete(conn, tx, "DELETE FROM invoice_items;");
        ExecuteDelete(conn, tx, "DELETE FROM invoices;");
        ExecuteDelete(conn, tx, "DELETE FROM dynamic_field_values;");
        ExecuteDelete(conn, tx, "DELETE FROM customers;");
        ExecuteDelete(conn, tx, "DELETE FROM products;");
        ExecuteDelete(conn, tx, "DELETE FROM sqlite_sequence WHERE name IN ('stock_movements','invoice_items','invoices','customers','products','dynamic_field_values');");

        tx.Commit();
    }

    public DashboardMetrics GetDashboardMetrics(DateTime? fromDate = null, DateTime? toDate = null, decimal lowStockThreshold = 10)
    {
        using var conn = Open();
        var metrics = new DashboardMetrics();

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(1), IFNULL(SUM(stock_qty), 0) FROM products WHERE is_active = 1;";
            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                metrics.TotalProducts = reader.GetInt32(0);
                metrics.TotalStockQty = reader.GetDecimal(1);
            }
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(1) FROM customers WHERE is_active = 1;";
            metrics.TotalCustomers = Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(1) FROM invoices WHERE status IN ('issued', 'paid');";
            AppendDateFilter(cmd, "invoice_date", fromDate, toDate, includeWhere: true);
            metrics.TotalInvoices = Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"SELECT
                                IFNULL(SUM(CASE WHEN invoice_date = date('now', 'localtime') THEN grand_total ELSE 0 END), 0),
                                IFNULL(SUM(CASE WHEN strftime('%Y-%m', invoice_date) = strftime('%Y-%m', date('now', 'localtime')) THEN grand_total ELSE 0 END), 0),
                                IFNULL(SUM(CASE WHEN strftime('%Y', invoice_date) = strftime('%Y', date('now', 'localtime')) THEN grand_total ELSE 0 END), 0)
                            FROM invoices
                            WHERE status IN ('issued', 'paid')";
            AppendDateFilter(cmd, "invoice_date", fromDate, toDate, includeWhere: false);
            cmd.CommandText += ";";
            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                metrics.SalesDaily = reader.GetDecimal(0);
                metrics.SalesMonthly = reader.GetDecimal(1);
                metrics.SalesYearly = reader.GetDecimal(2);
            }
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(1) FROM products WHERE is_active = 1 AND stock_qty < $threshold;";
            cmd.Parameters.AddWithValue("$threshold", lowStockThreshold);
            metrics.LowStockCount = Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
        }

        return metrics;
    }

    public List<StockLevelRow> GetStockLevels(decimal lowStockThreshold = 10, int limit = 200)
    {
        var rows = new List<StockLevelRow>();
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT id, sku, name, stock_qty, sell_price,
                                   CASE WHEN stock_qty < $threshold THEN 1 ELSE 0 END AS is_low
                            FROM products
                            WHERE is_active = 1
                            ORDER BY stock_qty ASC, name ASC
                            LIMIT $limit;";
        cmd.Parameters.AddWithValue("$threshold", lowStockThreshold);
        cmd.Parameters.AddWithValue("$limit", limit);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new StockLevelRow
            {
                ProductId = reader.GetInt32(0),
                Sku = reader.GetString(1),
                Name = reader.GetString(2),
                StockQty = reader.GetDecimal(3),
                SellPrice = reader.GetDecimal(4),
                IsLowStock = reader.GetInt32(5) == 1
            });
        }

        return rows;
    }

    public List<SalesSeriesPoint> GetDailySalesSeries(int days = 7, DateTime? fromDate = null, DateTime? toDate = null)
    {
        var points = new List<SalesSeriesPoint>();
        using var conn = Open();

        var filterFrom = fromDate?.Date ?? DateTime.Today.AddDays(-(days - 1));
        var filterTo = toDate?.Date ?? DateTime.Today;

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT invoice_date, IFNULL(SUM(grand_total), 0)
                            FROM invoices
                            WHERE status IN ('issued', 'paid')
                              AND invoice_date BETWEEN $from AND $to
                            GROUP BY invoice_date
                            ORDER BY invoice_date;";
        cmd.Parameters.AddWithValue("$from", filterFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$to", filterTo.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        var map = new Dictionary<string, decimal>(StringComparer.Ordinal);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            map[reader.GetString(0)] = reader.GetDecimal(1);
        }

        for (var d = filterFrom; d <= filterTo; d = d.AddDays(1))
        {
            var key = d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            points.Add(new SalesSeriesPoint
            {
                Label = d.ToString("dd/MM", CultureInfo.InvariantCulture),
                Amount = map.TryGetValue(key, out var amount) ? amount : 0m
            });
        }

        return points;
    }

    public List<SalesSeriesPoint> GetMonthlySalesSeries(int months = 12, DateTime? fromDate = null, DateTime? toDate = null)
    {
        var points = new List<SalesSeriesPoint>();
        using var conn = Open();

        var to = toDate?.Date ?? DateTime.Today;
        var from = fromDate?.Date ?? new DateTime(to.Year, to.Month, 1).AddMonths(-(months - 1));
        from = new DateTime(from.Year, from.Month, 1);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT strftime('%Y-%m', invoice_date) AS ym, IFNULL(SUM(grand_total), 0)
                            FROM invoices
                            WHERE status IN ('issued', 'paid')
                              AND invoice_date BETWEEN $from AND $to
                            GROUP BY ym
                            ORDER BY ym;";
        cmd.Parameters.AddWithValue("$from", from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$to", to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        var map = new Dictionary<string, decimal>(StringComparer.Ordinal);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            map[reader.GetString(0)] = reader.GetDecimal(1);
        }

        for (var m = from; m <= to; m = m.AddMonths(1))
        {
            var key = m.ToString("yyyy-MM", CultureInfo.InvariantCulture);
            points.Add(new SalesSeriesPoint
            {
                Label = m.ToString("MMM yy", CultureInfo.InvariantCulture),
                Amount = map.TryGetValue(key, out var amount) ? amount : 0m
            });
        }

        return points;
    }

    private Dictionary<string, string> GetEntityFieldValuesForPdf(string entityType, int entityId)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT d.label, IFNULL(v.field_value, '')
                            FROM dynamic_field_defs d
                            LEFT JOIN dynamic_field_values v
                                   ON v.entity_type = d.entity_type
                                  AND v.field_key = d.field_key
                                  AND v.entity_id = $entityId
                            WHERE d.entity_type = $type
                              AND d.show_in_pdf = 1
                              AND d.is_visible = 1
                            ORDER BY d.sort_order, d.id;";
        cmd.Parameters.AddWithValue("$entityId", entityId);
        cmd.Parameters.AddWithValue("$type", entityType);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result[reader.GetString(0)] = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
        }

        return result;
    }

    private BackupPayload BuildBackupPayload()
    {
        using var conn = Open();
        var payload = new BackupPayload
        {
            Products = GetManageProducts(),
            Customers = GetManageCustomers(),
            DynamicFields = GetAllDynamicFieldDefinitions(conn),
            DynamicValues = GetAllDynamicFieldValues(conn)
        };

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"SELECT id, invoice_no, customer_id, invoice_date, due_date, status, notes, subtotal, tax_total, grand_total
                                FROM invoices
                                ORDER BY id;";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                payload.Invoices.Add(new InvoiceDetail
                {
                    Id = reader.GetInt32(0),
                    InvoiceNo = reader.GetString(1),
                    CustomerId = reader.GetInt32(2),
                    InvoiceDate = DateTime.Parse(reader.GetString(3), CultureInfo.InvariantCulture),
                    DueDate = reader.IsDBNull(4) ? null : DateTime.Parse(reader.GetString(4), CultureInfo.InvariantCulture),
                    Status = reader.GetString(5),
                    Notes = reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
                    Subtotal = reader.GetDecimal(7),
                    TaxTotal = reader.GetDecimal(8),
                    GrandTotal = reader.GetDecimal(9)
                });
            }
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"SELECT invoice_id, line_no, product_id, qty, unit_price, discount, line_total
                                FROM invoice_items
                                ORDER BY invoice_id, line_no;";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                payload.InvoiceItems.Add(new InvoiceItemRowBackup
                {
                    InvoiceId = reader.GetInt32(0),
                    LineNo = reader.GetInt32(1),
                    ProductId = reader.GetInt32(2),
                    Qty = reader.GetDecimal(3),
                    UnitPrice = reader.GetDecimal(4),
                    Discount = reader.GetDecimal(5),
                    LineTotal = reader.GetDecimal(6)
                });
            }
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"SELECT id, product_id, movement_type, ref_type, ref_id, movement_date, qty_change, unit_cost, note
                                FROM stock_movements
                                ORDER BY id;";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                payload.StockMovements.Add(new StockMovementBackup
                {
                    Id = reader.GetInt32(0),
                    ProductId = reader.GetInt32(1),
                    MovementType = reader.GetString(2),
                    RefType = reader.GetString(3),
                    RefId = reader.IsDBNull(4) ? null : reader.GetInt32(4),
                    MovementDate = DateTime.Parse(reader.GetString(5), CultureInfo.InvariantCulture),
                    QtyChange = reader.GetDecimal(6),
                    UnitCost = reader.GetDecimal(7),
                    Note = reader.IsDBNull(8) ? string.Empty : reader.GetString(8)
                });
            }
        }

        return payload;
    }

    private static List<DynamicFieldDefinition> GetAllDynamicFieldDefinitions(SqliteConnection conn)
    {
        var rows = new List<DynamicFieldDefinition>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT id, entity_type, field_key, label, data_type, is_required, is_visible, show_in_pdf, sort_order
                            FROM dynamic_field_defs
                            ORDER BY entity_type, sort_order, id;";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new DynamicFieldDefinition
            {
                Id = reader.GetInt32(0),
                EntityType = reader.GetString(1),
                FieldKey = reader.GetString(2),
                Label = reader.GetString(3),
                DataType = reader.GetString(4),
                IsRequired = reader.GetInt32(5) == 1,
                IsVisible = reader.GetInt32(6) == 1,
                ShowInPdf = reader.GetInt32(7) == 1,
                SortOrder = reader.GetInt32(8)
            });
        }

        return rows;
    }

    private static List<EntityFieldValue> GetAllDynamicFieldValues(SqliteConnection conn)
    {
        var rows = new List<EntityFieldValue>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT id, entity_type, entity_id, field_key, IFNULL(field_value, '')
                            FROM dynamic_field_values
                            ORDER BY entity_type, entity_id, field_key;";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new EntityFieldValue
            {
                Id = reader.GetInt32(0),
                EntityType = reader.GetString(1),
                EntityId = reader.GetInt32(2),
                FieldKey = reader.GetString(3),
                FieldValue = reader.GetString(4)
            });
        }

        return rows;
    }

    private void EnsureDynamicTables()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS dynamic_field_defs (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                entity_type TEXT NOT NULL CHECK (entity_type IN ('customer', 'product', 'invoice')),
                field_key TEXT NOT NULL,
                label TEXT NOT NULL,
                data_type TEXT NOT NULL DEFAULT 'text' CHECK (data_type IN ('text', 'number', 'date', 'boolean', 'image')),
                is_required INTEGER NOT NULL DEFAULT 0 CHECK (is_required IN (0,1)),
                is_visible INTEGER NOT NULL DEFAULT 1 CHECK (is_visible IN (0,1)),
                show_in_pdf INTEGER NOT NULL DEFAULT 0 CHECK (show_in_pdf IN (0,1)),
                sort_order INTEGER NOT NULL DEFAULT 0,
                created_at TEXT NOT NULL DEFAULT (datetime('now')),
                updated_at TEXT NOT NULL DEFAULT (datetime('now')),
                UNIQUE(entity_type, field_key)
            );
            CREATE TABLE IF NOT EXISTS dynamic_field_values (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                entity_type TEXT NOT NULL CHECK (entity_type IN ('customer', 'product', 'invoice')),
                entity_id INTEGER NOT NULL,
                field_key TEXT NOT NULL,
                field_value TEXT,
                created_at TEXT NOT NULL DEFAULT (datetime('now')),
                updated_at TEXT NOT NULL DEFAULT (datetime('now')),
                UNIQUE(entity_type, entity_id, field_key)
            );
            CREATE INDEX IF NOT EXISTS idx_dyn_vals_entity ON dynamic_field_values(entity_type, entity_id);
            CREATE INDEX IF NOT EXISTS idx_dyn_defs_entity ON dynamic_field_defs(entity_type);
        ";
        cmd.ExecuteNonQuery();
    }

    private static void AppendDateFilter(SqliteCommand cmd, string column, DateTime? fromDate, DateTime? toDate, bool includeWhere)
    {
        var hasFrom = fromDate.HasValue;
        var hasTo = toDate.HasValue;
        if (!hasFrom && !hasTo)
        {
            return;
        }

        cmd.CommandText += includeWhere ? " WHERE 1=1" : string.Empty;
        if (hasFrom)
        {
            cmd.CommandText += $" AND {column} >= $fromDate";
            cmd.Parameters.AddWithValue("$fromDate", fromDate!.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        if (hasTo)
        {
            cmd.CommandText += $" AND {column} <= $toDate";
            cmd.Parameters.AddWithValue("$toDate", toDate!.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }
    }

    private static void RecalculateProductsStock(SqliteConnection conn, SqliteTransaction tx, IEnumerable<int> productIds)
    {
        foreach (var productId in productIds)
        {
            using var updateStock = conn.CreateCommand();
            updateStock.Transaction = tx;
            updateStock.CommandText = @"UPDATE products
                                        SET stock_qty = (
                                            SELECT IFNULL(ROUND(SUM(sm.qty_change), 2), 0)
                                            FROM stock_movements sm
                                            WHERE sm.product_id = products.id
                                        ),
                                        updated_at = datetime('now')
                                        WHERE id = $id;";
            updateStock.Parameters.AddWithValue("$id", productId);
            updateStock.ExecuteNonQuery();
        }
    }

    private static void ExecuteDelete(SqliteConnection conn, SqliteTransaction tx, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection(_env.ConnectionString);
        conn.Open();
        using var pragma = conn.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = ON;";
        pragma.ExecuteNonQuery();
        return conn;
    }

    private void ExecuteSqlScript(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("SQL script not found.", path);
        }

        var sql = File.ReadAllText(path);
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}

public sealed class ProductCsvRow
{
    public string Sku { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Unit { get; set; } = "pcs";
    public decimal SellPrice { get; set; }
    public decimal CostPrice { get; set; }
    public decimal TaxRate { get; set; } = 7;
}

public sealed class CustomerCsvRow
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string TaxId { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
}
