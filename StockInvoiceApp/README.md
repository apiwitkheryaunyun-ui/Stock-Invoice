# StockInvoiceApp (WPF Starter)

This WPF starter includes:
- App mode switch (`demo` / `prod`) via `appsettings.json`
- SQLite database initialization from workspace SQL scripts
- Dashboard tab for stock/customer/invoice counts and daily-monthly-yearly sales
- Dashboard date filter (From/To) and low-stock threshold filter
- Low-stock highlight in stock table (rows are tinted)
- Dashboard PDF export
- Invoice CRUD first version with auto subtotal/tax/grand total
- CSV import page for `products` and `customers`

## Run
1. Ensure workspace root has `database/sqlite/01_schema.sql` and `02_seed_demo.sql`.
2. Open terminal at `StockInvoiceApp`.
3. Run:

```powershell
dotnet run
```

## Can this template be customized later?
Yes. The structure is intentionally split so future changes are easy:
- `Services/` for DB and import logic
- `Models/` for domain model and DTO shape
- `MainWindow.xaml` and `MainWindow.xaml.cs` for first UI flow
- `appsettings.json` for mode and connection switching

Recommended next customizations:
- Add ViewModel layer for larger screens/modules
- Add role/permission and user audit fields
- Add migration versioning per schema change
- Add report/export modules (PDF/Excel)
- Split invoice logic into dedicated service classes

## Desktop icon (open app from Desktop directly)
Use included scripts:

1. Publish executable:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Publish-App.ps1
```

2. Create desktop shortcut icon:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Create-DesktopShortcut.ps1
```

After step 2, you will get `StockInvoiceApp.lnk` on Desktop and can open the app by double-click.

Optional self-contained publish (run without installed .NET runtime):

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Publish-App.ps1 -SelfContained
```

## App mode
Configure in `appsettings.json`:
- `AppMode`: `demo` or `prod`
- `ConnectionStrings.DemoDb`
- `ConnectionStrings.ProdDb`
- `Seeding.EnableDemoSeed`

## CSV format
Use templates:
- `SampleCsv/products_template.csv`
- `SampleCsv/customers_template.csv`

## Dashboard usage
1. Open `Dashboard` tab.
2. Set optional date range in `From` and `To`.
3. Set low-stock threshold (default `10`).
4. Click `Apply Filter` to refresh KPI + charts + stock table.
5. Click `Export PDF` to save dashboard report as PDF.

Charts included:
- Sales trend for latest 7 days
- Sales trend for latest 12 months

## Notes
- In demo mode, if invoice table is empty, demo seed runs automatically.
- Invoice save writes stock movement of type `sale` and updates product stock summary.
