using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Doctor.Web.Controllers
{
    [AllowAnonymous]
    public class MaintenanceController : Controller
    {
        public IActionResult Index() => RedirectToAction("Pos", "Sales");
        public IActionResult Orders() => RedirectToAction("Pos", "Sales");
        public IActionResult Details(int? id) => RedirectToAction("Pos", "Sales");
        public IActionResult Receive() => RedirectToAction("Pos", "Sales");
        public IActionResult SpareParts() => RedirectToAction("Products", "Sales");
        public IActionResult CreateSparePart() => RedirectToAction("Products", "Sales");
        public IActionResult PrintReceipt(int? id) => RedirectToAction("Pos", "Sales");
    }
}
