using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Doctor.Web.Models.Entities;

public enum RepairStatus
{
    Received = 1,                 // تم الاستلام
    UnderInspection = 2,          // تحت الفحص
    PendingCustomerApproval = 3,  // في انتظار موافقة العميل
    InProgress = 4,               // قيد الإصلاح
    WaitingForParts = 5,          // في انتظار قطعة غيار
    ReadyForPickup = 6,           // جاهز للاستلام
    Delivered = 7,                // تم التسليم
    Cancelled = 8                 // تم إلغاء الصيانة
}

public class SparePart
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(150)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? PartType { get; set; } // شاشة، بطارية، فلاتة شحن، كاميرا، باغة، سماعة، مايك، بوردة

    public int? BrandId { get; set; }
    public Brand? Brand { get; set; }

    [MaxLength(100)]
    public string? CompatibleModel { get; set; }

    [MaxLength(100)]
    public string Barcode { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? Sku { get; set; }

    public int StockQuantity { get; set; }

    public int MinStockLevel { get; set; } = 2;

    [Column(TypeName = "decimal(18,2)")]
    public decimal PurchasePrice { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal SellingPrice { get; set; }

    [MaxLength(500)]
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<RepairPart> RepairParts { get; set; } = new List<RepairPart>();
    public ICollection<InventoryTransaction> InventoryTransactions { get; set; } = new List<InventoryTransaction>();
}

public class RepairOrder
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(50)]
    public string RepairCode { get; set; } = string.Empty; // e.g. REP-2026-00001

    public int? CustomerId { get; set; }
    public Customer? Customer { get; set; }

    [Required, MaxLength(100)]
    public string DeviceBrand { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string DeviceModel { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? ImeiOrSerial { get; set; }

    [MaxLength(50)]
    public string? DeviceColor { get; set; }

    [MaxLength(250)]
    public string? DeviceCondition { get; set; } // سليم، خدوش خفيفة، كسر بالشاشة، كسر بالظهر

    [Required, MaxLength(1000)]
    public string ProblemDescription { get; set; } = string.Empty; // العطل كما وصفه العميل

    [MaxLength(300)]
    public string? ReceivedAccessories { get; set; } // الشاحن، العلبة، الكابل، جراب، بطاقة ذاكرة

    [MaxLength(2000)]
    public string? PhotoPathsJson { get; set; } // صور توثيق حالة الجهاز عند الاستلام

    [MaxLength(1000)]
    public string? Diagnosis { get; set; } // التشخيص الفني

    [MaxLength(1000)]
    public string? TechnicianNotes { get; set; } // ملاحظات الإصلاح

    public RepairStatus Status { get; set; } = RepairStatus.Received;

    [Column(TypeName = "decimal(18,2)")]
    public decimal PartsCost { get; set; } // تكلفة قطع الغيار

    [Column(TypeName = "decimal(18,2)")]
    public decimal LaborCost { get; set; } // مصنعية الصيانة

    [Column(TypeName = "decimal(18,2)")]
    public decimal DiscountAmount { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal TotalCost { get; set; } // قطع + مصنعية - خصم

    [Column(TypeName = "decimal(18,2)")]
    public decimal PaidAmount { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal RemainingAmount { get; set; }

    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;

    public DateTime? EstimatedDeliveryDate { get; set; }

    public DateTime? CompletedAt { get; set; }

    public DateTime? DeliveredAt { get; set; }

    public int CreatedByUserId { get; set; }
    public User CreatedByUser { get; set; } = null!;

    public int? TechnicianUserId { get; set; }
    public User? TechnicianUser { get; set; }

    public ICollection<RepairPart> RepairParts { get; set; } = new List<RepairPart>();
    public ICollection<RepairStatusHistory> StatusHistories { get; set; } = new List<RepairStatusHistory>();
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
}

public class RepairStatusHistory
{
    [Key]
    public int Id { get; set; }

    public int RepairOrderId { get; set; }
    public RepairOrder RepairOrder { get; set; } = null!;

    public RepairStatus OldStatus { get; set; }
    public RepairStatus NewStatus { get; set; }

    public int ChangedByUserId { get; set; }
    public User ChangedByUser { get; set; } = null!;

    [MaxLength(500)]
    public string? Notes { get; set; }

    public DateTime ChangedAt { get; set; } = DateTime.UtcNow;
}

public class RepairPart
{
    [Key]
    public int Id { get; set; }

    public int RepairOrderId { get; set; }
    public RepairOrder RepairOrder { get; set; } = null!;

    public int SparePartId { get; set; }
    public SparePart SparePart { get; set; } = null!;

    public int Quantity { get; set; } = 1;

    [Column(TypeName = "decimal(18,2)")]
    public decimal UnitPrice { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal TotalPrice { get; set; }

    [MaxLength(250)]
    public string? Notes { get; set; }
}
