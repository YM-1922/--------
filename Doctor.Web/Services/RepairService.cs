using Doctor.Web.Data;
using Doctor.Web.Models.Entities;
using Doctor.Web.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace Doctor.Web.Services;

public interface IRepairService
{
    Task<string> GenerateRepairCodeAsync();
    Task<(bool Success, string Message, RepairOrder? Repair)> CreateRepairOrderAsync(CreateRepairOrderDto dto, int userId);
    Task<(bool Success, string Message, string? WhatsAppUrl)> UpdateRepairStatusAsync(UpdateRepairStatusDto dto, int userId);
    Task<(bool Success, string Message)> AddRepairPartAsync(AddRepairPartDto dto, int userId);
    Task<(bool Success, string Message)> RemoveRepairPartAsync(int repairPartId, int userId);
    Task<(bool Success, string Message)> AddPaymentAsync(int repairOrderId, decimal amount, PaymentMethod method, string? notes, int userId);
    Task<RepairOrder?> GetRepairOrderDetailsAsync(int id);
    Task<RepairOrder?> GetRepairOrderByCodeAsync(string repairCode);
    Task<(List<RepairOrder> Items, int TotalCount)> GetRepairOrdersAsync(RepairStatus? status = null, string? search = null, int page = 1, int pageSize = 20);
    Task<Dictionary<RepairStatus, int>> GetRepairStatusCountsAsync();
}

public class RepairService : IRepairService
{
    private readonly ApplicationDbContext _context;
    private readonly IInventoryService _inventoryService;
    private readonly IWhatsAppService _whatsAppService;
    private readonly INotificationService _notificationService;
    private readonly IAuditLogService _auditLogService;

    public RepairService(
        ApplicationDbContext context,
        IInventoryService inventoryService,
        IWhatsAppService whatsAppService,
        INotificationService notificationService,
        IAuditLogService auditLogService)
    {
        _context = context;
        _inventoryService = inventoryService;
        _whatsAppService = whatsAppService;
        _notificationService = notificationService;
        _auditLogService = auditLogService;
    }

    public async Task<string> GenerateRepairCodeAsync()
    {
        string year = DateTime.UtcNow.Year.ToString();
        var lastOrder = await _context.RepairOrders
            .Where(r => r.RepairCode.StartsWith($"REP-{year}-"))
            .OrderByDescending(r => r.Id)
            .FirstOrDefaultAsync();

        int nextSeq = 1;
        if (lastOrder != null && !string.IsNullOrEmpty(lastOrder.RepairCode))
        {
            var parts = lastOrder.RepairCode.Split('-');
            if (parts.Length == 3 && int.TryParse(parts[2], out int parsedSeq))
            {
                nextSeq = parsedSeq + 1;
            }
        }

        return $"REP-{year}-{nextSeq:D5}";
    }

    public async Task<(bool Success, string Message, RepairOrder? Repair)> CreateRepairOrderAsync(CreateRepairOrderDto dto, int userId)
    {
        var user = await _context.Users.FindAsync(userId);
        if (user == null)
        {
            return (false, "المستخدم غير صالح.", null);
        }

        // Find or create customer
        Customer? customer = null;
        if (dto.CustomerId.HasValue && dto.CustomerId.Value > 0)
        {
            customer = await _context.Customers.FindAsync(dto.CustomerId.Value);
        }
        else
        {
            string cleanPhone = dto.CustomerPhone.Trim();
            customer = await _context.Customers.FirstOrDefaultAsync(c => c.PhoneNumber == cleanPhone);
            if (customer == null)
            {
                customer = new Customer
                {
                    Name = dto.CustomerName.Trim(),
                    PhoneNumber = cleanPhone,
                    WhatsAppNumber = !string.IsNullOrWhiteSpace(dto.CustomerWhatsApp) ? dto.CustomerWhatsApp.Trim() : cleanPhone,
                    Email = dto.CustomerEmail,
                    Address = dto.CustomerAddress,
                    CreatedAt = DateTime.UtcNow
                };
                _context.Customers.Add(customer);
                await _context.SaveChangesAsync();
            }
        }

        if (customer == null)
        {
            return (false, "بيانات العميل غير مكتملة أو تعذر تسجيل العميل.", null);
        }

        string repairCode = await GenerateRepairCodeAsync();
        decimal total = dto.EstimatedCost;
        decimal paid = dto.PaidAmount;
        decimal remaining = Math.Max(0, total - paid);

        var order = new RepairOrder
        {
            RepairCode = repairCode,
            CustomerId = customer.Id,
            DeviceBrand = dto.DeviceBrand.Trim(),
            DeviceModel = dto.DeviceModel.Trim(),
            ImeiOrSerial = dto.ImeiOrSerial?.Trim(),
            DeviceColor = dto.DeviceColor?.Trim(),
            DeviceCondition = dto.DeviceCondition?.Trim(),
            ProblemDescription = dto.ProblemDescription.Trim(),
            ReceivedAccessories = dto.ReceivedAccessories?.Trim(),
            PhotoPathsJson = dto.PhotoPathsJson,
            Status = RepairStatus.Received,
            PartsCost = 0m,
            LaborCost = dto.EstimatedCost,
            DiscountAmount = 0m,
            TotalCost = total,
            PaidAmount = paid,
            RemainingAmount = remaining,
            ReceivedAt = DateTime.UtcNow,
            EstimatedDeliveryDate = dto.EstimatedDeliveryDate,
            CreatedByUserId = userId,
            TechnicianUserId = dto.TechnicianUserId,
            TechnicianNotes = dto.TechnicianNotes
        };

        // Add initial history
        order.StatusHistories.Add(new RepairStatusHistory
        {
            OldStatus = RepairStatus.Received,
            NewStatus = RepairStatus.Received,
            ChangedByUserId = userId,
            Notes = paid > 0 ? $"تم استلام الجهاز وسداد دفعة مقدمة قدرها {paid:N2} ج.م" : "تم استلام الجهاز من العميل",
            ChangedAt = DateTime.UtcNow
        });

        if (paid > 0)
        {
            order.Payments.Add(new Payment
            {
                Amount = paid,
                PaymentMethod = PaymentMethod.Cash,
                Notes = "عربون استلام الصيانة",
                CreatedAt = DateTime.UtcNow
            });
        }

        _context.RepairOrders.Add(order);
        await _context.SaveChangesAsync();

        // System Notification
        await _notificationService.AddNotificationAsync(
            title: "استلام جهاز صيانة جديد",
            message: $"تم تسجيل استلام جهاز {order.DeviceBrand} {order.DeviceModel} برقم {order.RepairCode} للعميل {customer.Name}.",
            type: NotificationType.NewRepair,
            relatedEntity: "RepairOrder",
            relatedId: order.Id);

        // Audit Log
        await _auditLogService.LogAsync(
            userId: userId,
            username: user.Username,
            action: "CreateRepairOrder",
            entity: "RepairOrder",
            entityId: order.Id.ToString(),
            details: $"تسجيل استلام جهاز {order.RepairCode} ({order.DeviceBrand} {order.DeviceModel})",
            ipAddress: null);

        return (true, "تم تسجيل أمر الصيانة وتوليد الكود بنجاح!", order);
    }

    public async Task<(bool Success, string Message, string? WhatsAppUrl)> UpdateRepairStatusAsync(UpdateRepairStatusDto dto, int userId)
    {
        var order = await _context.RepairOrders
            .Include(r => r.Customer)
            .Include(r => r.RepairParts)
            .FirstOrDefaultAsync(r => r.Id == dto.RepairOrderId);

        if (order == null)
        {
            return (false, "أمر الصيانة غير موجود.", null);
        }

        var user = await _context.Users.FindAsync(userId);
        var oldStatus = order.Status;
        order.Status = dto.NewStatus;

        if (dto.AdditionalLaborCost.HasValue)
        {
            order.LaborCost = dto.AdditionalLaborCost.Value;
        }

        if (dto.AdditionalDiscount.HasValue)
        {
            order.DiscountAmount = dto.AdditionalDiscount.Value;
        }

        if (!string.IsNullOrWhiteSpace(dto.Diagnosis))
        {
            order.Diagnosis = dto.Diagnosis;
        }

        if (!string.IsNullOrWhiteSpace(dto.TechnicianNotes))
        {
            order.TechnicianNotes = dto.TechnicianNotes;
        }

        // Recalculate Total
        order.TotalCost = Math.Max(0, (order.PartsCost + order.LaborCost) - order.DiscountAmount);
        order.RemainingAmount = Math.Max(0, order.TotalCost - order.PaidAmount);

        if (dto.NewStatus == RepairStatus.ReadyForPickup && !order.CompletedAt.HasValue)
        {
            order.CompletedAt = DateTime.UtcNow;
        }
        else if (dto.NewStatus == RepairStatus.Delivered)
        {
            order.DeliveredAt = DateTime.UtcNow;
        }

        // Add history log
        order.StatusHistories.Add(new RepairStatusHistory
        {
            OldStatus = oldStatus,
            NewStatus = dto.NewStatus,
            ChangedByUserId = userId,
            Notes = dto.Notes ?? GetDefaultStatusChangeNote(oldStatus, dto.NewStatus),
            ChangedAt = DateTime.UtcNow
        });

        await _context.SaveChangesAsync();

        string? whatsAppUrl = null;

        // Trigger WhatsApp Notification if ready for pickup
        if (dto.NewStatus == RepairStatus.ReadyForPickup && dto.SendWhatsApp)
        {
            var waResult = await _whatsAppService.SendRepairReadyNotificationAsync(order.Id);
            whatsAppUrl = waResult.WhatsAppUrl;

            await _notificationService.AddNotificationAsync(
                title: "جهاز صيانة جاهز للاستلام",
                message: $"الجهاز {order.DeviceBrand} {order.DeviceModel} (رقم {order.RepairCode}) جاهز للاستلام. العميل: {order.Customer.Name}.",
                type: NotificationType.RepairReady,
                relatedEntity: "RepairOrder",
                relatedId: order.Id);
        }

        await _auditLogService.LogAsync(
            userId: userId,
            username: user?.Username ?? "Tech",
            action: "UpdateRepairStatus",
            entity: "RepairOrder",
            entityId: order.Id.ToString(),
            details: $"تغيير حالة الصيانة {order.RepairCode} من {oldStatus} إلى {dto.NewStatus}",
            ipAddress: null);

        return (true, "تم تحديث حالة الصيانة بنجاح!", whatsAppUrl);
    }

    public async Task<(bool Success, string Message)> AddRepairPartAsync(AddRepairPartDto dto, int userId)
    {
        var order = await _context.RepairOrders
            .Include(r => r.RepairParts)
            .FirstOrDefaultAsync(r => r.Id == dto.RepairOrderId);

        if (order == null)
        {
            return (false, "أمر الصيانة غير موجود.");
        }

        var part = await _context.SpareParts.FindAsync(dto.SparePartId);
        if (part == null)
        {
            return (false, "قطعة الغيار غير موجودة.");
        }

        if (part.StockQuantity < dto.Quantity)
        {
            return (false, $"الرصيد المتاح من قطعة الغيار ({part.Name}) لا يكفي! المتاح حالياً: {part.StockQuantity}.");
        }

        decimal price = dto.UnitPrice > 0 ? dto.UnitPrice : part.SellingPrice;
        decimal totalPrice = price * dto.Quantity;

        var repairPart = new RepairPart
        {
            RepairOrderId = dto.RepairOrderId,
            SparePartId = dto.SparePartId,
            Quantity = dto.Quantity,
            UnitPrice = price,
            TotalPrice = totalPrice,
            Notes = dto.Notes
        };

        order.RepairParts.Add(repairPart);

        // Deduct from spare part stock
        await _inventoryService.DeductSparePartStockAsync(
            dto.SparePartId,
            dto.Quantity,
            userId,
            order.RepairCode,
            $"استهلاك في صيانة جهاز {order.RepairCode}");

        // Update order costs
        order.PartsCost = order.RepairParts.Sum(p => p.TotalPrice);
        order.TotalCost = Math.Max(0, (order.PartsCost + order.LaborCost) - order.DiscountAmount);
        order.RemainingAmount = Math.Max(0, order.TotalCost - order.PaidAmount);

        await _context.SaveChangesAsync();

        return (true, $"تمت إضافة قطعة الغيار ({part.Name}) وخصمها من المخزون بنجاح.");
    }

    public async Task<(bool Success, string Message)> RemoveRepairPartAsync(int repairPartId, int userId)
    {
        var repairPart = await _context.RepairParts
            .Include(rp => rp.RepairOrder)
            .Include(rp => rp.SparePart)
            .FirstOrDefaultAsync(rp => rp.Id == repairPartId);

        if (repairPart == null)
        {
            return (false, "قطعة الغيار غير موجودة في أمر الصيانة.");
        }

        var order = repairPart.RepairOrder;

        // Return stock
        await _inventoryService.AddSparePartStockAsync(
            repairPart.SparePartId,
            repairPart.Quantity,
            userId,
            order.RepairCode,
            $"إلغاء تركيب قطعة في صيانة {order.RepairCode}",
            InventoryTransactionType.Return);

        _context.RepairParts.Remove(repairPart);
        await _context.SaveChangesAsync();

        // Update costs
        order.PartsCost = await _context.RepairParts.Where(rp => rp.RepairOrderId == order.Id).SumAsync(rp => rp.TotalPrice);
        order.TotalCost = Math.Max(0, (order.PartsCost + order.LaborCost) - order.DiscountAmount);
        order.RemainingAmount = Math.Max(0, order.TotalCost - order.PaidAmount);

        await _context.SaveChangesAsync();

        return (true, "تم حذف قطعة الغيار واسترجاع كميتها إلى المخزن.");
    }

    public async Task<(bool Success, string Message)> AddPaymentAsync(int repairOrderId, decimal amount, PaymentMethod method, string? notes, int userId)
    {
        var order = await _context.RepairOrders.FindAsync(repairOrderId);
        if (order == null)
        {
            return (false, "أمر الصيانة غير موجود.");
        }

        if (amount <= 0)
        {
            return (false, "المبلغ المدفوع يجب أن يكون أكبر من الصفر.");
        }

        var payment = new Payment
        {
            RepairOrderId = repairOrderId,
            Amount = amount,
            PaymentMethod = method,
            Notes = notes ?? "دفعة تحت حساب الصيانة",
            CreatedAt = DateTime.UtcNow
        };

        order.Payments.Add(payment);
        order.PaidAmount += amount;
        order.RemainingAmount = Math.Max(0, order.TotalCost - order.PaidAmount);

        await _context.SaveChangesAsync();

        return (true, $"تم تسجيل سداد بمبلغ {amount:N2} ج.م بنجاح.");
    }

    public async Task<RepairOrder?> GetRepairOrderDetailsAsync(int id)
    {
        return await _context.RepairOrders
            .Include(r => r.Customer)
            .Include(r => r.CreatedByUser)
            .Include(r => r.TechnicianUser)
            .Include(r => r.Payments)
            .Include(r => r.StatusHistories)
                .ThenInclude(h => h.ChangedByUser)
            .Include(r => r.RepairParts)
                .ThenInclude(rp => rp.SparePart)
                    .ThenInclude(sp => sp.Brand)
            .FirstOrDefaultAsync(r => r.Id == id);
    }

    public async Task<RepairOrder?> GetRepairOrderByCodeAsync(string repairCode)
    {
        return await _context.RepairOrders
            .Include(r => r.Customer)
            .Include(r => r.CreatedByUser)
            .Include(r => r.TechnicianUser)
            .Include(r => r.Payments)
            .Include(r => r.StatusHistories)
                .ThenInclude(h => h.ChangedByUser)
            .Include(r => r.RepairParts)
                .ThenInclude(rp => rp.SparePart)
            .FirstOrDefaultAsync(r => r.RepairCode == repairCode);
    }

    public async Task<(List<RepairOrder> Items, int TotalCount)> GetRepairOrdersAsync(RepairStatus? status = null, string? search = null, int page = 1, int pageSize = 20)
    {
        var query = _context.RepairOrders
            .Include(r => r.Customer)
            .Include(r => r.TechnicianUser)
            .AsQueryable();

        if (status.HasValue)
        {
            query = query.Where(r => r.Status == status.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            search = search.Trim();
            query = query.Where(r =>
                r.RepairCode.Contains(search) ||
                r.DeviceModel.Contains(search) ||
                r.DeviceBrand.Contains(search) ||
                (r.ImeiOrSerial != null && r.ImeiOrSerial.Contains(search)) ||
                r.Customer.Name.Contains(search) ||
                r.Customer.PhoneNumber.Contains(search));
        }

        int total = await query.CountAsync();

        var items = await query
            .OrderByDescending(r => r.ReceivedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, total);
    }

    public async Task<Dictionary<RepairStatus, int>> GetRepairStatusCountsAsync()
    {
        var list = await _context.RepairOrders
            .GroupBy(r => r.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync();

        var result = Enum.GetValues<RepairStatus>()
            .ToDictionary(s => s, s => 0);

        foreach (var item in list)
        {
            result[item.Status] = item.Count;
        }

        return result;
    }

    private static string GetDefaultStatusChangeNote(RepairStatus oldStatus, RepairStatus newStatus)
    {
        return newStatus switch
        {
            RepairStatus.Received => "تم استلام الجهاز في مركز الصيانة",
            RepairStatus.UnderInspection => "تم بدء الفحص الفني وتشخيص العطل",
            RepairStatus.PendingCustomerApproval => "تم التواصل مع العميل لطلب الموافقة على تكلفة الإصلاح",
            RepairStatus.InProgress => "موافقة العميل تمت، وبدء عملية الإصلاح والفك والتركيب",
            RepairStatus.WaitingForParts => "في انتظار توريد أو توفير قطعة غيار مطلوبة",
            RepairStatus.ReadyForPickup => "اكتمل الإصلاح بنجاح، الجهاز جاهز للاستلام",
            RepairStatus.Delivered => "تم تسليم الجهاز للعميل وتحصيل التكلفة المتبقية",
            RepairStatus.Cancelled => "تم إلغاء عملية الصيانة وإعادة الجهاز للعميل",
            _ => $"تغيير الحالة من {oldStatus} إلى {newStatus}"
        };
    }
}
