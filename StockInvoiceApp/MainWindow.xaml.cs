

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
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
        private readonly InvoicePdfService _invoicePdf;

        private readonly ObservableCollection<InvoiceItemRow> _invoiceItems = new();
        private readonly Dictionary<string, FrameworkElement> _productDynamicInputs = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, FrameworkElement> _customerDynamicInputs = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, FrameworkElement> _invoiceDynamicInputs = new(StringComparer.OrdinalIgnoreCase);

        private List<ProductLookup> _products = new();
        private List<CustomerLookup> _customers = new();


        private List<StockLevelRow> _stockRows = new();
        private List<SalesSeriesPoint> _dailySeries = new();
        private List<SalesSeriesPoint> _monthlySeries = new();

        private int _editingInvoiceId;
        private int _editingProductId;
        private int _editingCustomerId;
        private int _editingDynamicFieldId;
        private bool _invoiceLocked;


        public MainWindow(DatabaseService db, CsvImportService csvImport, PdfReportService pdfReport, InvoicePdfService invoicePdf)
        {
            _db = db;
            _csvImport = csvImport;
            _pdfReport = pdfReport;
            _invoicePdf = invoicePdf;
            InitializeComponent();
            SetupView();
        }

        // --- Language Switcher ---
        private void BtnLangTh_Click(object sender, RoutedEventArgs e)
        {
            SwitchLanguage("th");
        }

        private void BtnLangEn_Click(object sender, RoutedEventArgs e)
        {
            SwitchLanguage("en");
        }

        private void SwitchLanguage(string lang)
        {
            var dict = new ResourceDictionary();
            switch (lang)
            {
                case "en":
                    dict.Source = new Uri("Resources/Strings.en.xaml", UriKind.Relative);
                    break;
                default:
                    dict.Source = new Uri("Resources/Strings.th.xaml", UriKind.Relative);
                    break;
            }
            var appResources = Application.Current.Resources.MergedDictionaries;
            if (appResources.Count > 0)
                appResources[0] = dict;
            else
                appResources.Add(dict);
        }

        private void SetupView()
        {
            // txtMode ถูกลบออกจาก XAML แล้ว ไม่ต้องเซ็ตค่า
            cbStatus.ItemsSource = new[] { "draft", "issued", "paid", "cancelled" };
            cbStatus.SelectedItem = "issued";
            cbDynDataType.ItemsSource = new[] { "text", "number", "date", "boolean", "image" };
            cbDynDataType.SelectedItem = "text";
            cbDynamicEntity.ItemsSource = new[] { "customer", "product", "invoice" };
            cbDynamicEntity.SelectedIndex = 0;
            dgItems.ItemsSource = _invoiceItems;

            ReloadLookups();
            ReloadMasterData();
            ReloadInvoices();
            ResetInvoiceForm();
            RefreshDashboard();
            NewProductForm();
            NewCustomerForm();
            ReloadDynamicFields();
            ReloadAllDynamicInputPanels();
        }

        private void ReloadLookups()
        {
            _products = _db.GetProducts();
            _customers = _db.GetCustomers();

            cbItemProduct.ItemsSource = _products;
            cbCustomer.ItemsSource = _customers;
            cbAdjustProduct.ItemsSource = _products;

            if (_products.Count > 0 && cbAdjustProduct.SelectedIndex < 0)
            {
                cbAdjustProduct.SelectedIndex = 0;
            }
        }

        private void ReloadMasterData()
        {
            dgManageProducts.ItemsSource = _db.GetManageProducts();
            dgManageCustomers.ItemsSource = _db.GetManageCustomers();
        }

        private void ReloadInvoices()
        {
            dgInvoices.ItemsSource = _db.GetInvoiceSummaries();
        }

        private void ResetInvoiceForm()
        {
            _editingInvoiceId = 0;
            txtInvoiceNo.Text = $"INV{DateTime.Now:yyyyMMddHHmmss}";
            cbCustomer.SelectedIndex = _customers.Count > 0 ? 0 : -1;
            dpInvoiceDate.SelectedDate = DateTime.Today;
            dpDueDate.SelectedDate = DateTime.Today.AddDays(7);
            cbStatus.SelectedItem = "issued";
            txtNotes.Text = string.Empty;
            _invoiceItems.Clear();
            RecalculateTotals();
            RenderDynamicInputs("invoice", spInvoiceDynamicFields, _invoiceDynamicInputs, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
            ApplyInvoiceLock(false, string.Empty);
        }

        private void ApplyInvoiceLock(bool isLocked, string status)
        {
            _invoiceLocked = isLocked;
            txtInvoiceNo.IsEnabled = !isLocked;
            cbCustomer.IsEnabled = !isLocked;
            dpInvoiceDate.IsEnabled = !isLocked;
            dpDueDate.IsEnabled = !isLocked;
            cbStatus.IsEnabled = !isLocked;
            txtNotes.IsEnabled = !isLocked;
            cbItemProduct.IsEnabled = !isLocked;
            txtItemQty.IsEnabled = !isLocked;
            txtItemUnitPrice.IsEnabled = !isLocked;
            txtItemDiscount.IsEnabled = !isLocked;
            btnAddLine.IsEnabled = !isLocked;
            btnRemoveLine.IsEnabled = !isLocked;
            btnSaveInvoice.IsEnabled = !isLocked;

            foreach (var input in _invoiceDynamicInputs.Values)
            {
                input.IsEnabled = !isLocked;
            }

            btnDeleteInvoice.IsEnabled = !string.Equals(status, "paid", StringComparison.OrdinalIgnoreCase);
            txtInvoiceLockStatus.Text = isLocked ? "This invoice is PAID and locked. Editing/deleting is disabled." : string.Empty;
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

        private void CbItemProduct_SelectionChanged(object sender, SelectionChangedEventArgs e)
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
                if (_invoiceLocked)
                {
                    MessageBox.Show("Paid invoice cannot be edited.");
                    return;
                }

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

                _invoiceItems.Add(new InvoiceItemRow
                {
                    ProductId = product.Id,
                    ProductDisplay = $"{product.Sku} - {product.Name}",
                    Qty = qty,
                    UnitPrice = price,
                    Discount = discount
                });
                RecalculateTotals();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Add line failed: {ex.Message}");
            }
        }

        private void RemoveLine_Click(object sender, RoutedEventArgs e)
        {
            if (_invoiceLocked)
            {
                MessageBox.Show("Paid invoice cannot be edited.");
                return;
            }

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
                if (_invoiceLocked)
                {
                    MessageBox.Show("Paid invoice cannot be edited.");
                    return;
                }

                if (_invoiceItems.Count == 0)
                {
                    MessageBox.Show("Add at least one item before saving.");
                    return;
                }

                var invoice = BuildInvoiceFromForm();
                var savedId = _db.SaveInvoice(invoice, _invoiceItems);
                var dynamicValues = CollectDynamicValues(_invoiceDynamicInputs);
                _db.SaveEntityFieldValues("invoice", savedId, dynamicValues);

                ReloadInvoices();
                ReloadLookups();
                ReloadMasterData();
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
            dgInvoices.SelectedItem = null;
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
                ReloadLookups();
                ReloadMasterData();
                RefreshDashboard();
                ResetInvoiceForm();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Delete failed: {ex.Message}");
            }
        }

        private void ExportInvoicePdf_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (dgInvoices.SelectedItem is not InvoiceSummary summary)
                {
                    MessageBox.Show("Please select an invoice first.");
                    return;
                }

                var dialog = new SaveFileDialog
                {
                    Filter = "PDF files (*.pdf)|*.pdf",
                    FileName = $"invoice-{summary.InvoiceNo}.pdf"
                };

                if (dialog.ShowDialog() != true)
                {
                    return;
                }

                _invoicePdf.ExportInvoice(summary.Id, dialog.FileName);
                MessageBox.Show("Invoice PDF exported.");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Export invoice PDF failed: {ex.Message}");
            }
        }

        private void ReloadInvoices_Click(object sender, RoutedEventArgs e)
        {
            ReloadInvoices();
        }

        private void DgInvoices_SelectionChanged(object sender, SelectionChangedEventArgs e)
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

                var dynamicValues = _db.GetEntityFieldValues("invoice", invoice.Id);
                RenderDynamicInputs("invoice", spInvoiceDynamicFields, _invoiceDynamicInputs, dynamicValues);

                RecalculateTotals();
                ApplyInvoiceLock(string.Equals(invoice.Status, "paid", StringComparison.OrdinalIgnoreCase), invoice.Status);
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
                ReloadMasterData();
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
                ReloadMasterData();
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
            model.Axes.Add(new CategoryAxis { Position = AxisPosition.Bottom, ItemsSource = points.Select(p => p.Label).ToList() });
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
            model.Axes.Add(new CategoryAxis { Position = AxisPosition.Bottom, ItemsSource = points.Select(p => p.Label).ToList() });
            model.Axes.Add(new LinearAxis { Position = AxisPosition.Left, Minimum = 0, StringFormat = "N0" });

            var series = new LineSeries { MarkerType = MarkerType.Circle };
            for (var i = 0; i < points.Count; i++)
            {
                series.Points.Add(new DataPoint(i, (double)points[i].Amount));
            }

            model.Series.Add(series);
            plotMonthlySales.Model = model;
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

                MessageBox.Show("Dashboard PDF exported successfully.");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Export PDF failed: {ex.Message}");
            }
        }

        private void NewProductForm()
        {
            _editingProductId = 0;
            txtProdSku.Text = string.Empty;
            txtProdName.Text = string.Empty;
            txtProdUnit.Text = "pcs";
            txtProdSellPrice.Text = "0";
            txtProdCostPrice.Text = "0";
            txtProdTaxRate.Text = "7";
            chkProdActive.IsChecked = true;
            RenderDynamicInputs("product", spProductDynamicFields, _productDynamicInputs, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        }

        private void NewProduct_Click(object sender, RoutedEventArgs e)
        {
            NewProductForm();
        }

        private void SaveProduct_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var product = new ProductManageRow
                {
                    Id = _editingProductId,
                    Sku = txtProdSku.Text.Trim(),
                    Name = txtProdName.Text.Trim(),
                    Unit = txtProdUnit.Text.Trim(),
                    SellPrice = decimal.Parse(txtProdSellPrice.Text, CultureInfo.InvariantCulture),
                    CostPrice = decimal.Parse(txtProdCostPrice.Text, CultureInfo.InvariantCulture),
                    TaxRate = decimal.Parse(txtProdTaxRate.Text, CultureInfo.InvariantCulture),
                    IsActive = chkProdActive.IsChecked == true
                };

                var savedId = _db.SaveProduct(product);
                _db.SaveEntityFieldValues("product", savedId, CollectDynamicValues(_productDynamicInputs));

                ReloadLookups();
                ReloadMasterData();
                RefreshDashboard();
                NewProductForm();
                MessageBox.Show("Product saved.");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Save product failed: {ex.Message}");
            }
        }

        private void DeleteProduct_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (dgManageProducts.SelectedItem is not ProductManageRow row)
                {
                    MessageBox.Show("Please select a product to delete.");
                    return;
                }

                if (MessageBox.Show($"Delete product {row.Sku}?", "Confirm", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
                {
                    return;
                }

                _db.DeleteProduct(row.Id);
                ReloadLookups();
                ReloadMasterData();
                RefreshDashboard();
                NewProductForm();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Delete product failed: {ex.Message}");
            }
        }

        private void DgManageProducts_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (dgManageProducts.SelectedItem is not ProductManageRow row)
            {
                return;
            }

            _editingProductId = row.Id;
            txtProdSku.Text = row.Sku;
            txtProdName.Text = row.Name;
            txtProdUnit.Text = row.Unit;
            txtProdSellPrice.Text = row.SellPrice.ToString("0.##", CultureInfo.InvariantCulture);
            txtProdCostPrice.Text = row.CostPrice.ToString("0.##", CultureInfo.InvariantCulture);
            txtProdTaxRate.Text = row.TaxRate.ToString("0.##", CultureInfo.InvariantCulture);
            chkProdActive.IsChecked = row.IsActive;
            var values = _db.GetEntityFieldValues("product", row.Id);
            RenderDynamicInputs("product", spProductDynamicFields, _productDynamicInputs, values);
        }

        private void NewCustomerForm()
        {
            _editingCustomerId = 0;
            txtCustCode.Text = string.Empty;
            txtCustName.Text = string.Empty;
            txtCustTaxId.Text = string.Empty;
            txtCustPhone.Text = string.Empty;
            txtCustEmail.Text = string.Empty;
            txtCustAddress.Text = string.Empty;
            chkCustActive.IsChecked = true;
            RenderDynamicInputs("customer", spCustomerDynamicFields, _customerDynamicInputs, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        }

        private void NewCustomer_Click(object sender, RoutedEventArgs e)
        {
            NewCustomerForm();
        }

        private void SaveCustomer_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var customer = new CustomerManageRow
                {
                    Id = _editingCustomerId,
                    Code = txtCustCode.Text.Trim(),
                    Name = txtCustName.Text.Trim(),
                    TaxId = txtCustTaxId.Text.Trim(),
                    Phone = txtCustPhone.Text.Trim(),
                    Email = txtCustEmail.Text.Trim(),
                    Address = txtCustAddress.Text.Trim(),
                    IsActive = chkCustActive.IsChecked == true
                };

                var savedId = _db.SaveCustomer(customer);
                _db.SaveEntityFieldValues("customer", savedId, CollectDynamicValues(_customerDynamicInputs));

                ReloadLookups();
                ReloadMasterData();
                RefreshDashboard();
                NewCustomerForm();
                MessageBox.Show("Customer saved.");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Save customer failed: {ex.Message}");
            }
        }

        private void DeleteCustomer_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (dgManageCustomers.SelectedItem is not CustomerManageRow row)
                {
                    MessageBox.Show("Please select a customer to delete.");
                    return;
                }

                if (MessageBox.Show($"Delete customer {row.Code}?", "Confirm", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
                {
                    return;
                }

                _db.DeleteCustomer(row.Id);
                ReloadLookups();
                ReloadMasterData();
                RefreshDashboard();
                NewCustomerForm();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Delete customer failed: {ex.Message}");
            }
        }

        private void DgManageCustomers_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (dgManageCustomers.SelectedItem is not CustomerManageRow row)
            {
                return;
            }

            _editingCustomerId = row.Id;
            txtCustCode.Text = row.Code;
            txtCustName.Text = row.Name;
            txtCustTaxId.Text = row.TaxId;
            txtCustPhone.Text = row.Phone;
            txtCustEmail.Text = row.Email;
            txtCustAddress.Text = row.Address;
            chkCustActive.IsChecked = row.IsActive;
            var values = _db.GetEntityFieldValues("customer", row.Id);
            RenderDynamicInputs("customer", spCustomerDynamicFields, _customerDynamicInputs, values);
        }

        private void AdjustStock_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (cbAdjustProduct.SelectedValue is not int productId)
                {
                    MessageBox.Show("Please select product for stock adjustment.");
                    return;
                }

                if (!decimal.TryParse(txtAdjustQty.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var qtyChange))
                {
                    MessageBox.Show("Invalid adjustment qty.");
                    return;
                }

                if (!decimal.TryParse(txtAdjustUnitCost.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var unitCost))
                {
                    MessageBox.Show("Invalid unit cost.");
                    return;
                }

                _db.AdjustStock(productId, qtyChange, unitCost, txtAdjustNote.Text);
                ReloadLookups();
                ReloadMasterData();
                RefreshDashboard();
                txtAdjustQty.Text = "0";
                txtAdjustUnitCost.Text = "0";
                txtAdjustNote.Text = string.Empty;
                MessageBox.Show("Stock adjusted.");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Adjust stock failed: {ex.Message}");
            }
        }

        private void ReloadAllDynamicInputPanels()
        {
            RenderDynamicInputs("product", spProductDynamicFields, _productDynamicInputs, _editingProductId > 0
                ? _db.GetEntityFieldValues("product", _editingProductId)
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

            RenderDynamicInputs("customer", spCustomerDynamicFields, _customerDynamicInputs, _editingCustomerId > 0
                ? _db.GetEntityFieldValues("customer", _editingCustomerId)
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

            RenderDynamicInputs("invoice", spInvoiceDynamicFields, _invoiceDynamicInputs, _editingInvoiceId > 0
                ? _db.GetEntityFieldValues("invoice", _editingInvoiceId)
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        }

        private void RenderDynamicInputs(string entityType, Panel container, Dictionary<string, FrameworkElement> collector, IReadOnlyDictionary<string, string> values)
        {
            collector.Clear();
            container.Children.Clear();

            var fields = _db.GetDynamicFieldDefinitions(entityType).Where(x => x.IsVisible).OrderBy(x => x.SortOrder).ThenBy(x => x.Id).ToList();
            if (fields.Count == 0)
            {
                container.Children.Add(new TextBlock
                {
                    Text = "No dynamic fields configured.",
                    Opacity = 0.7,
                    Margin = new Thickness(0, 2, 0, 2)
                });
                return;
            }

            foreach (var field in fields)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
                var label = new TextBlock
                {
                    Text = field.IsRequired ? $"{field.Label} *" : field.Label,
                    Width = 180,
                    VerticalAlignment = System.Windows.VerticalAlignment.Center
                };
                row.Children.Add(label);

                var existing = values.TryGetValue(field.FieldKey, out var raw) ? raw : string.Empty;
                FrameworkElement input;
                switch (field.DataType)
                {
                    case "boolean":
                        var chk = new CheckBox { IsChecked = string.Equals(existing, "true", StringComparison.OrdinalIgnoreCase) || existing == "1" };
                        input = chk;
                        break;
                    case "date":
                        var dp = new DatePicker { Width = 180 };
                        if (DateTime.TryParse(existing, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                        {
                            dp.SelectedDate = dt;
                        }
                        input = dp;
                        break;
                    default:
                        input = new TextBox { Width = 260, Text = existing };
                        break;
                }

                collector[field.FieldKey] = input;
                row.Children.Add(input);
                container.Children.Add(row);
            }
        }

        private static Dictionary<string, string> CollectDynamicValues(Dictionary<string, FrameworkElement> inputs)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (key, element) in inputs)
            {
                switch (element)
                {
                    case TextBox textBox:
                        result[key] = textBox.Text?.Trim() ?? string.Empty;
                        break;
                    case CheckBox checkBox:
                        result[key] = checkBox.IsChecked == true ? "true" : "false";
                        break;
                    case DatePicker datePicker:
                        result[key] = datePicker.SelectedDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;
                        break;
                    default:
                        result[key] = string.Empty;
                        break;
                }
            }

            return result;
        }

        private void CbDynamicEntity_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ReloadDynamicFields();
        }

        private void ReloadDynamicFields_Click(object sender, RoutedEventArgs e)
        {
            ReloadDynamicFields();
        }

        private void ReloadDynamicFields()
        {
            if (cbDynamicEntity.SelectedItem is not string entity)
            {
                return;
            }

            dgDynamicFields.ItemsSource = _db.GetDynamicFieldDefinitions(entity);
            NewDynamicFieldForm();
        }

        private void NewDynamicField_Click(object sender, RoutedEventArgs e)
        {
            NewDynamicFieldForm();
        }

        private void NewDynamicFieldForm()
        {
            _editingDynamicFieldId = 0;
            txtDynKey.Text = string.Empty;
            txtDynLabel.Text = string.Empty;
            cbDynDataType.SelectedItem = "text";
            chkDynRequired.IsChecked = false;
            chkDynVisible.IsChecked = true;
            chkDynPdf.IsChecked = false;
            txtDynSort.Text = "0";
        }

        private void SaveDynamicField_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (cbDynamicEntity.SelectedItem is not string entity)
                {
                    MessageBox.Show("Please select entity type.");
                    return;
                }

                if (!int.TryParse(txtDynSort.Text, out var sortOrder))
                {
                    MessageBox.Show("Sort must be integer.");
                    return;
                }

                var field = new DynamicFieldDefinition
                {
                    Id = _editingDynamicFieldId,
                    EntityType = entity,
                    FieldKey = txtDynKey.Text.Trim(),
                    Label = txtDynLabel.Text.Trim(),
                    DataType = cbDynDataType.SelectedItem?.ToString() ?? "text",
                    IsRequired = chkDynRequired.IsChecked == true,
                    IsVisible = chkDynVisible.IsChecked == true,
                    ShowInPdf = chkDynPdf.IsChecked == true,
                    SortOrder = sortOrder
                };

                _db.SaveDynamicFieldDefinition(field);
                ReloadDynamicFields();
                ReloadAllDynamicInputPanels();
                MessageBox.Show("Dynamic field saved.");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Save dynamic field failed: {ex.Message}");
            }
        }

        private void DeleteDynamicField_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (dgDynamicFields.SelectedItem is not DynamicFieldDefinition row)
                {
                    MessageBox.Show("Please select field to delete.");
                    return;
                }

                if (MessageBox.Show($"Delete dynamic field {row.FieldKey}?", "Confirm", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
                {
                    return;
                }

                _db.DeleteDynamicFieldDefinition(row.Id);
                ReloadDynamicFields();
                ReloadAllDynamicInputPanels();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Delete dynamic field failed: {ex.Message}");
            }
        }

        private void DgDynamicFields_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (dgDynamicFields.SelectedItem is not DynamicFieldDefinition row)
            {
                return;
            }

            _editingDynamicFieldId = row.Id;
            txtDynKey.Text = row.FieldKey;
            txtDynLabel.Text = row.Label;
            cbDynDataType.SelectedItem = row.DataType;
            chkDynRequired.IsChecked = row.IsRequired;
            chkDynVisible.IsChecked = row.IsVisible;
            chkDynPdf.IsChecked = row.ShowInPdf;
            txtDynSort.Text = row.SortOrder.ToString(CultureInfo.InvariantCulture);
        }

        private void ExportBackup_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new SaveFileDialog
                {
                    Filter = "JSON files (*.json)|*.json",
                    FileName = $"backup-{DateTime.Now:yyyyMMdd-HHmmss}.json"
                };

                if (dialog.ShowDialog() != true)
                {
                    return;
                }

                _db.ExportBackup(dialog.FileName);
                MessageBox.Show("Backup exported.");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Export backup failed: {ex.Message}");
            }
        }

        private void ImportBackup_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new OpenFileDialog
                {
                    Filter = "JSON files (*.json)|*.json",
                    CheckFileExists = true,
                    Multiselect = false
                };

                if (dialog.ShowDialog() != true)
                {
                    return;
                }

                if (MessageBox.Show("Import backup will replace current data. Continue?", "Confirm", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
                {
                    return;
                }

                _db.ImportBackup(dialog.FileName);
                ReloadLookups();
                ReloadMasterData();
                ReloadInvoices();
                ReloadDynamicFields();
                ReloadAllDynamicInputPanels();
                RefreshDashboard();
                ResetInvoiceForm();
                NewProductForm();
                NewCustomerForm();
                MessageBox.Show("Backup imported.");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Import backup failed: {ex.Message}");
            }
        }

        private void HardReset_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var backupDialog = new SaveFileDialog
                {
                    Filter = "JSON files (*.json)|*.json",
                    FileName = $"pre-reset-backup-{DateTime.Now:yyyyMMdd-HHmmss}.json"
                };

                if (backupDialog.ShowDialog() != true)
                {
                    return;
                }

                _db.ExportBackup(backupDialog.FileName);

                if (MessageBox.Show("Hard reset will delete all business data after backup. Continue?", "Confirm Hard Reset", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
                {
                    return;
                }

                _db.HardResetBusinessData();
                ReloadLookups();
                ReloadMasterData();
                ReloadInvoices();
                ReloadAllDynamicInputPanels();
                RefreshDashboard();
                ResetInvoiceForm();
                NewProductForm();
                NewCustomerForm();
                MessageBox.Show("Hard reset completed.");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Hard reset failed: {ex.Message}");
            }
        }
    }
}
