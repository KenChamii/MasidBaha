namespace MasidBaha.Application.Common.Exceptions;

// Mapped to HTTP 404 by the exception middleware
public class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message) { }
}

// Mapped to HTTP 409 by the exception middleware
public class ConflictException : Exception
{
    public ConflictException(string message) : base(message) { }
}

// Mapped to HTTP 403 by the exception middleware
public class ForbiddenException : Exception
{
    public ForbiddenException(string message) : base(message) { }
}

// Mapped to HTTP 400 with a list of field errors
public class ValidationFailedException : Exception
{
    public IReadOnlyDictionary<string, string[]> Errors { get; }

    public ValidationFailedException(string field, string message)
        : base(message)
    {
        Errors = new Dictionary<string, string[]> { [field] = [message] };
    }
}
