using System.Security.Claims;
using System.Security.Cryptography;
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
using Microsoft.Extensions.Caching.Memory;

namespace Doctor.Web.Controllers;

public class AccountController : Controller
{
    private readonly IAuthService _authService;
    private readonly IAuditLogService _auditLogService;
    private readonly ApplicationDbContext _context;
    private readonly IEmailService _emailService;
    private readonly IMemoryCache _memoryCache;

    public AccountController(
        IAuthService authService,
        IAuditLogService auditLogService,
        ApplicationDbContext context,
        IEmailService emailService,
        IMemoryCache memoryCache)
    {
        _authService = authService;
        _auditLogService = auditLogService;
        _context = context;
        _emailService = emailService;
        _memoryCache = memoryCache;
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity != null && User.Identity.IsAuthenticated)
        {
            return RedirectToRoleDashboard();
        }

        ViewData["ReturnUrl"] = returnUrl;
        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = await _authService.AuthenticateAsync(model.Username, model.Password);
        if (user == null)
        {
            ModelState.AddModelError(string.Empty, "اسم المستخدم أو كلمة المرور غير صحيحة، أو الحساب معطل.");
            return View(model);
        }

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Username),
            new Claim("FullName", user.FullName),
            new Claim(ClaimTypes.Role, user.Role.Name),
            new Claim("RoleAr", user.Role.NameAr)
        };

        // Add permissions as claims
        var permissions = await _authService.GetUserPermissionCodesAsync(user.Id);
        foreach (var perm in permissions)
        {
            claims.Add(new Claim("Permission", perm));
        }

        var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var authProperties = new AuthenticationProperties
        {
            IsPersistent = model.RememberMe,
            ExpiresUtc = model.RememberMe ? DateTimeOffset.UtcNow.AddDays(14) : null
        };

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(claimsIdentity),
            authProperties);

        await _auditLogService.LogAsync(
            user.Id,
            user.Username,
            "Login",
            "User",
            user.Id.ToString(),
            $"تسجيل دخول ناجح للمستخدم {user.FullName}",
            HttpContext.Connection.RemoteIpAddress?.ToString());

        if (!string.IsNullOrEmpty(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
        {
            return Redirect(model.ReturnUrl);
        }

        return RedirectToRoleDashboard(user.Role.Name);
    }

    [HttpGet]
    [HttpPost]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        string username = User.Identity?.Name ?? "";
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out int userId);

        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

        if (userId > 0)
        {
            await _auditLogService.LogAsync(
                userId,
                username,
                "Logout",
                "User",
                userId.ToString(),
                $"تسجيل خروج المستخدم {username}",
                HttpContext.Connection.RemoteIpAddress?.ToString());
        }

        return RedirectToAction("Login", "Account");
    }

    [HttpGet]
    public IActionResult AccessDenied()
    {
        return View();
    }

    private IActionResult RedirectToRoleDashboard(string? roleName = null)
    {
        string role = roleName ?? User.FindFirstValue(ClaimTypes.Role) ?? "";

        return role switch
        {
            "Admin" => RedirectToAction("Index", "Admin"),
            "Sales" => RedirectToAction("Index", "Sales"),
            "Maintenance" => RedirectToAction("Index", "Maintenance"),
            _ => RedirectToAction("Index", "Sales")
        };
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult ForgotPassword()
    {
        return View(new ForgotPasswordViewModel());
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var clean = model.Identifier.Trim().ToLower();
        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Username.ToLower() == clean || (u.Email != null && u.Email.ToLower() == clean));

        if (user == null)
        {
            ModelState.AddModelError("", "لم يتم العثور على حساب مرتبط باسم المستخدم أو البريد المدخل.");
            return View(model);
        }

        string targetEmail = !string.IsNullOrWhiteSpace(user.Email) ? user.Email : (model.Identifier.Contains("@") ? model.Identifier.Trim() : "");

        if (string.IsNullOrWhiteSpace(targetEmail))
        {
            ModelState.AddModelError("", "هذا الحساب غير مسجل له بريد إلكتروني في النظام. يرجى مراجعة مسؤول النظام.");
            return View(model);
        }

        string code = RandomNumberGenerator.GetInt32(100000, 999999).ToString();
        string token = Guid.NewGuid().ToString("N");

        _memoryCache.Set($"pwd_reset_{token}", new ResetCodeData
        {
            UserId = user.Id,
            Code = code,
            Email = targetEmail,
            ExpiresAt = DateTime.UtcNow.AddMinutes(15)
        }, TimeSpan.FromMinutes(15));

        var emailResult = await _emailService.SendPasswordResetCodeAsync(targetEmail, code, user.FullName);
        if (!emailResult.Success)
        {
            // Helpful fallback so user is never locked out even if Gmail SMTP app password isn't entered yet in appsettings.json!
            TempData["Info"] = $"تنبيه مؤقت للتجربة: كود التأكيد الخاص بك هو [{code}]. (لإرساله تلقائياً عبر الإيميل ضع بيانات Gmail في appsettings.json).";
        }
        else
        {
            TempData["Success"] = $"تم إرسال كود التأكيد بنجاح إلى: {targetEmail}";
        }

        return RedirectToAction(nameof(VerifyResetCode), new { token });
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult VerifyResetCode(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || !_memoryCache.TryGetValue($"pwd_reset_{token}", out ResetCodeData? data) || data == null)
        {
            TempData["Error"] = "انتهت صلاحية جلسة التحقق، يرجى إعادة المحاولة من جديد.";
            return RedirectToAction(nameof(ForgotPassword));
        }

        return View(new VerifyResetCodeViewModel
        {
            Token = token,
            Email = data.Email
        });
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> VerifyResetCode(VerifyResetCodeViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        if (!_memoryCache.TryGetValue($"pwd_reset_{model.Token}", out ResetCodeData? data) || data == null)
        {
            TempData["Error"] = "انتهت صلاحية كود التأكيد (صلاحيته 15 دقيقة). يرجى طلب كود جديد.";
            return RedirectToAction(nameof(ForgotPassword));
        }

        if (data.Code != model.Code.Trim())
        {
            ModelState.AddModelError("Code", "كود التأكيد المدخل غير صحيح! تأكد من كتابة الأرقام الستة بدقة.");
            return View(model);
        }

        var user = await _context.Users.FindAsync(data.UserId);
        if (user == null)
        {
            TempData["Error"] = "المستخدم غير موجود.";
            return RedirectToAction(nameof(Login));
        }

        user.PasswordHash = PasswordHasher.HashPassword(model.NewPassword);
        if (!string.IsNullOrWhiteSpace(data.Email) && user.Email != data.Email)
        {
            user.Email = data.Email;
        }

        await _context.SaveChangesAsync();
        _memoryCache.Remove($"pwd_reset_{model.Token}");

        await _auditLogService.LogAsync(
            user.Id,
            user.Username,
            "ResetPasswordByEmail",
            "User",
            user.Id.ToString(),
            $"تمت إعادة تعيين كلمة المرور بنجاح بواسطة كود التحقق المرسل إلى {data.Email}",
            HttpContext.Connection.RemoteIpAddress?.ToString());

        TempData["Success"] = "تم تغيير كلمة المرور بنجاح! يمكنك الآن تسجيل الدخول بها.";
        return RedirectToAction(nameof(Login));
    }
}

public class ResetCodeData
{
    public int UserId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
}
