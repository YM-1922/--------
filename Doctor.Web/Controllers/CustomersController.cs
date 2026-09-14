using Doctor.Web.Data;
using Doctor.Web.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Doctor.Web.Controllers;

[Authorize]
public class CustomersController : Controller
{
    private readonly ApplicationDbContext _context;

    public CustomersController(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> Index(string? search, bool? onlyDebt)
    {
        var query = _context.Customers
            .Include(c => c.Sales)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            search = search.Trim();
            query = query.Where(c =>
                c.Name.Contains(search) ||
                c.PhoneNumber.Contains(search) ||
                (c.WhatsAppNumber != null && c.WhatsAppNumber.Contains(search)) ||
                (c.Notes != null && c.Notes.Contains(search)));
        }

        var customers = await query
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync();

        if (onlyDebt == true)
        {
            customers = customers.Where(c => c.Sales.Any(s => s.Status == SaleStatus.Completed && s.RemainingAmount > 0)).ToList();
        }

        ViewBag.Search = search;
        ViewBag.OnlyDebt = onlyDebt;
        ViewBag.TotalCustomersCount = await _context.Customers.CountAsync();

        var allSalesWithRemaining = await _context.Sales
            .Where(s => s.Status == SaleStatus.Completed && s.RemainingAmount > 0)
            .ToListAsync();

        ViewBag.TotalDebtAmount = allSalesWithRemaining.Sum(s => s.RemainingAmount);
        ViewBag.DebtCustomersCount = allSalesWithRemaining.Select(s => s.CustomerId).Where(id => id.HasValue).Distinct().Count();

        return View(customers);
    }

    public async Task<IActionResult> Details(int id)
    {
        var customer = await _context.Customers
            .Include(c => c.Sales)
                .ThenInclude(s => s.Items)
                    .ThenInclude(i => i.Product)
            .Include(c => c.Sales)
                .ThenInclude(s => s.Payments)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (customer == null)
        {
            return NotFound("العميل غير موجود");
        }

        return View(customer);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RecordPayment(int customerId, decimal amount, PaymentMethod paymentMethod, string? notes, string? referenceNumber)
    {
        if (amount <= 0)
        {
            TempData["Error"] = "يرجى إدخال مبلغ سداد صحيح أكبر من الصفر.";
            return RedirectToAction(nameof(Details), new { id = customerId });
        }

        var customer = await _context.Customers
            .Include(c => c.Sales.Where(s => s.Status == SaleStatus.Completed && s.RemainingAmount > 0))
                .ThenInclude(s => s.Payments)
            .FirstOrDefaultAsync(c => c.Id == customerId);

        if (customer == null)
        {
            return NotFound("العميل غير موجود");
        }

        decimal totalDebt = customer.Sales.Sum(s => s.RemainingAmount);
        if (totalDebt <= 0)
        {
            TempData["Warning"] = "حساب هذا العميل خالص بالفعل ولا توجد عليه أي مديونيات متبقية!";
            return RedirectToAction(nameof(Details), new { id = customerId });
        }

        decimal remainingPayment = amount;
        var unpaidSales = customer.Sales.OrderBy(s => s.CreatedAt).ToList();

        string refCode = !string.IsNullOrWhiteSpace(referenceNumber) 
            ? referenceNumber.Trim() 
            : $"REC-{DateTime.UtcNow:yyyyMMddHHmmss}";

        foreach (var sale in unpaidSales)
        {
            if (remainingPayment <= 0) break;

            decimal payForThisSale = Math.Min(sale.RemainingAmount, remainingPayment);
            sale.PaidAmount += payForThisSale;
            sale.RemainingAmount -= payForThisSale;
            remainingPayment -= payForThisSale;

            sale.Payments.Add(new Payment
            {
                Amount = payForThisSale,
                PaymentMethod = paymentMethod,
                ReferenceNumber = refCode,
                Notes = string.IsNullOrWhiteSpace(notes) 
                    ? $"سداد دفعة حساب نقدية على فاتورة ({sale.InvoiceNumber})" 
                    : notes.Trim(),
                CreatedAt = DateTime.UtcNow
            });
        }

        await _context.SaveChangesAsync();

        decimal settledAmount = amount - remainingPayment;
        TempData["Success"] = $"تم تسجيل دفعة سداد بمبلغ {settledAmount:N2} ج.م برقم سند ({refCode}) بنجاح وحفظها في سجل الحسابات.";
        return RedirectToAction(nameof(Details), new { id = customerId });
    }

    [HttpGet]
    public async Task<IActionResult> PaymentReceipt(int id)
    {
        var payment = await _context.Payments
            .Include(p => p.Sale)
                .ThenInclude(s => s!.Customer)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (payment == null)
        {
            return NotFound("سند السداد غير موجود");
        }

        decimal customerRemainingDebt = 0;
        int? custId = payment.Sale?.CustomerId;
        if (custId.HasValue)
        {
            customerRemainingDebt = await _context.Sales
                .Where(s => s.CustomerId == custId.Value && s.Status == SaleStatus.Completed)
                .SumAsync(s => s.RemainingAmount);
        }

        ViewBag.RemainingDebt = customerRemainingDebt;
        return View(payment);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Customer customer)
    {
        if (string.IsNullOrWhiteSpace(customer.Name))
        {
            TempData["Error"] = "اسم العميل حقل إلزامي.";
            return RedirectToAction(nameof(Index));
        }

        customer.PhoneNumber = string.IsNullOrWhiteSpace(customer.PhoneNumber) ? "بدون هاتف" : customer.PhoneNumber.Trim();
        if (customer.PhoneNumber != "بدون هاتف")
        {
            bool exists = await _context.Customers.AnyAsync(c => c.PhoneNumber == customer.PhoneNumber);
            if (exists)
            {
                TempData["Error"] = "رقم الهاتف مسجل بالفعل لعميل آخر.";
                return RedirectToAction(nameof(Index));
            }
        }

        customer.CreatedAt = DateTime.UtcNow;
        _context.Customers.Add(customer);
        await _context.SaveChangesAsync();

        TempData["Success"] = $"تمت إضافة العميل ({customer.Name}) بنجاح.";
        return RedirectToAction(nameof(Details), new { id = customer.Id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var customer = await _context.Customers
            .Include(c => c.Sales)
            .Include(c => c.RepairOrders)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (customer == null)
        {
            TempData["Error"] = "العميل غير موجود.";
            return RedirectToAction(nameof(Index));
        }

        foreach (var sale in customer.Sales)
        {
            sale.CustomerId = null;
        }

        foreach (var repair in customer.RepairOrders)
        {
            repair.CustomerId = null;
        }

        string customerName = customer.Name;
        _context.Customers.Remove(customer);
        await _context.SaveChangesAsync();

        TempData["Success"] = $"تم حذف العميل ({customerName}) بنجاح من قاعدة البيانات.";
        return RedirectToAction(nameof(Index));
    }
}
