using Microsoft.EntityFrameworkCore;

namespace Discount.Grpc.Data;

public static class Extensions
{
    public static async Task<IApplicationBuilder> UseMigrationAsync(this IApplicationBuilder app)
    {
        using var scope = app.ApplicationServices.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DiscountDbContext>();
        await dbContext.Database.MigrateAsync();
        return app;
    }
}
