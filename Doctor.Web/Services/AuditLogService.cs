using Doctor.Web.Data;
using Doctor.Web.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Doctor.Web.Services;

public interface IAuditLogService
{
    Task LogAsync(int? userId, string username, string action, string entity, string? entityId, string? details, string? ipAddress);
    Task<List<AuditLog>> GetLogsAsync(DateTime? from = null, DateTime? to = null, string? action = null, int limit = 100);
}

public class AuditLogService : IAuditLogService
{
    private readonly ApplicationDbContext _context;

    public AuditLogService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task LogAsync(int? userId, string username, string action, string entity, string? entityId, string? details, string? ipAddress)
    {
        var log = new AuditLog
        {
            UserId = userId,
            UsernameSnapshot = string.IsNullOrWhiteSpace(username) ? "النظام" : username,
            Action = action,
            Entity = entity,
            EntityId = entityId,
            Details = details,
            IpAddress = ipAddress,
            Timestamp = DateTime.UtcNow
        };

        _context.AuditLogs.Add(log);
        await _context.SaveChangesAsync();
    }

    public async Task<List<AuditLog>> GetLogsAsync(DateTime? from = null, DateTime? to = null, string? action = null, int limit = 100)
    {
        var query = _context.AuditLogs
            .Include(a => a.User)
            .AsQueryable();

        if (from.HasValue)
        {
            query = query.Where(a => a.Timestamp >= from.Value);
        }

        if (to.HasValue)
        {
            query = query.Where(a => a.Timestamp <= to.Value);
        }

        if (!string.IsNullOrWhiteSpace(action))
        {
            query = query.Where(a => a.Action.Contains(action));
        }

        return await query
            .OrderByDescending(a => a.Timestamp)
            .Take(limit)
            .ToListAsync();
    }
}
