using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Doctor.Web.Models.Entities;

public enum UserStatus
{
    Active = 1,
    Inactive = 2
}

public class User
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(50)]
    public string Username { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string FullName { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? Email { get; set; }

    [MaxLength(20)]
    public string? PhoneNumber { get; set; }

    [Required]
    public string PasswordHash { get; set; } = string.Empty;

    public int RoleId { get; set; }
    public UserRole Role { get; set; } = null!;

    public UserStatus Status { get; set; } = UserStatus.Active;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }

    public ICollection<AuditLog> AuditLogs { get; set; } = new List<AuditLog>();
    public ICollection<Sale> Sales { get; set; } = new List<Sale>();
}

public class UserRole
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(50)]
    public string Name { get; set; } = string.Empty; // Admin, Sales, Maintenance

    [MaxLength(100)]
    public string NameAr { get; set; } = string.Empty; // مدير النظام، مبيعات، صيانة

    [MaxLength(200)]
    public string? Description { get; set; }

    public ICollection<User> Users { get; set; } = new List<User>();
    public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
}

public class Permission
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(100)]
    public string Code { get; set; } = string.Empty; // e.g. "pos.create", "repairs.update"

    [Required, MaxLength(150)]
    public string NameAr { get; set; } = string.Empty;

    [MaxLength(100)]
    public string Category { get; set; } = string.Empty; // المبيعات، الصيانة، المخزون، الإدارة

    public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
}

public class RolePermission
{
    public int RoleId { get; set; }
    public UserRole Role { get; set; } = null!;

    public int PermissionId { get; set; }
    public Permission Permission { get; set; } = null!;
}
