using Microsoft.Extensions.Logging;

namespace LoggerService;

public sealed class LoggerManager(ILoggerFactory factory) : ILoggerManager
{
    private readonly ILogger _app = factory.CreateLogger("TuniPlan");
    private readonly ILogger _security = factory.CreateLogger("TuniPlan.Security");

#pragma warning disable CA2254 // templates are provided by callers
    public void LogInfo(string message, params object?[] args) => _app.LogInformation(message, args);
    public void LogWarn(string message, params object?[] args) => _app.LogWarning(message, args);
    public void LogDebug(string message, params object?[] args) => _app.LogDebug(message, args);
    public void LogError(string message, params object?[] args) => _app.LogError(message, args);
    public void LogError(Exception exception, string message, params object?[] args) => _app.LogError(exception, message, args);
    public void LogSecurity(string message, params object?[] args) => _security.LogWarning(message, args);
#pragma warning restore CA2254
}
