using Doctor.Web.Data;
using Doctor.Web.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Doctor.Web.Services;

public interface IInventoryService
{
    Task<bool> DeductProductStockAsync(int productId, int quantity, int userId, string? referenceId, string? notes);
    Task<bool> AddProductStockAsync(int productId, int quantity, int userId, string? referenceId, string? notes, InventoryTransactionType type = InventoryTransactionType.Purchase);
    Task<bool> DeductSparePartStockAsync(int sparePartId, int quantity, int userId, string? referenceId, string? notes);
    Task<bool> AddSparePartStockAsync(int sparePartId, int quantity, int userId, string? referenceId, string? notes, InventoryTransactionType type = InventoryTransactionType.Purchase);
    Task<List<Product>> GetLowStockProductsAsync();
    Task<List<SparePart>> GetLowStockSparePartsAsync();
    Task<List<InventoryTransaction>> GetTransactionsAsync(int? productId = null, int? sparePartId = null, int limit = 50);
}

public class InventoryService : IInventoryService
{
    private readonly ApplicationDbContext _context;
    private readonly INotificationService _notificationService;

    public InventoryService(ApplicationDbContext context, INotificationService notificationService)
    {
        _context = context;
        _notificationService = notificationService;
    }

    public async Task<bool> DeductProductStockAsync(int productId, int quantity, int userId, string? referenceId, string? notes)
    {
        var product = await _context.Products.FindAsync(productId);
        if (product == null)
        {
            return false;
        }

        int before = product.StockQuantity;
        product.StockQuantity -= quantity;
        int after = product.StockQuantity;

        var tx = new InventoryTransaction
        {
            ProductId = productId,
            TransactionType = InventoryTransactionType.Sale,
            Quantity = -quantity,
            QuantityBefore = before,
            QuantityAfter = after,
            UserId = userId,
            ReferenceId = referenceId,
            Notes = notes,
            CreatedAt = DateTime.UtcNow
        };

        _context.InventoryTransactions.Add(tx);
        await _context.SaveChangesAsync();

        // Check low stock threshold
        if (product.StockQuantity <= product.MinStockLevel)
        {
            await _notificationService.AddNotificationAsync(
                title: "تنبيه مخزون منخفض",
                message: $"المنتج ({product.Name}) شارف على النفاد. الكمية الحالية: {product.StockQuantity}.",
                type: NotificationType.LowStock,
                relatedEntity: "Product",
                relatedId: product.Id);
        }

        return true;
    }

    public async Task<bool> AddProductStockAsync(int productId, int quantity, int userId, string? referenceId, string? notes, InventoryTransactionType type = InventoryTransactionType.Purchase)
    {
        var product = await _context.Products.FindAsync(productId);
        if (product == null || quantity <= 0)
        {
            return false;
        }

        int before = product.StockQuantity;
        product.StockQuantity += quantity;
        int after = product.StockQuantity;

        var tx = new InventoryTransaction
        {
            ProductId = productId,
            TransactionType = type,
            Quantity = quantity,
            QuantityBefore = before,
            QuantityAfter = after,
            UserId = userId,
            ReferenceId = referenceId,
            Notes = notes,
            CreatedAt = DateTime.UtcNow
        };

        _context.InventoryTransactions.Add(tx);
        await _context.SaveChangesAsync();

        return true;
    }

    public async Task<bool> DeductSparePartStockAsync(int sparePartId, int quantity, int userId, string? referenceId, string? notes)
    {
        var part = await _context.SpareParts.FindAsync(sparePartId);
        if (part == null || part.StockQuantity < quantity)
        {
            return false;
        }

        int before = part.StockQuantity;
        part.StockQuantity -= quantity;
        int after = part.StockQuantity;

        var tx = new InventoryTransaction
        {
            SparePartId = sparePartId,
            TransactionType = InventoryTransactionType.RepairUsage,
            Quantity = -quantity,
            QuantityBefore = before,
            QuantityAfter = after,
            UserId = userId,
            ReferenceId = referenceId,
            Notes = notes,
            CreatedAt = DateTime.UtcNow
        };

        _context.InventoryTransactions.Add(tx);
        await _context.SaveChangesAsync();

        if (part.StockQuantity <= part.MinStockLevel)
        {
            await _notificationService.AddNotificationAsync(
                title: "تنبيه مخزون قطعة غيار",
                message: $"قطعة الغيار ({part.Name}) شارفت على النفاد. الرصيد المتبقي: {part.StockQuantity}.",
                type: NotificationType.LowSparePart,
                relatedEntity: "SparePart",
                relatedId: part.Id);
        }

        return true;
    }

    public async Task<bool> AddSparePartStockAsync(int sparePartId, int quantity, int userId, string? referenceId, string? notes, InventoryTransactionType type = InventoryTransactionType.Purchase)
    {
        var part = await _context.SpareParts.FindAsync(sparePartId);
        if (part == null || quantity <= 0)
        {
            return false;
        }

        int before = part.StockQuantity;
        part.StockQuantity += quantity;
        int after = part.StockQuantity;

        var tx = new InventoryTransaction
        {
            SparePartId = sparePartId,
            TransactionType = type,
            Quantity = quantity,
            QuantityBefore = before,
            QuantityAfter = after,
            UserId = userId,
            ReferenceId = referenceId,
            Notes = notes,
            CreatedAt = DateTime.UtcNow
        };

        _context.InventoryTransactions.Add(tx);
        await _context.SaveChangesAsync();

        return true;
    }

    public async Task<List<Product>> GetLowStockProductsAsync()
    {
        return await _context.Products
            .Include(p => p.Brand)
            .Include(p => p.ProductCategory)
            .Where(p => p.IsActive && p.StockQuantity <= p.MinStockLevel)
            .OrderBy(p => p.StockQuantity)
            .ToListAsync();
    }

    public async Task<List<SparePart>> GetLowStockSparePartsAsync()
    {
        return await _context.SpareParts
            .Include(sp => sp.Brand)
            .Where(sp => sp.StockQuantity <= sp.MinStockLevel)
            .OrderBy(sp => sp.StockQuantity)
            .ToListAsync();
    }

    public async Task<List<InventoryTransaction>> GetTransactionsAsync(int? productId = null, int? sparePartId = null, int limit = 50)
    {
        var query = _context.InventoryTransactions
            .Include(t => t.Product)
            .Include(t => t.SparePart)
            .Include(t => t.User)
            .AsQueryable();

        if (productId.HasValue)
        {
            query = query.Where(t => t.ProductId == productId.Value);
        }

        if (sparePartId.HasValue)
        {
            query = query.Where(t => t.SparePartId == sparePartId.Value);
        }

        return await query
            .OrderByDescending(t => t.CreatedAt)
            .Take(limit)
            .ToListAsync();
    }
}
