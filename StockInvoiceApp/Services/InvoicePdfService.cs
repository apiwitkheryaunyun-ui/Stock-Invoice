using System;
using System.Globalization;
using System.IO;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using StockInvoiceApp.Models;

namespace StockInvoiceApp.Services;

public sealed class InvoicePdfService
{
    private readonly AppEnvironment _env;
    private readonly DatabaseService _db;

    public InvoicePdfService(AppEnvironment env, DatabaseService db)
    {
        _env = env;
        _db = db;
    }

    public void ExportInvoice(int invoiceId, string outputPath)
    {
        var invoice = _db.GetInvoicePrintData(invoiceId);
        var company = _env.Settings.Company;

        using var document = new PdfDocument();
        document.Info.Title = $"Invoice {invoice.InvoiceNo}";

        var page = document.AddPage();
        page.Size = PdfSharpCore.PageSize.A4;
        var gfx = XGraphics.FromPdfPage(page);

        var titleFont = new XFont("Arial", 18, XFontStyle.Bold);
        var hFont = new XFont("Arial", 12, XFontStyle.Bold);
        var normalFont = new XFont("Arial", 10, XFontStyle.Regular);
        var boldFont = new XFont("Arial", 10, XFontStyle.Bold);

        var y = 30d;
        DrawCompanyHeader(gfx, company, titleFont, normalFont, ref y);

        y += 8;
        gfx.DrawLine(XPens.Gray, 25, y, page.Width - 25, y);
        y += 16;

        gfx.DrawString("INVOICE", titleFont, XBrushes.Black, new XPoint(25, y));
        y += 22;

        gfx.DrawString($"Invoice No: {invoice.InvoiceNo}", boldFont, XBrushes.Black, new XPoint(25, y));
        gfx.DrawString($"Date: {invoice.InvoiceDate:yyyy-MM-dd}", normalFont, XBrushes.Black, new XPoint(320, y));
        y += 14;
        gfx.DrawString($"Status: {invoice.Status.ToUpperInvariant()}", normalFont, XBrushes.Black, new XPoint(25, y));
        gfx.DrawString($"Due Date: {(invoice.DueDate.HasValue ? invoice.DueDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "-")}", normalFont, XBrushes.Black, new XPoint(320, y));
        y += 18;

        gfx.DrawString("Bill To", hFont, XBrushes.Black, new XPoint(25, y));
        y += 16;
        gfx.DrawString($"{invoice.CustomerCode} - {invoice.CustomerName}", boldFont, XBrushes.Black, new XPoint(25, y));
        y += 13;
        gfx.DrawString($"Tax ID: {(string.IsNullOrWhiteSpace(invoice.CustomerTaxId) ? "-" : invoice.CustomerTaxId)}", normalFont, XBrushes.Black, new XPoint(25, y));
        y += 13;
        gfx.DrawString($"Phone: {(string.IsNullOrWhiteSpace(invoice.CustomerPhone) ? "-" : invoice.CustomerPhone)}", normalFont, XBrushes.Black, new XPoint(25, y));
        y += 13;
        gfx.DrawString($"Email: {(string.IsNullOrWhiteSpace(invoice.CustomerEmail) ? "-" : invoice.CustomerEmail)}", normalFont, XBrushes.Black, new XPoint(25, y));
        y += 13;
        gfx.DrawString($"Address: {(string.IsNullOrWhiteSpace(invoice.CustomerAddress) ? "-" : invoice.CustomerAddress)}", normalFont, XBrushes.Black, new XPoint(25, y));
        y += 20;

        DrawItemsTableHeader(gfx, boldFont, ref y);

        foreach (var item in invoice.Items)
        {
            if (y > page.Height - 120)
            {
                page = document.AddPage();
                page.Size = PdfSharpCore.PageSize.A4;
                gfx = XGraphics.FromPdfPage(page);
                y = 30;
                DrawItemsTableHeader(gfx, boldFont, ref y);
            }

            gfx.DrawString(item.LineNo.ToString(CultureInfo.InvariantCulture), normalFont, XBrushes.Black, new XPoint(28, y));
            gfx.DrawString(item.ProductSku, normalFont, XBrushes.Black, new XPoint(55, y));
            gfx.DrawString(item.ProductName, normalFont, XBrushes.Black, new XPoint(130, y));
            gfx.DrawString(item.Qty.ToString("N2", CultureInfo.InvariantCulture), normalFont, XBrushes.Black, new XPoint(360, y), XStringFormats.TopRight);
            gfx.DrawString(item.UnitPrice.ToString("N2", CultureInfo.InvariantCulture), normalFont, XBrushes.Black, new XPoint(440, y), XStringFormats.TopRight);
            gfx.DrawString(item.Discount.ToString("N2", CultureInfo.InvariantCulture), normalFont, XBrushes.Black, new XPoint(510, y), XStringFormats.TopRight);
            gfx.DrawString(item.LineTotal.ToString("N2", CultureInfo.InvariantCulture), normalFont, XBrushes.Black, new XPoint(585, y), XStringFormats.TopRight);
            y += 14;
        }

        y += 8;
        gfx.DrawLine(XPens.Gray, 25, y, 585, y);
        y += 14;

        gfx.DrawString("Subtotal:", boldFont, XBrushes.Black, new XPoint(470, y), XStringFormats.TopRight);
        gfx.DrawString(invoice.Subtotal.ToString("N2", CultureInfo.InvariantCulture), boldFont, XBrushes.Black, new XPoint(585, y), XStringFormats.TopRight);
        y += 14;

        gfx.DrawString("Tax:", boldFont, XBrushes.Black, new XPoint(470, y), XStringFormats.TopRight);
        gfx.DrawString(invoice.TaxTotal.ToString("N2", CultureInfo.InvariantCulture), boldFont, XBrushes.Black, new XPoint(585, y), XStringFormats.TopRight);
        y += 14;

        gfx.DrawString("Grand Total:", new XFont("Arial", 12, XFontStyle.Bold), XBrushes.Black, new XPoint(470, y), XStringFormats.TopRight);
        gfx.DrawString(invoice.GrandTotal.ToString("N2", CultureInfo.InvariantCulture), new XFont("Arial", 12, XFontStyle.Bold), XBrushes.Black, new XPoint(585, y), XStringFormats.TopRight);
        y += 20;

        gfx.DrawString("หมายเหตุ:", boldFont, XBrushes.Black, new XPoint(25, y));
        y += 12;
        gfx.DrawString(string.IsNullOrWhiteSpace(invoice.Notes) ? "-" : invoice.Notes, normalFont, XBrushes.Black, new XRect(25, y, 560, 50), XStringFormats.TopLeft);

        document.Save(outputPath);
    }

    private void DrawCompanyHeader(XGraphics gfx, CompanyProfile company, XFont titleFont, XFont normalFont, ref double y)
    {
        var logoPath = ResolveLogoPath(company.LogoPath);
        if (!string.IsNullOrWhiteSpace(logoPath) && File.Exists(logoPath))
        {
            using var img = XImage.FromFile(logoPath);
            gfx.DrawImage(img, 25, y, 90, 55);
        }

        gfx.DrawString(company.Name, titleFont, XBrushes.Black, new XPoint(130, y + 18));
        y += 20;
        gfx.DrawString($"Tax ID: {company.TaxId}", normalFont, XBrushes.Black, new XPoint(130, y + 14));
        y += 14;
        gfx.DrawString(company.AddressLine1, normalFont, XBrushes.Black, new XPoint(130, y + 14));
        y += 14;
        gfx.DrawString(company.AddressLine2, normalFont, XBrushes.Black, new XPoint(130, y + 14));
        y += 14;
        gfx.DrawString($"Phone: {company.Phone} | Email: {company.Email}", normalFont, XBrushes.Black, new XPoint(130, y + 14));
        y += 16;
    }

    private static void DrawItemsTableHeader(XGraphics gfx, XFont font, ref double y)
    {
        gfx.DrawLine(XPens.Gray, 25, y, 585, y);
        y += 8;
        gfx.DrawString("#", font, XBrushes.Black, new XPoint(28, y));
        gfx.DrawString("SKU", font, XBrushes.Black, new XPoint(55, y));
        gfx.DrawString("Product", font, XBrushes.Black, new XPoint(130, y));
        gfx.DrawString("Qty", font, XBrushes.Black, new XPoint(360, y), XStringFormats.TopRight);
        gfx.DrawString("Unit", font, XBrushes.Black, new XPoint(440, y), XStringFormats.TopRight);
        gfx.DrawString("Disc", font, XBrushes.Black, new XPoint(510, y), XStringFormats.TopRight);
        gfx.DrawString("Total", font, XBrushes.Black, new XPoint(585, y), XStringFormats.TopRight);
        y += 10;
        gfx.DrawLine(XPens.Gray, 25, y, 585, y);
        y += 12;
    }

    private string ResolveLogoPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        if (Path.IsPathRooted(path))
        {
            return path;
        }

        var workspaceRelative = Path.Combine(_env.WorkspaceRoot, path);
        if (File.Exists(workspaceRelative))
        {
            return workspaceRelative;
        }

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, path));
    }
}
