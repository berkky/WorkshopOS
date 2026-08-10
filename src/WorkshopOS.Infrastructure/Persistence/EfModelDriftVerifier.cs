using Microsoft.EntityFrameworkCore;
using WorkshopOS.Infrastructure.Persistence;

namespace WorkshopOS.Infrastructure.Persistence;

public static class EfModelDriftVerifier
{
    public static bool HasPendingModelChanges(AppDbContext context) =>
        context.Database.HasPendingModelChanges();

    public static int GetMigrationCount(AppDbContext context) =>
        context.Database.GetMigrations().Count();
}
