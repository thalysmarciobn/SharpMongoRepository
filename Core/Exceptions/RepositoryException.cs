namespace SharpMongoRepository;

/// <summary>
///     Raised for configuration and wiring faults that originate in this library rather than in the server.
/// </summary>
/// <remarks>
///     <para>
///         This exception covers problems the library itself detects: a missing
///         <see cref="BsonCollectionAttribute" />, an index definition without a name, a key type with no
///         identifier generator, an unusable connection string.
///     </para>
///     <para>
///         Server-side failures are deliberately <b>not</b> wrapped. A duplicate key stays a
///         <c>MongoWriteException</c>, a server timeout stays a <c>MongoExecutionTimeoutException</c>, and an
///         authentication failure stays a <c>MongoAuthenticationException</c>, because callers need the
///         driver's error codes to decide whether to retry. Catch <c>MongoException</c> for those.
///     </para>
/// </remarks>
public class RepositoryException : Exception
{
    /// <summary>
    ///     Initializes a new instance with an error message.
    /// </summary>
    /// <param name="message">Description of the fault.</param>
    public RepositoryException(string message) : base(message)
    {
    }

    /// <summary>
    ///     Initializes a new instance with an error message and the underlying cause.
    /// </summary>
    /// <param name="message">Description of the fault.</param>
    /// <param name="innerException">The exception that caused this one.</param>
    public RepositoryException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
