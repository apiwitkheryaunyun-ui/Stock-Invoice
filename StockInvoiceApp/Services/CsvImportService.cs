using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using CsvHelper;

namespace StockInvoiceApp.Services;

public sealed class CsvImportService
{
    public IEnumerable<ProductCsvRow> ReadProducts(string filePath)
    {
        using var reader = new StreamReader(filePath);
        using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);
        return csv.GetRecords<ProductCsvRow>().ToList();
    }

    public IEnumerable<CustomerCsvRow> ReadCustomers(string filePath)
    {
        using var reader = new StreamReader(filePath);
        using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);
        return csv.GetRecords<CustomerCsvRow>().ToList();
    }
}
