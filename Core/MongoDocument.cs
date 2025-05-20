using System.Linq.Expressions;
using MongoDB.Driver;
using SharpMongoRepository.Interface;

namespace SharpMongoRepository;

public static class MongoDocument<TDocument> where TDocument : IDocument
{
    public static MongoIndex<TDocument> CreateAscendingIndex<TKey>(
        Expression<Func<TDocument, TKey>> keySelector,
        bool unique = false)
    {
        var fieldName = GetFieldName(keySelector);
        var keys = Builders<TDocument>.IndexKeys.Ascending(fieldName);
        var options = new CreateIndexOptions
        {
            Name = fieldName,
            Unique = unique
        };
        return new MongoIndex<TDocument>
        {
            Keys = keys,
            Options = options
        };
    }

    public static MongoIndex<TDocument> CreateDescendingIndex<TKey>(
        Expression<Func<TDocument, TKey>> keySelector,
        bool unique = false)
    {
        var fieldName = GetFieldName(keySelector);
        var keys = Builders<TDocument>.IndexKeys.Descending(fieldName);
        var options = new CreateIndexOptions
        {
            Name = fieldName,
            Unique = unique
        };
        return new MongoIndex<TDocument>
        {
            Keys = keys,
            Options = options
        };
    }

    public static MongoIndex<TDocument> CreateCompoundIndex<TKey1, TKey2, TKey3>(
        Expression<Func<TDocument, TKey1>> keySelector1,
        Expression<Func<TDocument, TKey2>> keySelector2,
        Expression<Func<TDocument, TKey3>> keySelector3,
        bool unique = false)
    {
        var fieldName1 = GetFieldName(keySelector1);
        var fieldName2 = GetFieldName(keySelector2);
        var fieldName3 = GetFieldName(keySelector3);

        var keys = Builders<TDocument>.IndexKeys.Ascending(fieldName1)
            .Ascending(fieldName2)
            .Ascending(fieldName3);

        var options = new CreateIndexOptions
        {
            Name = $"{fieldName1}_{fieldName2}_{fieldName3}",
            Unique = unique
        };

        return new MongoIndex<TDocument>
        {
            Keys = keys,
            Options = options
        };
    }

    public static MongoIndex<TDocument> CreateCompoundIndex<TKey1, TKey2, TKey3, TKey4>(
        Expression<Func<TDocument, TKey1>> keySelector1,
        Expression<Func<TDocument, TKey2>> keySelector2,
        Expression<Func<TDocument, TKey3>> keySelector3,
        Expression<Func<TDocument, TKey4>> keySelector4,
        bool unique = false)
    {
        var fieldName1 = GetFieldName(keySelector1);
        var fieldName2 = GetFieldName(keySelector2);
        var fieldName3 = GetFieldName(keySelector3);
        var fieldName4 = GetFieldName(keySelector4);

        var keys = Builders<TDocument>.IndexKeys.Ascending(fieldName1)
            .Ascending(fieldName2)
            .Ascending(fieldName3)
            .Ascending(fieldName4);

        var options = new CreateIndexOptions
        {
            Name = $"{fieldName1}_{fieldName2}_{fieldName3}_{fieldName4}",
            Unique = unique
        };

        return new MongoIndex<TDocument>
        {
            Keys = keys,
            Options = options
        };
    }

    private static string GetFieldName(LambdaExpression keySelector)
    {
        if (keySelector.Body is MemberExpression memberExpression) return memberExpression.Member.Name;
        throw new ArgumentException("Key selector must be a member expression.", nameof(keySelector));
    }
}