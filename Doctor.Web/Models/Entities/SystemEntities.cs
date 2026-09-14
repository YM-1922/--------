using System.ComponentModel.DataAnnotations;

namespace Doctor.Web.Models.Entities;

public enum NotificationType
{
    LowStock = 1,
    LowSparePart = 2,
    RepairReady = 3,
    NewSale = 4,
    NewRepair = 5,
    System = 6
}

public class Notification
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(150)]
    public string Title { get; set; } = string.Empty;

    [Required, MaxLength(500)]
    public string Message { get; set; } = string.Empty;

    public NotificationType Type { get; set; } = NotificationType.System;

    [MaxLength(50)]
    public string? RelatedEntity { get; set; }

    public int? RelatedId { get; set; }

    public bool IsRead { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class NotificationLog
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(20)]
    public string RecipientPhone { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? RecipientName { get; set; }

    [Required, MaxLength(30)]
    public string Channel { get; set; } = "WhatsApp";

    [Required, MaxLength(1000)]
    public string MessageContent { get; set; } = string.Empty;

    [Required, MaxLength(30)]
    public string Status { get; set; } = "Sent"; // Sent, Simulated, Failed

    [MaxLength(500)]
    public string? ErrorDetails { get; set; }

    public DateTime SentAt { get; set; } = DateTime.UtcNow;
}

public class Setting
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(100)]
    public string Key { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;

    [MaxLength(200)]
    public string? Description { get; set; }

    [MaxLength(50)]
    public string Category { get; set; } = "General"; // General, WhatsApp, Printing, CenterInfo
}

public class AuditLog
{
    [Key]
    public int Id { get; set; }

    public int? UserId { get; set; }
    public User? User { get; set; }

    [MaxLength(100)]
    public string UsernameSnapshot { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string Action { get; set; } = string.Empty; // e.g. "CreateSale", "ChangeRepairStatus", "Login"

    [Required, MaxLength(100)]
    public string Entity { get; set; } = string.Empty; // e.g. "Sale", "RepairOrder", "Product"

    [MaxLength(50)]
    public string? EntityId { get; set; }

    [MaxLength(2000)]
    public string? Details { get; set; }

    [MaxLength(50)]
    public string? IpAddress { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
