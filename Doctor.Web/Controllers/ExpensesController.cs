using Doctor.Web.Data;
using Doctor.Web.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Doctor.Web.Controllers;

[Authorize]
public class ExpensesController : Controller
{
    private readonly ApplicationDbContext _context;

    public ExpensesController(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> Index(string? search, DateTime? fromDate, DateTime? toDate)
    {
        var query = _context.Expenses
            .Include(e => e.User)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            search = search.Trim();
            query = query.Where(e => e.Title.Contains(search) || (e.Notes != null && e.Notes.Contains(search)));
        }

        if (fromDate.HasValue)
        {
            var start = fromDate.Value.Date;
            query = query.Where(e => e.ExpenseDate >= start);
        }

        if (toDate.HasValue)
        {
            var end = toDate.Value.Date.AddDays(1).AddTicks(-1);
            query = query.Where(e => e.ExpenseDate <= end);
        }

        var expenses = await query.OrderByDescending(e => e.ExpenseDate).ToListAsync();

        ViewBag.Search = search;
        ViewBag.FromDate = fromDate?.ToString("yyyy-MM-dd");
        ViewBag.ToDate = toDate?.ToString("yyyy-MM-dd");

        ViewBag.PeriodTotal = expenses.Sum(e => e.Amount);
        ViewBag.AllTimeTotal = await _context.Expenses.SumAsync(e => (decimal?)e.Amount) ?? 0m;
        ViewBag.TotalCount = expenses.Count;

        return View(expenses);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Expense expense)
    {
        if (string.IsNullOrWhiteSpace(expense.Title))
        {
            TempData["Error"] = "يرجى كتابة عنوان المصروف أو الملاحظة.";
            return RedirectToAction(nameof(Index));
        }

        if (expense.Amount < 0)
        {
            TempData["Error"] = "قيمة المصروف يجب ألا تكون بالسالب.";
            return RedirectToAction(nameof(Index));
        }

        int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out int userId);
        if (userId > 0)
        {
            expense.UserId = userId;
        }

        // Set time: if only date was chosen or default, ensure it has local current time
        if (expense.ExpenseDate == default)
        {
            expense.ExpenseDate = DateTime.Now;
        }
        else if (expense.ExpenseDate.TimeOfDay == TimeSpan.Zero && expense.ExpenseDate.Date == DateTime.Today)
        {
            expense.ExpenseDate = DateTime.Now;
        }

        expense.CreatedAt = DateTime.Now;
        expense.Title = expense.Title.Trim();
        expense.Notes = expense.Notes?.Trim();

        _context.Expenses.Add(expense);
        await _context.SaveChangesAsync();

        TempData["Success"] = "تم إضافة المصروف / الملاحظة بنجاح.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, Expense input)
    {
        var expense = await _context.Expenses.FindAsync(id);
        if (expense == null)
        {
            TempData["Error"] = "المصروف المطلوب غير موجود.";
            return RedirectToAction(nameof(Index));
        }

        if (string.IsNullOrWhiteSpace(input.Title))
        {
            TempData["Error"] = "يرجى كتابة عنوان المصروف أو الملاحظة.";
            return RedirectToAction(nameof(Index));
        }

        if (input.Amount < 0)
        {
            TempData["Error"] = "قيمة المصروف يجب ألا تكون بالسالب.";
            return RedirectToAction(nameof(Index));
        }

        expense.Title = input.Title.Trim();
        expense.Notes = input.Notes?.Trim();
        expense.Amount = input.Amount;
        if (input.ExpenseDate != default)
        {
            expense.ExpenseDate = input.ExpenseDate;
        }

        await _context.SaveChangesAsync();

        TempData["Success"] = "تم تعديل بيانات المصروف / الملاحظة بنجاح.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var expense = await _context.Expenses.FindAsync(id);
        if (expense == null)
        {
            TempData["Error"] = "المصروف المطلوب غير موجود.";
            return RedirectToAction(nameof(Index));
        }

        _context.Expenses.Remove(expense);
        await _context.SaveChangesAsync();

        TempData["Success"] = "تم حذف المصروف بنجاح.";
        return RedirectToAction(nameof(Index));
    }
}
