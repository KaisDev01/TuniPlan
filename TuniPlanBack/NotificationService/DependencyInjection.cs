using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NotificationService.Senders;

namespace NotificationService;

public static class DependencyInjection
{
    public static IServiceCollection AddNotificationServices(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(NotificationOptions.Section);
        services.Configure<NotificationOptions>(section);

        if (string.Equals(section["SmsProvider"], "Http", StringComparison.OrdinalIgnoreCase))
            services.AddHttpClient<ISmsSender, HttpSmsSender>(c => c.Timeout = TimeSpan.FromSeconds(15));
        else
            services.AddSingleton<ISmsSender, ConsoleSmsSender>();

        services.AddSingleton<IWhatsAppSender, ConsoleWhatsAppSender>();
        services.AddSingleton<IEmailSender, ConsoleEmailSender>();
        services.AddSingleton<IPushSender, ConsolePushSender>();
        return services;
    }
}
