using System.Security.Claims;
using Doctor.Web.Data;
using Doctor.Web.Models.Entities;
using Doctor.Web.Services;
using Doctor.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Doctor.Web.Controllers;

[Route("api")]
[ApiController]
[Authorize]
public class ApiController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly IPosService _posService;
    private readonly IRepairService _repairService;
    private readonly INotificationService _notificationService;

    public ApiController(
        ApplicationDbContext context,
        IPosService posService,
        IRepairService repairService,
        INotificationService notificationService)
    {
        _context = context;
        _posService = posService;
        _repairService = repairService;
        _notificationService = notificationService;
    }

    [HttpGet("products/search")]
    public async Task<IActionResult> SearchProducts([FromQuery] string? query)
    {
        var qable = _context.Products
            .Include(p => p.Brand)
            .Where(p => p.IsActive);

        if (!string.IsNullOrWhiteSpace(query))
        {
            string q = query.Trim();
            qable = qable.Where(p =>
                p.Name.Contains(q) ||
                p.Barcode.Contains(q) ||
                (p.SerialNumberOrImei != null && p.SerialNumberOrImei.Contains(q)) ||
                (p.Model != null && p.Model.Contains(q)) ||
                (p.Brand != null && p.Brand.Name.Contains(q)));
        }

        var products = await qable
            .OrderBy(p => p.Name)
            .Take(30)
            .Select(p => new
            {
                p.Id,
                p.Name,
                p.Barcode,
                p.SellingPrice,
                p.PurchasePrice,
                p.StockQuantity,
                p.SerialNumberOrImei,
                p.Model,
                p.Storage,
                p.Ram,
                p.Color,
                BrandName = p.Brand != null ? p.Brand.Name : "",
                ProductType = p.ProductType.ToString()
            })
            .ToListAsync();

        return Ok(products);
    }

    [HttpGet("products/by-barcode/{barcode}")]
    public async Task<IActionResult> GetProductByBarcode(string barcode)
    {
        if (string.IsNullOrWhiteSpace(barcode)) return NotFound();

        string b = barcode.Trim();
        var product = await _context.Products
            .Include(p => p.Brand)
            .FirstOrDefaultAsync(p => p.IsActive && p.Barcode == b);

        if (product == null)
        {
            return NotFound(new { message = $"لم يتم العثور على منتج بالباركود: {b}" });
        }

        return Ok(new
        {
            product.Id,
            product.Name,
            product.Barcode,
            product.SellingPrice,
            product.PurchasePrice,
            product.StockQuantity,
            product.SerialNumberOrImei,
            product.Model,
            product.Storage,
            product.Ram,
            product.Color,
            BrandName = product.Brand?.Name ?? ""
        });
    }

    [HttpGet("customers/search")]
    public async Task<IActionResult> SearchCustomers([FromQuery] string? query)
    {
        var baseQuery = _context.Customers
            .Include(c => c.Sales)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(query))
        {
            string q = query.Trim();
            baseQuery = baseQuery.Where(c => 
                c.Name.Contains(q) || 
                c.PhoneNumber.Contains(q) || 
                (c.WhatsAppNumber != null && c.WhatsAppNumber.Contains(q)));
        }

        var customers = await baseQuery
            .OrderByDescending(c => c.Sales.Any(s => s.RemainingAmount > 0))
            .ThenByDescending(c => c.CreatedAt)
            .Take(15)
            .Select(c => new
            {
                c.Id,
                c.Name,
                c.PhoneNumber,
                c.WhatsAppNumber,
                c.Address,
                c.Notes,
                TotalDebt = c.Sales.Where(s => s.Status == SaleStatus.Completed).Sum(s => s.RemainingAmount),
                SalesCount = c.Sales.Count(s => s.Status == SaleStatus.Completed)
            })
            .ToListAsync();

        return Ok(customers);
    }

    [HttpPost("customers/quick-create")]
    public async Task<IActionResult> QuickCreateCustomer([FromBody] QuickCreateCustomerDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
        {
            return BadRequest(new { success = false, message = "اسم العميل مطلوب." });
        }

        string cleanName = dto.Name.Trim();
        string cleanPhone = string.IsNullOrWhiteSpace(dto.PhoneNumber) ? "بدون هاتف" : dto.PhoneNumber.Trim();

        // Check if a customer already exists by phone (if real phone provided)
        if (cleanPhone != "بدون هاتف")
        {
            var existing = await _context.Customers
                .Include(c => c.Sales)
                .FirstOrDefaultAsync(c => c.PhoneNumber == cleanPhone);

            if (existing != null)
            {
                decimal existingDebt = existing.Sales
                    .Where(s => s.Status == SaleStatus.Completed)
                    .Sum(s => s.RemainingAmount);

                return Ok(new
                {
                    success = true,
                    isExisting = true,
                    message = $"العميل ({existing.Name}) مسجل بالفعل برقم الهاتف {cleanPhone}، تم تحديده تلقائياً.",
                    customer = new
                    {
                        existing.Id,
                        existing.Name,
                        existing.PhoneNumber,
                        existing.Address,
                        totalDebt = existingDebt
                    }
                });
            }
        }

        // Check if existing by name
        var existingByName = await _context.Customers
            .Include(c => c.Sales)
            .FirstOrDefaultAsync(c => c.Name.ToLower() == cleanName.ToLower());

        if (existingByName != null && cleanPhone == "بدون هاتف")
        {
            decimal existingDebt = existingByName.Sales
                .Where(s => s.Status == SaleStatus.Completed)
                .Sum(s => s.RemainingAmount);

            return Ok(new
            {
                success = true,
                isExisting = true,
                message = $"العميل ({existingByName.Name}) مسجل بالفعل في النظام، تم اختياره.",
                customer = new
                {
                    existingByName.Id,
                    existingByName.Name,
                    existingByName.PhoneNumber,
                    existingByName.Address,
                    totalDebt = existingDebt
                }
            });
        }

        var customer = new Customer
        {
            Name = cleanName,
            PhoneNumber = cleanPhone,
            WhatsAppNumber = !string.IsNullOrWhiteSpace(dto.WhatsAppNumber) ? dto.WhatsAppNumber.Trim() : (cleanPhone != "بدون هاتف" ? cleanPhone : null),
            Address = !string.IsNullOrWhiteSpace(dto.Address) ? dto.Address.Trim() : null,
            Notes = !string.IsNullOrWhiteSpace(dto.Notes) ? dto.Notes.Trim() : "تم تسجيله وحفظ بياناته عبر الكاشير (POS)",
            CreatedAt = DateTime.UtcNow
        };

        _context.Customers.Add(customer);
        await _context.SaveChangesAsync();

        return Ok(new
        {
            success = true,
            isExisting = false,
            message = $"تم تسجيل وحفظ العميل ({customer.Name}) بنجاح!",
            customer = new
            {
                customer.Id,
                customer.Name,
                customer.PhoneNumber,
                customer.Address,
                totalDebt = 0m
            }
        });
    }

    [HttpPost("pos/checkout")]
    public async Task<IActionResult> SubmitSale([FromBody] CreateSaleDto dto)
    {
        try
        {
            int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out int userId);
            var (success, message, sale) = await _posService.CreateSaleAsync(dto, userId);

            if (!success || sale == null)
            {
                return Ok(new { success = false, message });
            }

            return Ok(new
            {
                success = true,
                message,
                saleId = sale.Id,
                invoiceNumber = sale.InvoiceNumber,
                total = sale.TotalAmount,
                redirectUrl = Url.Action("Invoice", "Sales", new { id = sale.InvoiceNumber })
            });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = "حدث خطأ غير متوقع أثناء حفظ الفاتورة: " + ex.Message });
        }
    }

    [HttpPost("repairs/status")]
    public async Task<IActionResult> SubmitStatusChange([FromBody] UpdateRepairStatusDto dto)
    {
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out int userId);
        var (success, message, waUrl) = await _repairService.UpdateRepairStatusAsync(dto, userId);

        if (!success)
        {
            return BadRequest(new { success = false, message });
        }

        return Ok(new { success = true, message, whatsAppUrl = waUrl });
    }

    [HttpPost("repairs/add-part")]
    public async Task<IActionResult> AddRepairPart([FromBody] AddRepairPartDto dto)
    {
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out int userId);
        var (success, message) = await _repairService.AddRepairPartAsync(dto, userId);

        if (!success)
        {
            return BadRequest(new { success = false, message });
        }

        return Ok(new { success = true, message });
    }

    [HttpGet("notifications/unread")]
    public async Task<IActionResult> GetNotifications()
    {
        var list = await _notificationService.GetUnreadNotificationsAsync(10);
        int count = await _notificationService.GetUnreadCountAsync();

        return Ok(new
        {
            count,
            notifications = list.Select(n =>
            {
                string targetUrl = "/Admin";
                if (n.Type == NotificationType.NewRepair || n.Type == NotificationType.RepairReady || n.RelatedEntity == "RepairOrder")
                {
                    targetUrl = n.RelatedId.HasValue ? $"/Maintenance/PrintReceipt/{n.RelatedId.Value}" : "/Maintenance";
                }
                else if (n.Type == NotificationType.NewSale || n.RelatedEntity == "Sale")
                {
                    targetUrl = n.RelatedId.HasValue ? $"/Sales/Invoice/{n.RelatedId.Value}" : "/Sales";
                }
                else if (n.Type == NotificationType.LowStock || n.RelatedEntity == "Product")
                {
                    targetUrl = "/Sales/Products";
                }
                else if (n.Type == NotificationType.LowSparePart || n.RelatedEntity == "SparePart")
                {
                    targetUrl = "/Maintenance/SpareParts";
                }

                return new
                {
                    n.Id,
                    n.Title,
                    n.Message,
                    Type = n.Type.ToString(),
                    TargetUrl = targetUrl,
                    CreatedAt = n.CreatedAt.ToString("HH:mm - yyyy/MM/dd")
                };
            })
        });
    }

    [HttpPost("notifications/read/{id}")]
    public async Task<IActionResult> MarkNotificationRead(int id)
    {
        await _notificationService.MarkAsReadAsync(id);
        return Ok(new { success = true });
    }

    [HttpPost("notifications/read-all")]
    public async Task<IActionResult> MarkAllNotificationsRead()
    {
        await _notificationService.MarkAllAsReadAsync();
        return Ok(new { success = true });
    }

    [HttpGet("global-search")]
    public async Task<IActionResult> GlobalSearch([FromQuery] string query)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length < 2)
        {
            return Ok(new { products = new List<object>(), invoices = new List<object>(), customers = new List<object>() });
        }

        string q = query.Trim();

        var products = await _context.Products
            .Where(p => p.IsActive && (p.Name.Contains(q) || p.Barcode == q || (p.SerialNumberOrImei != null && p.SerialNumberOrImei.Contains(q))))
            .Take(6)
            .Select(p => new { p.Id, p.Name, p.Barcode, p.SellingPrice, p.StockQuantity, Type = p.ProductType.ToString() })
            .ToListAsync();

        var invoices = await _context.Sales
            .Include(s => s.Customer)
            .Where(s => s.InvoiceNumber.Contains(q) || (s.Customer != null && s.Customer.Name.Contains(q)))
            .Take(6)
            .Select(s => new {
                s.Id,
                s.InvoiceNumber,
                CustomerName = s.Customer != null ? s.Customer.Name : "عميل نقدي",
                Date = s.CreatedAt.ToString("yyyy-MM-dd"),
                s.TotalAmount
            })
            .ToListAsync();

        var customers = await _context.Customers
            .Where(c => c.Name.Contains(q) || c.PhoneNumber.Contains(q))
            .Take(6)
            .Select(c => new { c.Id, c.Name, c.PhoneNumber, c.Address })
            .ToListAsync();

        return Ok(new { products, invoices, customers });
    }

    #region POS Cashier Shift Management (حركة الدرج وبداية ونهاية اليوم)

    [HttpGet("pos/shift/status")]
    public async Task<IActionResult> GetShiftStatus()
    {
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out int userId);

        var shift = await _context.CashShifts
            .Where(cs => cs.Status == ShiftStatus.Open && (cs.UserId == userId || userId == 0))
            .OrderByDescending(cs => cs.StartTime)
            .FirstOrDefaultAsync();

        if (shift == null)
        {
            // Check any open shift in system
            shift = await _context.CashShifts
                .Where(cs => cs.Status == ShiftStatus.Open)
                .OrderByDescending(cs => cs.StartTime)
                .FirstOrDefaultAsync();
        }

        if (shift == null)
        {
            return Ok(new { hasOpenShift = false });
        }

        // Live calculations of sales since shift opened
        decimal cashSales = await _context.Payments
            .Where(p => p.PaymentMethod == PaymentMethod.Cash && p.CreatedAt >= shift.StartTime)
            .SumAsync(p => p.Amount);

        decimal visaSales = await _context.Payments
            .Where(p => p.PaymentMethod == PaymentMethod.Card && p.CreatedAt >= shift.StartTime)
            .SumAsync(p => p.Amount);

        decimal instaPaySales = await _context.Payments
            .Where(p => p.PaymentMethod == PaymentMethod.InstaPay && p.CreatedAt >= shift.StartTime)
            .SumAsync(p => p.Amount);

        decimal transferSales = await _context.Payments
            .Where(p => p.PaymentMethod == PaymentMethod.Transfer && p.CreatedAt >= shift.StartTime)
            .SumAsync(p => p.Amount);

        decimal otherSales = visaSales + instaPaySales + transferSales;

        decimal supplierCashOut = 0;
        try
        {
            supplierCashOut = await _context.SupplierPayments
                .Where(p => p.PaymentMethod == PaymentMethod.Cash && p.CreatedAt >= shift.StartTime)
                .SumAsync(p => p.Amount);
        }
        catch { }

        decimal expectedCash = shift.StartingCash + cashSales - supplierCashOut;

        return Ok(new
        {
            hasOpenShift = true,
            shiftId = shift.Id,
            startTime = shift.StartTime.ToString("yyyy-MM-dd HH:mm"),
            startingCash = shift.StartingCash,
            cashSales,
            visaSales,
            instaPaySales,
            otherSales,
            totalSales = cashSales + otherSales,
            supplierCashOut,
            expectedCash
        });
    }

    [HttpPost("pos/shift/open")]
    public async Task<IActionResult> OpenShift([FromBody] OpenShiftDto dto)
    {
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out int userId);
        if (userId == 0)
        {
            var firstUser = await _context.Users.FirstOrDefaultAsync();
            if (firstUser != null) userId = firstUser.Id;
        }

        // Check if there is already an open shift
        var existing = await _context.CashShifts
            .FirstOrDefaultAsync(cs => cs.Status == ShiftStatus.Open);

        if (existing != null)
        {
            return BadRequest(new { success = false, message = "يوجد بالفعل وردية مفتوحة في النظام!" });
        }

        var shift = new CashShift
        {
            UserId = userId,
            StartTime = DateTime.UtcNow,
            StartingCash = dto.StartingCash,
            Status = ShiftStatus.Open,
            Notes = dto.Notes?.Trim()
        };

        _context.CashShifts.Add(shift);
        await _context.SaveChangesAsync();

        return Ok(new
        {
            success = true,
            message = $"تم فتح وردية اليوم بنجاح برصيد افتتاحي {dto.StartingCash:N2} ج.م",
            shiftId = shift.Id
        });
    }

    [HttpPost("pos/shift/close")]
    public async Task<IActionResult> CloseShift([FromBody] CloseShiftDto dto)
    {
        var shift = await _context.CashShifts.FindAsync(dto.ShiftId);
        if (shift == null || shift.Status != ShiftStatus.Open)
        {
            return BadRequest(new { success = false, message = "الوردية غير موجودة أو مغلقة بالفعل." });
        }

        decimal cashSales = await _context.Payments
            .Where(p => p.PaymentMethod == PaymentMethod.Cash && p.CreatedAt >= shift.StartTime)
            .SumAsync(p => p.Amount);

        decimal visaSales = await _context.Payments
            .Where(p => p.PaymentMethod == PaymentMethod.Card && p.CreatedAt >= shift.StartTime)
            .SumAsync(p => p.Amount);

        decimal instaPaySales = await _context.Payments
            .Where(p => p.PaymentMethod == PaymentMethod.InstaPay && p.CreatedAt >= shift.StartTime)
            .SumAsync(p => p.Amount);

        decimal transferSales = await _context.Payments
            .Where(p => p.PaymentMethod == PaymentMethod.Transfer && p.CreatedAt >= shift.StartTime)
            .SumAsync(p => p.Amount);

        decimal otherSales = visaSales + instaPaySales + transferSales;

        decimal supplierCashOut = 0;
        try
        {
            supplierCashOut = await _context.SupplierPayments
                .Where(p => p.PaymentMethod == PaymentMethod.Cash && p.CreatedAt >= shift.StartTime)
                .SumAsync(p => p.Amount);
        }
        catch { }

        decimal expectedCash = shift.StartingCash + cashSales - supplierCashOut;
        decimal difference = dto.ActualCash - expectedCash;

        // Calculate estimated sales revenue and cost to calculate profit today
        var salesToday = await _context.Sales
            .Include(s => s.Items)
                .ThenInclude(i => i.Product)
            .Where(s => s.Status == SaleStatus.Completed && s.CreatedAt >= shift.StartTime)
            .ToListAsync();

        decimal totalRevenue = salesToday.Sum(s => s.TotalAmount);
        decimal totalCost = salesToday.SelectMany(s => s.Items).Sum(i => i.Quantity * i.Product.PurchasePrice);
        decimal estimatedProfit = totalRevenue - totalCost;

        shift.EndTime = DateTime.UtcNow;
        shift.ActualCash = dto.ActualCash;
        shift.TotalCashSales = cashSales;
        shift.TotalVisaSales = visaSales;
        shift.TotalInstaPaySales = instaPaySales;
        shift.TotalOtherSales = otherSales;
        shift.ExpectedCash = expectedCash;
        shift.Difference = difference;
        shift.Status = ShiftStatus.Closed;
        shift.Notes = dto.Notes?.Trim();

        await _context.SaveChangesAsync();

        string diffStatusAr = difference == 0 
            ? "مطابق تماماً بدون عجز أو زيادة" 
            : (difference < 0 ? $"عجز في الدرج بمقدار {Math.Abs(difference):N2} ج.م" : $"زيادة في الدرج بمقدار {difference:N2} ج.م");

        return Ok(new
        {
            success = true,
            message = $"تم تقفيل وردية اليوم بنجاح ({diffStatusAr})",
            summary = new
            {
                startingCash = shift.StartingCash,
                cashSales,
                visaSales,
                instaPaySales,
                otherSales,
                totalSales = totalRevenue,
                expectedCash,
                actualCash = dto.ActualCash,
                difference,
                estimatedProfit,
                diffStatusAr,
                isBalanced = difference == 0,
                isShortage = difference < 0,
                isSurplus = difference > 0,
                closedAt = shift.EndTime?.ToString("yyyy-MM-dd HH:mm")
            }
        });
    }

    #endregion
}
