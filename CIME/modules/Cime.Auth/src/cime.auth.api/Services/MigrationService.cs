
using Microsoft.EntityFrameworkCore;
using ProSales.Repository.Contexts;

namespace Services;
public class MigrationService
{
    public static void ApplyMigrations(DefaultContext context)
    {
        context.Database.Migrate();
    }
}
