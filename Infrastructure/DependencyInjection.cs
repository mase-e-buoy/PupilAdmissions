using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PupilAdmissions.Application;

namespace PupilAdmissions.Infrastructure;

/// <summary>
/// Registers the concrete EF Core/SQLite implementations. Only
/// <c>Program.cs</c> (the composition root, AD-8) is permitted to call
/// this — no Web page or controller references Infrastructure directly.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<AppDbContext>(options => options.UseSqlite(connectionString));
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        return services;
    }
}
