using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using Microsoft.Win32;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using StockInvoiceApp.Models;
using StockInvoiceApp.Services;

namespace StockInvoiceApp
{
    public partial class MainWindow : Window
    {
        private readonly DatabaseService _db;
        private readonly CsvImportService _csvImport;
        private readonly PdfReportService _pdfReport;
        private readonly ObservableCollection<InvoiceItemRow> _invoiceItems = new();
        private List<ProductLookup> _products = new();
        private List<CustomerLookup> _customers = new();
        private List<StockLevelRow> _stockRows = new();
        private List<SalesSeriesPoint> _dailySeries = new();
        private List<SalesSeriesPoint> _monthlySeries = new();
        private int _editingInvoiceId;

        public MainWindow(DatabaseService db, CsvImportService csvImport, PdfReportService pdfReport)
        {
            _db = db;
            _csvImport = csvImport;
            _pdfReport = pdfReport;
            InitializeComponent();
            SetupView();
        }

        private void SetupView()
        {
            txtMode.Text = $"Mode: {_db.AppMode.ToUpperInvariant()}";

            cbStatus.ItemsSource = new[] { "draft", "issued", "paid", "cancelled" };
            cbStatus.SelectedIndex = 1;

            dgItems.ItemsSource = _invoiceItems;

            ReloadLookups();
            ReloadInvoices();
            ResetInvoiceForm();
            RefreshDashboard();
        }

        private void RefreshDashboard()
        {
            var fromDate = dpDashFrom.SelectedDate;
            var toDate = dpDashTo.SelectedDate;
            var lowStockThreshold = ParseLowStockThreshold();

            var metrics = _db.GetDashboardMetrics(fromDate, toDate, lowStockThreshold);
            txtTotalProducts.Text = metrics.TotalProducts.ToString(CultureInfo.InvariantCulture);
            txtTotalStockQty.Text = metrics.TotalStockQty.ToString("N2", CultureInfo.InvariantCulture);
            txtTotalCustomers.Text = metrics.TotalCustomers.ToString(CultureInfo.InvariantCulture);
            txtTotalInvoices.Text = metrics.TotalInvoices.ToString(CultureInfo.InvariantCulture);

            txtSalesDaily.Text = metrics.SalesDaily.ToString("N2", CultureInfo.InvariantCulture);
            txtSalesMonthly.Text = metrics.SalesMonthly.ToString("N2", CultureInfo.InvariantCulture);
            txtSalesYearly.Text = metrics.SalesYearly.ToString("N2", CultureInfo.InvariantCulture);
            txtTaxRateHint.Text = $"{_db.TaxRatePercent:0.##}% | Low Stock: {metrics.LowStockCount}";

            _stockRows = _db.GetStockLevels(lowStockThreshold);
            dgStockLevels.ItemsSource = _stockRows;

            _dailySeries = _db.GetDailySalesSeries(7, fromDate, toDate);
            _monthlySeries = _db.GetMonthlySalesSeries(12, fromDate, toDate);
            RenderDailySalesChart(_dailySeries);
            RenderMonthlySalesChart(_monthlySeries);
        }

        private decimal ParseLowStockThreshold()
        {
            if (decimal.TryParse(txtLowStockThreshold.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var threshold) && threshold >= 0)
            {
                return threshold;
            }

            txtLowStockThreshold.Text = "10";
            return 10m;
        }

        private void RenderDailySalesChart(IReadOnlyList<SalesSeriesPoint> points)
        {
            var model = new PlotModel { Title = "Daily Sales" };
            model.Axes.Add(new CategoryAxis
            {
                Position = AxisPosition.Bottom,
                ItemsSource = points.Select(p => p.Label).ToList()
            });
            model.Axes.Add(new LinearAxis { Position = AxisPosition.Left, Minimum = 0, StringFormat = "N0" });

            var series = new LineSeries { MarkerType = MarkerType.Circle };
            for (var i = 0; i < points.Count; i++)
            {
                series.Points.Add(new DataPoint(i, (double)points[i].Amount));
            }

            model.Series.Add(series);
            plotDailySales.Model = model;
        }

        private void RenderMonthlySalesChart(IReadOnlyList<SalesSeriesPoint> points)
        {
            var model = new PlotModel { Title = "Monthly Sales" };
            model.Axes.Add(new CategoryAxis
            {
                Position = AxisPosition.Bottom,
                ItemsSource = points.Select(p => p.Label).ToList()
            });
            model.Axes.Add(new LinearAxis { Position = AxisPosition.Left, Minimum = 0, StringFormat = "N0" });

            var series = new LineSeries { MarkerType = MarkerType.Circle };
            for (var i = 0; i < points.Count; i++)
            {
                series.Points.Add(new DataPoint(i, (double)points[i].Amount));
            }

            model.Series.Add(series);
            plotMonthlySales.Model = model;
        }

        private void ReloadLookups()
        {
            _products = _db.GetProducts();
            _customers = _db.GetCustomers();

            cbItemProduct.ItemsSource = _products;
            cbCustomer.ItemsSource = _customers;
        }

        private void ReloadInvoices()
        {
            dgInvoices.ItemsSource = _db.GetInvoiceSummaries();
        }

        private void ResetInvoiceForm()
        {
            _editingInvoiceId = 0;
            txtInvoiceNo.Text = GenerateInvoiceNo();
            cbCustomer.SelectedIndex = _customers.Count > 0 ? 0 : -1;
            dpInvoiceDate.SelectedDate = DateTime.Today;
            dpDueDate.SelectedDate = DateTime.Today.AddDays(7);
            cbStatus.SelectedItem = "issued";
            txtNotes.Text = string.Empty;
            _invoiceItems.Clear();
            RecalculateTotals();
        }

        private static string GenerateInvoiceNo()
        {
            var now = DateTime.Now;
            return $"INV{now:yyyyMMddHHmmss}";
        }

        private void RecalculateTotals()
        {
            var subtotal = _invoiceItems.Sum(x => x.LineTotal);
            var tax = Math.Round(subtotal * (_db.TaxRatePercent / 100m), 2);
            var grand = subtotal + tax;

            txtSubtotal.Text = subtotal.ToString("N2", CultureInfo.InvariantCulture);
            txtTax.Text = tax.ToString("N2", CultureInfo.InvariantCulture);
            txtGrand.Text = grand.ToString("N2", CultureInfo.InvariantCulture);
        }

        private InvoiceDetail BuildInvoiceFromForm()
        {
            if (string.IsNullOrWhiteSpace(txtInvoiceNo.Text))
            {
                throw new InvalidOperationException("Invoice No is required.");
            }

            if (cbCustomer.SelectedValue is not int customerId)
            {
                throw new InvalidOperationException("Customer is required.");
            }

            if (dpInvoiceDate.SelectedDate is null)
            {
                throw new InvalidOperationException("Invoice Date is required.");
            }

            var subtotal = _invoiceItems.Sum(x => x.LineTotal);
            var tax = Math.Round(subtotal * (_db.TaxRatePercent / 100m), 2);

            return new InvoiceDetail
            {
                Id = _editingInvoiceId,
                InvoiceNo = txtInvoiceNo.Text.Trim(),
                CustomerId = customerId,
                InvoiceDate = dpInvoiceDate.SelectedDate.Value,
                DueDate = dpDueDate.SelectedDate,
                Status = cbStatus.SelectedItem?.ToString() ?? "issued",
                Notes = txtNotes.Text.Trim(),
                Subtotal = subtotal,
                TaxTotal = tax,
                GrandTotal = subtotal + tax
            };
        }

        private void CbItemProduct_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (cbItemProduct.SelectedItem is ProductLookup p)
            {
                txtItemUnitPrice.Text = p.SellPrice.ToString("0.00", CultureInfo.InvariantCulture);
            }
        }

        private void AddLine_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (cbItemProduct.SelectedItem is not ProductLookup product)
                {
                    MessageBox.Show("Please select a product.");
                    return;
                }

                if (!decimal.TryParse(txtItemQty.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var qty) || qty <= 0)
                {
                    MessageBox.Show("Qty must be greater than 0.");
                    return;
                }

                if (!decimal.TryParse(txtItemUnitPrice.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var price) || price < 0)
                {
                    MessageBox.Show("Unit price cannot be negative.");
                    return;
                }

                if (!decimal.TryParse(txtItemDiscount.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var discount) || discount < 0)
                {
                    MessageBox.Show("Discount cannot be negative.");
                    return;
                }

                var item = new InvoiceItemRow
                {
                    ProductId = product.Id,
                    ProductDisplay = $"{product.Sku} - {product.Name}",
                    Qty = qty,
                    UnitPrice = price,
                    Discount = discount
                };
                _invoiceItems.Add(item);
                RecalculateTotals();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Add line failed: {ex.Message}");
            }
        }

        private void RemoveLine_Click(object sender, RoutedEventArgs e)
        {
            if (dgItems.SelectedItem is InvoiceItemRow selected)
            {
                _invoiceItems.Remove(selected);
                RecalculateTotals();
            }
        }

        private void SaveInvoice_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_invoiceItems.Count == 0)
                {
                    MessageBox.Show("Add at least one item before saving.");
                    return;
                }

                var invoice = BuildInvoiceFromForm();
                var savedId = _db.SaveInvoice(invoice, _invoiceItems);

                ReloadInvoices();
                RefreshDashboard();
                _editingInvoiceId = savedId;
                MessageBox.Show("Invoice saved.");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Save failed: {ex.Message}");
            }
        }

        private void NewInvoice_Click(object sender, RoutedEventArgs e)
        {
            ResetInvoiceForm();
        }

        private void DeleteInvoice_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (dgInvoices.SelectedItem is not InvoiceSummary summary)
                {
                    MessageBox.Show("Please select an invoice to delete.");
                    return;
                }

                var confirm = MessageBox.Show($"Delete invoice {summary.InvoiceNo}?", "Confirm", MessageBoxButton.YesNo);
                if (confirm != MessageBoxResult.Yes)
                {
                    return;
                }

                _db.DeleteInvoice(summary.Id);
                ReloadInvoices();
                ResetInvoiceForm();
                RefreshDashboard();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Delete failed: {ex.Message}");
            }
        }

        private void ReloadInvoices_Click(object sender, RoutedEventArgs e)
        {
            ReloadInvoices();
        }

        private void DgInvoices_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            try
            {
                if (dgInvoices.SelectedItem is not InvoiceSummary summary)
                {
                    return;
                }

                var (invoice, items) = _db.GetInvoiceWithItems(summary.Id);
                _editingInvoiceId = invoice.Id;

                txtInvoiceNo.Text = invoice.InvoiceNo;
                cbCustomer.SelectedValue = invoice.CustomerId;
                dpInvoiceDate.SelectedDate = invoice.InvoiceDate;
                dpDueDate.SelectedDate = invoice.DueDate;
                cbStatus.SelectedItem = invoice.Status;
                txtNotes.Text = invoice.Notes;

                _invoiceItems.Clear();
                foreach (var item in items)
                {
                    _invoiceItems.Add(item);
                }

                RecalculateTotals();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Load invoice failed: {ex.Message}");
            }
        }

        private void ImportProducts_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var file = PickCsvFile();
                if (string.IsNullOrWhiteSpace(file))
                {
                    return;
                }

                var rows = _csvImport.ReadProducts(file);
                var count = _db.ImportProducts(rows);
                ReloadLookups();
                RefreshDashboard();
                txtImportLog.Text += $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Imported products: {count} from {file}{Environment.NewLine}";
            }
            catch (Exception ex)
            {
                txtImportLog.Text += $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Product import failed: {ex.Message}{Environment.NewLine}";
            }
        }

        private void ImportCustomers_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var file = PickCsvFile();
                if (string.IsNullOrWhiteSpace(file))
                {
                    return;
                }

                var rows = _csvImport.ReadCustomers(file);
                var count = _db.ImportCustomers(rows);
                ReloadLookups();
                RefreshDashboard();
                txtImportLog.Text += $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Imported customers: {count} from {file}{Environment.NewLine}";
            }
            catch (Exception ex)
            {
                txtImportLog.Text += $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Customer import failed: {ex.Message}{Environment.NewLine}";
            }
        }

        private static string PickCsvFile()
        {
            var dialog = new OpenFileDialog
            {
                Filter = "CSV files (*.csv)|*.csv",
                CheckFileExists = true,
                Multiselect = false
            };

            return dialog.ShowDialog() == true ? dialog.FileName : string.Empty;
        }

        private void RefreshDashboard_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                RefreshDashboard();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Dashboard refresh failed: {ex.Message}");
            }
        }

        private void ClearDashboardFilter_Click(object sender, RoutedEventArgs e)
        {
            dpDashFrom.SelectedDate = null;
            dpDashTo.SelectedDate = null;
            txtLowStockThreshold.Text = "10";
            RefreshDashboard();
        }

        private void ExportDashboardPdf_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_stockRows.Count == 0)
                {
                    RefreshDashboard();
                }

                var dialog = new SaveFileDialog
                {
                    Filter = "PDF files (*.pdf)|*.pdf",
                    FileName = $"dashboard-{DateTime.Now:yyyyMMdd-HHmmss}.pdf"
                };

                if (dialog.ShowDialog() != true)
                {
                    return;
                }

                var metrics = _db.GetDashboardMetrics(dpDashFrom.SelectedDate, dpDashTo.SelectedDate, ParseLowStockThreshold());
                _pdfReport.ExportDashboardReport(
                    dialog.FileName,
                    metrics,
                    _stockRows,
                    _dailySeries,
                    _monthlySeries,
                    dpDashFrom.SelectedDate,
                    dpDashTo.SelectedDate,
                    ParseLowStockThreshold());

                MessageBox.Show("PDF exported successfully.");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Export PDF failed: {ex.Message}");
            }
        }
    }
}
