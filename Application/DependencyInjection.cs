using Microsoft.Extensions.DependencyInjection;

namespace PupilAdmissions.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<PupilService>();
        return services;
    }
}
