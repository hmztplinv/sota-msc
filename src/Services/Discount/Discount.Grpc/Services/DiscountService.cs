using Discount.Grpc.Data;
using Discount.Grpc.Models;
using Grpc.Core;
using Mapster;
using Microsoft.EntityFrameworkCore;

namespace Discount.Grpc.Services;

public sealed class DiscountService(DiscountDbContext dbContext, ILogger<DiscountService> logger)
    : DiscountProtoService.DiscountProtoServiceBase
{
    public override async Task<CouponModel> GetDiscount(GetDiscountRequest request, ServerCallContext context)
    {
        var coupon = await dbContext.Coupons
            .FirstOrDefaultAsync(c => c.ProductName == request.ProductName);

        if (coupon is null)
        {
            // Kupon bulunamadı — amount=0 döndür (no discount)
            logger.LogInformation("Discount not found for product: {ProductName}. Returning no discount.", request.ProductName);
            return new CouponModel
            {
                ProductName = request.ProductName,
                Description = "No discount",
                Amount = 0
            };
        }

        logger.LogInformation("Discount retrieved for product: {ProductName}, Amount: {Amount}", coupon.ProductName, coupon.Amount);
        return coupon.Adapt<CouponModel>();
    }

    public override async Task<CouponModel> CreateDiscount(CreateDiscountRequest request, ServerCallContext context)
    {
        var coupon = request.Adapt<Coupon>();

        if (string.IsNullOrEmpty(coupon.ProductName))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "ProductName is required."));

        dbContext.Coupons.Add(coupon);
        await dbContext.SaveChangesAsync();

        logger.LogInformation("Discount created for product: {ProductName}, Amount: {Amount}", coupon.ProductName, coupon.Amount);
        return coupon.Adapt<CouponModel>();
    }

    public override async Task<CouponModel> UpdateDiscount(UpdateDiscountRequest request, ServerCallContext context)
    {
        var coupon = await dbContext.Coupons.FindAsync(request.Id)
            ?? throw new RpcException(new Status(StatusCode.NotFound, $"Discount with Id={request.Id} not found."));

        // Mevcut entity'yi güncelle
        request.Adapt(coupon);
        dbContext.Coupons.Update(coupon);
        await dbContext.SaveChangesAsync();

        logger.LogInformation("Discount updated for product: {ProductName}, Amount: {Amount}", coupon.ProductName, coupon.Amount);
        return coupon.Adapt<CouponModel>();
    }

    public override async Task<DeleteDiscountResponse> DeleteDiscount(DeleteDiscountRequest request, ServerCallContext context)
    {
        var coupon = await dbContext.Coupons
            .FirstOrDefaultAsync(c => c.ProductName == request.ProductName)
            ?? throw new RpcException(new Status(StatusCode.NotFound, $"Discount for product '{request.ProductName}' not found."));

        dbContext.Coupons.Remove(coupon);
        await dbContext.SaveChangesAsync();

        logger.LogInformation("Discount deleted for product: {ProductName}", request.ProductName);
        return new DeleteDiscountResponse { Success = true };
    }
}
