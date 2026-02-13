using Discount.Grpc.Models;
using Microsoft.EntityFrameworkCore;

namespace Discount.Grpc.Data;

public sealed class DiscountDbContext(DbContextOptions<DiscountDbContext> options) 
    : DbContext(options)
{
    public DbSet<Coupon> Coupons => Set<Coupon>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Coupon>(entity =>
        {
            entity.HasKey(c => c.Id);
            
            entity.Property(c => c.ProductName)
                  .IsRequired()
                  .HasMaxLength(200);

            entity.Property(c => c.Description)
                  .HasMaxLength(500);

            // Seed Data — başlangıç kuponları
            entity.HasData(
                new Coupon { Id = 1, ProductName = "IPhone X", Description = "IPhone X Discount", Amount = 150 },
                new Coupon { Id = 2, ProductName = "Samsung 10", Description = "Samsung 10 Discount", Amount = 100 }
            );
        });
    }
}
