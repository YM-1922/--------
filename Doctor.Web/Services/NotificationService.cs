using Doctor.Web.Data;
using Doctor.Web.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Doctor.Web.Services;

public interface INotificationService
{
    Task AddNotificationAsync(string title, string message, NotificationType type, string? relatedEntity = null, int? relatedId = null);
    Task<List<Notification>> GetUnreadNotificationsAsync(int limit = 10);
    Task<int> GetUnreadCountAsync();
    Task MarkAsReadAsync(int notificationId);
    Task MarkAllAsReadAsync();
}

public class NotificationService : INotificationService
{
    private readonly ApplicationDbContext _context;

    public NotificationService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task AddNotificationAsync(string title, string message, NotificationType type, string? relatedEntity = null, int? relatedId = null)
    {
        var notification = new Notification
        {
            Title = title,
            Message = message,
            Type = type,
            RelatedEntity = relatedEntity,
            RelatedId = relatedId,
            IsRead = false,
            CreatedAt = DateTime.UtcNow
        };

        _context.Notifications.Add(notification);
        await _context.SaveChangesAsync();
    }

    public async Task<List<Notification>> GetUnreadNotificationsAsync(int limit = 10)
    {
        return await _context.Notifications
            .Where(n => !n.IsRead)
            .OrderByDescending(n => n.CreatedAt)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<int> GetUnreadCountAsync()
    {
        return await _context.Notifications.CountAsync(n => !n.IsRead);
    }

    public async Task MarkAsReadAsync(int notificationId)
    {
        var item = await _context.Notifications.FindAsync(notificationId);
        if (item != null)
        {
            item.IsRead = true;
            await _context.SaveChangesAsync();
        }
    }

    public async Task MarkAllAsReadAsync()
    {
        var unread = await _context.Notifications.Where(n => !n.IsRead).ToListAsync();
        foreach (var n in unread)
        {
            n.IsRead = true;
        }
        await _context.SaveChangesAsync();
    }
}
