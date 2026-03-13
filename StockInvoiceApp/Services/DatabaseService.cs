using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
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

    public int SaveInvoice(InvoiceDetail invoice, IReadOnlyCollection<InvoiceItemRow> items)
    {
        if (items.Count == 0)
        {
            throw new InvalidOperationException("Invoice must contain at least one item.");
        }

        using var conn = Open();
        using var tx = conn.BeginTransaction();

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

        var lineNo = 1;
        foreach (var item in items)
        {
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

        using (var updateStock = conn.CreateCommand())
        {
            updateStock.Transaction = tx;
            updateStock.CommandText = @"UPDATE products
                                        SET stock_qty = (
                                            SELECT IFNULL(ROUND(SUM(sm.qty_change), 2), 0)
                                            FROM stock_movements sm
                                            WHERE sm.product_id = products.id
                                        ),
                                        updated_at = datetime('now')
                                        WHERE id IN (SELECT DISTINCT product_id FROM invoice_items WHERE invoice_id = $id);";
            updateStock.Parameters.AddWithValue("$id", invoice.Id);
            updateStock.ExecuteNonQuery();
        }

        tx.Commit();
        return invoice.Id;
    }

    public void DeleteInvoice(int invoiceId)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();

        using (var productIdsCmd = conn.CreateCommand())
        {
            productIdsCmd.Transaction = tx;
            productIdsCmd.CommandText = "CREATE TEMP TABLE IF NOT EXISTS tmp_product_ids(id INTEGER PRIMARY KEY);";
            productIdsCmd.ExecuteNonQuery();
        }

        using (var insertTemp = conn.CreateCommand())
        {
            insertTemp.Transaction = tx;
            insertTemp.CommandText = "INSERT OR IGNORE INTO tmp_product_ids(id) SELECT DISTINCT product_id FROM invoice_items WHERE invoice_id = $id;";
            insertTemp.Parameters.AddWithValue("$id", invoiceId);
            insertTemp.ExecuteNonQuery();
        }

        using (var deleteMovements = conn.CreateCommand())
        {
            deleteMovements.Transaction = tx;
            deleteMovements.CommandText = "DELETE FROM stock_movements WHERE ref_type = 'sale' AND ref_id = $id;";
            deleteMovements.Parameters.AddWithValue("$id", invoiceId);
            deleteMovements.ExecuteNonQuery();
        }

        using (var deleteInvoice = conn.CreateCommand())
        {
            deleteInvoice.Transaction = tx;
            deleteInvoice.CommandText = "DELETE FROM invoices WHERE id = $id;";
            deleteInvoice.Parameters.AddWithValue("$id", invoiceId);
            deleteInvoice.ExecuteNonQuery();
        }

        using (var updateStock = conn.CreateCommand())
        {
            updateStock.Transaction = tx;
            updateStock.CommandText = @"UPDATE products
                                        SET stock_qty = (
                                            SELECT IFNULL(ROUND(SUM(sm.qty_change), 2), 0)
                                            FROM stock_movements sm
                                            WHERE sm.product_id = products.id
                                        ),
                                        updated_at = datetime('now')
                                        WHERE id IN (SELECT id FROM tmp_product_ids);";
            updateStock.ExecuteNonQuery();
        }

        using (var dropTemp = conn.CreateCommand())
        {
            dropTemp.Transaction = tx;
            dropTemp.CommandText = "DROP TABLE IF EXISTS tmp_product_ids;";
            dropTemp.ExecuteNonQuery();
        }

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

        using (var cmd = conn.CreateCommand())
        {
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

        using (var cmd = conn.CreateCommand())
        {
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
        }

        return points;
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
