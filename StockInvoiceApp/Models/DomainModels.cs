using System;

namespace StockInvoiceApp.Models;

public sealed class AppSettings
{
    public string AppMode { get; set; } = "demo";
    public decimal TaxRatePercent { get; set; } = 7;
    public ConnectionStrings ConnectionStrings { get; set; } = new();
    public SeedingSettings Seeding { get; set; } = new();
}

public sealed class ConnectionStrings
{
    public string DemoDb { get; set; } = "Data Source=./data/stock_demo.db";
    public string ProdDb { get; set; } = "Data Source=./data/stock_prod.db";
}

public sealed class SeedingSettings
{
    public bool EnableDemoSeed { get; set; } = true;
}

public sealed class CustomerLookup
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public override string ToString() => $"{Code} - {Name}";
}

public sealed class ProductLookup
{
    public int Id { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal SellPrice { get; set; }
    public override string ToString() => $"{Sku} - {Name}";
}

public sealed class InvoiceSummary
{
    public int Id { get; set; }
    public string InvoiceNo { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public DateTime InvoiceDate { get; set; }
    public decimal GrandTotal { get; set; }
    public string Status { get; set; } = "issued";
}

public sealed class InvoiceItemRow
{
    public int? Id { get; set; }
    public int ProductId { get; set; }
    public string ProductDisplay { get; set; } = string.Empty;
    public decimal Qty { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Discount { get; set; }
    public decimal LineTotal => Math.Max(0, (Qty * UnitPrice) - Discount);
}

public sealed class InvoiceDetail
{
    public int Id { get; set; }
    public string InvoiceNo { get; set; } = string.Empty;
    public int CustomerId { get; set; }
    public DateTime InvoiceDate { get; set; }
    public DateTime? DueDate { get; set; }
    public string Status { get; set; } = "issued";
    public string Notes { get; set; } = string.Empty;
    public decimal Subtotal { get; set; }
    public decimal TaxTotal { get; set; }
    public decimal GrandTotal { get; set; }
}

public sealed class DashboardMetrics
{
    public int TotalProducts { get; set; }
    public decimal TotalStockQty { get; set; }
    public int TotalCustomers { get; set; }
    public int TotalInvoices { get; set; }
    public int LowStockCount { get; set; }
    public decimal SalesDaily { get; set; }
    public decimal SalesMonthly { get; set; }
    public decimal SalesYearly { get; set; }
}

public sealed class StockLevelRow
{
    public int ProductId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal StockQty { get; set; }
    public decimal SellPrice { get; set; }
    public bool IsLowStock { get; set; }
}

public sealed class SalesSeriesPoint
{
    public string Label { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}
