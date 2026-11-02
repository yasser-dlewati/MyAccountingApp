# MyAccountingApp

Local-only (offline) inventory & sales accounting app for **Android**, built with **.NET 10 MAUI**.
UI in **Arabic + English (RTL-aware)**. All data lives in a SQLite file on the device.

## Architecture

**MVVM + service layer + EF Core SQLite, with the business logic in a plain `net10.0` Core library.**

| Layer | Choice | Notes |
|---|---|---|
| UI | MAUI Shell (custom bottom nav, flyout disabled), XAML with compiled bindings (`x:DataType` everywhere) | SourceGen XAML inflator is on |
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
  AppServices/                  DialogService (alerts/prompts), BackupService (backup/restore .db3),
                                BarcodeScannerService (camera permission + scanner page)
  Localization/                 LocalizationService, {loc:Translate Key} markup extension
  Helpers/                      Formats (number/date format + parsing), Option<T> (picker items), EnumLabels
  Resources/Strings/            AppResources.resx (en) + AppResources.ar.resx (ar)
  Controls/                     BottomNavBar
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
| products | id, name, **category_id?**, **supplier_id?**, barcode (unique), price, is_active, created_at | `supplier_id` = default/preferred supplier (nullable); `category_id` null = built-in "General" |
| product_suppliers | id, product_id, contact_id, cost_price (**decimal**), is_active, created_at | all suppliers of a product; unique (product_id, contact_id) |
| orders | id, order_number (unique), order_type, **contact_id?**, status, total_amount, created_at, modified_at | `contact_id` null = walk-in customer / supplier |
| order_items | id, order_id, product_id, quantity, unit_price | |
| ledger | id, product_id, document_type, document_id, quantity, unit_cost, unit_price, created_at | inventory journal |

Changes vs. the original design: `product_suppliers.cost_price` int → decimal; `products.supplier_id` nullable (default supplier); `products.category_id` and `orders.contact_id` nullable; `categories.name` required; unique indexes on barcode / order_number / (product, supplier); `created_at` and `modified_at` are stamped in UTC by `AppDbContext.SaveChanges`.

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
- Contact is optional: null = walk-in customer / supplier (`DisplayNames.WalkIn`, shown as the first picker entry with Id 0). A walk-in purchase records no supplier cost / default supplier. Settings → Walk-in orders shows "Require a customer / supplier" switches (off by default), stored inverted as `WalkInSettings` (`Preferences` "allow_walkin_customer" / "allow_walkin_supplier", default true); the app passes `allowWalkIn` to `ConfirmAsync`, which throws `Error_ContactRequired` when it is false. A posted walk-in order still shows its walk-in label.
- Drafts may be saved incomplete (no contact, no items, zero quantity / blank price); `ConfirmAsync` checks items exist and quantities are > 0. A contact that is set must match the side even on a draft.
- Category: `ProductService.SaveAsync(product, categoryRequired)`. The app passes `CategorySettings.Enabled` (Settings → Categories, `Preferences` "use_categories", default on). Off: the category picker and the More page's Categories row are hidden, new products get a null category (shown as "General" via `DisplayNames.CategoryOf`), existing products keep theirs.
- Contact, when set, must match the order side: Sale/SaleReturn → customer, Purchase/PurchaseReturn → supplier (`Both` matches either).
- `order_number` = per-type prefix + 6 digits: `S-000001`, `P-…`, `SR-…`, `PR-…`, assigned when the draft is created.
- Sales summary / profit (dashboard): over Sale + SaleReturn ledger rows, `revenue = Σ −qty × unit_price`, `cost = Σ −qty × unit_cost`, `items sold = Σ −qty` — this nets out returns and cancellations automatically.
- Validation errors are `BusinessRuleException(ErrorKey, args)`; `ErrorKey` is a resx key (`ErrorKeys` class), so Core never contains UI text.

## Localization & RTL

- Strings: `Resources/Strings/AppResources.resx` + `.ar.resx`. **Every key must exist in both files.** Use `{loc:Translate Key}` in XAML, `LocalizationService.Get/Format` in C#. Enum labels use keys `OrderType_*`, `OrderStatus_*`, `ContactType_*` (`EnumLabels`).
- Language is stored in `Preferences` ("language"); default = device language if Arabic. Changing it calls `App.ReloadShell()` which rebuilds the Shell (no live-binding of strings).
- `AppShell` sets `FlowDirection` from the language; pages inherit it. In layouts use **Start/End**, never Left/Right.
- Money is whole Syrian pounds: `Formats.Money` → `#,0` + localized `Currency_Symbol` ("SYP" / "ل.س"); no decimals in views (stored values stay `decimal`).
- Only `CurrentUICulture` changes. Numbers and dates are always formatted with the **invariant culture** via `Helpers/Formats` (Latin digits, `.` decimal, Gregorian dates). `Formats.TryParseDecimal` also accepts Arabic-Indic digits and `,`/`٫` separators. Decimal inputs are bound as **strings** in VMs and parsed with it.

## UI conventions

- Pages and VMs are registered transient in `MauiProgram`; Core services/`DialogService`/`BackupService` are singletons.
- List pages: VM has `LoadCommand`, called from `OnAppearing`. Edit pages: VM implements `IQueryAttributable`; navigate with `["id"] = 0` for "new" so `ApplyQueryAttributes` always runs. `BaseViewModel.GoToAsync` wraps parameters in `ShellNavigationQueryParameters` (single-use).
- Pickers bind to `Option<T>` lists or entities with `ToString()` → `Name` (no `ItemDisplayBinding`, which conflicts with compiled bindings).
- Item-template commands bind to the page VM with `{Binding Source={RelativeSource AncestorType={x:Type vm:XViewModel}}, Path=…, x:DataType=vm:XViewModel}`.
- `Contact` clashes with `Microsoft.Maui.ApplicationModel.Communication.Contact` — add `using Contact = MyAccountingApp.Core.Entities.Contact;` in app files that use it.
- Navigation: no flyout (`FlyoutBehavior="Disabled"`, no hamburger). Shell root items: dashboard, orders, inventory (`ProductsPage`), more. Bottom nav: `Controls/BottomNavBar` (Home, Invoices, raised center scan button, Inventory, More) sits in the last row of every root page and of the Categories page (the dashboard's New sale / New purchase buttons sit just above it) with `Current="dashboard|orders|inventory|more"`; tabs navigate with `//route`; tapping the current tab on a pushed page pops to its root. `Views/MorePage` lists Contacts, Categories (hidden when categories are off) and Settings as rows that push `Routes.Contacts/Categories/Settings`, then Share logs and the About card. The center button runs `QuickScanService`: known product → new sale / new purchase (`["productId"]` pre-adds the line in order edit) / open product; unknown → offer to create it.
- Back button (`AppShell.OnBackButtonPressed`): pushed/modal pages pop as usual (More → Settings → back returns to More); any other root page goes to the dashboard; on the dashboard the app quits, after an `Exit_Prompt` confirm when Settings → General → Confirm before exiting is on (`ExitSettings`, `Preferences` "confirm_exit", default true).
- Icon buttons: Add = toolbar `ic_add_circle.png`; scan / in-page add = `ImageButton` with style `IconButton` (`ic_barcode.png` / `ic_plus.png`). Their label goes in `ToolTipProperties.Text` (Android shows it on long press) + `SemanticProperties.Description`.
- Orders page: Sales / Purchases tabs (returns included on their side, `GetOrdersAsync(salesSide:)`) + search + status filter. Inventory page (`ProductsPage`, merged products + stock): stock value at cost (Σ qty × last cost of in-stock rows) on top, each row shows price, stock (red when ≤ 0), last cost and value; inactive products shown per Settings → Products (`ProductSettings`, "show_inactive_products"); scanning shows a details dialog with an Edit button.
- Soft keyboard: `Platforms/Android/MainActivity.DispatchTouchEvent` hides it (and clears focus) on a tap outside the focused text field, app-wide — no per-page handling needed.
- Theme: Settings → Theme (System / Light / Dark) via `AppServices/ThemeService`, saved in `Preferences` ("theme") and applied as `Application.UserAppTheme` (live, no Shell reload). Every color must use `AppThemeBinding`.
- Branding: app name "متجر وائل" (`ApplicationTitle`, `App_Title`). Icon/splash SVGs have the Arabic text as **paths** (resizetizer can't shape Arabic `<text>`). Splash "Created by YASDLE" strip = `@drawable/splash_branding` (`drawable/` → `Resources/Images/branding_on_light.svg`, `drawable-night/` → `branding_on_dark.svg`), shown via `AppSplashTheme` (`Platforms/Android/Resources/values*/styles.xml`: branding image on API 31+, `drawable/app_splash.xml` layer-list below). Splash image = the launcher icon (green circle baked into `splash.svg`; API 31+ icon is `maui_splash_image`, not MAUI's `maui_splash`, which would add a background square). Splash background = `@color/app_splash_background` (`values/` white, `values-night/` #1F1F1F); `ThemeService` also sets `UiModeManager.SetApplicationNightMode` so on API 31+ the splash follows the in-app theme (older Android: system theme only). More → About shows `yasdle.svg`.
- Styles: template `Styles.xaml` + app styles in `Resources/Styles/AppStyles.xaml` (`Card`, `ItemTitle`, `Caption`, `Amount`, `KpiValue`, `SecondaryButton`, `DangerButton`, `FormLabel`).

## Barcode scanning (camera)

- Package: `ZXing.Net.Maui.Controls` (on-device decoding, works offline), registered with `.UseBarcodeReader()` in `MauiProgram`.
- Entry point: `BarcodeScannerService.ScanAsync()` → barcode string, or null (cancelled / no camera / permission denied). VMs call only this, never the camera control.
- Permission flow (always via the service, before the camera opens): `CheckStatusAsync` → if previously denied show `Scan_PermissionRationale` → `RequestAsync` → if denied and Android won't ask again (`ShouldShowRationale` false), offer `AppInfo.ShowSettingsUI()`.
- `Views/BarcodeScanPage` is a **modal** page created by the service (not a Shell route, not in DI). Its logic stays in code-behind because the camera control is event-based. It sets its own `FlowDirection` (modals don't inherit the Shell's), and on disappearing it disconnects the camera handler.
- Formats: EAN-13/8, UPC-A/E, Code 128/39/93, ITF, QR. Codabar/RSS are left out because they give false positives.
- Used in: order edit (scan → exact match adds the line, or +1 qty; no match → `Scan_ProductNotFound`), product edit (fills the barcode field), products list (shows the product's details with an Edit button, or offers to create one with `["barcode"]` pre-filled).
- Manifest: `CAMERA`, `VIBRATE` (scan haptic), `uses-feature camera.any required=false`. iOS/Mac Info.plist: `NSCameraUsageDescription`.

## Logging

- `Microsoft.Extensions.Logging` everywhere: inject `ILogger<T>` (Core services take it as an **optional** ctor parameter, `NullLogger` fallback, so tests can `new` them).
- `AppServices/FileLoggerProvider` writes `FileSystem.AppDataDirectory/logs/app-yyyy-MM-dd.log` (Android: `/data/data/com.companyname.myaccountingapp/files/logs/`), keeps 7 days, appends + closes per line (crash-safe). Level: Information; `Microsoft.*` Warning (Debug builds also log EF SQL and to the debugger/logcat).
- Logged: startup (version/device/migrations), unhandled + unobserved-task exceptions, navigation, order create/delete/confirm/cancel, business-rule errors shown to the user (`DialogService.ShowErrorAsync`), backup/restore, scans and camera permission.
- More → Share logs zips the folder into the cache dir and opens the share sheet. Never log credentials or full customer data dumps.

## Local-only considerations

- The phone holds the only copy of the data. Settings → Backup shares a `VACUUM INTO` copy of the DB; Restore validates the picked file, replaces the DB and runs migrations.
- Future sync/server: keep all data access inside Core services so an API can replace or sit beside them; a `uuid` column per table would be the first schema step.
- Android Release builds trim assemblies and EF Core is not fully trim-safe — test a Release build; if queries fail at runtime, root the EF/Core assemblies (`<TrimmerRootAssembly>`).

## Not done yet / ideas

- Continuous scanning mode for orders (scan many items without reopening the scanner).
- Customer/supplier balances & payments (no payments table yet), date-range reports, receipt printing/sharing.
