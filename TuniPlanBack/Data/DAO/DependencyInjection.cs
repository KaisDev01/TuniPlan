using DAO.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace DAO;

public static class DependencyInjection
{
    public static IServiceCollection AddDataAccessObjects(this IServiceCollection services)
    {
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        return services;
    }
}
