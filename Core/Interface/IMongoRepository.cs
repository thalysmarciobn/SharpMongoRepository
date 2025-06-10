using System.Linq.Expressions;
using MongoDB.Driver;

namespace SharpMongoRepository.Interface;

/// <summary>
/// Representa uma interface genérica de repositório para operações com MongoDB.
/// </summary>
/// <typeparam name="TDocument">Tipo do documento que implementa <see cref="IDocument"/>.</typeparam>
/// <remarks>
/// Esta interface fornece operações síncronas e assíncronas de CRUD para MongoDB,
/// com suporte a filtragem, projeção, contagem e execução de transações.
/// </remarks>
public interface IMongoRepository<TDocument, TKey> where TDocument : IDocument<TKey>
{
    /// <summary>
    /// Encontra documentos que correspondem à definição de filtro do MongoDB especificada.
    /// </summary>
    /// <param name="filter">A definição de filtro do MongoDB.</param>
    /// <returns>Um <see cref="IFindFluent{TDocument, TDocument}"/> para operações adicionais de consulta.</returns>
    IFindFluent<TDocument, TDocument> Find(FilterDefinition<TDocument> filter);

    /// <summary>
    /// Fornece capacidades de consulta LINQ para a coleção de documentos.
    /// </summary>
    /// <returns>Um <see cref="IQueryable{TDocument}"/> para realizar consultas LINQ.</returns>
    IQueryable<TDocument?> AsQueryable();

    /// <summary>
    /// Filtra documentos usando a expressão de predicado especificada.
    /// </summary>
    /// <param name="filterExpression">Expressão LINQ utilizada para filtrar os documentos.</param>
    /// <returns>Uma coleção de documentos que correspondem ao filtro.</returns>
    IEnumerable<TDocument?> FilterBy(Expression<Func<TDocument, bool>> filterExpression);
    IEnumerable<TDocument?> FilterBy(Expression<Func<TDocument, bool>> filterExpression, IClientSessionHandle clientSessionHandle);

    /// <summary>
    /// Filtra e projeta documentos utilizando as expressões fornecidas.
    /// </summary>
    /// <typeparam name="TProjected">Tipo do resultado projetado.</typeparam>
    /// <param name="filterExpression">Expressão LINQ usada para filtrar os documentos.</param>
    /// <param name="projectionExpression">Expressão LINQ usada para projetar os documentos.</param>
    /// <returns>Uma coleção de resultados projetados.</returns>
    IEnumerable<TProjected> FilterBy<TProjected>(
        Expression<Func<TDocument, bool>> filterExpression,
        Expression<Func<TDocument, TProjected>> projectionExpression);
    IEnumerable<TProjected> FilterBy<TProjected>(
        Expression<Func<TDocument, bool>> filterExpression,
        Expression<Func<TDocument, TProjected>> projectionExpression,
        IClientSessionHandle clientSessionHandle);

    /// <summary>
    /// Recupera assincronamente todos os documentos da coleção.
    /// </summary>
    /// <returns>Uma tarefa que retorna um cursor assíncrono para iterar sobre os documentos.</returns>
    Task<IAsyncCursor<TDocument>> AllAsync();
    Task<IAsyncCursor<TDocument>> AllAsync(IClientSessionHandle clientSessionHandle);

    /// <summary>
    /// Encontra um único documento que corresponda à expressão fornecida.
    /// </summary>
    /// <param name="filterExpression">Expressão LINQ para filtrar o documento.</param>
    /// <returns>O documento correspondente, ou null se não encontrado.</returns>
    TDocument? FindOne(Expression<Func<TDocument, bool>> filterExpression);
    TDocument? FindOne(Expression<Func<TDocument, bool>> filterExpression, IClientSessionHandle clientSessionHandle);

    /// <summary>
    /// Encontra assincronamente um único documento que corresponda à expressão fornecida.
    /// </summary>
    /// <param name="filterExpression">Expressão LINQ para filtrar o documento.</param>
    /// <returns>Uma tarefa que retorna o documento correspondente ou null.</returns>
    Task<TDocument?> FindOneAsync(Expression<Func<TDocument, bool>> filterExpression);
    Task<TDocument?> FindOneAsync(Expression<Func<TDocument, bool>> filterExpression, IClientSessionHandle clientSessionHandle);

    /// <summary>
    /// Encontra um documento pelo seu identificador único.
    /// </summary>
    /// <param name="id">Representação em string do ObjectId do documento.</param>
    /// <returns>O documento se encontrado; caso contrário, null.</returns>
    TDocument? FindById(TKey id);
    TDocument? FindById(TKey id, IClientSessionHandle clientSessionHandle);

    /// <summary>
    /// Encontra assincronamente um documento pelo seu identificador único.
    /// </summary>
    /// <param name="id">Representação em string do ObjectId do documento.</param>
    /// <returns>Uma tarefa que retorna o documento se encontrado; caso contrário, null.</returns>
    Task<TDocument?> FindByIdAsync(TKey id);
    Task<TDocument?> FindByIdAsync(TKey id, IClientSessionHandle clientSessionHandle);

    /// <summary>
    /// Insere um único documento na coleção.
    /// </summary>
    /// <param name="document">Documento a ser inserido.</param>
    void InsertOne(TDocument document);
    void InsertOne(TDocument document, IClientSessionHandle clientSessionHandle);

    /// <summary>
    /// Insere assincronamente um único documento na coleção.
    /// </summary>
    /// <param name="document">Documento a ser inserido.</param>
    /// <returns>Uma tarefa que representa a operação assíncrona.</returns>
    Task InsertOneAsync(TDocument document);
    Task InsertOneAsync(TDocument document, IClientSessionHandle clientSessionHandle);

    /// <summary>
    /// Insere múltiplos documentos na coleção em uma única operação.
    /// </summary>
    /// <param name="documents">Coleção de documentos a serem inseridos.</param>
    void InsertMany(ICollection<TDocument> documents);
    void InsertMany(ICollection<TDocument> documents, IClientSessionHandle clientSessionHandle);

    /// <summary>
    /// Insere assincronamente múltiplos documentos na coleção.
    /// </summary>
    /// <param name="documents">Coleção de documentos a serem inseridos.</param>
    /// <param name="options">Opções opcionais para a operação de inserção.</param>
    /// <returns>Uma tarefa que representa a operação assíncrona de inserção.</returns>
    Task InsertManyAsync(ICollection<TDocument> documents);
    Task InsertManyAsync(ICollection<TDocument> documents, IClientSessionHandle clientSessionHandle);
    Task InsertManyAsync(ICollection<TDocument> documents, InsertManyOptions options);
    Task InsertManyAsync(ICollection<TDocument> documents, IClientSessionHandle clientSessionHandle, InsertManyOptions options);

    /// <summary>
    /// Conta assincronamente o número de documentos que correspondem à expressão de filtro.
    /// </summary>
    /// <param name="filterExpression">Expressão para filtrar os documentos.</param>
    /// <returns>Uma tarefa que retorna o número de documentos correspondentes.</returns>
    Task<long> AsyncCount(Expression<Func<TDocument, bool>> filterExpression);
    Task<long> AsyncCount(Expression<Func<TDocument, bool>> filterExpression, IClientSessionHandle clientSessionHandle);

    /// <summary>
    /// Conta o número de documentos que correspondem à expressão de filtro.
    /// </summary>
    /// <param name="filterExpression">Expressão para filtrar os documentos.</param>
    /// <returns>O número de documentos correspondentes.</returns>
    long Count(Expression<Func<TDocument, bool>> filterExpression);
    long Count(Expression<Func<TDocument, bool>> filterExpression, IClientSessionHandle clientSessionHandle);

    /// <summary>
    /// Substitui um único documento identificado pelo seu Id.
    /// </summary>
    /// <param name="document">Novo documento para substituição.</param>
    /// <param name="options">Opções para a operação de substituição.</param>
    /// <returns>O documento substituído.</returns>
    TDocument FindOneAndReplace(TDocument document);
    TDocument FindOneAndReplace(TDocument document, IClientSessionHandle clientSessionHandle);
    TDocument FindOneAndReplace(TDocument document, FindOneAndReplaceOptions<TDocument> options);
    TDocument FindOneAndReplace(TDocument document, IClientSessionHandle clientSessionHandle, FindOneAndReplaceOptions<TDocument> options);

    /// <summary>
    /// Substitui assincronamente um documento identificado pelo seu Id.
    /// </summary>
    /// <param name="document">Novo documento para substituição.</param>
    /// <param name="options">Opções para a operação de substituição.</param>
    /// <returns>Uma tarefa que retorna o documento substituído.</returns>
    Task<TDocument> FindOneAndReplaceAsync(TDocument document);
    Task<TDocument> FindOneAndReplaceAsync(TDocument document, IClientSessionHandle clientSessionHandle);
    Task<TDocument> FindOneAndReplaceAsync(TDocument document, FindOneAndReplaceOptions<TDocument> options);
    Task<TDocument> FindOneAndReplaceAsync(TDocument document, IClientSessionHandle clientSessionHandle, FindOneAndReplaceOptions<TDocument> options);

    /// <summary>
    /// Exclui um único documento que corresponde à expressão de filtro.
    /// </summary>
    /// <param name="filterExpression">Expressão para localizar o documento a ser excluído.</param>
    /// <param name="options">Opções para a operação de exclusão.</param>
    void DeleteOne(Expression<Func<TDocument, bool>> filterExpression);
    void DeleteOne(Expression<Func<TDocument, bool>> filterExpression, IClientSessionHandle clientSessionHandle);
    void DeleteOne(Expression<Func<TDocument, bool>> filterExpression, FindOneAndDeleteOptions<TDocument> options);
    void DeleteOne(Expression<Func<TDocument, bool>> filterExpression, IClientSessionHandle clientSessionHandle, FindOneAndDeleteOptions<TDocument> options);

    /// <summary>
    /// Exclui assincronamente um único documento que corresponde à expressão de filtro.
    /// </summary>
    /// <param name="filterExpression">Expressão para localizar o documento a ser excluído.</param>
    /// <param name="options">Opções para a operação de exclusão.</param>
    /// <returns>Uma tarefa que representa a operação de exclusão.</returns>
    Task DeleteOneAsync(Expression<Func<TDocument, bool>> filterExpression);
    Task DeleteOneAsync(Expression<Func<TDocument, bool>> filterExpression, IClientSessionHandle clientSessionHandle);
    Task DeleteOneAsync(Expression<Func<TDocument, bool>> filterExpression, FindOneAndDeleteOptions<TDocument> options);
    Task DeleteOneAsync(Expression<Func<TDocument, bool>> filterExpression, IClientSessionHandle clientSessionHandle, FindOneAndDeleteOptions<TDocument> options);

    /// <summary>
    /// Exclui um documento pelo seu identificador único.
    /// </summary>
    /// <param name="id">Representação em string do ObjectId do documento.</param>
    /// <param name="options">Opções para a operação de exclusão.</param>
    void DeleteById(TKey id);
    void DeleteById(TKey id, IClientSessionHandle clientSessionHandle);
    void DeleteById(TKey id, FindOneAndDeleteOptions<TDocument> options);
    void DeleteById(TKey id, IClientSessionHandle clientSessionHandle, FindOneAndDeleteOptions<TDocument> options);

    /// <summary>
    /// Exclui assincronamente um documento pelo seu identificador único.
    /// </summary>
    /// <param name="id">Representação em string do ObjectId do documento.</param>
    /// <param name="options">Opções para a operação de exclusão.</param>
    /// <returns>Uma tarefa que representa a operação de exclusão.</returns>
    Task DeleteByIdAsync(TKey id);
    Task DeleteByIdAsync(TKey id, IClientSessionHandle clientSessionHandle);
    Task DeleteByIdAsync(TKey id, FindOneAndDeleteOptions<TDocument> options);
    Task DeleteByIdAsync(TKey id, IClientSessionHandle clientSessionHandle, FindOneAndDeleteOptions<TDocument> options);

    /// <summary>
    /// Exclui múltiplos documentos que correspondem à expressão de filtro.
    /// </summary>
    /// <param name="filterExpression">Expressão para localizar os documentos a serem excluídos.</param>
    void DeleteMany(Expression<Func<TDocument, bool>> filterExpression);
    void DeleteMany(Expression<Func<TDocument, bool>> filterExpression, IClientSessionHandle clientSessionHandle);

    /// <summary>
    /// Exclui assincronamente múltiplos documentos que correspondem à expressão de filtro.
    /// </summary>
    /// <param name="filterExpression">Expressão para localizar os documentos a serem excluídos.</param>
    /// <returns>Uma tarefa que representa a operação de exclusão.</returns>
    Task DeleteManyAsync(Expression<Func<TDocument, bool>> filterExpression);
    Task DeleteManyAsync(Expression<Func<TDocument, bool>> filterExpression, IClientSessionHandle clientSessionHandle);

    /// <summary>
    /// Executa uma série de operações dentro de um contexto transacional do MongoDB, de forma assíncrona.
    /// </summary>
    /// <typeparam name="TResult">Tipo do resultado retornado pelo corpo da transação.</typeparam>
    /// <param name="transactionBody">Função que define as operações transacionais usando a sessão fornecida.</param>
    /// <returns>Uma tarefa que representa a operação transacional, contendo o resultado das operações executadas.</returns>
    Task<TResult> WithTransactionAsync<TResult>(
        Func<IClientSessionHandle, Task<TResult>> transactionBody);
}
