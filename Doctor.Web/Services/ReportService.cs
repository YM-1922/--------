using Doctor.Web.Data;
using Doctor.Web.Models.Entities;
using Doctor.Web.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace Doctor.Web.Services;

public interface IReportService
{
    Task<DashboardStatsViewModel> GetDashboardStatsAsync();
    Task<SalesReportViewModel> GetSalesReportAsync(DateTime from, DateTime to);
    Task<MaintenanceReportViewModel> GetMaintenanceReportAsync(DateTime from, DateTime to);
    Task<InventoryReportViewModel> GetInventoryReportAsync();
}

public class SalesReportViewModel
{
    public DateTime From { get; set; }
    public DateTime To { get; set; }
    public decimal TotalSales { get; set; }
    public decimal TotalDiscounts { get; set; }
    public decimal TotalNetSales { get; set; }
    public decimal TotalCost { get; set; }
    public decimal TotalProfit => TotalNetSales - TotalCost;
    public int InvoiceCount { get; set; }
    public int TotalDevicesSoldCount { get; set; }
    public int TotalAccessoriesSoldCount { get; set; }
    public List<Sale> Sales { get; set; } = new();
    public List<TopSellingProductPoint> TopProducts { get; set; } = new();
    public TopSellingProductPoint? TopSellingDevice { get; set; }
    public List<BrandSalesSummaryPoint> BrandSalesBreakdown { get; set; } = new();
    public List<ProductStockAlertPoint> LowStockDevices { get; set; } = new();
    public List<SlowMovingDevicePoint> SlowMovingDevices { get; set; } = new();
}

public class MaintenanceReportViewModel
{
    public DateTime From { get; set; }
    public DateTime To { get; set; }
    public int TotalRepairsReceived { get; set; }
    public int TotalRepairsDelivered { get; set; }
    public decimal TotalRevenue { get; set; }
    public decimal TotalPartsCost { get; set; }
    public decimal TotalLaborIncome => TotalRevenue - TotalPartsCost;
    public List<CategoryCountPoint> StatusBreakdown { get; set; } = new();
    public List<RepairOrder> Repairs { get; set; } = new();
}

public class InventoryReportViewModel
{
    public int TotalProductsCount { get; set; }
    public int TotalDevicesCount { get; set; }
    public int TotalAccessoriesCount { get; set; }
    public int TotalSparePartsCount { get; set; }
    public decimal TotalInventoryCostValue { get; set; }
    public decimal TotalInventorySellingValue { get; set; }
    public decimal PotentialProfit => TotalInventorySellingValue - TotalInventoryCostValue;
    public List<Product> LowStockProducts { get; set; } = new();
    public List<SparePart> LowStockSpareParts { get; set; } = new();
}

public class ReportService : IReportService
{
    private readonly ApplicationDbContext _context;

    public ReportService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<DashboardStatsViewModel> GetDashboardStatsAsync()
    {
        var now = DateTime.UtcNow;
        var todayStart = now.Date;
        var monthStart = new DateTime(now.Year, now.Month, 1);

        var model = new DashboardStatsViewModel();

        // 1. Sales stats
        model.TodaySales = await _context.Sales
            .Where(s => s.CreatedAt >= todayStart && s.Status == SaleStatus.Completed)
            .SumAsync(s => (decimal?)s.TotalAmount) ?? 0m;

        model.MonthSales = await _context.Sales
            .Where(s => s.CreatedAt >= monthStart && s.Status == SaleStatus.Completed)
            .SumAsync(s => (decimal?)s.TotalAmount) ?? 0m;

        // 2. Inventory stats
        model.TotalDevicesInStock = await _context.Products
            .Where(p => p.IsActive && p.ProductType == ProductType.Device)
            .SumAsync(p => (int?)p.StockQuantity) ?? 0;

        model.TotalAccessoriesInStock = await _context.Products
            .Where(p => p.IsActive && p.ProductType == ProductType.Accessory)
            .SumAsync(p => (int?)p.StockQuantity) ?? 0;

        model.TotalInventoryValue = await _context.Products
            .Where(p => p.IsActive)
            .SumAsync(p => (decimal?)(p.SellingPrice * p.StockQuantity)) ?? 0m;

        // 3. Maintenance stats
        model.ActiveRepairsCount = await _context.RepairOrders
            .CountAsync(r => r.Status != RepairStatus.Delivered && r.Status != RepairStatus.Cancelled);

        model.ReadyForPickupCount = await _context.RepairOrders
            .CountAsync(r => r.Status == RepairStatus.ReadyForPickup);

        model.TotalCustomersCount = await _context.Customers.CountAsync();

        // Estimated profit this month (Sales margin + Maintenance labor)
        decimal salesRevenueMonth = model.MonthSales;
        decimal salesCostMonth = await _context.SaleItems
            .Where(i => i.Sale.CreatedAt >= monthStart && i.Sale.Status == SaleStatus.Completed)
            .SumAsync(i => (decimal?)(i.Product.PurchasePrice * i.Quantity)) ?? 0m;
        
        decimal maintenanceLaborMonth = await _context.RepairOrders
            .Where(r => r.ReceivedAt >= monthStart && r.Status != RepairStatus.Cancelled)
            .SumAsync(r => (decimal?)r.LaborCost) ?? 0m;

        model.EstimatedProfitThisMonth = (salesRevenueMonth - salesCostMonth) + maintenanceLaborMonth;

        // 4. Low stock items
        model.LowStockProducts = await _context.Products
            .Include(p => p.Brand)
            .Include(p => p.ProductCategory)
            .Where(p => p.IsActive && p.StockQuantity <= p.MinStockLevel)
            .OrderBy(p => p.StockQuantity)
            .Take(5)
            .ToListAsync();

        model.LowStockSpareParts = await _context.SpareParts
            .Include(sp => sp.Brand)
            .Where(sp => sp.StockQuantity <= sp.MinStockLevel)
            .OrderBy(sp => sp.StockQuantity)
            .Take(5)
            .ToListAsync();

        model.LowStockItemsCount = model.LowStockProducts.Count + model.LowStockSpareParts.Count;

        // 5. Last 7 days sales
        var last7Days = Enumerable.Range(0, 7)
            .Select(i => todayStart.AddDays(-6 + i))
            .ToList();

        var salesByDay = await _context.Sales
            .Where(s => s.CreatedAt >= todayStart.AddDays(-6) && s.Status == SaleStatus.Completed)
            .GroupBy(s => s.CreatedAt.Date)
            .Select(g => new { Date = g.Key, Total = g.Sum(x => x.TotalAmount), Count = g.Count() })
            .ToListAsync();

        foreach (var day in last7Days)
        {
            var match = salesByDay.FirstOrDefault(s => s.Date == day);
            model.Last7DaysSales.Add(new DailySalesPoint
            {
                Date = day.ToString("dd/MM"),
                Total = match?.Total ?? 0m,
                Count = match?.Count ?? 0
            });
        }

        // 6. Repairs by status
        var statusColors = new Dictionary<RepairStatus, string>
        {
            { RepairStatus.Received, "#6b7280" },
            { RepairStatus.UnderInspection, "#3b82f6" },
            { RepairStatus.PendingCustomerApproval, "#f59e0b" },
            { RepairStatus.InProgress, "#8b5cf6" },
            { RepairStatus.WaitingForParts, "#ec4899" },
            { RepairStatus.ReadyForPickup, "#10b981" },
            { RepairStatus.Delivered, "#059669" },
            { RepairStatus.Cancelled, "#ef4444" }
        };

        var statusArNames = new Dictionary<RepairStatus, string>
        {
            { RepairStatus.Received, "تم الاستلام" },
            { RepairStatus.UnderInspection, "تحت الفحص" },
            { RepairStatus.PendingCustomerApproval, "موافقة العميل" },
            { RepairStatus.InProgress, "قيد الإصلاح" },
            { RepairStatus.WaitingForParts, "في انتظار قطعة" },
            { RepairStatus.ReadyForPickup, "جاهز للاستلام" },
            { RepairStatus.Delivered, "تم التسليم" },
            { RepairStatus.Cancelled, "ملغي" }
        };

        var repairGroups = await _context.RepairOrders
            .GroupBy(r => r.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync();

        foreach (var s in Enum.GetValues<RepairStatus>())
        {
            var grp = repairGroups.FirstOrDefault(g => g.Status == s);
            model.RepairsByStatus.Add(new CategoryCountPoint
            {
                StatusNameAr = statusArNames[s],
                Count = grp?.Count ?? 0,
                Color = statusColors[s]
            });
        }

        // 5.1. Monthly Sales (last 6 months dynamically from actual database sales)
        string[] arabicMonths = { "", "يناير", "فبراير", "مارس", "أبريل", "مايو", "يونيو", "يوليو", "أغسطس", "سبتمبر", "أكتوبر", "نوفمبر", "ديسمبر" };
        var sixMonthsAgo = new DateTime(now.Year, now.Month, 1).AddMonths(-5);

        var monthlySalesRaw = await _context.Sales
            .Where(s => s.CreatedAt >= sixMonthsAgo && s.Status == SaleStatus.Completed)
            .GroupBy(s => new { s.CreatedAt.Year, s.CreatedAt.Month })
            .Select(g => new { g.Key.Year, g.Key.Month, Total = g.Sum(x => x.TotalAmount), Count = g.Count() })
            .ToListAsync();

        for (int i = 5; i >= 0; i--)
        {
            var targetDate = now.AddMonths(-i);
            var match = monthlySalesRaw.FirstOrDefault(m => m.Year == targetDate.Year && m.Month == targetDate.Month);
            string monthLabel = arabicMonths[targetDate.Month] + (targetDate.Year != now.Year ? $" {targetDate.Year}" : "");
            model.MonthlySales.Add(new MonthlySalesPoint
            {
                MonthName = monthLabel,
                Total = match?.Total ?? 0m,
                Count = match?.Count ?? 0
            });
        }

        // 7. Top Selling Products (Real products sold in system)
        model.TopSellingProducts = await _context.SaleItems
            .Where(i => i.Sale.Status == SaleStatus.Completed)
            .GroupBy(i => i.Product.Name)
            .Select(g => new TopSellingProductPoint
            {
                ProductName = g.Key,
                QuantitySold = g.Sum(x => x.Quantity),
                TotalRevenue = g.Sum(x => x.TotalPrice)
            })
            .OrderByDescending(x => x.QuantitySold)
            .Take(6)
            .ToListAsync();

        // 8. Top Brands (Real brand sales with exact calculated percentages and colors)
        string[] palette = { "#0284c7", "#06b6d4", "#f97316", "#10b981", "#8b5cf6", "#ec4899", "#64748b" };
        var brandSalesRaw = await _context.SaleItems
            .Where(i => i.Sale.Status == SaleStatus.Completed && i.Product.Brand != null)
            .GroupBy(i => i.Product.Brand!.Name)
            .Select(g => new
            {
                BrandName = g.Key,
                QuantitySold = g.Sum(x => x.Quantity),
                TotalRevenue = g.Sum(x => x.TotalPrice)
            })
            .OrderByDescending(x => x.QuantitySold)
            .Take(6)
            .ToListAsync();

        int totalBrandQty = brandSalesRaw.Sum(b => b.QuantitySold);
        int colorIdx = 0;
        foreach (var b in brandSalesRaw)
        {
            model.TopBrandsSales.Add(new BrandSalesPoint
            {
                BrandName = b.BrandName,
                QuantitySold = b.QuantitySold,
                TotalRevenue = b.TotalRevenue,
                Percentage = totalBrandQty > 0 ? Math.Round((double)b.QuantitySold / totalBrandQty * 100, 1) : 0,
                Color = palette[colorIdx % palette.Length]
            });
            colorIdx++;
        }

        // 9. Recent Sales & Repairs
        model.RecentSales = await _context.Sales
            .Include(s => s.Customer)
            .Include(s => s.User)
            .OrderByDescending(s => s.CreatedAt)
            .Take(5)
            .ToListAsync();

        model.RecentRepairs = await _context.RepairOrders
            .Include(r => r.Customer)
            .OrderByDescending(r => r.ReceivedAt)
            .Take(5)
            .ToListAsync();

        return model;
    }

    public async Task<SalesReportViewModel> GetSalesReportAsync(DateTime from, DateTime to)
    {
        var endOfTo = to.Date.AddDays(1).AddTicks(-1);

        var sales = await _context.Sales
            .Include(s => s.Customer)
            .Include(s => s.User)
            .Include(s => s.Items)
                .ThenInclude(i => i.Product)
                    .ThenInclude(p => p.Brand)
            .Where(s => s.CreatedAt >= from && s.CreatedAt <= endOfTo && s.Status == SaleStatus.Completed)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync();

        var allSoldItems = sales.SelectMany(s => s.Items).ToList();

        // 1. Devices & Accessories sold count
        int totalDevicesSold = allSoldItems
            .Where(i => i.Product != null && i.Product.ProductType == ProductType.Device)
            .Sum(i => i.Quantity);

        int totalAccessoriesSold = allSoldItems
            .Where(i => i.Product != null && i.Product.ProductType == ProductType.Accessory)
            .Sum(i => i.Quantity);

        // 2. Top Products overall
        var topProducts = allSoldItems
            .Where(i => i.Product != null)
            .GroupBy(i => i.Product.Name)
            .Select(g => new TopSellingProductPoint
            {
                ProductName = g.Key,
                QuantitySold = g.Sum(x => x.Quantity),
                TotalRevenue = g.Sum(x => x.TotalPrice)
            })
            .OrderByDescending(x => x.QuantitySold)
            .Take(10)
            .ToList();

        // 3. Top Selling Device specifically
        var topSellingDevice = allSoldItems
            .Where(i => i.Product != null && i.Product.ProductType == ProductType.Device)
            .GroupBy(i => i.Product.Name)
            .Select(g => new TopSellingProductPoint
            {
                ProductName = g.Key,
                QuantitySold = g.Sum(x => x.Quantity),
                TotalRevenue = g.Sum(x => x.TotalPrice)
            })
            .OrderByDescending(x => x.QuantitySold)
            .FirstOrDefault();

        // 4. Brand Sales Breakdown (for Devices)
        var brandGroups = allSoldItems
            .Where(i => i.Product != null && i.Product.ProductType == ProductType.Device && i.Product.Brand != null)
            .GroupBy(i => i.Product.Brand!.Name)
            .Select(g => new BrandSalesSummaryPoint
            {
                BrandName = g.Key,
                DevicesSold = g.Sum(x => x.Quantity),
                TotalRevenue = g.Sum(x => x.TotalPrice),
                Percentage = totalDevicesSold > 0 ? Math.Round((double)g.Sum(x => x.Quantity) / totalDevicesSold * 100, 1) : 0
            })
            .OrderByDescending(b => b.DevicesSold)
            .ToList();

        // 5. Low Stock Devices (in stock but <= MinStockLevel)
        var lowStockDevices = await _context.Products
            .Include(p => p.Brand)
            .Where(p => p.IsActive && p.ProductType == ProductType.Device && p.StockQuantity <= p.MinStockLevel)
            .OrderBy(p => p.StockQuantity)
            .Select(p => new ProductStockAlertPoint
            {
                ProductId = p.Id,
                Name = p.Name,
                BrandName = p.Brand != null ? p.Brand.Name : "-",
                CurrentStock = p.StockQuantity,
                MinStockLevel = p.MinStockLevel,
                SellingPrice = p.SellingPrice
            })
            .Take(15)
            .ToListAsync();

        // 6. Slow Moving Devices (Devices in stock that had 0 sales in this period)
        var soldProductIds = allSoldItems.Select(i => i.ProductId).Distinct().ToHashSet();
        var slowMovingDevices = await _context.Products
            .Include(p => p.Brand)
            .Where(p => p.IsActive && p.ProductType == ProductType.Device && p.StockQuantity > 0 && !soldProductIds.Contains(p.Id))
            .OrderByDescending(p => p.StockQuantity)
            .Select(p => new SlowMovingDevicePoint
            {
                ProductId = p.Id,
                Name = p.Name,
                BrandName = p.Brand != null ? p.Brand.Name : "-",
                CurrentStock = p.StockQuantity,
                SellingPrice = p.SellingPrice
            })
            .Take(15)
            .ToListAsync();

        decimal totalSub = sales.Sum(s => s.SubTotal);
        decimal totalDisc = sales.Sum(s => s.DiscountAmount);
        decimal totalNet = sales.Sum(s => s.TotalAmount);
        decimal totalCost = allSoldItems.Sum(i => (i.Product?.PurchasePrice ?? 0m) * i.Quantity);

        return new SalesReportViewModel
        {
            From = from,
            To = to,
            TotalSales = totalSub,
            TotalDiscounts = totalDisc,
            TotalNetSales = totalNet,
            TotalCost = totalCost,
            InvoiceCount = sales.Count,
            TotalDevicesSoldCount = totalDevicesSold,
            TotalAccessoriesSoldCount = totalAccessoriesSold,
            Sales = sales,
            TopProducts = topProducts,
            TopSellingDevice = topSellingDevice,
            BrandSalesBreakdown = brandGroups,
            LowStockDevices = lowStockDevices,
            SlowMovingDevices = slowMovingDevices
        };
    }

    public async Task<MaintenanceReportViewModel> GetMaintenanceReportAsync(DateTime from, DateTime to)
    {
        var endOfTo = to.Date.AddDays(1).AddTicks(-1);

        var repairs = await _context.RepairOrders
            .Include(r => r.Customer)
            .Include(r => r.TechnicianUser)
            .Include(r => r.RepairParts)
            .Where(r => r.ReceivedAt >= from && r.ReceivedAt <= endOfTo)
            .OrderByDescending(r => r.ReceivedAt)
            .ToListAsync();

        int deliveredCount = repairs.Count(r => r.Status == RepairStatus.Delivered);
        decimal totalRevenue = repairs.Where(r => r.Status != RepairStatus.Cancelled).Sum(r => r.TotalCost);
        decimal totalParts = repairs.Where(r => r.Status != RepairStatus.Cancelled).Sum(r => r.PartsCost);

        var statusArNames = new Dictionary<RepairStatus, string>
        {
            { RepairStatus.Received, "تم الاستلام" },
            { RepairStatus.UnderInspection, "تحت الفحص" },
            { RepairStatus.PendingCustomerApproval, "موافقة العميل" },
            { RepairStatus.InProgress, "قيد الإصلاح" },
            { RepairStatus.WaitingForParts, "في انتظار قطعة" },
            { RepairStatus.ReadyForPickup, "جاهز للاستلام" },
            { RepairStatus.Delivered, "تم التسليم" },
            { RepairStatus.Cancelled, "ملغي" }
        };

        var breakdown = repairs
            .GroupBy(r => r.Status)
            .Select(g => new CategoryCountPoint
            {
                StatusNameAr = statusArNames[g.Key],
                Count = g.Count()
            })
            .ToList();

        return new MaintenanceReportViewModel
        {
            From = from,
            To = to,
            TotalRepairsReceived = repairs.Count,
            TotalRepairsDelivered = deliveredCount,
            TotalRevenue = totalRevenue,
            TotalPartsCost = totalParts,
            StatusBreakdown = breakdown,
            Repairs = repairs
        };
    }

    public async Task<InventoryReportViewModel> GetInventoryReportAsync()
    {
        var products = await _context.Products.Where(p => p.IsActive).ToListAsync();
        var spareParts = await _context.SpareParts.ToListAsync();

        return new InventoryReportViewModel
        {
            TotalProductsCount = products.Sum(p => p.StockQuantity),
            TotalDevicesCount = products.Where(p => p.ProductType == ProductType.Device).Sum(p => p.StockQuantity),
            TotalAccessoriesCount = products.Where(p => p.ProductType == ProductType.Accessory).Sum(p => p.StockQuantity),
            TotalSparePartsCount = spareParts.Sum(sp => sp.StockQuantity),
            TotalInventoryCostValue = products.Sum(p => p.PurchasePrice * p.StockQuantity) + spareParts.Sum(sp => sp.PurchasePrice * sp.StockQuantity),
            TotalInventorySellingValue = products.Sum(p => p.SellingPrice * p.StockQuantity) + spareParts.Sum(sp => sp.SellingPrice * sp.StockQuantity),
            LowStockProducts = await _context.Products.Include(p => p.Brand).Where(p => p.IsActive && p.StockQuantity <= p.MinStockLevel).ToListAsync(),
            LowStockSpareParts = await _context.SpareParts.Include(sp => sp.Brand).Where(sp => sp.StockQuantity <= sp.MinStockLevel).ToListAsync()
        };
    }
}
