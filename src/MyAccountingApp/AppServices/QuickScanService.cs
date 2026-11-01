using MyAccountingApp.Core.Enums;
using MyAccountingApp.Core.Services;
using MyAccountingApp.Localization;

namespace MyAccountingApp.AppServices;

/// <summary>
/// The bottom bar's scan button: scan a barcode from anywhere, then
/// known product → choose new sale / new purchase (with that product as the first line) / open product;
/// unknown barcode → offer to create the product.
/// </summary>
public class QuickScanService(BarcodeScannerService scanner, ProductService products, DialogService dialogs)
{
    private bool _busy;

    public async Task ScanAsync()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            var barcode = await scanner.ScanAsync();
            if (barcode is null) return;

            var product = await products.GetByBarcodeAsync(barcode);
            if (product is null)
            {
                if (await dialogs.ConfirmAsync(LocalizationService.Format("Scan_CreateProductPrompt", barcode)))
                    await GoToAsync(Routes.ProductEdit, new() { ["id"] = 0, ["barcode"] = barcode });
                return;
            }

            var sale = LocalizationService.Get("Dashboard_NewSale");
            var purchase = LocalizationService.Get("Dashboard_NewPurchase");
            var open = LocalizationService.Get("Scan_OpenProduct");
            // Inactive products can still be opened (e.g. to reactivate them), but not ordered.
            var choice = product.IsActive
                ? await dialogs.ChooseAsync(product.Name, sale, purchase, open)
                : await dialogs.ChooseAsync(product.Name, open);

            if (choice == sale || choice == purchase)
                await GoToAsync(Routes.OrderEdit, new()
                {
                    ["type"] = choice == sale ? OrderType.Sale : OrderType.Purchase,
                    ["productId"] = product.Id,
                });
            else if (choice == open)
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
