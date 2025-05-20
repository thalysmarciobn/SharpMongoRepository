using SharpMongoRepository.Interface;

namespace SharpMongoRepository.Configuration;

/// <summary>
///     Represents configuration options for a MongoDB repository, including index definitions.
/// </summary>
/// <typeparam name="TDocument">The document type that implements <see cref="IDocument" />.</typeparam>
/// <remarks>
///     This record type provides a strongly-typed way to configure MongoDB repository settings,
///     particularly for index management during application startup or repository initialization.
///     The <see cref="Indexes" /> property is marked as required to ensure explicit index configuration.
/// </remarks>
public record MongoRepositoryOptions<TDocument> where TDocument : IDocument
{
    /// <summary>
    ///     Gets the collection of index definitions to be created for the document type.
    /// </summary>
    /// <value>
    ///     A list of <see cref="MongoIndex{TDocument}" /> objects, each defining:
    ///     <list type="bullet">
    ///         <item>
    ///             <description>The indexed fields and their sort order</description>
    ///         </item>
    ///         <item>
    ///             <description>Additional index options (unique, TTL, etc.)</description>
    ///         </item>
    ///     </list>
    /// </value>
    /// <example>
    ///     Example configuration:
    ///     <code>
    /// new MongoRepositoryOptions&lt;User&gt;
    /// {
    ///     Indexes = new List&lt;MongoIndex&lt;User&gt;&gt;
    ///     {
    ///         new()
    ///         {
    ///             Keys = Builders&lt;User&gt;.IndexKeys.Ascending(u => u.Email),
    ///             Options = new CreateIndexOptions { Unique = true }
    ///         }
    ///     }
    /// }
    /// </code>
    /// </example>
    public required List<MongoIndex<TDocument>>? Indexes { get; init; }
}