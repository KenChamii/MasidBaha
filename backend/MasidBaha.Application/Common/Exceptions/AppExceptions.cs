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
