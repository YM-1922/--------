using System.ComponentModel.DataAnnotations;

namespace Doctor.Web.Models.Entities;

public class Customer
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required, MaxLength(20)]
    public string PhoneNumber { get; set; } = string.Empty;

    [MaxLength(20)]
    public string? WhatsAppNumber { get; set; }

    [MaxLength(100)]
    public string? Email { get; set; }

    [MaxLength(250)]
    public string? Address { get; set; }

    [MaxLength(500)]
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Sale> Sales { get; set; } = new List<Sale>();
    public ICollection<RepairOrder> RepairOrders { get; set; } = new List<RepairOrder>();
}
