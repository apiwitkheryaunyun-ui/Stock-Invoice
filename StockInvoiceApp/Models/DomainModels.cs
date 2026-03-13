using System;
using System.Collections.Generic;

namespace StockInvoiceApp.Models;

public sealed class AppSettings
{
    public string AppMode { get; set; } = "demo";
    public decimal TaxRatePercent { get; set; } = 7;
    public ConnectionStrings ConnectionStrings { get; set; } = new();
    public SeedingSettings Seeding { get; set; } = new();
    public CompanyProfile Company { get; set; } = new();
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

public sealed class CompanyProfile
{
    public string Name { get; set; } = "My Company Co., Ltd.";
    public string TaxId { get; set; } = "0100000000000";
    public string AddressLine1 { get; set; } = "Company Address Line 1";
    public string AddressLine2 { get; set; } = "Company Address Line 2";
    public string Phone { get; set; } = "02-000-0000";
    public string Email { get; set; } = "hello@company.local";
    public string LogoPath { get; set; } = "";
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

public sealed class ProductManageRow
{
    public int Id { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Unit { get; set; } = "pcs";
    public decimal SellPrice { get; set; }
    public decimal CostPrice { get; set; }
    public decimal TaxRate { get; set; } = 7;
    public decimal StockQty { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class CustomerManageRow
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string TaxId { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

public sealed class InvoicePrintData
{
    public int InvoiceId { get; set; }
    public string InvoiceNo { get; set; } = string.Empty;
    public DateTime InvoiceDate { get; set; }
    public DateTime? DueDate { get; set; }
    public string Status { get; set; } = "issued";
    public string Notes { get; set; } = string.Empty;
    public decimal Subtotal { get; set; }
    public decimal TaxTotal { get; set; }
    public decimal GrandTotal { get; set; }
    public int CustomerId { get; set; }
    public string CustomerCode { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerTaxId { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    public string CustomerAddress { get; set; } = string.Empty;
    public Dictionary<string, string> CustomerCustomFields { get; set; } = new();
    public Dictionary<string, string> InvoiceCustomFields { get; set; } = new();
    public List<InvoicePrintItem> Items { get; set; } = new();
}

public sealed class InvoicePrintItem
{
    public int LineNo { get; set; }
    public string ProductSku { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public decimal Qty { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Discount { get; set; }
    public decimal LineTotal { get; set; }
}

public sealed class DynamicFieldDefinition
{
    public int Id { get; set; }
    public string EntityType { get; set; } = "customer";
    public string FieldKey { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string DataType { get; set; } = "text";
    public bool IsRequired { get; set; }
    public bool IsVisible { get; set; } = true;
    public bool ShowInPdf { get; set; }
    public int SortOrder { get; set; }
}

public sealed class BackupPayload
{
    public List<ProductManageRow> Products { get; set; } = new();
    public List<CustomerManageRow> Customers { get; set; } = new();
    public List<InvoiceDetail> Invoices { get; set; } = new();
    public List<InvoiceItemRowBackup> InvoiceItems { get; set; } = new();
    public List<StockMovementBackup> StockMovements { get; set; } = new();
    public List<DynamicFieldDefinition> DynamicFields { get; set; } = new();
    public List<EntityFieldValue> DynamicValues { get; set; } = new();
}

public sealed class InvoiceItemRowBackup
{
    public int InvoiceId { get; set; }
    public int LineNo { get; set; }
    public int ProductId { get; set; }
    public decimal Qty { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Discount { get; set; }
    public decimal LineTotal { get; set; }
}

public sealed class EntityFieldValue
{
    public int Id { get; set; }
    public string EntityType { get; set; } = "customer";
    public int EntityId { get; set; }
    public string FieldKey { get; set; } = string.Empty;
    public string FieldValue { get; set; } = string.Empty;
}

public sealed class StockMovementBackup
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public string MovementType { get; set; } = "in";
    public string RefType { get; set; } = "adjustment";
    public int? RefId { get; set; }
    public DateTime MovementDate { get; set; }
    public decimal QtyChange { get; set; }
    public decimal UnitCost { get; set; }
    public string Note { get; set; } = string.Empty;
}
