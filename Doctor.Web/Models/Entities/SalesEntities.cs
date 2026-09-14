using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Doctor.Web.Models.Entities;

public enum PaymentMethod
{
    Cash = 1,       // نقدي
    Card = 2,       // بطاقة / فيزا
    Transfer = 3,   // تحويل بنكي / محفظة إلكترونية
    Split = 4,      // متعدد
    InstaPay = 5    // انستاباي (InstaPay)
}

public enum SaleStatus
{
    Completed = 1,
    Cancelled = 2,
    Refunded = 3
}

public class Sale
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(50)]
    public string InvoiceNumber { get; set; } = string.Empty;

    public int? CustomerId { get; set; }
    public Customer? Customer { get; set; }

    public int UserId { get; set; }
    public User User { get; set; } = null!;

    [Column(TypeName = "decimal(18,2)")]
    public decimal SubTotal { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal DiscountAmount { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal TaxAmount { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal TotalAmount { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal PaidAmount { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal RemainingAmount { get; set; }

    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Cash;

    [MaxLength(100)]
    public string? PaymentReferenceNumber { get; set; }

    public SaleStatus Status { get; set; } = SaleStatus.Completed;

    [MaxLength(500)]
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<SaleItem> Items { get; set; } = new List<SaleItem>();
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
}

public class SaleItem
{
    [Key]
    public int Id { get; set; }

    public int SaleId { get; set; }
    public Sale Sale { get; set; } = null!;

    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public int Quantity { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal UnitPrice { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal DiscountAmount { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal TotalPrice { get; set; }

    [MaxLength(100)]
    public string? ImeiOrSerialSnapshot { get; set; }

    [MaxLength(500)]
    public string? ItemDetails { get; set; }
}

public class Payment
{
    [Key]
    public int Id { get; set; }

    public int? SaleId { get; set; }
    public Sale? Sale { get; set; }

    public int? RepairOrderId { get; set; }
    public RepairOrder? RepairOrder { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal Amount { get; set; }

    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Cash;

    [MaxLength(100)]
    public string? ReferenceNumber { get; set; }

    [MaxLength(250)]
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
