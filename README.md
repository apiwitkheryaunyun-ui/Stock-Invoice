# Stock & Invoice Blueprint (Demo-first)

This workspace is prepared with a **demo-first** approach for enterprise-style development.

## Why this approach
- Validate full flow quickly
- Collect user feedback early
- Reduce schema and workflow risk before production

## Runtime modes
Use the same codebase with different DB targets:
- `demo`: development and stakeholder demos
- `prod`: real business operations

Recommended implementation:
- Keep two connection strings (`DemoDb`, `ProdDb`)
- Add config flag: `AppMode=demo|prod`
- Disable demo seed in prod

## Data model (real schema from day 1)
Core tables included:
- `products`
- `customers`
- `invoices`
- `invoice_items`
- `stock_movements`

Schema includes:
- PK/FK and indexes
- non-negative checks for prices/totals
- required timestamps (`created_at`, `updated_at`)

## Files in this blueprint
- `database/sqlite/01_schema.sql`
- `database/sqlite/02_seed_demo.sql`
- `database/sqlite/03_reset_demo.sql`
- `database/sqlserver/01_schema.sql`
- `database/sqlserver/02_seed_demo.sql`
- `database/sqlserver/03_reset_demo.sql`
- `docs/weekly_plan.md`
- `StockInvoiceApp/` (WPF starter application)
- `LICENSE` (proprietary, all rights reserved)

## Quick start (SQLite)
1. Create DB file and run schema script.
2. Run demo seed script.
3. Build UI and reports against seeded data.
4. Use reset script to restore demo state as needed.

## Moving to production later
1. Switch connection to production DB.
2. Disable demo seeding.
3. Add CSV/Excel import screen (optional but recommended).
4. Keep validation and constraints unchanged.

## Current app features (implemented)
- Invoice CRUD with auto subtotal/tax/grand total
- CSV import for products and customers
- Dashboard KPIs (products, stock qty, customers, invoices)
- Sales charts (7-day and 12-month)
- Custom date range filter on dashboard
- Low-stock alert highlighting (threshold configurable)
- Dashboard PDF export

## Publish and Desktop icon
From `StockInvoiceApp` folder:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Publish-App.ps1
powershell -ExecutionPolicy Bypass -File .\scripts\Create-DesktopShortcut.ps1
```

## Push to GitHub
If this workspace is not yet a git repo:

```powershell
git init
git add .
git commit -m "Initial StockInvoiceApp release"
git branch -M main
git remote add origin <your-github-repo-url>
git push -u origin main
```

If repository already exists, run only `git add/commit/push`.

## License
This project is protected as proprietary software in `LICENSE`.
Any usage, modification, or distribution requires prior permission from the owner.
Thai copyright notice is available in `COPYRIGHT-TH.md`.

## Suggested app config shape
```json
{
  "AppMode": "demo",
  "ConnectionStrings": {
    "DemoDb": "Data Source=./data/stock_demo.db",
    "ProdDb": "Data Source=./data/stock_prod.db"
  },
  "Seeding": {
    "EnableDemoSeed": true,
    "ResetDemoOnDemand": true
  }
}
```
