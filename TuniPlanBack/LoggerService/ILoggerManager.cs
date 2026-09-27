namespace LoggerService;

/// <summary>Application logger used by the business layer (wraps Microsoft.Extensions.Logging).</summary>
public interface ILoggerManager
{
    void LogInfo(string message, params object?[] args);
    void LogWarn(string message, params object?[] args);
    void LogDebug(string message, params object?[] args);
    void LogError(string message, params object?[] args);
    void LogError(Exception exception, string message, params object?[] args);
    /// <summary>Security events (login, lockout, token reuse…) go to a dedicated category.</summary>
    void LogSecurity(string message, params object?[] args);
}
