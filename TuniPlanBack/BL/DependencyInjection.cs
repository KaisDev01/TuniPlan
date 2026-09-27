using BL.Interfaces;
using BL.Managers;
using BL.Options;
using Common.Helpers;
using Common.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BL;

public static class DependencyInjection
{
    public static IServiceCollection AddBusinessLayer(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.Section));
        services.Configure<SecurityOptions>(configuration.GetSection(SecurityOptions.Section));
        services.Configure<AppOptions>(configuration.GetSection(AppOptions.Section));

        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IPaymentGateway, MockPaymentGateway>();

        services.AddScoped<IAuditManager, AuditManager>();
        services.AddScoped<IOrganizationAccess, OrganizationAccess>();
        services.AddScoped<INotificationManager, NotificationManager>();
        services.AddScoped<IAuthManager, AuthManager>();
        services.AddScoped<IAccountManager, AccountManager>();
        services.AddScoped<IAvailabilityManager, AvailabilityManager>();
        services.AddScoped<ICatalogManager, CatalogManager>();
        services.AddScoped<IOrganizationManager, OrganizationManager>();
        services.AddScoped<IReviewManager, ReviewManager>();
        services.AddScoped<IClientManager, ClientManager>();
        services.AddScoped<IPaymentManager, PaymentManager>();
        services.AddScoped<IAppointmentManager, AppointmentManager>();
        services.AddScoped<IDashboardManager, DashboardManager>();
        services.AddScoped<IReminderManager, ReminderManager>();
        services.AddScoped<IAiSecretaryManager, AiSecretaryManager>();
        return services;
    }
}
