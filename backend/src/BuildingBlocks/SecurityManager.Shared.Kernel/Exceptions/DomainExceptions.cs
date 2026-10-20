namespace SecurityManager.Shared.Kernel.Exceptions;

/// <summary>Thrown when a requested resource does not exist (maps to HTTP 404).</summary>
public sealed class NotFoundException(string entity, object key)
    : Exception($"{entity} '{key}' was not found.");

/// <summary>Thrown when a request violates a business rule (maps to HTTP 400).</summary>
public sealed class BusinessRuleException(string message) : Exception(message);

/// <summary>Thrown when credentials or tokens are invalid (maps to HTTP 401).</summary>
public sealed class UnauthorizedException(string message) : Exception(message);

/// <summary>Thrown when the caller is authenticated but not permitted (maps to HTTP 403).</summary>
public sealed class ForbiddenException(string message) : Exception(message);

/// <summary>Thrown when a request conflicts with existing state (maps to HTTP 409).</summary>
public sealed class ConflictException(string message) : Exception(message);
