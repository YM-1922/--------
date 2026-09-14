using Doctor.Web.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Doctor.Web.Controllers;

[Authorize]
public class BarcodeController : Controller
{
    private readonly ApplicationDbContext _context;

    public BarcodeController(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> Index()
    {
        ViewBag.Products = await _context.Products
            .Where(p => p.IsActive)
            .OrderBy(p => p.Name)
            .ToListAsync();

        ViewBag.SpareParts = await _context.SpareParts
            .OrderBy(sp => sp.Name)
            .ToListAsync();

        return View();
    }

    public IActionResult PrintLabels(string barcode, string name, decimal price, int count = 1, string? model = null)
    {
        ViewBag.Barcode = barcode;
        ViewBag.Name = name;
        ViewBag.Price = price;
        ViewBag.Count = Math.Clamp(count, 1, 100);
        ViewBag.Model = model;

        return View();
    }
}
