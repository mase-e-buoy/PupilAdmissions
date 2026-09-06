using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PupilAdmissions.Infrastructure;

/// <summary>
/// Design-time factory so <c>dotnet ef migrations add</c> /
/// <c>database update</c> can create an <see cref="AppDbContext"/> without
/// needing the Web host's full DI container to spin up. The connection
/// string here is used only at design time; the running app uses the one
/// registered via <see cref="DependencyInjection.AddInfrastructure"/> from
/// Web's <c>appsettings.json</c>.
/// </summary>
public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseSqlite("Data Source=App_Data/pupiladmissions.db");
        return new AppDbContext(optionsBuilder.Options);
    }
}
