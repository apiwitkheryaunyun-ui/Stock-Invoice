using System.Windows;
using StockInvoiceApp.Services;

namespace StockInvoiceApp
{
    public partial class App : Application
    {
        private void Application_Startup(object sender, StartupEventArgs e)
        {
            var env = new AppEnvironment();
            var db = new DatabaseService(env);
            db.InitializeDatabase();

            var csv = new CsvImportService();
            var pdf = new PdfReportService();
            var window = new MainWindow(db, csv, pdf);
            window.Show();
        }
    }
}
