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

            modelBuilder.Entity<User>().HasData(
                new User
                {
                    Id = 1,
                    Username = "staff",
                    Password = "staff123", // learning only; later hash this
                    Role = "Staff"
                }
            );
        }

    }
}