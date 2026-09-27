using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LoggerService;

public static class DependencyInjection
{
    public static IServiceCollection AddLoggerService(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<ILoggerManager, LoggerManager>();
        var directory = configuration["Logging:File:Directory"];
        if (!string.IsNullOrWhiteSpace(directory))
        {
            var level = Enum.TryParse<LogLevel>(configuration["Logging:File:MinLevel"], out var l) ? l : LogLevel.Information;
            services.AddLogging(b => b.AddProvider(new FileLoggerProvider(directory, level)));
        }
        return services;
    }
}
