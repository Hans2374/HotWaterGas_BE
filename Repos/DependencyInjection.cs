using Microsoft.Extensions.DependencyInjection;
using Repos.Implementations;
using Repos.Interfaces;

namespace Repos;

public static class DependencyInjection
{
    public static IServiceCollection AddRepos(this IServiceCollection services)
    {
        services.AddScoped<IUserRepository, UserRepository>();
        return services;
    }
}
