using Microsoft.EntityFrameworkCore;
using Doctor.Web.Models.Entities;

namespace Doctor.Web.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();

    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Brand> Brands => Set<Brand>();
    public DbSet<ProductCategory> ProductCategories => Set<ProductCategory>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<InventoryTransaction> InventoryTransactions => Set<InventoryTransaction>();

    public DbSet<Sale> Sales => Set<Sale>();
    public DbSet<SaleItem> SaleItems => Set<SaleItem>();
    public DbSet<Payment> Payments => Set<Payment>();

    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<SupplyOrder> SupplyOrders => Set<SupplyOrder>();
    public DbSet<SupplyOrderItem> SupplyOrderItems => Set<SupplyOrderItem>();
    public DbSet<SupplierPayment> SupplierPayments => Set<SupplierPayment>();
    public DbSet<CashShift> CashShifts => Set<CashShift>();

    public DbSet<SparePart> SpareParts => Set<SparePart>();
    public DbSet<RepairOrder> RepairOrders => Set<RepairOrder>();
    public DbSet<RepairStatusHistory> RepairStatusHistories => Set<RepairStatusHistory>();
    public DbSet<RepairPart> RepairParts => Set<RepairPart>();

    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<NotificationLog> NotificationLogs => Set<NotificationLog>();
    public DbSet<Setting> Settings => Set<Setting>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Expense> Expenses => Set<Expense>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // RolePermission Composite Key
        modelBuilder.Entity<RolePermission>()
            .HasKey(rp => new { rp.RoleId, rp.PermissionId });

        modelBuilder.Entity<RolePermission>()
            .HasOne(rp => rp.Role)
            .WithMany(r => r.RolePermissions)
            .HasForeignKey(rp => rp.RoleId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<RolePermission>()
            .HasOne(rp => rp.Permission)
            .WithMany(p => p.RolePermissions)
            .HasForeignKey(rp => rp.PermissionId)
            .OnDelete(DeleteBehavior.Cascade);

        // User indexes
        modelBuilder.Entity<User>()
            .HasIndex(u => u.Username)
            .IsUnique();

        modelBuilder.Entity<User>()
            .HasOne(u => u.Role)
            .WithMany(r => r.Users)
            .HasForeignKey(u => u.RoleId)
            .OnDelete(DeleteBehavior.Restrict);

        // Customer indexes
        modelBuilder.Entity<Customer>()
            .HasIndex(c => c.PhoneNumber);

        // Product indexes & config
        modelBuilder.Entity<Product>()
            .HasIndex(p => p.Barcode);

        modelBuilder.Entity<Product>()
            .HasOne(p => p.Brand)
            .WithMany(b => b.Products)
            .HasForeignKey(p => p.BrandId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<Product>()
            .HasOne(p => p.ProductCategory)
            .WithMany(c => c.Products)
            .HasForeignKey(p => p.ProductCategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        // Inventory Transactions
        modelBuilder.Entity<InventoryTransaction>()
            .HasOne(t => t.Product)
            .WithMany(p => p.InventoryTransactions)
            .HasForeignKey(t => t.ProductId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<InventoryTransaction>()
            .HasOne(t => t.SparePart)
            .WithMany(sp => sp.InventoryTransactions)
            .HasForeignKey(t => t.SparePartId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<InventoryTransaction>()
            .HasOne(t => t.User)
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.SetNull);

        // Sale & Invoices
        modelBuilder.Entity<Sale>()
            .HasIndex(s => s.InvoiceNumber)
            .IsUnique();

        modelBuilder.Entity<Sale>()
            .HasOne(s => s.Customer)
            .WithMany(c => c.Sales)
            .HasForeignKey(s => s.CustomerId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<Sale>()
            .HasOne(s => s.User)
            .WithMany(u => u.Sales)
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<SaleItem>()
            .HasOne(si => si.Sale)
            .WithMany(s => s.Items)
            .HasForeignKey(si => si.SaleId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<SaleItem>()
            .HasOne(si => si.Product)
            .WithMany(p => p.SaleItems)
            .HasForeignKey(si => si.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        // Supplier Configurations
        modelBuilder.Entity<Supplier>()
            .HasIndex(s => s.PhoneNumber);

        modelBuilder.Entity<SupplyOrder>()
            .HasIndex(so => so.OrderNumber)
            .IsUnique();

        modelBuilder.Entity<SupplyOrder>()
            .HasOne(so => so.Supplier)
            .WithMany(s => s.SupplyOrders)
            .HasForeignKey(so => so.SupplierId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<SupplyOrderItem>()
            .HasOne(soi => soi.SupplyOrder)
            .WithMany(so => so.Items)
            .HasForeignKey(soi => soi.SupplyOrderId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<SupplyOrderItem>()
            .HasOne(soi => soi.Product)
            .WithMany()
            .HasForeignKey(soi => soi.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<SupplierPayment>()
            .HasOne(sp => sp.Supplier)
            .WithMany(s => s.Payments)
            .HasForeignKey(sp => sp.SupplierId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<SupplierPayment>()
            .HasOne(sp => sp.SupplyOrder)
            .WithMany(so => so.Payments)
            .HasForeignKey(sp => sp.SupplyOrderId)
            .OnDelete(DeleteBehavior.SetNull);

        // Cash Shift
        modelBuilder.Entity<CashShift>()
            .HasOne(cs => cs.User)
            .WithMany()
            .HasForeignKey(cs => cs.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        // RepairOrder & Status & Parts
        modelBuilder.Entity<RepairOrder>()
            .HasIndex(ro => ro.RepairCode)
            .IsUnique();

        modelBuilder.Entity<RepairOrder>()
            .HasOne(ro => ro.Customer)
            .WithMany(c => c.RepairOrders)
            .HasForeignKey(ro => ro.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<RepairOrder>()
            .HasOne(ro => ro.CreatedByUser)
            .WithMany()
            .HasForeignKey(ro => ro.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<RepairOrder>()
            .HasOne(ro => ro.TechnicianUser)
            .WithMany()
            .HasForeignKey(ro => ro.TechnicianUserId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<RepairStatusHistory>()
            .HasOne(h => h.RepairOrder)
            .WithMany(ro => ro.StatusHistories)
            .HasForeignKey(h => h.RepairOrderId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<RepairStatusHistory>()
            .HasOne(h => h.ChangedByUser)
            .WithMany()
            .HasForeignKey(h => h.ChangedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<RepairPart>()
            .HasOne(rp => rp.RepairOrder)
            .WithMany(ro => ro.RepairParts)
            .HasForeignKey(rp => rp.RepairOrderId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<RepairPart>()
            .HasOne(rp => rp.SparePart)
            .WithMany(sp => sp.RepairParts)
            .HasForeignKey(rp => rp.SparePartId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Payment>()
            .HasOne(p => p.Sale)
            .WithMany(s => s.Payments)
            .HasForeignKey(p => p.SaleId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Payment>()
            .HasOne(p => p.RepairOrder)
            .WithMany(ro => ro.Payments)
            .HasForeignKey(p => p.RepairOrderId)
            .OnDelete(DeleteBehavior.Cascade);

        // Settings index
        modelBuilder.Entity<Setting>()
            .HasIndex(s => s.Key)
            .IsUnique();

        // Expense mapping
        modelBuilder.Entity<Expense>()
            .HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
