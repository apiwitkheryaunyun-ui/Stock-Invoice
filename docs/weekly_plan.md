# Weekly Implementation Plan (WinForms/WPF)

## Week 1 - Foundation and Data Layer
- Finalize DB choice (SQLite first, SQL Server ready)
- Execute schema scripts
- Implement repository/data access for products and customers
- Add basic app configuration for `demo` and `prod` modes
- Add startup seeding guard (`EnableDemoSeed`)

Deliverable:
- App can list products/customers from demo DB

## Week 2 - Sales Invoice Flow
- Invoice header CRUD (`invoices`)
- Invoice line CRUD (`invoice_items`)
- Auto calculate subtotal, tax, grand total
- Basic validation (required fields, non-negative numbers)

Deliverable:
- User can create and save invoice with multiple items

## Week 3 - Stock Movement Integration
- Write stock movement on invoice post/cancel
- Product stock summary from movement ledger
- Add stock card screen (by product/date range)

Deliverable:
- Sales affects stock correctly and is traceable

## Week 4 - UX Hardening and Reporting
- Improve forms (search/filter/sort)
- Print/export invoice (PDF or print view)
- Add key dashboard widgets: sales this month, top products, low stock

Deliverable:
- Near-demo-ready flow for business review

## Week 5 - Production Readiness
- Add role basics (admin/sales)
- Add error logging and audit-friendly messages
- Add optional CSV/Excel import for products/customers
- UAT bug fixes and stabilization

Deliverable:
- Pilot-ready build with safe data model and controls

## Ongoing Quality Checklist
- Keep FK constraints enabled in every environment
- Keep `created_at` and `updated_at` maintained
- Add indexes for frequent search fields
- Add migration/version notes per DB change
