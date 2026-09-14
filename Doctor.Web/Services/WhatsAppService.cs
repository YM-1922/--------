using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Doctor.Web.Data;
using Doctor.Web.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Doctor.Web.Services;

public interface IWhatsAppService
{
    Task<(bool Success, string Message, string? WhatsAppUrl)> SendRepairReadyNotificationAsync(int repairOrderId);
    Task<(bool Success, string Message, string? WhatsAppUrl)> SendCustomNotificationAsync(string phoneNumber, string recipientName, string messageContent);
    Task<List<NotificationLog>> GetNotificationLogsAsync(int limit = 50);
}

public class WhatsAppService : IWhatsAppService
{
    private readonly ApplicationDbContext _context;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;

    public WhatsAppService(ApplicationDbContext context, IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        _context = context;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
    }

    public async Task<(bool Success, string Message, string? WhatsAppUrl)> SendRepairReadyNotificationAsync(int repairOrderId)
    {
        var repair = await _context.RepairOrders
            .Include(r => r.Customer)
            .FirstOrDefaultAsync(r => r.Id == repairOrderId);

        if (repair == null)
        {
            return (false, "طلب الصيانة غير موجود", null);
        }

        var customer = repair.Customer;
        string phone = !string.IsNullOrWhiteSpace(customer.WhatsAppNumber) ? customer.WhatsAppNumber : customer.PhoneNumber;

        // Clean phone number (e.g. 010... -> 2010...)
        string formattedPhone = FormatEgyptianPhoneNumber(phone);

        // Fetch settings
        var settings = await _context.Settings.ToDictionaryAsync(s => s.Key, s => s.Value);

        string template = settings.GetValueOrDefault("WhatsApp_ReadyTemplate", 
            "مرحبًا {CustomerName}،\nنود إبلاغك بأن جهازك {Device} أصبح جاهزًا للاستلام بنجاح بعد الصيانة.\nرقم الصيانة: {RepairCode}\nالمبلغ المطلوب: {RemainingAmount} {Currency}\nيمكنك التوجه إلى السنتر للاستلام.\nشكرًا لاختيارك {CenterName}.");

        string centerName = settings.GetValueOrDefault("CenterName", "دكتور سنتر");
        string centerPhone = settings.GetValueOrDefault("CenterPhone", "01020304050");
        string centerAddress = settings.GetValueOrDefault("CenterAddress", "القاهرة");
        string currency = settings.GetValueOrDefault("Currency", "ج.م");

        string message = template
            .Replace("{CustomerName}", customer.Name)
            .Replace("{Device}", $"{repair.DeviceBrand} {repair.DeviceModel}")
            .Replace("{RepairCode}", repair.RepairCode)
            .Replace("{RemainingAmount}", repair.RemainingAmount.ToString("N2"))
            .Replace("{TotalCost}", repair.TotalCost.ToString("N2"))
            .Replace("{PaidAmount}", repair.PaidAmount.ToString("N2"))
            .Replace("{CenterName}", centerName)
            .Replace("{CenterPhone}", centerPhone)
            .Replace("{CenterAddress}", centerAddress)
            .Replace("{Currency}", currency);

        return await SendCustomNotificationAsync(formattedPhone, customer.Name, message);
    }

    public async Task<(bool Success, string Message, string? WhatsAppUrl)> SendCustomNotificationAsync(string phoneNumber, string recipientName, string messageContent)
    {
        string cleanPhone = FormatEgyptianPhoneNumber(phoneNumber);
        string waDirectUrl = $"https://wa.me/{cleanPhone}?text={Uri.EscapeDataString(messageContent)}";

        var settings = await _context.Settings.ToDictionaryAsync(s => s.Key, s => s.Value);
        bool isEnabled = bool.TryParse(settings.GetValueOrDefault("WhatsApp_Enabled", "true"), out bool en) && en;

        if (!isEnabled)
        {
            return (true, "خدمة الواتساب معطلة حالياً في الإعدادات", waDirectUrl);
        }

        string apiUrl = settings.GetValueOrDefault("WhatsApp_ApiUrl", _configuration["WhatsApp:ApiUrl"] ?? "");
        string apiKey = settings.GetValueOrDefault("WhatsApp_ApiKey", _configuration["WhatsApp:ApiKey"] ?? "");
        string instanceId = settings.GetValueOrDefault("WhatsApp_InstanceId", _configuration["WhatsApp:InstanceId"] ?? "");

        // If no API Key configured, simulate and log
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            var log = new NotificationLog
            {
                RecipientPhone = cleanPhone,
                RecipientName = recipientName,
                Channel = "WhatsApp",
                MessageContent = messageContent,
                Status = "Simulated",
                ErrorDetails = "وضع التطوير / المحاكاة (بدون مفتاح API خارجي) - رابط المحادثة المباشر جاهز",
                SentAt = DateTime.UtcNow
            };

            _context.NotificationLogs.Add(log);
            await _context.SaveChangesAsync();

            return (true, "تم تسجيل إشعار الواتساب في سجل الإشعارات (وضع المحاكاة).", waDirectUrl);
        }

        try
        {
            var client = _httpClientFactory.CreateClient();
            var payload = new
            {
                token = apiKey,
                to = cleanPhone,
                body = messageContent
            };

            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            var response = await client.PostAsync(apiUrl, content);

            bool success = response.IsSuccessStatusCode;
            string responseBody = await response.Content.ReadAsStringAsync();

            var log = new NotificationLog
            {
                RecipientPhone = cleanPhone,
                RecipientName = recipientName,
                Channel = "WhatsApp",
                MessageContent = messageContent,
                Status = success ? "Sent" : "Failed",
                ErrorDetails = success ? null : responseBody,
                SentAt = DateTime.UtcNow
            };

            _context.NotificationLogs.Add(log);
            await _context.SaveChangesAsync();

            return (success, success ? "تم إرسال رسالة الواتساب بنجاح!" : $"فشل الإرسال: {response.StatusCode}", waDirectUrl);
        }
        catch (Exception ex)
        {
            var log = new NotificationLog
            {
                RecipientPhone = cleanPhone,
                RecipientName = recipientName,
                Channel = "WhatsApp",
                MessageContent = messageContent,
                Status = "Failed",
                ErrorDetails = ex.Message,
                SentAt = DateTime.UtcNow
            };

            _context.NotificationLogs.Add(log);
            await _context.SaveChangesAsync();

            return (false, $"خطأ في الاتصال ببوابة الواتساب: {ex.Message}", waDirectUrl);
        }
    }

    public async Task<List<NotificationLog>> GetNotificationLogsAsync(int limit = 50)
    {
        return await _context.NotificationLogs
            .OrderByDescending(l => l.SentAt)
            .Take(limit)
            .ToListAsync();
    }

    private static string FormatEgyptianPhoneNumber(string phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return "";
        string clean = new string(phone.Where(char.IsDigit).ToArray());

        if (clean.StartsWith("01") && clean.Length == 11)
        {
            clean = "2" + clean;
        }
        else if (clean.StartsWith("1") && clean.Length == 10)
        {
            clean = "20" + clean;
        }

        return clean;
    }
}
