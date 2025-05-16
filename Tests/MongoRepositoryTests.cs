using MongoDB.Bson;
using MongoDB.Driver;
using MongoRepository.Interface;
using Moq;
using System.Linq.Expressions;
using MongoRepository.Attributes;

namespace Tests;

[BsonCollection("test_entities")]
public class TestEntity : IDocument
{
    public ObjectId Id { get; set; }
    public required string Name { get; init; }
    public int Value { get; init; }
}

public class MongoRepositoryTests
{
    private readonly Mock<IMongoRepository<TestEntity>> _mockRepository;
    private readonly TestEntity _testEntity;
    private readonly List<TestEntity> _testEntities;

    public MongoRepositoryTests()
    {
        _mockRepository = new Mock<IMongoRepository<TestEntity>>();
        
        _testEntity = new TestEntity
        {
            Id = ObjectId.GenerateNewId(),
            Name = "Test Entity",
            Value = 100
        };

        _testEntities =
        [
            _testEntity,
            new TestEntity { Id = ObjectId.GenerateNewId(), Name = "Second Entity", Value = 200 },
            new TestEntity { Id = ObjectId.GenerateNewId(), Name = "Third Entity", Value = 300 }
        ];
    }

    [Fact]
    public void Should_return_fluent_interface_when_finding_documents()
    {
        var filter = Builders<TestEntity>.Filter.Eq(x => x.Id, _testEntity.Id);
        var mockFindFluent = new Mock<IFindFluent<TestEntity, TestEntity>>();
        
        _mockRepository.Setup(x => x.Find(It.IsAny<FilterDefinition<TestEntity>>()))
                      .Returns(mockFindFluent.Object);

        var result = _mockRepository.Object.Find(filter);

        Assert.Equal(mockFindFluent.Object, result);
        _mockRepository.Verify(x => x.Find(filter), Times.Once);
    }

    [Fact]
    public void Should_return_queryable_interface_for_linq_queries()
    {
        var mockQueryable = _testEntities.AsQueryable();
        _mockRepository.Setup(x => x.AsQueryable()).Returns(mockQueryable);

        var result = _mockRepository.Object.AsQueryable();

        Assert.Equal(3, result.Count());
        _mockRepository.Verify(x => x.AsQueryable(), Times.Once);
    }

    [Fact]
    public void Should_filter_documents_based_on_expression()
    {
        Expression<Func<TestEntity, bool>> filter = x => x.Value > 150;
        var expected = _testEntities.Where(x => x.Value > 150).ToList();
        
        _mockRepository.Setup(x => x.FilterBy(It.IsAny<Expression<Func<TestEntity, bool>>>()))
                      .Returns(expected);

        var result = _mockRepository.Object.FilterBy(filter);

        Assert.Equal(2, result.Count());
        _mockRepository.Verify(x => x.FilterBy(filter), Times.Once);
    }

    [Fact]
    public void Should_return_projected_results_when_filtering_with_projection()
    {
        Expression<Func<TestEntity, bool>> filter = x => x.Value > 150;
        Expression<Func<TestEntity, string>> projection = x => x.Name;
        var expected = _testEntities.Where(x => x.Value > 150).Select(x => x.Name).ToList();
        
        _mockRepository.Setup(x => x.FilterBy(It.IsAny<Expression<Func<TestEntity, bool>>>(),
                                      It.IsAny<Expression<Func<TestEntity, string>>>()))
                      .Returns(expected);

        var result = _mockRepository.Object.FilterBy(filter, projection);
        
        Assert.NotNull(result);
        var enumerable = result as string[] ?? result.ToArray();
        Assert.Equal(2, enumerable.Length);
        Assert.All(enumerable, x => Assert.IsType<string>(x));
        _mockRepository.Verify(x => x.FilterBy(filter, projection), Times.Once);
    }

    [Fact]
    public void Should_find_single_document_using_expression()
    {
        Expression<Func<TestEntity, bool>> filter = x => x.Id == _testEntity.Id;
        _mockRepository.Setup(x => x.FindOne(It.IsAny<Expression<Func<TestEntity, bool>>>()))
                      .Returns(_testEntity);

        var result = _mockRepository.Object.FindOne(filter);

        Assert.NotNull(result);
        Assert.Equal(_testEntity.Id, result.Id);
        _mockRepository.Verify(x => x.FindOne(filter), Times.Once);
    }

    [Fact]
    public void Should_find_document_by_id()
    {
        var id = _testEntity.Id.ToString();
        _mockRepository.Setup(x => x.FindById(It.IsAny<string>()))
                      .Returns(_testEntity);

        var result = _mockRepository.Object.FindById(id);

        Assert.NotNull(result);
        Assert.Equal(_testEntity.Id, result.Id);
        _mockRepository.Verify(x => x.FindById(id), Times.Once);
    }

    [Fact]
    public void Should_insert_single_document()
    {
        _mockRepository.Setup(x => x.InsertOne(It.IsAny<TestEntity>()));

        _mockRepository.Object.InsertOne(_testEntity);

        _mockRepository.Verify(x => x.InsertOne(_testEntity), Times.Once);
    }

    [Fact]
    public void Should_insert_multiple_documents_at_once()
    {
        _mockRepository.Setup(x => x.InsertMany(It.IsAny<ICollection<TestEntity>>()));

        _mockRepository.Object.InsertMany(_testEntities);

        _mockRepository.Verify(x => x.InsertMany(_testEntities), Times.Once);
    }

    [Fact]
    public void Should_return_correct_count_of_documents()
    {
        Expression<Func<TestEntity, bool>> filter = x => x.Value > 150;
        _mockRepository.Setup(x => x.Count(It.IsAny<Expression<Func<TestEntity, bool>>>()))
                      .Returns(2);

        var result = _mockRepository.Object.Count(filter);

        Assert.Equal(2, result);
        _mockRepository.Verify(x => x.Count(filter), Times.Once);
    }

    [Fact]
    public void Should_replace_existing_document()
    {
        _mockRepository.Setup(x => x.ReplaceOne(It.IsAny<TestEntity>()));

        _mockRepository.Object.ReplaceOne(_testEntity);

        _mockRepository.Verify(x => x.ReplaceOne(_testEntity), Times.Once);
    }

    [Fact]
    public void Should_delete_document_using_expression()
    {
        Expression<Func<TestEntity, bool>> filter = x => x.Id == _testEntity.Id;
        _mockRepository.Setup(x => x.DeleteOne(It.IsAny<Expression<Func<TestEntity, bool>>>()));

        _mockRepository.Object.DeleteOne(filter);

        _mockRepository.Verify(x => x.DeleteOne(filter), Times.Once);
    }

    [Fact]
    public void Should_delete_document_by_id()
    {
        var id = _testEntity.Id.ToString();
        _mockRepository.Setup(x => x.DeleteById(It.IsAny<string>()));

        _mockRepository.Object.DeleteById(id);

        _mockRepository.Verify(x => x.DeleteById(id), Times.Once);
    }

    [Fact]
    public void Should_delete_multiple_documents_using_expression()
    {
        Expression<Func<TestEntity, bool>> filter = x => x.Value > 150;
        _mockRepository.Setup(x => x.DeleteMany(It.IsAny<Expression<Func<TestEntity, bool>>>()));

        _mockRepository.Object.DeleteMany(filter);

        _mockRepository.Verify(x => x.DeleteMany(filter), Times.Once);
    }

    [Fact]
    public async Task Should_find_single_document_async()
    {
        Expression<Func<TestEntity, bool>> filter = x => x.Id == _testEntity.Id;
        _mockRepository.Setup(x => x.FindOneAsync(It.IsAny<Expression<Func<TestEntity, bool>>>()))
                      .ReturnsAsync(_testEntity);

        var result = await _mockRepository.Object.FindOneAsync(filter);

        Assert.NotNull(result);
        Assert.Equal(_testEntity.Id, result.Id);
        _mockRepository.Verify(x => x.FindOneAsync(filter), Times.Once);
    }

    [Fact]
    public async Task Should_find_document_by_id_async()
    {
        var id = _testEntity.Id.ToString();
        _mockRepository.Setup(x => x.FindByIdAsync(It.IsAny<string>()))
                      .ReturnsAsync(_testEntity);

        var result = await _mockRepository.Object.FindByIdAsync(id);

        Assert.NotNull(result);
        Assert.Equal(_testEntity.Id, result.Id);
        _mockRepository.Verify(x => x.FindByIdAsync(id), Times.Once);
    }

    [Fact]
    public async Task Should_insert_single_document_async()
    {
        _mockRepository.Setup(x => x.InsertOneAsync(It.IsAny<TestEntity>()))
                      .Returns(Task.CompletedTask);

        await _mockRepository.Object.InsertOneAsync(_testEntity);

        _mockRepository.Verify(x => x.InsertOneAsync(_testEntity), Times.Once);
    }

    [Fact]
    public async Task Should_insert_multiple_documents_async()
    {
        _mockRepository.Setup(x => x.InsertManyAsync(It.IsAny<ICollection<TestEntity>>()))
                      .Returns(Task.CompletedTask);

        await _mockRepository.Object.InsertManyAsync(_testEntities);

        _mockRepository.Verify(x => x.InsertManyAsync(_testEntities), Times.Once);
    }

    [Fact]
    public async Task Should_return_async_count_of_documents()
    {
        Expression<Func<TestEntity, bool>> filter = x => x.Value > 150;
        _mockRepository.Setup(x => x.AsyncCount(It.IsAny<Expression<Func<TestEntity, bool>>>()))
                      .ReturnsAsync(2);

        var result = await _mockRepository.Object.AsyncCount(filter);

        Assert.Equal(2, result);
        _mockRepository.Verify(x => x.AsyncCount(filter), Times.Once);
    }

    [Fact]
    public async Task Should_replace_document_and_return_updated_version()
    {
        var updatedEntity = new TestEntity { Id = _testEntity.Id, Name = "Updated" };
        _mockRepository.Setup(x => x.ReplaceOneAsync(It.IsAny<TestEntity>()))
                      .ReturnsAsync(updatedEntity);

        var result = await _mockRepository.Object.ReplaceOneAsync(updatedEntity);

        Assert.Equal("Updated", result.Name);
        _mockRepository.Verify(x => x.ReplaceOneAsync(updatedEntity), Times.Once);
    }

    [Fact]
    public async Task Should_delete_document_async_using_expression()
    {
        Expression<Func<TestEntity, bool>> filter = x => x.Id == _testEntity.Id;
        _mockRepository.Setup(x => x.DeleteOneAsync(It.IsAny<Expression<Func<TestEntity, bool>>>()))
                      .Returns(Task.CompletedTask);

        await _mockRepository.Object.DeleteOneAsync(filter);

        _mockRepository.Verify(x => x.DeleteOneAsync(filter), Times.Once);
    }

    [Fact]
    public async Task Should_delete_document_async_by_id()
    {
        var id = _testEntity.Id.ToString();
        _mockRepository.Setup(x => x.DeleteByIdAsync(It.IsAny<string>()))
                      .Returns(Task.CompletedTask);

        await _mockRepository.Object.DeleteByIdAsync(id);

        _mockRepository.Verify(x => x.DeleteByIdAsync(id), Times.Once);
    }

    [Fact]
    public async Task Should_delete_multiple_documents_async()
    {
        Expression<Func<TestEntity, bool>> filter = x => x.Value > 150;
        _mockRepository.Setup(x => x.DeleteManyAsync(It.IsAny<Expression<Func<TestEntity, bool>>>()))
                      .Returns(Task.CompletedTask);

        await _mockRepository.Object.DeleteManyAsync(filter);

        _mockRepository.Verify(x => x.DeleteManyAsync(filter), Times.Once);
    }

    [Fact]
    public void Should_return_null_when_document_not_found_by_id()
    {
        var id = ObjectId.GenerateNewId().ToString();
        _mockRepository.Setup(x => x.FindById(It.IsAny<string>()))
                      .Returns((TestEntity)null!);

        var result = _mockRepository.Object.FindById(id);

        Assert.Null(result);
        _mockRepository.Verify(x => x.FindById(id), Times.Once);
    }

    [Fact]
    public async Task Should_return_null_async_when_document_not_found_by_id()
    {
        var id = ObjectId.GenerateNewId().ToString();
        _mockRepository.Setup(x => x.FindByIdAsync(It.IsAny<string>()))
                      .ReturnsAsync((TestEntity)null!);

        var result = await _mockRepository.Object.FindByIdAsync(id);

        Assert.Null(result);
        _mockRepository.Verify(x => x.FindByIdAsync(id), Times.Once);
    }

    [Fact]
    public void Should_return_null_when_document_not_found_by_expression()
    {
        Expression<Func<TestEntity, bool>> filter = x => x.Id == ObjectId.GenerateNewId();
        _mockRepository.Setup(x => x.FindOne(It.IsAny<Expression<Func<TestEntity, bool>>>()))
                      .Returns((TestEntity)null!);

        var result = _mockRepository.Object.FindOne(filter);

        Assert.Null(result);
        _mockRepository.Verify(x => x.FindOne(filter), Times.Once);
    }

    [Fact]
    public async Task Should_return_null_async_when_document_not_found_by_expression()
    {
        Expression<Func<TestEntity, bool>> filter = x => x.Id == ObjectId.GenerateNewId();
        _mockRepository.Setup(x => x.FindOneAsync(It.IsAny<Expression<Func<TestEntity, bool>>>()))
                      .ReturnsAsync((TestEntity)null!);

        var result = await _mockRepository.Object.FindOneAsync(filter);

        Assert.Null(result);
        _mockRepository.Verify(x => x.FindOneAsync(filter), Times.Once);
    }

    [Fact]
    public void Should_return_empty_list_when_no_documents_match_filter()
    {
        Expression<Func<TestEntity, bool>> filter = x => x.Value > 1000;
        _mockRepository.Setup(x => x.FilterBy(It.IsAny<Expression<Func<TestEntity, bool>>>()))
                      .Returns(new List<TestEntity>());

        var result = _mockRepository.Object.FilterBy(filter);

        Assert.Empty(result);
        _mockRepository.Verify(x => x.FilterBy(filter), Times.Once);
    }
}