# MyAccountingApp

Local-only (offline) inventory & sales accounting app for **Android**, built with **.NET 10 MAUI**.
UI in **Arabic + English (RTL-aware)**. All data lives in a SQLite file on the device.

## Architecture

**MVVM + service layer + EF Core SQLite, with the business logic in a plain `net10.0` Core library.**

| Layer | Choice | Notes |
|---|---|---|
| UI | MAUI Shell (flyout), XAML with compiled bindings (`x:DataType` everywhere) | SourceGen XAML inflator is on |
| MVVM | `CommunityToolkit.Mvvm` — `[ObservableProperty]` on **partial properties**, `[RelayCommand]` | |
| Data | EF Core 10 + SQLite, migrations | DB file: `FileSystem.AppDataDirectory/accounting.db3`, migrated at startup in `MauiProgram` |
| DbContext lifetime | `IDbContextFactory<AppDbContext>` — new short-lived context per service call | never inject `AppDbContext` directly |
| Business logic | Services in Core (`OrderService`, `InventoryService`, …) | no repository layer — DbContext is the unit of work |
| Tests | xUnit + in-memory SQLite (`tests/.../TestDb.cs`) | Core runs without an emulator |

```
MyAccountingApp.slnx
src/MyAccountingApp/            MAUI app
  Views/                        XAML pages (code-behind only sets BindingContext / calls LoadCommand)
  ViewModels/                   one VM per page; BaseViewModel.RunAsync shows BusinessRuleException to the user
  AppServices/                  DialogService (alerts/prompts), BackupService (backup/restore .db3)
  Localization/                 LocalizationService, {loc:Translate Key} markup extension
  Helpers/                      Formats (number/date format + parsing), Option<T> (picker items), EnumLabels
  Resources/Strings/            AppResources.resx (en) + AppResources.ar.resx (ar)
  Routes.cs                     pushed-page routes (registered in AppShell)
src/MyAccountingApp.Core/       net10.0, no MAUI dependency
  Entities/  Enums/  Services/
  Data/AppDbContext.cs, Data/Configurations/ (one IEntityTypeConfiguration per table), Data/Migrations/
tests/MyAccountingApp.Core.Tests/
```

Why Core is separate: it is unit-testable on the desktop, and `dotnet ef` can't use the MAUI project as a startup project (`DesignTimeDbContextFactory` lives in Core).

## Commands

```bash
dotnet test tests/MyAccountingApp.Core.Tests                    # business-rule tests
dotnet build src/MyAccountingApp -f net10.0-android              # build
dotnet build src/MyAccountingApp -f net10.0-android -t:Run       # deploy + run on emulator/device
dotnet tool restore                                              # once: local dotnet-ef 10 (dotnet-tools.json)
dotnet ef migrations add <Name> --project src/MyAccountingApp.Core --output-dir Data/Migrations
```

After any entity/configuration change: **add a migration** (installed devices are upgraded by `Database.Migrate()` at startup, and restored backups by `BackupService.RestoreAsync`).

## Database schema

Tables and columns are snake_case to match the original design. All `*_id` columns are FKs with **Restrict** delete — referenced rows are deactivated via `is_active`, never deleted. Only exception: `orders → order_items` cascades (deleting a draft deletes its lines).
There is **no global `is_active` query filter**: old orders must still show inactive products/contacts. Filter in list queries instead.

| Table | Columns | Notes |
|---|---|---|
| categories | id, name (req), is_active | |
| contacts | id, name, phone, address?, is_active, type, created_at | `type` = `ContactType` |
| products | id, name, category_id, **supplier_id?**, barcode (unique), price, is_active, created_at | `supplier_id` = default/preferred supplier (nullable) |
| product_suppliers | id, product_id, contact_id, cost_price (**decimal**), is_active, created_at | all suppliers of a product; unique (product_id, contact_id) |
| orders | id, order_number (unique), order_type, contact_id, status, total_amount, created_at, modified_at | |
| order_items | id, order_id, product_id, quantity, unit_price | |
| ledger | id, product_id, document_type, document_id, quantity, unit_cost, unit_price, created_at | inventory journal |

Changes vs. the original design: `product_suppliers.cost_price` int → decimal; `products.supplier_id` nullable (default supplier); `categories.name` required; unique indexes on barcode / order_number / (product, supplier); `created_at` and `modified_at` are stamped in UTC by `AppDbContext.SaveChanges`.

Enums (stored as int):
- `ContactType`: Customer=1, Supplier=2, Both=3
- `OrderType`: Sale=1, Purchase=2, SaleReturn=3, PurchaseReturn=4 — also used as `ledger.document_type`
- `OrderStatus`: Draft=1, Confirmed=2, Cancelled=3

SQLite stores `decimal` as TEXT. EF translates `Sum`/comparisons fine (covered by tests), but keep multi-column decimal math (e.g. `qty * price`) client-side, as `InventoryService.GetSalesSummaryAsync` does.

## Business rules (implemented in Core services, covered by tests)

- **Ledger = append-only inventory journal.** `document_id` = `orders.id`. Quantity is signed:
  Purchase **+**, SaleReturn **+**, Sale **−**, PurchaseReturn **−** (`OrderService.StockDirection`).
- **Stock on hand = `SUM(ledger.quantity)`** per product. There is no stock column — never add one.
- Order lifecycle: **Draft → Confirmed → (Cancelled)**.
  - Drafts are freely editable/deletable and write nothing to the ledger.
  - `ConfirmAsync` (one transaction): posts one ledger row per item, recalculates `total_amount = Σ qty × unit_price`, sets Confirmed. Confirmed orders are read-only.
  - `CancelAsync` (Confirmed only): posts **reversing** ledger rows (same cost/price, negated qty); original rows stay.
- Stock-out orders that exceed stock throw `Error_InsufficientStock` unless `allowNegativeStock: true` (the UI asks the user first).
- **`ledger.unit_cost`**: purchase = its unit price; every other type = **last purchase cost** of the product = unit cost of the latest ledger row of a still-*Confirmed* purchase (cancelled purchases ignored), 0 if never purchased.
- Confirming a purchase upserts `product_suppliers.cost_price` for that supplier and sets `products.supplier_id` if it was empty.
- Contact must match the order side: Sale/SaleReturn → customer, Purchase/PurchaseReturn → supplier (`Both` matches either).
- `order_number` = per-type prefix + 6 digits: `S-000001`, `P-…`, `SR-…`, `PR-…`, assigned when the draft is created.
- Sales summary / profit (dashboard): over Sale + SaleReturn ledger rows, `revenue = Σ −qty × unit_price`, `cost = Σ −qty × unit_cost` — this nets out returns and cancellations automatically.
- Validation errors are `BusinessRuleException(ErrorKey, args)`; `ErrorKey` is a resx key (`ErrorKeys` class), so Core never contains UI text.

## Localization & RTL

- Strings: `Resources/Strings/AppResources.resx` + `.ar.resx`. **Every key must exist in both files.** Use `{loc:Translate Key}` in XAML, `LocalizationService.Get/Format` in C#. Enum labels use keys `OrderType_*`, `OrderStatus_*`, `ContactType_*` (`EnumLabels`).
- Language is stored in `Preferences` ("language"); default = device language if Arabic. Changing it calls `App.ReloadShell()` which rebuilds the Shell (no live-binding of strings).
- `AppShell` sets `FlowDirection` from the language; pages inherit it. In layouts use **Start/End**, never Left/Right.
- Only `CurrentUICulture` changes. Numbers and dates are always formatted with the **invariant culture** via `Helpers/Formats` (Latin digits, `.` decimal, Gregorian dates). `Formats.TryParseDecimal` also accepts Arabic-Indic digits and `,`/`٫` separators. Decimal inputs are bound as **strings** in VMs and parsed with it.

## UI conventions

- Pages and VMs are registered transient in `MauiProgram`; Core services/`DialogService`/`BackupService` are singletons.
- List pages: VM has `LoadCommand`, called from `OnAppearing`. Edit pages: VM implements `IQueryAttributable`; navigate with `["id"] = 0` for "new" so `ApplyQueryAttributes` always runs. `BaseViewModel.GoToAsync` wraps parameters in `ShellNavigationQueryParameters` (single-use).
- Pickers bind to `Option<T>` lists or entities with `ToString()` → `Name` (no `ItemDisplayBinding`, which conflicts with compiled bindings).
- Item-template commands bind to the page VM with `{Binding Source={RelativeSource AncestorType={x:Type vm:XViewModel}}, Path=…, x:DataType=vm:XViewModel}`.
- `Contact` clashes with `Microsoft.Maui.ApplicationModel.Communication.Contact` — add `using Contact = MyAccountingApp.Core.Entities.Contact;` in app files that use it.
- Styles: template `Styles.xaml` + app styles in `Resources/Styles/AppStyles.xaml` (`Card`, `ItemTitle`, `Caption`, `Amount`, `KpiValue`, `SecondaryButton`, `DangerButton`, `FormLabel`).

## Local-only considerations

- The phone holds the only copy of the data. Settings → Backup shares a `VACUUM INTO` copy of the DB; Restore validates the picked file, replaces the DB and runs migrations.
- Future sync/server: keep all data access inside Core services so an API can replace or sit beside them; a `uuid` column per table would be the first schema step.
- Android Release builds trim assemblies and EF Core is not fully trim-safe — test a Release build; if queries fail at runtime, root the EF/Core assemblies (`<TrimmerRootAssembly>`).

## Not done yet / ideas

- Camera barcode scanning (barcode is typed or pasted; an exact barcode match in the order search adds the line directly).
- Customer/supplier balances & payments (no payments table yet), date-range reports, receipt printing/sharing.
