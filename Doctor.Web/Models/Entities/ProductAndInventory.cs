using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Doctor.Web.Models.Entities;

public enum ProductType
{
    Device = 1,     // هاتف محمول
    Accessory = 2   // إكسسوار
}

public class Product
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(150)]
    public string Name { get; set; } = string.Empty;

    public ProductType ProductType { get; set; } = ProductType.Device;

    public int? BrandId { get; set; }
    public Brand? Brand { get; set; }

    public int? ProductCategoryId { get; set; }
    public ProductCategory? ProductCategory { get; set; }

    [MaxLength(100)]
    public string? Model { get; set; }

    [MaxLength(50)]
    public string? Color { get; set; }

    [MaxLength(50)]
    public string? Storage { get; set; } // e.g. 128GB, 256GB

    [MaxLength(50)]
    public string? Ram { get; set; } // e.g. 8GB, 12GB

    [MaxLength(100)]
    public string? SerialNumberOrImei { get; set; } // فريد للأجهزة

    [Required, MaxLength(100)]
    public string Barcode { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? Sku { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal PurchasePrice { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal SellingPrice { get; set; }

    public int StockQuantity { get; set; }

    public int MinStockLevel { get; set; } = 3;

    [MaxLength(250)]
    public string? ImageUrl { get; set; }

    [MaxLength(500)]
    public string? Notes { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<InventoryTransaction> InventoryTransactions { get; set; } = new List<InventoryTransaction>();
    public ICollection<SaleItem> SaleItems { get; set; } = new List<SaleItem>();
}

public enum InventoryTransactionType
{
    Purchase = 1,       // توريد / إضافة مخزون
    Sale = 2,           // بيع
    RepairUsage = 3,    // استهلاك في الصيانة
    Adjustment = 4,     // جرد / تعديل يدوي
    Return = 5          // مرتجع
}

public class InventoryTransaction
{
    [Key]
    public int Id { get; set; }

    public int? ProductId { get; set; }
    public Product? Product { get; set; }

    public int? SparePartId { get; set; }
    public SparePart? SparePart { get; set; }

    public InventoryTransactionType TransactionType { get; set; }

    public int Quantity { get; set; } // موجب للإضافة، سالب للخصم أو مسجل بالموجب مع النوع

    public int QuantityBefore { get; set; }

    public int QuantityAfter { get; set; }

    public int? UserId { get; set; }
    public User? User { get; set; }

    [MaxLength(100)]
    public string? ReferenceId { get; set; } // Invoice Number or Repair Code

    [MaxLength(500)]
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
