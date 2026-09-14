using Doctor.Web.Data;
using Doctor.Web.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Doctor.Web.Controllers;

[Authorize]
public class SuppliersController : Controller
{
    private readonly ApplicationDbContext _context;

    public SuppliersController(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> Index(string? search, bool? onlyDebt)
    {
        var query = _context.Suppliers
            .Include(s => s.SupplyOrders)
            .Include(s => s.Payments)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            search = search.Trim();
            query = query.Where(s =>
                s.Name.Contains(search) ||
                (s.CompanyName != null && s.CompanyName.Contains(search)) ||
                s.PhoneNumber.Contains(search));
        }

        var list = await query.OrderByDescending(s => s.Id).ToListAsync();

        if (onlyDebt == true)
        {
            list = list.Where(s => (s.SupplyOrders.Sum(o => o.TotalAmount) - s.Payments.Sum(p => p.Amount)) > 0).ToList();
        }

        ViewBag.Search = search;
        ViewBag.OnlyDebt = onlyDebt;
        ViewBag.TotalSuppliers = await _context.Suppliers.CountAsync();
        
        var allOrdersSum = await _context.SupplyOrders.SumAsync(o => (decimal?)o.TotalAmount) ?? 0m;
        var allPaymentsSum = await _context.SupplierPayments.SumAsync(p => (decimal?)p.Amount) ?? 0m;
        ViewBag.TotalDebtToSuppliers = allOrdersSum - allPaymentsSum;

        return View(list);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Supplier supplier, decimal? initialBalance)
    {
        if (string.IsNullOrWhiteSpace(supplier.Name) || string.IsNullOrWhiteSpace(supplier.PhoneNumber))
        {
            TempData["Error"] = "اسم المورد ورقم الهاتف حقول مطلوبة.";
            return RedirectToAction(nameof(Index));
        }

        supplier.Name = supplier.Name.Trim();
        supplier.PhoneNumber = supplier.PhoneNumber.Trim();
        supplier.CreatedAt = DateTime.UtcNow;
        supplier.IsActive = true;

        _context.Suppliers.Add(supplier);
        await _context.SaveChangesAsync();

        // If there's an opening balance owed to the supplier, record a supply order for it
        if (initialBalance.HasValue && initialBalance.Value > 0)
        {
            int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out int userId);
            var initialOrder = new SupplyOrder
            {
                SupplierId = supplier.Id,
                OrderNumber = "INIT-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss"),
                TotalAmount = initialBalance.Value,
                PaidAmount = 0,
                RemainingAmount = initialBalance.Value,
                Notes = "رصيد سابق / افتتاحي",
                UserId = userId > 0 ? userId : null,
                CreatedAt = DateTime.UtcNow
            };
            _context.SupplyOrders.Add(initialOrder);
            await _context.SaveChangesAsync();
        }

        TempData["Success"] = $"تمت إضافة المورد ({supplier.Name}) بنجاح.";
        return RedirectToAction(nameof(Details), new { id = supplier.Id });
    }

    public async Task<IActionResult> Details(int id)
    {
        var supplier = await _context.Suppliers
            .Include(s => s.SupplyOrders)
                .ThenInclude(o => o.Items)
                    .ThenInclude(i => i.Product)
            .Include(s => s.Payments)
            .FirstOrDefaultAsync(s => s.Id == id);

        if (supplier == null)
        {
            return NotFound("المورد غير موجود");
        }

        var totalSupplies = supplier.SupplyOrders.Sum(o => o.TotalAmount);
        var totalPaid = supplier.Payments.Sum(p => p.Amount);
        var balance = totalSupplies - totalPaid;

        ViewBag.TotalSupplies = totalSupplies;
        ViewBag.TotalPaid = totalPaid;
        ViewBag.CurrentBalance = balance;

        // Provide active brands and active products for the supply modal
        ViewBag.Brands = await _context.Brands.Where(b => b.IsActive).OrderBy(b => b.Name).ToListAsync();
        ViewBag.Products = await _context.Products
            .Where(p => p.IsActive && p.ProductType == ProductType.Device)
            .OrderBy(p => p.Name)
            .ToListAsync();

        return View(supplier);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Supply(
        int supplierId,
        string? supplyTypeRadio,
        int? productId,
        string? newDeviceName,
        int? brandId,
        string? storage,
        string? ram,
        string? color,
        string? imei,
        decimal costPrice,
        decimal sellingPrice,
        int quantity,
        decimal paidAmount,
        PaymentMethod paymentMethod,
        string? notes)
    {
        try
        {
            var supplier = await _context.Suppliers.FindAsync(supplierId);
            if (supplier == null) return NotFound("المورد غير موجود.");

            if (quantity <= 0)
            {
                TempData["Error"] = "يجب تحديد كمية توريد صحيحة أكبر من صفر.";
                return RedirectToAction(nameof(Details), new { id = supplierId });
            }

            if (costPrice <= 0)
            {
                TempData["Error"] = "يجب تحديد سعر شراء / تكلفة صحيح.";
                return RedirectToAction(nameof(Details), new { id = supplierId });
            }

            int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out int userId);
            Product product;

            // If user selected "new", ignore any productId
            if (supplyTypeRadio == "new")
            {
                productId = null;
            }

            // Either attach to existing device or create a new one
            if (productId.HasValue && productId.Value > 0)
            {
                var existing = await _context.Products.FindAsync(productId.Value);
                if (existing == null)
                {
                    TempData["Error"] = "الجهاز المحدد غير موجود.";
                    return RedirectToAction(nameof(Details), new { id = supplierId });
                }
                product = existing;
                int beforeStock = product.StockQuantity;
                product.StockQuantity += quantity;
                product.PurchasePrice = costPrice;
                if (sellingPrice > 0) product.SellingPrice = sellingPrice;

                _context.InventoryTransactions.Add(new InventoryTransaction
                {
                    ProductId = product.Id,
                    TransactionType = InventoryTransactionType.Purchase,
                    Quantity = quantity,
                    QuantityBefore = beforeStock,
                    QuantityAfter = product.StockQuantity,
                    UserId = userId > 0 ? userId : null,
                    Notes = $"توريد من المورد {supplier.Name}",
                    CreatedAt = DateTime.UtcNow
                });
            }
            else
            {
                if (string.IsNullOrWhiteSpace(newDeviceName))
                {
                    TempData["Error"] = "يرجى كتابة اسم الجهاز عند إنشاء جهاز جديد للتوريد.";
                    return RedirectToAction(nameof(Details), new { id = supplierId });
                }

                var defaultCat = await _context.ProductCategories.FirstOrDefaultAsync();

                product = new Product
                {
                    Name = newDeviceName.Trim(),
                    BrandId = (brandId.HasValue && brandId.Value > 0) ? brandId : null,
                    ProductCategoryId = defaultCat?.Id,
                    Storage = storage?.Trim(),
                    Ram = ram?.Trim(),
                    Color = color?.Trim(),
                    SerialNumberOrImei = imei?.Trim(),
                    PurchasePrice = costPrice,
                    SellingPrice = sellingPrice > 0 ? sellingPrice : Math.Round(costPrice * 1.15m, 2),
                    StockQuantity = quantity,
                    MinStockLevel = 1,
                    ProductType = ProductType.Device,
                    Barcode = DateTime.UtcNow.Ticks.ToString()[^12..],
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                };

                _context.Products.Add(product);
                await _context.SaveChangesAsync();

                _context.InventoryTransactions.Add(new InventoryTransaction
                {
                    ProductId = product.Id,
                    TransactionType = InventoryTransactionType.Purchase,
                    Quantity = quantity,
                    QuantityBefore = 0,
                    QuantityAfter = quantity,
                    UserId = userId > 0 ? userId : null,
                    Notes = $"توريد جهاز جديد من المورد {supplier.Name}",
                    CreatedAt = DateTime.UtcNow
                });
            }

            decimal totalAmount = costPrice * quantity;
            decimal actualPaid = Math.Clamp(paidAmount, 0, totalAmount);
            decimal remaining = totalAmount - actualPaid;

            var supplyOrder = new SupplyOrder
            {
                SupplierId = supplierId,
                OrderNumber = "ORD-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss"),
                UserId = userId > 0 ? userId : null,
                TotalAmount = totalAmount,
                PaidAmount = actualPaid,
                RemainingAmount = remaining,
                Notes = notes,
                CreatedAt = DateTime.UtcNow
            };

            _context.SupplyOrders.Add(supplyOrder);
            await _context.SaveChangesAsync();

            var orderItem = new SupplyOrderItem
            {
                SupplyOrderId = supplyOrder.Id,
                ProductId = product.Id,
                Quantity = quantity,
                CostPrice = costPrice,
                SellingPrice = product.SellingPrice,
                TotalPrice = totalAmount
            };
            _context.SupplyOrderItems.Add(orderItem);

            // If paid immediately, record payment
            if (actualPaid > 0)
            {
                var payment = new SupplierPayment
                {
                    SupplierId = supplierId,
                    SupplyOrderId = supplyOrder.Id,
                    Amount = actualPaid,
                    PaymentMethod = paymentMethod,
                    Notes = $"دفعة فورية لتوريد ({supplyOrder.OrderNumber})",
                    ReferenceNumber = supplyOrder.OrderNumber,
                    CreatedAt = DateTime.UtcNow
                };
                _context.SupplierPayments.Add(payment);
            }

            await _context.SaveChangesAsync();

            TempData["Success"] = $"تم تسجيل عملية التوريد بنجاح! تم إضافة {quantity} جهاز إلى المخزون وتحديث حساب المورد.";
            return RedirectToAction(nameof(Details), new { id = supplierId });
        }
        catch (Exception ex)
        {
            TempData["Error"] = "حدث خطأ أثناء حفظ التوريد: " + ex.Message;
            return RedirectToAction(nameof(Details), new { id = supplierId });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RecordPayment(
        int supplierId,
        decimal amount,
        PaymentMethod paymentMethod,
        string? referenceNumber,
        string? notes)
    {
        var supplier = await _context.Suppliers.FindAsync(supplierId);
        if (supplier == null) return NotFound("المورد غير موجود.");

        if (amount <= 0)
        {
            TempData["Error"] = "يجب تحديد مبلغ سداد صحيح أكبر من صفر.";
            return RedirectToAction(nameof(Details), new { id = supplierId });
        }

        var payment = new SupplierPayment
        {
            SupplierId = supplierId,
            Amount = amount,
            PaymentMethod = paymentMethod,
            ReferenceNumber = referenceNumber?.Trim(),
            Notes = notes?.Trim(),
            CreatedAt = DateTime.UtcNow
        };

        _context.SupplierPayments.Add(payment);
        await _context.SaveChangesAsync();

        TempData["Success"] = $"تم تسجيل سند صرف دفعة بقيمة {amount:N0} ج.م للمورد ({supplier.Name}) بنجاح.";
        return RedirectToAction(nameof(PaymentReceipt), new { id = payment.Id });
    }

    public async Task<IActionResult> PaymentReceipt(int id)
    {
        var payment = await _context.SupplierPayments
            .Include(p => p.Supplier)
            .Include(p => p.SupplyOrder)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (payment == null)
        {
            return NotFound("إيصال السداد غير موجود.");
        }

        var settings = await _context.Settings.ToDictionaryAsync(s => s.Key, s => s.Value);
        ViewBag.Settings = settings;

        return View(payment);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, Supplier input)
    {
        var supplier = await _context.Suppliers.FindAsync(id);
        if (supplier == null) return NotFound();

        supplier.Name = input.Name.Trim();
        supplier.CompanyName = input.CompanyName?.Trim();
        supplier.PhoneNumber = input.PhoneNumber.Trim();
        supplier.WhatsAppNumber = input.WhatsAppNumber?.Trim();
        supplier.Address = input.Address?.Trim();
        supplier.Notes = input.Notes?.Trim();

        await _context.SaveChangesAsync();
        TempData["Success"] = "تم تحديث بيانات المورد بنجاح.";
        return RedirectToAction(nameof(Details), new { id = supplier.Id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var supplier = await _context.Suppliers
            .Include(s => s.SupplyOrders)
                .ThenInclude(o => o.Items)
            .Include(s => s.Payments)
            .FirstOrDefaultAsync(s => s.Id == id);

        if (supplier == null)
        {
            TempData["Error"] = "المورد غير موجود.";
            return RedirectToAction(nameof(Index));
        }

        string name = supplier.Name;

        if (supplier.Payments.Any())
        {
            _context.SupplierPayments.RemoveRange(supplier.Payments);
        }

        if (supplier.SupplyOrders.Any())
        {
            foreach (var order in supplier.SupplyOrders)
            {
                if (order.Items.Any())
                {
                    _context.SupplyOrderItems.RemoveRange(order.Items);
                }
            }
            _context.SupplyOrders.RemoveRange(supplier.SupplyOrders);
        }

        _context.Suppliers.Remove(supplier);
        await _context.SaveChangesAsync();

        TempData["Success"] = $"تم حذف المورد ({name}) وكافة بيانات توريداته بنجاح.";
        return RedirectToAction(nameof(Index));
    }
}
