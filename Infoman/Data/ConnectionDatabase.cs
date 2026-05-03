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
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Product>()
                .Property(p => p.Price)
                .HasPrecision(10, 2);
            modelBuilder.Entity<Order>()
               .Property(p => p.TotalAmount)
               .HasPrecision(10, 2);
            modelBuilder.Entity<OrderItem>()
               .Property(p => p.Price)
               .HasPrecision(10, 2);
            modelBuilder.Entity<RawMaterial>()
               .Property(p => p.Price)
               .HasPrecision(10, 2);
            modelBuilder.Entity<RawMaterial>()
               .Property(p => p.Stock)
               .HasPrecision(10, 2);
            modelBuilder.Entity<ProductSize>()
                .Property(p => p.Price)
                .HasPrecision(10,2);
            modelBuilder.Entity<RecipeItem>()
               .Property(r => r.Quantity)
               .HasPrecision(10, 2);
            modelBuilder.Entity<ProductSize>()
               .Property(p => p.Margin)
               .HasPrecision(10, 2);
            modelBuilder.Entity<WasteLog>()
                .Property(p => p.Quantity)
                .HasPrecision(10, 2);
            modelBuilder.Entity<RawMaterialBatch>()
                .Property(b => b.QuantityReceived)
               .HasPrecision(18, 2);

            modelBuilder.Entity<RawMaterialBatch>()
                .Property(b => b.QuantityRemaining)
                .HasPrecision(18, 2);

            modelBuilder.Entity<RawMaterialBatch>()
                .Property(b => b.UnitPrice)
                .HasPrecision(18, 2);

            // For ProductionUsage table
            modelBuilder.Entity<ProductionUsage>()
                .Property(p => p.QuantityUsed)
                .HasPrecision(18, 2);
            // Optional: Configure table mappings kung hindi conventional ang pangalan
            modelBuilder.Entity<RawMaterialBatch>()
                .ToTable("RawMaterialBatches");

            modelBuilder.Entity<ProductionUsage>()
                .ToTable("ProductionUsages");

            
        }

    }
}