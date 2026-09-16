using Doctor.Web.Data;
using Doctor.Web.Models.Entities;
using Doctor.Web.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace Doctor.Web.Services;

public interface IPosService
{
    Task<(bool Success, string Message, Sale? Sale)> CreateSaleAsync(CreateSaleDto dto, int userId);
    Task<Sale?> GetSaleByInvoiceNumberAsync(string invoiceNumber);
    Task<Sale?> GetSaleByIdAsync(int id);
    Task<(List<Sale> Items, int TotalCount)> GetSalesAsync(string? search = null, DateTime? from = null, DateTime? to = null, PaymentMethod? paymentMethod = null, int page = 1, int pageSize = 20);
    Task<string> GenerateInvoiceNumberAsync();
    Task<(bool Success, string Message)> ReturnSaleAsync(int saleId, int userId, string? reason = null);
    Task<(bool Success, string Message)> DeleteSaleAsync(int saleId, int userId, bool returnStock = true);
}

public class PosService : IPosService
{
    private readonly ApplicationDbContext _context;
    private readonly IInventoryService _inventoryService;
    private readonly IAuditLogService _auditLogService;
    private readonly INotificationService _notificationService;

    public PosService(
        ApplicationDbContext context,
        IInventoryService inventoryService,
        IAuditLogService auditLogService,
        INotificationService notificationService)
    {
        _context = context;
        _inventoryService = inventoryService;
        _auditLogService = auditLogService;
        _notificationService = notificationService;
    }

    public async Task<string> GenerateInvoiceNumberAsync()
    {
        string year = DateTime.UtcNow.Year.ToString();
        var lastSale = await _context.Sales
            .Where(s => s.InvoiceNumber.StartsWith($"INV-{year}-"))
            .OrderByDescending(s => s.Id)
            .FirstOrDefaultAsync();

        int nextSeq = 1;
        if (lastSale != null && !string.IsNullOrEmpty(lastSale.InvoiceNumber))
        {
            var parts = lastSale.InvoiceNumber.Split('-');
            if (parts.Length == 3 && int.TryParse(parts[2], out int parsedSeq))
            {
                nextSeq = parsedSeq + 1;
            }
        }

        return $"INV-{year}-{nextSeq:D5}";
    }

    public async Task<(bool Success, string Message, Sale? Sale)> CreateSaleAsync(CreateSaleDto dto, int userId)
    {
        if (dto.Items == null || !dto.Items.Any())
        {
            return (false, "سلة المشتريات فارغة! يرجى إضافة منتج واحد على الأقل.", null);
        }

        var user = await _context.Users.FindAsync(userId);
        if (user == null)
        {
            user = await _context.Users.FirstOrDefaultAsync(u => u.Status == UserStatus.Active);
        }

        if (user == null)
        {
            return (false, "لم يتم العثور على مستخدم صالح لإصدار الفاتورة.", null);
        }

        userId = user.Id;

        // Ensure products exist before proceeding
        foreach (var item in dto.Items)
        {
            var product = await _context.Products.FindAsync(item.ProductId);
            if (product == null)
            {
                return (false, $"المنتج #{item.ProductId} غير موجود في النظام.", null);
            }
        }

        // Handle Customer (find existing or create new on the fly)
        Customer? customer = null;
        if (dto.CustomerId.HasValue && dto.CustomerId.Value > 0)
        {
            customer = await _context.Customers.FindAsync(dto.CustomerId.Value);
        }

        if (customer == null && (!string.IsNullOrWhiteSpace(dto.CustomerName) || !string.IsNullOrWhiteSpace(dto.CustomerPhone)))
        {
            string cleanPhone = (dto.CustomerPhone ?? "").Trim();
            string cleanName = (dto.CustomerName ?? "").Trim();

            if (!string.IsNullOrEmpty(cleanPhone) && cleanPhone != "بدون هاتف")
            {
                customer = await _context.Customers.FirstOrDefaultAsync(c => c.PhoneNumber == cleanPhone);
            }

            if (customer == null && !string.IsNullOrEmpty(cleanName))
            {
                customer = await _context.Customers.FirstOrDefaultAsync(c => c.Name == cleanName || c.Name.ToLower() == cleanName.ToLower());
            }

            if (customer == null && !string.IsNullOrEmpty(cleanName))
            {
                customer = new Customer
                {
                    Name = cleanName,
                    PhoneNumber = (!string.IsNullOrEmpty(cleanPhone) && cleanPhone != "بدون هاتف") ? cleanPhone : "بدون هاتف",
                    WhatsAppNumber = (!string.IsNullOrEmpty(cleanPhone) && cleanPhone != "بدون هاتف") ? cleanPhone : null,
                    Notes = "تم تسجيله تلقائياً على الطاير عبر شاشة الكاشير (POS)",
                    CreatedAt = DateTime.UtcNow
                };
                _context.Customers.Add(customer);
                await _context.SaveChangesAsync();
            }
        }

        string invoiceNumber = await GenerateInvoiceNumberAsync();

        // Calculate totals accurately
        decimal subTotal = dto.Items.Sum(i => i.UnitPrice * i.Quantity);
        decimal totalDiscount = dto.DiscountAmount + dto.Items.Sum(i => i.Discount);
        decimal netTotal = Math.Max(0, subTotal - totalDiscount + dto.TaxAmount);
        
        // Accurate paid and remaining debt for wholesale/regular clients
        decimal paid = dto.PaidAmount;
        if (paid < 0) paid = 0;
        if (paid > netTotal) paid = netTotal;
        decimal remaining = Math.Max(0, netTotal - paid);

        var sale = new Sale
        {
            InvoiceNumber = invoiceNumber,
            CustomerId = customer?.Id,
            UserId = userId,
            SubTotal = subTotal,
            DiscountAmount = totalDiscount,
            TaxAmount = dto.TaxAmount,
            TotalAmount = netTotal,
            PaidAmount = paid,
            RemainingAmount = remaining,
            PaymentMethod = dto.PaymentMethod,
            PaymentReferenceNumber = dto.PaymentReferenceNumber?.Trim(),
            Status = SaleStatus.Completed,
            Notes = dto.Notes,
            CreatedAt = DateTime.Now
        };

        foreach (var item in dto.Items)
        {
            var product = await _context.Products.FindAsync(item.ProductId);
            sale.Items.Add(new SaleItem
            {
                ProductId = item.ProductId,
                Quantity = item.Quantity,
                UnitPrice = item.UnitPrice,
                DiscountAmount = item.Discount,
                TotalPrice = Math.Max(0, (item.UnitPrice * item.Quantity) - item.Discount),
                ImeiOrSerialSnapshot = !string.IsNullOrWhiteSpace(item.ImeiOrSerial) ? item.ImeiOrSerial.Trim() : product?.SerialNumberOrImei,
                ItemDetails = item.ItemDetails?.Trim()
            });
        }

        if (paid > 0)
        {
            string paymentNote = dto.PaymentMethod == PaymentMethod.InstaPay
                ? (!string.IsNullOrWhiteSpace(dto.PaymentReferenceNumber) ? $"سداد عبر انستاباي (InstaPay) - رقم التحويل: {dto.PaymentReferenceNumber.Trim()}" : "سداد عبر انستاباي (InstaPay)")
                : (dto.PaymentMethod == PaymentMethod.Card ? "سداد بالبطاقة / فيزا" : (remaining > 0 ? $"سداد دفعة أولى من الفاتورة (متبقي آجل: {remaining:N2} ج.م)" : "سداد كامل قيمة الفاتورة نقداً"));

            sale.Payments.Add(new Payment
            {
                Amount = paid,
                PaymentMethod = dto.PaymentMethod,
                ReferenceNumber = !string.IsNullOrWhiteSpace(dto.PaymentReferenceNumber) ? dto.PaymentReferenceNumber.Trim() : $"PAY-{DateTime.Now:yyyyMMdd}-{invoiceNumber}",
                Notes = paymentNote,
                CreatedAt = DateTime.Now
            });
        }

        _context.Sales.Add(sale);
        await _context.SaveChangesAsync();

        // Deduct inventory items & record transaction
        foreach (var item in dto.Items)
        {
            await _inventoryService.DeductProductStockAsync(
                item.ProductId,
                item.Quantity,
                userId,
                invoiceNumber,
                $"فاتورة مبيعات رقم {invoiceNumber}");
        }

        // Notification & Audit (Run safely so they never block the sale)
        try
        {
            await _notificationService.AddNotificationAsync(
                title: "فاتورة مبيعات جديدة",
                message: $"تم إصدار الفاتورة رقم {invoiceNumber} بقيمة {netTotal:N2} ج.م بواسطة {user.FullName}.",
                type: NotificationType.NewSale,
                relatedEntity: "Sale",
                relatedId: sale.Id);
        }
        catch { }

        try
        {
            await _auditLogService.LogAsync(
                userId: userId,
                username: user.Username,
                action: "CreateSale",
                entity: "Sale",
                entityId: sale.Id.ToString(),
                details: $"إصدار فاتورة {invoiceNumber} بقيمة إجمالية {netTotal:N2} ج.م",
                ipAddress: null);
        }
        catch { }

        return (true, "تم حفظ الفاتورة بنجاح!", sale);
    }

    public async Task<Sale?> GetSaleByInvoiceNumberAsync(string invoiceNumber)
    {
        return await _context.Sales
            .Include(s => s.Customer)
            .Include(s => s.User)
            .Include(s => s.Payments)
            .Include(s => s.Items)
                .ThenInclude(i => i.Product)
                    .ThenInclude(p => p.Brand)
            .FirstOrDefaultAsync(s => s.InvoiceNumber == invoiceNumber);
    }

    public async Task<Sale?> GetSaleByIdAsync(int id)
    {
        return await _context.Sales
            .Include(s => s.Customer)
            .Include(s => s.User)
            .Include(s => s.Payments)
            .Include(s => s.Items)
                .ThenInclude(i => i.Product)
                    .ThenInclude(p => p.Brand)
            .FirstOrDefaultAsync(s => s.Id == id);
    }

    public async Task<(List<Sale> Items, int TotalCount)> GetSalesAsync(string? search = null, DateTime? from = null, DateTime? to = null, PaymentMethod? paymentMethod = null, int page = 1, int pageSize = 20)
    {
        var query = _context.Sales
            .Include(s => s.Customer)
            .Include(s => s.User)
            .Include(s => s.Items)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            search = search.Trim();
            query = query.Where(s =>
                s.InvoiceNumber.Contains(search) ||
                (s.Customer != null && (s.Customer.Name.Contains(search) || s.Customer.PhoneNumber.Contains(search))) ||
                s.User.FullName.Contains(search));
        }

        if (from.HasValue)
        {
            var startOfFrom = from.Value.Date;
            query = query.Where(s => s.CreatedAt >= startOfFrom);
        }

        if (to.HasValue)
        {
            var endOfTo = to.Value.Date.AddDays(1).AddTicks(-1);
            query = query.Where(s => s.CreatedAt <= endOfTo);
        }

        if (paymentMethod.HasValue)
        {
            query = query.Where(s => s.PaymentMethod == paymentMethod.Value);
        }

        int totalCount = await query.CountAsync();

        var items = await query
            .OrderByDescending(s => s.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, totalCount);
    }

    public async Task<(bool Success, string Message)> ReturnSaleAsync(int saleId, int userId, string? reason = null)
    {
        var sale = await _context.Sales
            .Include(s => s.Items)
                .ThenInclude(i => i.Product)
            .Include(s => s.Customer)
            .Include(s => s.User)
            .FirstOrDefaultAsync(s => s.Id == saleId);

        if (sale == null)
        {
            return (false, "الفاتورة غير موجودة.");
        }

        if (sale.Status == SaleStatus.Refunded)
        {
            return (false, "تم إرجاع هذه الفاتورة بالفعل سابقاً.");
        }

        if (sale.Status == SaleStatus.Cancelled)
        {
            return (false, "هذه الفاتورة ملغاة بالفعل.");
        }

        var operatorUser = await _context.Users.FindAsync(userId);
        string operatorName = operatorUser?.FullName ?? "المستخدم";

        // Re-add items to stock with transaction type Return
        foreach (var item in sale.Items)
        {
            if (item.Product != null)
            {
                await _inventoryService.AddProductStockAsync(
                    item.ProductId,
                    item.Quantity,
                    userId,
                    sale.InvoiceNumber,
                    $"مرتجع فاتورة مبيعات رقم {sale.InvoiceNumber}" + (!string.IsNullOrWhiteSpace(reason) ? $" - سبب: {reason}" : ""),
                    InventoryTransactionType.Return);
            }
        }

        sale.Status = SaleStatus.Refunded;
        sale.RemainingAmount = 0; // Clear remaining debt for this returned sale
        string returnNote = $"[مرتجعة بالكامل بواسطة {operatorName} في {DateTime.UtcNow:yyyy/MM/dd HH:mm}]" +
                            (!string.IsNullOrWhiteSpace(reason) ? $" - السبب: {reason}" : "");
        sale.Notes = string.IsNullOrWhiteSpace(sale.Notes) ? returnNote : $"{sale.Notes}\n{returnNote}";

        await _context.SaveChangesAsync();

        await _auditLogService.LogAsync(
            userId: userId,
            username: operatorUser?.Username ?? "admin",
            action: "ReturnSale",
            entity: "Sale",
            entityId: sale.Id.ToString(),
            details: $"إرجاع الفاتورة {sale.InvoiceNumber} بقيمة {sale.TotalAmount:N2} ج.م وإعادة الأصناف إلى المخزن. السبب: {reason ?? "إرجاع جهاز"}",
            ipAddress: null);

        await _notificationService.AddNotificationAsync(
            title: "إرجاع فاتورة مبيعات",
            message: $"تم تسجيل إرجاع الفاتورة رقم {sale.InvoiceNumber} وإعادة المنتجات إلى المخزن.",
            type: NotificationType.System,
            relatedEntity: "Sale",
            relatedId: sale.Id);

        return (true, $"تم إرجاع الفاتورة رقم ({sale.InvoiceNumber}) بنجاح، وتمت إعادة الأجهزة/الأصناف إلى المخزن تلقائياً.");
    }

    public async Task<(bool Success, string Message)> DeleteSaleAsync(int saleId, int userId, bool returnStock = true)
    {
        var sale = await _context.Sales
            .Include(s => s.Items)
                .ThenInclude(i => i.Product)
            .Include(s => s.Payments)
            .FirstOrDefaultAsync(s => s.Id == saleId);

        if (sale == null)
        {
            return (false, "الفاتورة غير موجودة.");
        }

        var operatorUser = await _context.Users.FindAsync(userId);
        string invoiceNumber = sale.InvoiceNumber;

        // If requested and sale was Completed (not already Refunded), return stock
        if (returnStock && sale.Status == SaleStatus.Completed)
        {
            foreach (var item in sale.Items)
            {
                if (item.Product != null)
                {
                    await _inventoryService.AddProductStockAsync(
                        item.ProductId,
                        item.Quantity,
                        userId,
                        invoiceNumber,
                        $"إعادة صنف للمخزن عند حذف الفاتورة رقم {invoiceNumber}",
                        InventoryTransactionType.Return);
                }
            }
        }

        // Delete associated payments first
        if (sale.Payments.Any())
        {
            _context.Payments.RemoveRange(sale.Payments);
        }

        // Delete sale items
        if (sale.Items.Any())
        {
            _context.SaleItems.RemoveRange(sale.Items);
        }

        // Delete the sale itself
        _context.Sales.Remove(sale);
        await _context.SaveChangesAsync();

        await _auditLogService.LogAsync(
            userId: userId,
            username: operatorUser?.Username ?? "admin",
            action: "DeleteSale",
            entity: "Sale",
            entityId: saleId.ToString(),
            details: $"حذف الفاتورة {invoiceNumber} نهائياً" + (returnStock ? " مع إعادة الأجهزة والمخزون" : ""),
            ipAddress: null);

        return (true, $"تم حذف الفاتورة ({invoiceNumber}) نهائياً" + (returnStock ? " وإعادة الأصناف إلى المخزن بنجاح." : " بنجاح."));
    }
}
