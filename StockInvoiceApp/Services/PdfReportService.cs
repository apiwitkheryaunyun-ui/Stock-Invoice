using System;
using System.Collections.Generic;
using System.Globalization;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using StockInvoiceApp.Models;

namespace StockInvoiceApp.Services;

public sealed class PdfReportService
{
    public void ExportDashboardReport(
        string outputPath,
        DashboardMetrics metrics,
        IReadOnlyCollection<StockLevelRow> stockRows,
        IReadOnlyCollection<SalesSeriesPoint> dailyPoints,
        IReadOnlyCollection<SalesSeriesPoint> monthlyPoints,
        DateTime? fromDate,
        DateTime? toDate,
        decimal lowStockThreshold)
    {
        var document = new PdfDocument();
        document.Info.Title = "StockInvoice Dashboard Report";

        var page = document.AddPage();
        var gfx = XGraphics.FromPdfPage(page);
        var titleFont = new XFont("Arial", 16, XFontStyle.Bold);
        var normalFont = new XFont("Arial", 10, XFontStyle.Regular);
        var boldFont = new XFont("Arial", 10, XFontStyle.Bold);

        var y = 30d;
        gfx.DrawString("StockInvoice Dashboard Report", titleFont, XBrushes.Black, new XPoint(30, y));
        y += 24;

        var rangeText = $"Date Filter: {(fromDate.HasValue ? fromDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "-" )} to {(toDate.HasValue ? toDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "-")}";
        gfx.DrawString(rangeText, normalFont, XBrushes.Black, new XPoint(30, y));
        y += 16;
        gfx.DrawString($"Low Stock Threshold: {lowStockThreshold:0.##}", normalFont, XBrushes.Black, new XPoint(30, y));
        y += 22;

        gfx.DrawString("Summary", boldFont, XBrushes.Black, new XPoint(30, y));
        y += 16;
        gfx.DrawString($"Products: {metrics.TotalProducts}", normalFont, XBrushes.Black, new XPoint(30, y));
        y += 14;
        gfx.DrawString($"Total Stock Qty: {metrics.TotalStockQty:N2}", normalFont, XBrushes.Black, new XPoint(30, y));
        y += 14;
        gfx.DrawString($"Customers: {metrics.TotalCustomers}", normalFont, XBrushes.Black, new XPoint(30, y));
        y += 14;
        gfx.DrawString($"Invoices (filtered): {metrics.TotalInvoices}", normalFont, XBrushes.Black, new XPoint(30, y));
        y += 14;
        gfx.DrawString($"Low Stock Items: {metrics.LowStockCount}", normalFont, XBrushes.Black, new XPoint(30, y));
        y += 14;
        gfx.DrawString($"Sales Daily: {metrics.SalesDaily:N2} | Monthly: {metrics.SalesMonthly:N2} | Yearly: {metrics.SalesYearly:N2}", normalFont, XBrushes.Black, new XPoint(30, y));
        y += 22;

        gfx.DrawString("Latest 7-Day Sales", boldFont, XBrushes.Black, new XPoint(30, y));
        y += 16;
        foreach (var point in dailyPoints)
        {
            EnsurePage(ref document, ref page, ref gfx, ref y, normalFont);
            gfx.DrawString($"{point.Label}: {point.Amount:N2}", normalFont, XBrushes.Black, new XPoint(40, y));
            y += 13;
        }

        y += 10;
        gfx.DrawString("Latest 12-Month Sales", boldFont, XBrushes.Black, new XPoint(30, y));
        y += 16;
        foreach (var point in monthlyPoints)
        {
            EnsurePage(ref document, ref page, ref gfx, ref y, normalFont);
            gfx.DrawString($"{point.Label}: {point.Amount:N2}", normalFont, XBrushes.Black, new XPoint(40, y));
            y += 13;
        }

        y += 10;
        gfx.DrawString("Low Stock Items", boldFont, XBrushes.Black, new XPoint(30, y));
        y += 16;
        foreach (var row in stockRows)
        {
            if (!row.IsLowStock)
            {
                continue;
            }

            EnsurePage(ref document, ref page, ref gfx, ref y, normalFont);
            gfx.DrawString($"{row.Sku} | {row.Name} | Qty: {row.StockQty:N2} | Price: {row.SellPrice:N2}", normalFont, XBrushes.Black, new XPoint(40, y));
            y += 13;
        }

        document.Save(outputPath);
    }

    private static void EnsurePage(ref PdfDocument doc, ref PdfPage page, ref XGraphics gfx, ref double y, XFont font)
    {
        if (y < page.Height - 30)
        {
            return;
        }

        page = doc.AddPage();
        gfx = XGraphics.FromPdfPage(page);
        y = 30;
        gfx.DrawString("StockInvoice Dashboard Report (cont.)", font, XBrushes.Black, new XPoint(30, y));
        y += 18;
    }
}
