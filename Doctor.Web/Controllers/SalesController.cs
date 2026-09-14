using Doctor.Web.Data;
using Doctor.Web.Models.Entities;
using Doctor.Web.Services;
using Doctor.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Doctor.Web.Controllers;

[Authorize(Roles = "Admin,Sales")]
public class SalesController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IPosService _posService;
    private readonly IInventoryService _inventoryService;
    private readonly IBarcodeService _barcodeService;
    private readonly IAuditLogService _auditLogService;

    public SalesController(
        ApplicationDbContext context,
        IPosService posService,
        IInventoryService inventoryService,
        IBarcodeService barcodeService,
        IAuditLogService auditLogService)
    {
        _context = context;
        _posService = posService;
        _inventoryService = inventoryService;
        _barcodeService = barcodeService;
        _auditLogService = auditLogService;
    }

    public IActionResult Index()
    {
        return RedirectToAction(nameof(Pos));
    }

    public async Task<IActionResult> Pos()
    {
        ViewBag.Brands = await _context.Brands.Where(b => b.IsActive).ToListAsync();
        ViewBag.Categories = await _context.ProductCategories.Where(c => c.IsActive).ToListAsync();
        ViewBag.Customers = await _context.Customers.OrderBy(c => c.Name).Take(50).ToListAsync();

        // Sample initial quick products (top stock items)
        ViewBag.QuickProducts = await _context.Products
            .Include(p => p.Brand)
            .Where(p => p.IsActive && p.StockQuantity > 0)
            .OrderByDescending(p => p.StockQuantity)
            .Take(12)
            .ToListAsync();

        return View();
    }

    public async Task<IActionResult> Invoices(string? search, DateTime? from, DateTime? to, PaymentMethod? paymentMethod, int page = 1)
    {
        var result = await _posService.GetSalesAsync(search, from, to, paymentMethod, page, 20);
        ViewBag.Search = search;
        ViewBag.From = from?.ToString("yyyy-MM-dd");
        ViewBag.To = to?.ToString("yyyy-MM-dd");
        ViewBag.PaymentMethod = paymentMethod;
        ViewBag.CurrentPage = page;
        ViewBag.TotalPages = (int)Math.Ceiling(result.TotalCount / 20.0);

        return View(result.Items);
    }

    public async Task<IActionResult> Invoice(string id)
    {
        var sale = await _posService.GetSaleByInvoiceNumberAsync(id);
        if (sale == null)
        {
            if (int.TryParse(id, out int saleId))
            {
                sale = await _posService.GetSaleByIdAsync(saleId);
            }
        }

        if (sale == null)
        {
            return NotFound("الفاتورة غير موجودة");
        }

        // Generate QR code for invoice
        string qrPayload = $"INVOICE:{sale.InvoiceNumber}|DATE:{sale.CreatedAt:yyyy-MM-dd HH:mm}|TOTAL:{sale.TotalAmount:N2}|CENTER:دكتور سنتر";
        ViewBag.QrCodeBase64 = _barcodeService.GenerateQrCodeBase64(qrPayload);

        var settings = await _context.Settings.ToDictionaryAsync(s => s.Key, s => s.Value);
        ViewBag.Settings = settings;

        return View(sale);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReturnInvoice(int id, string? reason, string? returnUrl)
    {
        int.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out int userId);
        if (userId <= 0)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Status == UserStatus.Active);
            if (user != null) userId = user.Id;
        }

        var result = await _posService.ReturnSaleAsync(id, userId, reason);
        if (result.Success)
        {
            TempData["Success"] = result.Message;
        }
        else
        {
            TempData["Error"] = result.Message;
        }

        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return Redirect(returnUrl);
        }

        return RedirectToAction(nameof(Invoices));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteInvoice(int id, bool returnStock = true)
    {
        int.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out int userId);
        if (userId <= 0)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Status == UserStatus.Active);
            if (user != null) userId = user.Id;
        }

        var result = await _posService.DeleteSaleAsync(id, userId, returnStock);
        if (result.Success)
        {
            TempData["Success"] = result.Message;
        }
        else
        {
            TempData["Error"] = result.Message;
        }

        return RedirectToAction(nameof(Invoices));
    }

    #region Products Management

    public async Task<IActionResult> Products(int? brandId, string? search)
    {
        var query = _context.Products
            .Include(p => p.Brand)
            .Where(p => p.IsActive && p.ProductType == ProductType.Device)
            .AsQueryable();

        if (brandId.HasValue)
        {
            query = query.Where(p => p.BrandId == brandId.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            search = search.Trim();
            query = query.Where(p =>
                p.Name.Contains(search) ||
                p.Barcode.Contains(search) ||
                (p.SerialNumberOrImei != null && p.SerialNumberOrImei.Contains(search)) ||
                (p.Model != null && p.Model.Contains(search)));
        }

        var products = await query.OrderByDescending(p => p.Id).ToListAsync();

        ViewBag.BrandId = brandId;
        ViewBag.Search = search;
        ViewBag.Brands = await _context.Brands.Where(b => b.IsActive).OrderBy(b => b.Name).ToListAsync();

        return View(products);
    }

    [HttpGet]
    public async Task<IActionResult> CreateProduct()
    {
        ViewBag.Brands = await _context.Brands.Where(b => b.IsActive).OrderBy(b => b.Name).ToListAsync();

        var product = new Product
        {
            ProductType = ProductType.Device,
            Barcode = DateTime.UtcNow.Ticks.ToString()[^12..] // auto-suggested unique barcode
        };

        return View(product);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateProduct(Product product)
    {
        ModelState.Remove(nameof(product.ProductCategory));
        ModelState.Remove(nameof(product.Brand));

        if (string.IsNullOrWhiteSpace(product.Name) || string.IsNullOrWhiteSpace(product.Barcode))
        {
            ModelState.AddModelError("", "اسم الجهاز والباركود حقول إلزامية.");
        }

        bool barcodeExists = await _context.Products.AnyAsync(p => p.Barcode == product.Barcode.Trim());
        if (barcodeExists)
        {
            ModelState.AddModelError("Barcode", "الباركود المدخل مستخدم بالفعل لجهاز آخر.");
        }

        if (!product.ProductCategoryId.HasValue || product.ProductCategoryId <= 0)
        {
            var defaultCat = await _context.ProductCategories.FirstOrDefaultAsync();
            if (defaultCat != null) product.ProductCategoryId = defaultCat.Id;
        }

        if (!ModelState.IsValid)
        {
            ViewBag.Brands = await _context.Brands.Where(b => b.IsActive).OrderBy(b => b.Name).ToListAsync();
            return View(product);
        }

        product.ProductType = ProductType.Device;
        product.Name = product.Name.Trim();
        product.Barcode = product.Barcode.Trim();
        product.CreatedAt = DateTime.UtcNow;

        _context.Products.Add(product);
        await _context.SaveChangesAsync();

        // Record initial inventory transaction if quantity > 0
        if (product.StockQuantity > 0)
        {
            int.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out int userId);
            _context.InventoryTransactions.Add(new InventoryTransaction
            {
                ProductId = product.Id,
                TransactionType = InventoryTransactionType.Purchase,
                Quantity = product.StockQuantity,
                QuantityBefore = 0,
                QuantityAfter = product.StockQuantity,
                UserId = userId > 0 ? userId : null,
                Notes = "رصيد افتتاحي عند إضافة الجهاز",
                CreatedAt = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();
        }

        TempData["Success"] = $"تمت إضافة الجهاز ({product.Name}) بنجاح.";
        return RedirectToAction(nameof(Products));
    }

    [HttpGet]
    public async Task<IActionResult> EditProduct(int id)
    {
        var product = await _context.Products.FindAsync(id);
        if (product == null)
        {
            return NotFound();
        }

        ViewBag.Brands = await _context.Brands.Where(b => b.IsActive).OrderBy(b => b.Name).ToListAsync();

        return View(product);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditProduct(int id, Product productInput)
    {
        var product = await _context.Products.FindAsync(id);
        if (product == null)
        {
            return NotFound();
        }

        ModelState.Remove(nameof(productInput.ProductCategory));
        ModelState.Remove(nameof(productInput.Brand));

        bool barcodeExists = await _context.Products.AnyAsync(p => p.Barcode == productInput.Barcode.Trim() && p.Id != id);
        if (barcodeExists)
        {
            ModelState.AddModelError("Barcode", "الباركود مستخدم لجهاز آخر.");
        }

        if (!ModelState.IsValid)
        {
            ViewBag.Brands = await _context.Brands.Where(b => b.IsActive).OrderBy(b => b.Name).ToListAsync();
            return View(productInput);
        }

        int oldStock = product.StockQuantity;

        product.Name = productInput.Name.Trim();
        product.BrandId = productInput.BrandId;
        if (productInput.ProductCategoryId.HasValue && productInput.ProductCategoryId > 0)
        {
            product.ProductCategoryId = productInput.ProductCategoryId;
        }
        product.Model = productInput.Model;
        product.Color = productInput.Color;
        product.Storage = productInput.Storage;
        product.Ram = productInput.Ram;
        product.SerialNumberOrImei = productInput.SerialNumberOrImei;
        product.Barcode = productInput.Barcode.Trim();
        product.Sku = productInput.Sku;
        product.PurchasePrice = productInput.PurchasePrice;
        product.SellingPrice = productInput.SellingPrice;
        product.StockQuantity = productInput.StockQuantity;
        product.MinStockLevel = productInput.MinStockLevel;
        product.Notes = productInput.Notes;
        product.IsActive = productInput.IsActive;

        // If stock changed manually, record adjustment transaction
        if (oldStock != productInput.StockQuantity)
        {
            int diff = productInput.StockQuantity - oldStock;
            int.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out int userId);

            _context.InventoryTransactions.Add(new InventoryTransaction
            {
                ProductId = product.Id,
                TransactionType = InventoryTransactionType.Adjustment,
                Quantity = diff,
                QuantityBefore = oldStock,
                QuantityAfter = productInput.StockQuantity,
                UserId = userId > 0 ? userId : null,
                Notes = "تعديل رصيد يدوي من شاشة تعديل المنتج",
                CreatedAt = DateTime.UtcNow
            });
        }

        await _context.SaveChangesAsync();

        TempData["Success"] = $"تم تحديث بيانات المنتج ({product.Name}) بنجاح.";
        return RedirectToAction(nameof(Products));
    }

    [AcceptVerbs("GET", "POST")]
    public async Task<IActionResult> DeleteProduct(int id)
    {
        var product = await _context.Products.FindAsync(id);
        if (product == null) return NotFound();

        product.IsActive = false; // Soft delete ensures referential integrity with inventory, sales & supply orders
        await _context.SaveChangesAsync();
        TempData["Success"] = $"تم حذف الجهاز ({product.Name}) بنجاح.";

        return RedirectToAction(nameof(Products));
    }

    #endregion

    #region Brands & Categories

    public async Task<IActionResult> Brands()
    {
        var brands = await _context.Brands
            .Include(b => b.Products)
            .OrderBy(b => b.Name)
            .ToListAsync();
        return View(brands);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateBrand(string name)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            _context.Brands.Add(new Brand { Name = name.Trim(), IsActive = true, CreatedAt = DateTime.UtcNow });
            await _context.SaveChangesAsync();
            TempData["Success"] = $"تمت إضافة شركة ({name}) بنجاح.";
        }
        return RedirectToAction(nameof(Brands));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteBrand(int id)
    {
        var brand = await _context.Brands.Include(b => b.Products).FirstOrDefaultAsync(b => b.Id == id);
        if (brand != null)
        {
            if (brand.Products.Any())
            {
                TempData["Error"] = "لا يمكن حذف الشركة لوجود منتجات مرتبطة بها.";
            }
            else
            {
                _context.Brands.Remove(brand);
                await _context.SaveChangesAsync();
                TempData["Success"] = "تم حذف الشركة بنجاح.";
            }
        }
        return RedirectToAction(nameof(Brands));
    }

    public async Task<IActionResult> Categories()
    {
        var categories = await _context.ProductCategories
            .Include(c => c.Products)
            .OrderBy(c => c.Name)
            .ToListAsync();
        return View(categories);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateCategory(string name, string? description)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            _context.ProductCategories.Add(new ProductCategory { Name = name.Trim(), Description = description?.Trim(), IsActive = true });
            await _context.SaveChangesAsync();
            TempData["Success"] = $"تمت إضافة تصنيف ({name}) بنجاح.";
        }
        return RedirectToAction(nameof(Categories));
    }

    #endregion
}
