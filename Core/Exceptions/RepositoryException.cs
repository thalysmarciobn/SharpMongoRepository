namespace SharpMongoRepository.Exceptions;

/// <summary>
///     Represents custom exceptions specific to MongoDB repository operations.
/// </summary>
/// <remarks>
///     This exception class is used to wrap and handle all repository-related errors,
///     including database operations, data validation, and repository-specific business logic exceptions.
/// </remarks>
public class RepositoryException : Exception
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="RepositoryException" /> class with a specified error message.
    /// </summary>
    /// <param name="message">The error message that explains the reason for the exception.</param>
    /// <remarks>
    ///     Use this constructor when the exception is caused by a repository-specific error
    ///     that doesn't require including an inner exception.
    /// </remarks>
    public RepositoryException(string message) : base(message)
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="RepositoryException" /> class with a specified error message
    ///     and a reference to the inner exception that is the cause of this exception.
    /// </summary>
    /// <param name="message">The error message that explains the reason for the exception.</param>
    /// <param name="exception">The exception that is the cause of the current exception.</param>
    /// <remarks>
    ///     Use this constructor when you want to wrap an underlying exception (like MongoDB.Driver.MongoException)
    ///     with additional context about the repository operation that failed.
    /// </remarks>
    public RepositoryException(string message, Exception exception) : base(message, exception)
    {
    }
}