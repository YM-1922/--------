using System.Net;
using System.Net.Mail;

namespace Doctor.Web.Services;

public interface IEmailService
{
    Task<(bool Success, string Message)> SendPasswordResetCodeAsync(string toEmail, string code, string userName);
}

public class EmailService : IEmailService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<EmailService> _logger;

    public EmailService(IConfiguration configuration, ILogger<EmailService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<(bool Success, string Message)> SendPasswordResetCodeAsync(string toEmail, string code, string userName)
    {
        var smtpServer = _configuration["Email:SmtpServer"] ?? "smtp.gmail.com";
        int port = int.TryParse(_configuration["Email:Port"], out int p) ? p : 587;
        var senderEmail = _configuration["Email:SenderEmail"] ?? "";
        var senderPassword = _configuration["Email:SenderPassword"] ?? "";
        var displayName = _configuration["Email:SenderDisplayName"] ?? "دكتور سنتر - نظام الإدارة";
        bool enableSsl = !bool.TryParse(_configuration["Email:EnableSsl"], out bool ssl) || ssl;

        if (string.IsNullOrWhiteSpace(senderEmail) || string.IsNullOrWhiteSpace(senderPassword))
        {
            _logger.LogWarning("Email sending requested for {Email} with code {Code}, but SMTP credentials are not configured.", toEmail, code);
            return (false, "لم يتم ضبط بيانات إرسال البريد الإلكتروني (Gmail) بعد في إعدادات النظام.");
        }

        try
        {
            using var client = new SmtpClient(smtpServer, port)
            {
                EnableSsl = enableSsl,
                Credentials = new NetworkCredential(senderEmail.Trim(), senderPassword.Trim()),
                DeliveryMethod = SmtpDeliveryMethod.Network,
                Timeout = 10000
            };

            var mail = new MailMessage
            {
                From = new MailAddress(senderEmail.Trim(), displayName),
                Subject = "كود تأكيد إعادة تعيين كلمة المرور - دكتور سنتر",
                IsBodyHtml = true,
                Body = $@"
<!DOCTYPE html>
<html dir='rtl' lang='ar'>
<head><meta charset='utf-8'></head>
<body style='font-family: Arial, sans-serif; background-color: #f8fafc; padding: 20px; color: #1e293b; direction: rtl;'>
    <div style='max-width: 500px; margin: 0 auto; background: #ffffff; border-radius: 12px; padding: 30px; box-shadow: 0 4px 6px rgba(0,0,0,0.05); border: 1px solid #e2e8f0;'>
        <div style='text-align: center; margin-bottom: 25px;'>
            <h2 style='color: #0284c7; margin: 0;'>دكتور سنتر</h2>
            <p style='color: #64748b; font-size: 14px; margin-top: 5px;'>نظام إدارة السنتر والمبيعات والصيانة</p>
        </div>
        <p style='font-size: 15px;'>مرحباً <strong>{userName}</strong>،</p>
        <p style='font-size: 14px; line-height: 1.6;'>لقد تلقينا طلباً لإعادة تعيين كلمة المرور الخاصة بحسابك في النظام. استخدم كود التأكيد التالي لإكمال العملية:</p>
        <div style='text-align: center; margin: 30px 0;'>
            <span style='display: inline-block; font-size: 32px; font-weight: bold; letter-spacing: 6px; color: #0f172a; background: #f1f5f9; padding: 12px 24px; border-radius: 8px; border: 2px dashed #0284c7;'>{code}</span>
        </div>
        <p style='font-size: 13px; color: #dc2626;'>* هذا الكود صالح لمدة 15 دقيقة فقط من تاريخ إرساله.</p>
        <p style='font-size: 13px; color: #64748b;'>إذا لم تكن قد طلبت هذا الكود بنفسك، يمكنك تجاهل هذا البريد بكل أمان.</p>
        <hr style='border: none; border-top: 1px solid #e2e8f0; margin: 25px 0;' />
        <div style='text-align: center; font-size: 12px; color: #94a3b8;'>
            جميع الحقوق محفوظة &copy; {DateTime.UtcNow.Year} دكتور سنتر
        </div>
    </div>
</body>
</html>"
            };

            mail.To.Add(toEmail.Trim());

            await client.SendMailAsync(mail);
            _logger.LogInformation("Password reset email sent successfully to {Email}", toEmail);
            return (true, "تم إرسال كود التأكيد إلى بريدك الإلكتروني بنجاح.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send password reset email to {Email}", toEmail);
            return (false, $"فشل الاتصال بخادم البريد: {ex.Message}");
        }
    }
}
