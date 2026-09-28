namespace Common.Exceptions;

/// <summary>Base for all expected errors. The API turns them into ProblemDetails with the right status code.</summary>
public abstract class AppException(string message, string code) : Exception(message)
{
    public string Code { get; } = code;
    public abstract int StatusCode { get; }
}

public sealed class NotFoundException(string message = "Ressource introuvable.", string code = "not_found")
    : AppException(message, code)
{
    public override int StatusCode => 404;
}

public sealed class BadRequestException(string message, string code = "bad_request")
    : AppException(message, code)
{
    public override int StatusCode => 400;
}

public sealed class ValidationException(IDictionary<string, string[]> errors, string message = "Données invalides.")
    : AppException(message, "validation_error")
{
    public IDictionary<string, string[]> Errors { get; } = errors;
    public override int StatusCode => 400;

    public static ValidationException For(string field, string error) =>
        new(new Dictionary<string, string[]> { [field] = [error] });
}

public sealed class UnauthorizedException(string message = "Non authentifié.", string code = "unauthorized")
    : AppException(message, code)
{
    public override int StatusCode => 401;
}

public sealed class ForbiddenException(string message = "Accès refusé.", string code = "forbidden")
    : AppException(message, code)
{
    public override int StatusCode => 403;
}

public sealed class ConflictException(string message, string code = "conflict")
    : AppException(message, code)
{
    public override int StatusCode => 409;
}

/// <summary>A business rule was broken (e.g. cancellation deadline passed).</summary>
public sealed class BusinessRuleException(string message, string code = "business_rule")
    : AppException(message, code)
{
    public override int StatusCode => 422;
}

public sealed class TooManyRequestsException(string message, string code = "too_many_requests")
    : AppException(message, code)
{
    public override int StatusCode => 429;
}

/// <summary>An optional external provider (transcription, Google / Facebook login...) is not configured on this server.</summary>
public sealed class ServiceUnavailableException(string message, string code = "service_unavailable")
    : AppException(message, code)
{
    public override int StatusCode => 503;
}

/// <summary>Account temporarily locked after too many failed logins.</summary>
public sealed class AccountLockedException(DateTime lockoutEndUtc)
    : AppException("Compte temporairement verrouillé après plusieurs tentatives. Réessayez plus tard.", "account_locked")
{
    public DateTime LockoutEndUtc { get; } = lockoutEndUtc;
    public override int StatusCode => 423;
}
