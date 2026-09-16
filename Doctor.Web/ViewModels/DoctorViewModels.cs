using System.ComponentModel.DataAnnotations;
using Doctor.Web.Models.Entities;

namespace Doctor.Web.ViewModels;

public class LoginViewModel
{
    [Required(ErrorMessage = "اسم المستخدم مطلوب")]
    [Display(Name = "اسم المستخدم")]
    public string Username { get; set; } = string.Empty;

    [Required(ErrorMessage = "كلمة المرور مطلوبة")]
    [DataType(DataType.Password)]
    [Display(Name = "كلمة المرور")]
    public string Password { get; set; } = string.Empty;

    [Display(Name = "تذكرني")]
    public bool RememberMe { get; set; } = true;

    public string? ReturnUrl { get; set; }
}

public class PosCartItemDto
{
    public int ProductId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Barcode { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
    public decimal Discount { get; set; }
    public decimal Total => (UnitPrice * Quantity) - Discount;
    public string? ImeiOrSerial { get; set; }
    public string? ItemDetails { get; set; }
}

public class CreateSaleDto
{
    public int? CustomerId { get; set; }
    public string? CustomerName { get; set; }
    public string? CustomerPhone { get; set; }
    public List<PosCartItemDto> Items { get; set; } = new();
    public decimal SubTotal { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal RemainingAmount { get; set; }
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Cash;
    public string? PaymentReferenceNumber { get; set; }
    public string? Notes { get; set; }
}

public class QuickCreateCustomerDto
{
    [Required(ErrorMessage = "اسم العميل مطلوب")]
    public string Name { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string? WhatsAppNumber { get; set; }
    public string? Address { get; set; }
    public string? Notes { get; set; }
}

public class CreateRepairOrderDto
{
    // Customer
    public int? CustomerId { get; set; }
    [Required(ErrorMessage = "اسم العميل مطلوب")]
    public string CustomerName { get; set; } = string.Empty;

    [Required(ErrorMessage = "رقم هاتف العميل مطلوب")]
    public string CustomerPhone { get; set; } = string.Empty;
    public string? CustomerWhatsApp { get; set; }
    public string? CustomerEmail { get; set; }
    public string? CustomerAddress { get; set; }

    // Device
    [Required(ErrorMessage = "ماركة الجهاز مطلوبة")]
    public string DeviceBrand { get; set; } = string.Empty;

    [Required(ErrorMessage = "موديل الجهاز مطلوب")]
    public string DeviceModel { get; set; } = string.Empty;
    public string? ImeiOrSerial { get; set; }
    public string? DeviceColor { get; set; }
    public string? DeviceCondition { get; set; }

    [Required(ErrorMessage = "وصف المشكلة مطلوب")]
    public string ProblemDescription { get; set; } = string.Empty;

    public string? ReceivedAccessories { get; set; }
    public string? PhotoPathsJson { get; set; }

    public decimal EstimatedCost { get; set; }
    public decimal PaidAmount { get; set; }
    public DateTime? EstimatedDeliveryDate { get; set; }
    public int? TechnicianUserId { get; set; }
    public string? TechnicianNotes { get; set; }
}

public class AddRepairPartDto
{
    public int RepairOrderId { get; set; }
    public int SparePartId { get; set; }
    public int Quantity { get; set; } = 1;
    public decimal UnitPrice { get; set; }
    public string? Notes { get; set; }
}

public class UpdateRepairStatusDto
{
    public int RepairOrderId { get; set; }
    public RepairStatus NewStatus { get; set; }
    public string? Notes { get; set; }
    public decimal? AdditionalLaborCost { get; set; }
    public decimal? AdditionalDiscount { get; set; }
    public string? Diagnosis { get; set; }
    public string? TechnicianNotes { get; set; }
    public bool SendWhatsApp { get; set; } = true;
}

public class DashboardStatsViewModel
{
    public decimal TodaySales { get; set; }
    public decimal MonthSales { get; set; }
    public int TotalDevicesInStock { get; set; }
    public int TotalAccessoriesInStock { get; set; }
    public int ActiveRepairsCount { get; set; }
    public int ReadyForPickupCount { get; set; }
    public int TotalCustomersCount { get; set; }
    public decimal TotalInventoryValue { get; set; }
    public decimal EstimatedProfitThisMonth { get; set; }
    public int LowStockItemsCount { get; set; }

    // Chart data
    public List<DailySalesPoint> Last7DaysSales { get; set; } = new();
    public List<MonthlySalesPoint> MonthlySales { get; set; } = new();
    public List<CategoryCountPoint> RepairsByStatus { get; set; } = new();
    public List<TopSellingProductPoint> TopSellingProducts { get; set; } = new();
    public List<BrandSalesPoint> TopBrandsSales { get; set; } = new();

    public List<Product> LowStockProducts { get; set; } = new();
    public List<SparePart> LowStockSpareParts { get; set; } = new();
    public List<RepairOrder> RecentRepairs { get; set; } = new();
    public List<Sale> RecentSales { get; set; } = new();
}

public class DailySalesPoint
{
    public string Date { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public int Count { get; set; }
}

public class MonthlySalesPoint
{
    public string MonthName { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public int Count { get; set; }
}

public class CategoryCountPoint
{
    public string StatusNameAr { get; set; } = string.Empty;
    public int Count { get; set; }
    public string Color { get; set; } = "#3b82f6";
}

public class TopSellingProductPoint
{
    public string ProductName { get; set; } = string.Empty;
    public int QuantitySold { get; set; }
    public decimal TotalRevenue { get; set; }
}

public class BrandSalesPoint
{
    public string BrandName { get; set; } = string.Empty;
    public int QuantitySold { get; set; }
    public decimal TotalRevenue { get; set; }
    public double Percentage { get; set; }
    public string Color { get; set; } = "#0284c7";
}

public class OpenShiftDto
{
    public decimal StartingCash { get; set; }
    public string? Notes { get; set; }
}

public class CloseShiftDto
{
    public int ShiftId { get; set; }
    public decimal ActualCash { get; set; }
    public string? Notes { get; set; }
}

public class ForgotPasswordViewModel
{
    [Required(ErrorMessage = "يرجى إدخال اسم المستخدم أو البريد الإلكتروني")]
    [Display(Name = "اسم المستخدم أو البريد الإلكتروني (Gmail)")]
    public string Identifier { get; set; } = string.Empty;
}

public class VerifyResetCodeViewModel
{
    [Required]
    public string Token { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "كود التأكيد مطلوب")]
    [StringLength(6, MinimumLength = 6, ErrorMessage = "كود التأكيد مكون من 6 أرقام")]
    [Display(Name = "كود التأكيد المرسل للإيميل")]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "كلمة المرور الجديدة مطلوبة")]
    [StringLength(100, MinimumLength = 6, ErrorMessage = "كلمة المرور يجب أن لا تقل عن 6 خانات")]
    [DataType(DataType.Password)]
    [Display(Name = "كلمة المرور الجديدة")]
    public string NewPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "تأكيد كلمة المرور مطلوب")]
    [DataType(DataType.Password)]
    [Compare("NewPassword", ErrorMessage = "كلمة المرور وتأكيدها غير متطابقين")]
    [Display(Name = "تأكيد كلمة المرور")]
    public string ConfirmPassword { get; set; } = string.Empty;
}

public class BrandSalesSummaryPoint
{
    public string BrandName { get; set; } = string.Empty;
    public int DevicesSold { get; set; }
    public decimal TotalRevenue { get; set; }
    public double Percentage { get; set; }
}

public class ProductStockAlertPoint
{
    public int ProductId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string BrandName { get; set; } = string.Empty;
    public int CurrentStock { get; set; }
    public int MinStockLevel { get; set; }
    public decimal SellingPrice { get; set; }
}

public class SlowMovingDevicePoint
{
    public int ProductId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string BrandName { get; set; } = string.Empty;
    public int CurrentStock { get; set; }
    public decimal SellingPrice { get; set; }
}

public class SupplyOrderItemDto
{
    public int? ProductId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Storage { get; set; }
    public string? Ram { get; set; }
    public string? Color { get; set; }
    public string? Imei { get; set; }
    public int? BrandId { get; set; }
    public int Quantity { get; set; } = 1;
    public decimal CostPrice { get; set; }
    public decimal SellingPrice { get; set; }
    public decimal TotalPrice => Quantity * CostPrice;
    public bool IsNew { get; set; }
}
