using MyAccountingApp.Core.Enums;
using MyAccountingApp.Core.Services;
using MyAccountingApp.Helpers;
using MyAccountingApp.Localization;

namespace MyAccountingApp.AppServices;

/// <summary>
/// The bottom bar's scan button: tap = multi-scan, then a new sale with the scanned products (none = no sale);
/// long press = menu with new sale / new purchase / check a product (scan → its details, or offer to create it).
/// </summary>
public class QuickScanService(
    BarcodeScannerService scanner, ProductService products, InventoryService inventory, DialogService dialogs)
{
    private bool _busy;

    /// <summary>Scans products first; only when at least one was found does a new sale open with them.</summary>
    public async Task ScanSaleAsync()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            var scanned = new List<int>(); // product ids, repeated = quantity
            await scanner.ScanManyAsync(async barcode =>
            {
                var product = await products.GetByBarcodeAsync(barcode);
                if (product is not { IsActive: true })
                    return new ScanOutcome(false, LocalizationService.Format("Scan_ProductNotFound", barcode));
                scanned.Add(product.Id);
                return new ScanOutcome(true, LocalizationService.Format("Scan_Added", product.Name,
                    scanned.Count(id => id == product.Id)));
            });

            if (scanned.Count > 0)
                await GoToAsync(Routes.OrderEdit, new() { ["type"] = OrderType.Sale, ["productIds"] = scanned });
        }
        catch (BusinessRuleException ex)
        {
            await dialogs.ShowErrorAsync(ex);
        }
        finally
        {
            _busy = false;
        }
    }

    public Task NewOrderAsync(OrderType type) =>
        GoToAsync(Routes.OrderEdit, new() { ["type"] = type });

    /// <summary>
    /// Scanned barcode → show that product's details (even if inactive) with an Edit button,
    /// or offer to create it.
    /// </summary>
    public async Task CheckProductAsync()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            var barcode = await scanner.ScanAsync();
            if (barcode is null) return;

            var found = await products.GetByBarcodeAsync(barcode);
            if (found is null)
            {
                if (await dialogs.ConfirmAsync(LocalizationService.Format("Scan_CreateProductPrompt", barcode)))
                    await GoToAsync(Routes.ProductEdit, new() { ["id"] = 0, ["barcode"] = barcode });
                return;
            }

            var product = await products.GetAsync(found.Id) ?? found;
            var details = LocalizationService.Format("Product_Details",
                product.Barcode,
                Formats.Money(product.Price),
                Formats.Quantity(await inventory.GetStockAsync(product.Id)),
                Formats.Money(await inventory.GetLastPurchaseCostAsync(product.Id)),
                DisplayNames.CategoryOf(product),
                product.Supplier?.Name ?? LocalizationService.Get("Product_NoSupplier"),
                LocalizationService.Get(product.IsActive ? "Product_Active" : "Product_Inactive"));

            if (await dialogs.DetailsAsync(product.Name, details, LocalizationService.Get("Product_Edit")))
                await GoToAsync(Routes.ProductEdit, new() { ["id"] = product.Id });
        }
        catch (BusinessRuleException ex)
        {
            await dialogs.ShowErrorAsync(ex);
        }
        finally
        {
            _busy = false;
        }
    }

    private static Task GoToAsync(string route, Dictionary<string, object> parameters) =>
        Shell.Current.GoToAsync(route, new ShellNavigationQueryParameters(parameters));
}
