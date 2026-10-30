using Infoman.Models;
using Microsoft.EntityFrameworkCore;

namespace Infoman.Data
{
    public class ConnectionDatabase : DbContext
    {
  

        public ConnectionDatabase(DbContextOptions<ConnectionDatabase> options) : base(options) { }

        public DbSet<User> Users { get; set; }
        public DbSet<Product> Products { get; set; }
        public DbSet<Order> Orders { get; set; }
        public DbSet<OrderItem> OrderItems { get; set; }
        public DbSet<RawMaterial> RawMaterials { get; set; }
        public DbSet<ProductSize> ProductSizes { get; set; }
        public DbSet<RecipeItem> RecipeItems { get; set; }
        public DbSet<WasteLog> WasteLogs { get; set; }
        public DbSet<ProductionUsage> ProductionUsages { get; set; }
        public DbSet<RawMaterialBatch> RawMaterialBatches { get; set; }
        public DbSet<EmployeeAttendance> EmployeeAttendances { get; set; }
        public DbSet<EmployeePerformance> EmployeePerformances { get; set; }
        public DbSet<Supplier> Suppliers { get; set; }
        public DbSet<Receiving> Receivings { get; set; }
        public DbSet<ReceivingItem> ReceivingItems { get; set; }
        public DbSet<RejectedForReturn> RejectedForReturns { get; set; }
        public DbSet<InventoryMovement> InventoryMovements { get; set; }
        public DbSet<RefundTransaction> RefundTransactions { get; set; }
        public DbSet<VoidLog> VoidLogs { get; set; }
        public DbSet<PendingRequest> PendingRequests { get; set; }
        public DbSet<SupplierReturn> SupplierReturns { get; set; }
        public DbSet<OrderAdjustment> OrderAdjustments { get; set; }
        public DbSet<ExchangeTransaction> ExchangeTransactions { get; set; }
        public DbSet<ExchangeItem> ExchangeItems { get; set; }
        public DbSet<Payment> Payments { get; set; }
        public DbSet<StockAdjustmentLog> StockAdjustmentLogs { get; set; }
        public DbSet<ItemDispositionStatus> ItemDispositionStatuses { get; set; }
        public DbSet<Category> Categories { get; set; }
        public DbSet<Expense> Expenses { get; set; }
        public DbSet<CashWithdrawal> CashWithdrawals { get; set; }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // ==========================================
            // ✅ I-RESTRICT LAHAT NG SECONDARY RELATIONSHIPS
            // ==========================================

            // OrderItem → Product (Restrict)
            modelBuilder.Entity<OrderItem>()
                .HasOne(oi => oi.Product)
                .WithMany()
                .HasForeignKey(oi => oi.ProductId)
                .OnDelete(DeleteBehavior.Restrict);

            // OrderItem → ProductSize (Restrict)
            modelBuilder.Entity<OrderItem>()
                .HasOne(oi => oi.ProductSize)
                .WithMany()
                .HasForeignKey(oi => oi.ProductSizeId)
                .OnDelete(DeleteBehavior.Restrict);

            // OrderItem → DispositionStatus (Restrict)
            modelBuilder.Entity<OrderItem>()
                .HasOne(oi => oi.DispositionStatus)
                .WithMany()
                .HasForeignKey(oi => oi.DispositionStatusId)
                .OnDelete(DeleteBehavior.Restrict);

            // SupplierReturn → ReceivingItem (Restrict)
            modelBuilder.Entity<SupplierReturn>()
                .HasOne(sr => sr.ReceivingItem)
                .WithMany()
                .HasForeignKey(sr => sr.ReceivingItemId)
                .OnDelete(DeleteBehavior.Restrict);

            // SupplierReturn → Receiving (Restrict)
            modelBuilder.Entity<SupplierReturn>()
                .HasOne(sr => sr.Receiving)
                .WithMany()
                .HasForeignKey(sr => sr.ReceivingId)
                .OnDelete(DeleteBehavior.Restrict);

            // RefundTransaction → Order (Restrict)
            modelBuilder.Entity<RefundTransaction>()
                .HasOne(rt => rt.OriginalOrder)
                .WithMany()
                .HasForeignKey(rt => rt.OriginalOrderId)
                .OnDelete(DeleteBehavior.Restrict);

            // RefundTransaction → OrderAdjustment (Restrict)
            modelBuilder.Entity<RefundTransaction>()
                .HasOne(rt => rt.Adjustment)
                .WithMany()
                .HasForeignKey(rt => rt.AdjustmentId)
                .OnDelete(DeleteBehavior.Restrict);

            // RefundTransaction → User (Restrict)
            modelBuilder.Entity<RefundTransaction>()
                .HasOne(rt => rt.ProcessedByUser)
                .WithMany()
                .HasForeignKey(rt => rt.ProcessedBy)
                .OnDelete(DeleteBehavior.Restrict);

            // OrderAdjustment → Order (Restrict)
            modelBuilder.Entity<OrderAdjustment>()
                .HasOne(oa => oa.OriginalOrder)
                .WithMany()
                .HasForeignKey(oa => oa.OriginalOrderId)
                .OnDelete(DeleteBehavior.Restrict);

            // OrderAdjustment → User (Restrict)
            modelBuilder.Entity<OrderAdjustment>()
                .HasOne(oa => oa.CreatedByUser)
                .WithMany()
                .HasForeignKey(oa => oa.CreatedBy)
                .OnDelete(DeleteBehavior.Restrict);

            // ExchangeTransaction → Order (Restrict)
            modelBuilder.Entity<ExchangeTransaction>()
                .HasOne(et => et.OriginalOrder)
                .WithMany()
                .HasForeignKey(et => et.OriginalOrderId)
                .OnDelete(DeleteBehavior.Restrict);

            // ExchangeTransaction → User (Restrict)
            modelBuilder.Entity<ExchangeTransaction>()
                .HasOne(et => et.CreatedByUser)
                .WithMany()
                .HasForeignKey(et => et.CreatedBy)
                .OnDelete(DeleteBehavior.Restrict);

            // ✅ ExchangeItem → OrderItem (Restrict) -- BAGONG DAGDAG
            modelBuilder.Entity<ExchangeItem>()
                .HasOne(ei => ei.OriginalOrderItem)
                .WithMany()
                .HasForeignKey(ei => ei.OriginalOrderItemId)
                .OnDelete(DeleteBehavior.Restrict);

            // ✅ ExchangeItem → ProductSize (Restrict) -- BAGONG DAGDAG
            modelBuilder.Entity<ExchangeItem>()
                .HasOne(ei => ei.ReplacementProductSize)
                .WithMany()
                .HasForeignKey(ei => ei.ReplacementProductSizeId)
                .OnDelete(DeleteBehavior.Restrict);

            // ✅ ExchangeItem → ExchangeTransaction (Restrict) -- BAGONG DAGDAG
            modelBuilder.Entity<ExchangeItem>()
                .HasOne(ei => ei.ExchangeTransaction)
                .WithMany()
                .HasForeignKey(ei => ei.ExchangeId)
                .OnDelete(DeleteBehavior.Restrict);

            // ==========================================
            // ✅ DECIMAL PRECISION CONFIGURATIONS
            // ==========================================
            modelBuilder.Entity<Product>().Property(p => p.Price).HasPrecision(10, 2);
            modelBuilder.Entity<Order>().Property(p => p.TotalAmount).HasPrecision(10, 2);
            modelBuilder.Entity<OrderItem>().Property(p => p.Price).HasPrecision(10, 2);
            modelBuilder.Entity<RawMaterial>().Property(p => p.Price).HasPrecision(10, 2);
            modelBuilder.Entity<RawMaterial>().Property(p => p.Stock).HasPrecision(10, 2);
            modelBuilder.Entity<ProductSize>().Property(p => p.Price).HasPrecision(10, 2);
            modelBuilder.Entity<RecipeItem>().Property(r => r.Quantity).HasPrecision(10, 2);
            modelBuilder.Entity<ProductSize>().Property(p => p.Margin).HasPrecision(10, 2);
            modelBuilder.Entity<WasteLog>().Property(p => p.Quantity).HasPrecision(10, 2);
            modelBuilder.Entity<RawMaterialBatch>().Property(b => b.QuantityReceived).HasPrecision(18, 2);
            modelBuilder.Entity<RawMaterialBatch>().Property(b => b.QuantityRemaining).HasPrecision(18, 2);
            modelBuilder.Entity<RawMaterialBatch>().Property(b => b.UnitPrice).HasPrecision(18, 2);
            modelBuilder.Entity<ProductionUsage>().Property(p => p.QuantityUsed).HasPrecision(18, 2);

            // ==========================================
            // ✅ ISSUE #27 — UNIQUE INDEXES
            // ==========================================

            // RawMaterial.SKU — unique, pero allow NULL (soft-deleted/legacy rows)
            modelBuilder.Entity<RawMaterial>()
                .HasIndex(r => r.SKU)
                .IsUnique()
                .HasFilter("[SKU] IS NOT NULL AND [SKU] <> ''");

            // Product.SKU — unique
            modelBuilder.Entity<Product>()
                .HasIndex(p => p.SKU)
                .IsUnique();

            // ProductSize.SKU — unique din (may GeneratedSizeSKU din)
            modelBuilder.Entity<ProductSize>()
                .HasIndex(ps => ps.SKU)
                .IsUnique()
                .HasFilter("[SKU] IS NOT NULL AND [SKU] <> ''");

            // Order.ReceiptNumber — unique
            modelBuilder.Entity<Order>()
                .HasIndex(o => o.ReceiptNumber)
                .IsUnique()
                .HasFilter("[ReceiptNumber] IS NOT NULL AND [ReceiptNumber] <> ''");

            // SupplierReturn.ReturnNumber — unique
            modelBuilder.Entity<SupplierReturn>()
                .HasIndex(sr => sr.ReturnNumber)
                .IsUnique();

            // Receiving.DRNumber — unique per supplier (hindi globally unique)
            // Kung globally unique ang gusto mo, tanggalin ang HasFilter at hayaan lang IsUnique()
            modelBuilder.Entity<Receiving>()
                .HasIndex(r => r.DRNumber)
                .IsUnique();

            // EmployeeAttendance (UserId, Date) — one record per employee per day
            modelBuilder.Entity<EmployeeAttendance>()
                .HasIndex(a => new { a.UserId, a.Date })
                .IsUnique();

            var seedDate = new DateTime(2026, 1, 1);

            modelBuilder.Entity<Category>(e =>
            {
                // Type naka-store bilang text: 'RawMaterial' / 'Product'
                e.Property(c => c.Type).HasConversion<string>().HasMaxLength(20);

                // Walang duplicate name sa loob ng iisang Type (case-insensitive dahil sa SQL collation)
                e.HasIndex(c => new { c.Type, c.Name }).IsUnique();

                // Default categories. Ang Id 1-5 ay sinadyang kapareho ng lumang enum order + 1
                // (Fruits=0 -> 1, Dairy=1 -> 2, ...) para ma-backfill ang existing raw materials.
                e.HasData(
                    new Category { CategoryId = 1, Name = "Fruits", Type = CategoryType.RawMaterial, IsActive = true, CreatedAt = seedDate },
                    new Category { CategoryId = 2, Name = "Dairy", Type = CategoryType.RawMaterial, IsActive = true, CreatedAt = seedDate },
                    new Category { CategoryId = 3, Name = "Others", Type = CategoryType.RawMaterial, IsActive = true, CreatedAt = seedDate },
                    new Category { CategoryId = 4, Name = "Sweeteners", Type = CategoryType.RawMaterial, IsActive = true, CreatedAt = seedDate },
                    new Category { CategoryId = 5, Name = "Toppings", Type = CategoryType.RawMaterial, IsActive = true, CreatedAt = seedDate },
                    new Category { CategoryId = 6, Name = "Fruit Shake", Type = CategoryType.Product, IsActive = true, CreatedAt = seedDate },
                    new Category { CategoryId = 7, Name = "Milk Shake", Type = CategoryType.Product, IsActive = true, CreatedAt = seedDate },
                    new Category { CategoryId = 8, Name = "Specialty", Type = CategoryType.Product, IsActive = true, CreatedAt = seedDate }
                );
            });

            // Restrict = hindi pwedeng i-delete ang category kung may raw material na gumagamit (extra proteksyon sa DB level)
            modelBuilder.Entity<RawMaterial>()
                .HasOne(r => r.CategoryInfo)
                .WithMany(c => c.RawMaterials)
                .HasForeignKey(r => r.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);


        }

    }
}