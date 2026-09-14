using System.Security.Claims;
using Doctor.Web.Data;
using Doctor.Web.Models.Entities;
using Doctor.Web.Services;
using Doctor.Web.Services.Security;
using Doctor.Web.ViewModels;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Doctor.Web.Controllers;

[Authorize(Roles = "Admin")]
public class AdminController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IReportService _reportService;
    private readonly IAuditLogService _auditLogService;
    private readonly IWhatsAppService _whatsAppService;
    private readonly IAuthService _authService;

    public AdminController(
        ApplicationDbContext context,
        IReportService reportService,
        IAuditLogService auditLogService,
        IWhatsAppService whatsAppService,
        IAuthService authService)
    {
        _context = context;
        _reportService = reportService;
        _auditLogService = auditLogService;
        _whatsAppService = whatsAppService;
        _authService = authService;
    }

    public async Task<IActionResult> Index()
    {
        var stats = await _reportService.GetDashboardStatsAsync();
        var adminIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (int.TryParse(adminIdStr, out int adminId))
        {
            var admin = await _context.Users.FindAsync(adminId);
            ViewBag.AdminEmail = admin?.Email;
            ViewBag.AdminUsername = admin?.Username;
            ViewBag.AdminFullName = admin?.FullName;
        }

        // Real recent sales from database
        ViewBag.RecentSales = await _context.Sales
            .Include(s => s.Customer)
            .OrderByDescending(s => s.CreatedAt)
            .Take(6)
            .ToListAsync();

        // Real low stock alerts (Devices only)
        ViewBag.LowStockAlerts = await _context.Products
            .Include(p => p.Brand)
            .Where(p => p.IsActive && p.StockQuantity <= p.MinStockLevel)
            .OrderBy(p => p.StockQuantity)
            .Take(5)
            .ToListAsync();

        var allOrdersSum = await _context.SupplyOrders.SumAsync(o => (decimal?)o.TotalAmount) ?? 0m;
        var allPaymentsSum = await _context.SupplierPayments.SumAsync(p => (decimal?)p.Amount) ?? 0m;
        ViewBag.SuppliersCount = await _context.Suppliers.CountAsync();
        ViewBag.SuppliersDebt = allOrdersSum - allPaymentsSum;

        return View(stats);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> QuickCreateStaff(string accountType, string fullName, string username, string password, string? phone)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(fullName) || string.IsNullOrWhiteSpace(password))
        {
            TempData["Error"] = "يرجى تعبئة كافة الحقول المطلوبة لإنشاء الحساب (الاسم الكامل، اسم المستخدم، وكلمة المرور).";
            return RedirectToAction(nameof(Index));
        }

        var cleanUsername = username.Trim().ToLower();
        bool exists = await _context.Users.AnyAsync(u => u.Username.ToLower() == cleanUsername);
        if (exists)
        {
            TempData["Error"] = $"اسم المستخدم ({username}) مسجل بالفعل، يرجى اختيار اسم آخر.";
            return RedirectToAction(nameof(Index));
        }

        string roleName = accountType == "Maintenance" ? "Maintenance" : "Sales";
        var role = await _context.UserRoles.FirstOrDefaultAsync(r => r.Name == roleName);
        if (role == null)
        {
            TempData["Error"] = "الدور المحدد غير موجود بالنظام.";
            return RedirectToAction(nameof(Index));
        }

        var newUser = new User
        {
            Username = cleanUsername,
            FullName = fullName.Trim(),
            PhoneNumber = phone?.Trim(),
            PasswordHash = PasswordHasher.HashPassword(password),
            RoleId = role.Id,
            Status = UserStatus.Active,
            CreatedAt = DateTime.UtcNow
        };

        _context.Users.Add(newUser);
        await _context.SaveChangesAsync();

        int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out int adminId);
        await _auditLogService.LogAsync(
            adminId,
            User.Identity?.Name ?? "admin",
            "QuickCreateStaff",
            "User",
            newUser.Id.ToString(),
            $"إنشاء حساب سريع: {newUser.Username} ({newUser.FullName}) كـ {role.NameAr}",
            HttpContext.Connection.RemoteIpAddress?.ToString());

        TempData["Success"] = $"تم بنجاح إنشاء حساب ({role.NameAr}: {newUser.FullName})! يمكنه الآن تسجيل الدخول فوراً باسم: {newUser.Username}";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateAdminProfile(string fullName, string username, string? email, string? currentPassword, string? newPassword)
    {
        var adminIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(adminIdStr, out int adminId))
        {
            return RedirectToAction("Login", "Account");
        }

        var admin = await _context.Users.Include(u => u.Role).FirstOrDefaultAsync(u => u.Id == adminId);
        if (admin == null)
        {
            TempData["Error"] = "حساب المدير غير موجود.";
            return RedirectToAction(nameof(Index));
        }

        if (string.IsNullOrWhiteSpace(fullName) || string.IsNullOrWhiteSpace(username))
        {
            TempData["Error"] = "الاسم الكامل واسم المستخدم حقول إلزامية.";
            return RedirectToAction(nameof(Index));
        }

        var cleanUsername = username.Trim().ToLower();
        bool exists = await _context.Users.AnyAsync(u => u.Id != adminId && u.Username.ToLower() == cleanUsername);
        if (exists)
        {
            TempData["Error"] = $"اسم المستخدم ({username}) محجوز لحساب آخر.";
            return RedirectToAction(nameof(Index));
        }

        // If new password provided, check current password
        if (!string.IsNullOrWhiteSpace(newPassword))
        {
            if (string.IsNullOrWhiteSpace(currentPassword) || !PasswordHasher.VerifyPassword(admin.PasswordHash, currentPassword))
            {
                TempData["Error"] = "كلمة المرور الحالية غير صحيحة! لم يتم تغيير كلمة المرور.";
                return RedirectToAction(nameof(Index));
            }

            admin.PasswordHash = PasswordHasher.HashPassword(newPassword);
        }

        admin.FullName = fullName.Trim();
        admin.Username = cleanUsername;
        admin.Email = email?.Trim();

        await _context.SaveChangesAsync();

        // Refresh Claims
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, admin.Id.ToString()),
            new Claim(ClaimTypes.Name, admin.Username),
            new Claim("FullName", admin.FullName),
            new Claim(ClaimTypes.Role, admin.Role.Name),
            new Claim("RoleAr", admin.Role.NameAr)
        };

        var permissions = await _authService.GetUserPermissionCodesAsync(admin.Id);
        foreach (var perm in permissions)
        {
            claims.Add(new Claim("Permission", perm));
        }

        var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(claimsIdentity));

        await _auditLogService.LogAsync(
            admin.Id,
            admin.Username,
            "UpdateAdminProfile",
            "User",
            admin.Id.ToString(),
            $"تحديث بيانات حساب المدير العام: {admin.Username}",
            HttpContext.Connection.RemoteIpAddress?.ToString());

        TempData["Success"] = "تم تحديث بيانات حساب المدير العام وكلمة المرور بنجاح.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetSales()
    {
        var adminIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(adminIdStr, out int adminId))
        {
            return RedirectToAction("Login", "Account");
        }

        // Delete all SaleItems
        _context.SaleItems.RemoveRange(_context.SaleItems);

        // Delete all payments associated with sales
        var salesPayments = await _context.Payments.Where(p => p.SaleId != null).ToListAsync();
        _context.Payments.RemoveRange(salesPayments);

        // Delete all Sales
        _context.Sales.RemoveRange(_context.Sales);

        await _context.SaveChangesAsync();

        await _auditLogService.LogAsync(
            adminId,
            User.Identity?.Name ?? "admin",
            "ResetSales",
            "Sale",
            "All",
            "تصفير كافة سجلات وفواتير المبيعات بالكامل",
            HttpContext.Connection.RemoteIpAddress?.ToString());

        TempData["Success"] = "تم تصفير مبيعات وفواتير السنتر بنجاح (0 جنيه).";
        return RedirectToAction(nameof(Index));
    }


    #region User Management

    public async Task<IActionResult> Users()
    {
        var users = await _context.Users
            .Include(u => u.Role)
            .OrderBy(u => u.Id)
            .ToListAsync();

        ViewBag.Roles = await _context.UserRoles.ToListAsync();
        return View(users);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateUser(string username, string fullName, string? email, string? phoneNumber, string password, int roleId)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(fullName) || string.IsNullOrWhiteSpace(password))
        {
            TempData["Error"] = "يرجى تعبئة كافة الحقول الإلزامية (اسم المستخدم، الاسم الكامل، وكلمة المرور).";
            return RedirectToAction(nameof(Users));
        }

        bool exists = await _context.Users.AnyAsync(u => u.Username.ToLower() == username.Trim().ToLower());
        if (exists)
        {
            TempData["Error"] = "اسم المستخدم هذا مستخدم بالفعل، يرجى اختيار اسم آخر.";
            return RedirectToAction(nameof(Users));
        }

        var user = new User
        {
            Username = username.Trim().ToLower(),
            FullName = fullName.Trim(),
            Email = email?.Trim(),
            PhoneNumber = phoneNumber?.Trim(),
            PasswordHash = PasswordHasher.HashPassword(password),
            RoleId = roleId,
            Status = UserStatus.Active,
            CreatedAt = DateTime.UtcNow
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        await _auditLogService.LogAsync(
            int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value),
            User.Identity!.Name!,
            "CreateUser",
            "User",
            user.Id.ToString(),
            $"إنشاء مستخدم جديد: {user.Username} ({user.FullName})",
            HttpContext.Connection.RemoteIpAddress?.ToString());

        TempData["Success"] = $"تم إنشاء حساب المستخدم ({user.FullName}) بنجاح.";
        return RedirectToAction(nameof(Users));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditUser(int id, string fullName, string? email, string? phoneNumber, int roleId, string? newPassword)
    {
        var user = await _context.Users.FindAsync(id);
        if (user == null)
        {
            TempData["Error"] = "المستخدم غير موجود.";
            return RedirectToAction(nameof(Users));
        }

        user.FullName = fullName.Trim();
        user.Email = email?.Trim();
        user.PhoneNumber = phoneNumber?.Trim();
        user.RoleId = roleId;

        if (!string.IsNullOrWhiteSpace(newPassword))
        {
            user.PasswordHash = PasswordHasher.HashPassword(newPassword);
        }

        await _context.SaveChangesAsync();

        await _auditLogService.LogAsync(
            int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value),
            User.Identity!.Name!,
            "EditUser",
            "User",
            user.Id.ToString(),
            $"تعديل بيانات المستخدم: {user.Username}",
            HttpContext.Connection.RemoteIpAddress?.ToString());

        TempData["Success"] = "تم تحديث بيانات المستخدم بنجاح.";
        return RedirectToAction(nameof(Users));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleUserStatus(int id)
    {
        var user = await _context.Users.FindAsync(id);
        if (user == null)
        {
            return NotFound();
        }

        // Prevent disabling main admin
        if (user.Username.ToLower() == "admin")
        {
            TempData["Error"] = "لا يمكن تعطيل حساب المدير الرئيسي.";
            return RedirectToAction(nameof(Users));
        }

        user.Status = user.Status == UserStatus.Active ? UserStatus.Inactive : UserStatus.Active;
        await _context.SaveChangesAsync();

        TempData["Success"] = $"تم تغيير حالة المستخدم {user.FullName} إلى {(user.Status == UserStatus.Active ? "نشط" : "معطل")}.";
        return RedirectToAction(nameof(Users));
    }

    #endregion

    #region Roles & Permissions

    public async Task<IActionResult> Roles()
    {
        var roles = await _context.UserRoles
            .Include(r => r.RolePermissions)
            .ToListAsync();

        var allPermissions = await _context.Permissions
            .OrderBy(p => p.Category)
            .ThenBy(p => p.Id)
            .ToListAsync();

        ViewBag.AllPermissions = allPermissions;
        return View(roles);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateRolePermissions(int roleId, List<int> permissionIds)
    {
        var role = await _context.UserRoles
            .Include(r => r.RolePermissions)
            .FirstOrDefaultAsync(r => r.Id == roleId);

        if (role == null)
        {
            TempData["Error"] = "الدور غير موجود.";
            return RedirectToAction(nameof(Roles));
        }

        // Remove old permissions
        _context.RolePermissions.RemoveRange(role.RolePermissions);

        // Add new permissions
        if (permissionIds != null)
        {
            foreach (var permId in permissionIds)
            {
                _context.RolePermissions.Add(new RolePermission
                {
                    RoleId = roleId,
                    PermissionId = permId
                });
            }
        }

        await _context.SaveChangesAsync();

        await _auditLogService.LogAsync(
            int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value),
            User.Identity!.Name!,
            "UpdateRolePermissions",
            "UserRole",
            role.Id.ToString(),
            $"تحديث صلاحيات دور: {role.NameAr}",
            HttpContext.Connection.RemoteIpAddress?.ToString());

        TempData["Success"] = $"تم حفظ صلاحيات ({role.NameAr}) بنجاح.";
        return RedirectToAction(nameof(Roles));
    }

    #endregion

    #region Settings

    public async Task<IActionResult> Settings()
    {
        var settings = await _context.Settings.ToDictionaryAsync(s => s.Key, s => s.Value);
        return View(settings);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveSettings(Dictionary<string, string> settings)
    {
        foreach (var (key, value) in settings)
        {
            var s = await _context.Settings.FirstOrDefaultAsync(item => item.Key == key);
            if (s != null)
            {
                s.Value = value ?? "";
            }
            else
            {
                _context.Settings.Add(new Setting
                {
                    Key = key,
                    Value = value ?? "",
                    Category = key.StartsWith("WhatsApp") ? "WhatsApp" : "CenterInfo"
                });
            }
        }

        await _context.SaveChangesAsync();

        await _auditLogService.LogAsync(
            int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value),
            User.Identity!.Name!,
            "UpdateSettings",
            "Setting",
            "System",
            "تحديث إعدادات النظام والسنتر وبوابة الواتساب",
            HttpContext.Connection.RemoteIpAddress?.ToString());

        TempData["Success"] = "تم حفظ كافة الإعدادات بنجاح.";
        return RedirectToAction(nameof(Settings));
    }

    #endregion

    #region Audit Logs

    public async Task<IActionResult> AuditLogs(string? search, DateTime? from, DateTime? to)
    {
        var logs = await _auditLogService.GetLogsAsync(from, to, search, 100);
        return View(logs);
    }

    #endregion

    #region Reports

    public async Task<IActionResult> Reports(string type = "sales", DateTime? from = null, DateTime? to = null)
    {
        var fromDate = from ?? DateTime.UtcNow.AddMonths(-12).Date;
        var toDate = to ?? DateTime.UtcNow.Date;

        ViewBag.ReportType = type.ToLower();
        ViewBag.FromDate = fromDate.ToString("yyyy-MM-dd");
        ViewBag.ToDate = toDate.ToString("yyyy-MM-dd");

        if (type.ToLower() == "inventory")
        {
            var invModel = await _reportService.GetInventoryReportAsync();
            return View("InventoryReport", invModel);
        }
        else
        {
            var salesModel = await _reportService.GetSalesReportAsync(fromDate, toDate);
            return View("SalesReport", salesModel);
        }
    }

    #endregion
}
