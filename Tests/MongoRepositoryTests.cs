using System.Linq.Expressions;
using MongoDB.Bson;
using MongoDB.Driver;
using Moq;
using SharpMongoRepository.Attributes;
using SharpMongoRepository.Interface;
using Xunit;

namespace Tests;

[BsonCollection("test_entities")]
public class TestEntity : IDocument<ObjectId>
{
    public required string Name { get; init; }
    public int Value { get; init; }
    public ObjectId Id { get; set; }
}

public class MongoRepositoryTests
{
    private readonly Mock<IMongoRepository<TestEntity, ObjectId>> _mockRepository;
    private readonly List<TestEntity> _testEntities;
    private readonly TestEntity _testEntity;

    public MongoRepositoryTests()
    {
        _mockRepository = new Mock<IMongoRepository<TestEntity, ObjectId>>();

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
    public void Find_ShouldReturnFluentInterface()
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
    public void AsQueryable_ShouldReturnQueryableInterface()
    {
        var mockQueryable = _testEntities.AsQueryable();
        _mockRepository.Setup(x => x.AsQueryable()).Returns(mockQueryable);

        var result = _mockRepository.Object.AsQueryable();

        Assert.Equal(3, result.Count());
        _mockRepository.Verify(x => x.AsQueryable(), Times.Once);
    }

    [Fact]
    public void FilterBy_ShouldFilterDocuments()
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
    public void FilterByWithProjection_ShouldReturnProjectedResults()
    {
        Expression<Func<TestEntity, bool>> filter = x => x.Value > 150;
        Expression<Func<TestEntity, string>> projection = x => x.Name;
        var expected = _testEntities.Where(x => x.Value > 150).Select(x => x.Name).ToList();

        _mockRepository.Setup(x => x.FilterBy(
                It.IsAny<Expression<Func<TestEntity, bool>>>(),
                It.IsAny<Expression<Func<TestEntity, string>>>()))
            .Returns(expected);

        var result = _mockRepository.Object.FilterBy(filter, projection);

        Assert.Equal(2, result.Count());
        Assert.All(result, x => Assert.IsType<string>(x));
        _mockRepository.Verify(x => x.FilterBy(filter, projection), Times.Once);
    }

    [Fact]
    public void FindOne_ShouldReturnSingleDocument()
    {
        Expression<Func<TestEntity, bool>> filter = x => x.Id == _testEntity.Id;
        _mockRepository.Setup(x => x.FindOne(filter))
            .Returns(_testEntity);

        var result = _mockRepository.Object.FindOne(filter);

        Assert.Equal(_testEntity.Id, result!.Id);
        _mockRepository.Verify(x => x.FindOne(filter), Times.Once);
    }

    [Fact]
    public void FindById_ShouldReturnDocument()
    {
        _mockRepository.Setup(x => x.FindById(_testEntity.Id, null))
            .Returns(_testEntity);

        var result = _mockRepository.Object.FindById(_testEntity.Id);

        Assert.Equal(_testEntity.Id, result!.Id);
        _mockRepository.Verify(x => x.FindById(_testEntity.Id, null), Times.Once);
    }

    [Fact]
    public void InsertOne_ShouldCallRepository()
    {
        _mockRepository.Setup(x => x.InsertOne(It.IsAny<TestEntity>(), null));

        _mockRepository.Object.InsertOne(_testEntity);

        _mockRepository.Verify(x => x.InsertOne(_testEntity, null), Times.Once);
    }

    [Fact]
    public void InsertMany_ShouldCallRepository()
    {
        _mockRepository.Setup(x => x.InsertMany(It.IsAny<ICollection<TestEntity>>(), null));

        _mockRepository.Object.InsertMany(_testEntities);

        _mockRepository.Verify(x => x.InsertMany(_testEntities, null), Times.Once);
    }

    [Fact]
    public void Count_ShouldReturnCorrectCount()
    {
        Expression<Func<TestEntity, bool>> filter = x => x.Value > 150;
        _mockRepository.Setup(x => x.Count(filter))
            .Returns(2);

        var result = _mockRepository.Object.Count(filter);

        Assert.Equal(2, result);
        _mockRepository.Verify(x => x.Count(filter), Times.Once);
    }

    [Fact]
    public void FindOneAndReplace_ShouldReplaceDocument()
    {
        var options = new FindOneAndReplaceOptions<TestEntity>();
        _mockRepository.Setup(x => x.FindOneAndReplace(_testEntity, options))
            .Returns(_testEntity);

        var result = _mockRepository.Object.FindOneAndReplace(_testEntity, options);

        Assert.Equal(_testEntity.Id, result.Id);
        _mockRepository.Verify(x => x.FindOneAndReplace(_testEntity, options), Times.Once);
    }

    [Fact]
    public void DeleteOne_ShouldCallRepository()
    {
        Expression<Func<TestEntity, bool>> filter = x => x.Id == _testEntity.Id;
        var options = new FindOneAndDeleteOptions<TestEntity>();

        _mockRepository.Setup(x => x.DeleteOne(filter, options));

        _mockRepository.Object.DeleteOne(filter, options);

        _mockRepository.Verify(x => x.DeleteOne(filter, options), Times.Once);
    }

    [Fact]
    public void DeleteById_ShouldCallRepository()
    {
        var options = new FindOneAndDeleteOptions<TestEntity>();
        _mockRepository.Setup(x => x.DeleteById(_testEntity.Id, options));

        _mockRepository.Object.DeleteById(_testEntity.Id, options);

        _mockRepository.Verify(x => x.DeleteById(_testEntity.Id, options), Times.Once);
    }

    [Fact]
    public void DeleteMany_ShouldCallRepository()
    {
        Expression<Func<TestEntity, bool>> filter = x => x.Value > 150;
        _mockRepository.Setup(x => x.DeleteMany(filter));

        _mockRepository.Object.DeleteMany(filter);

        _mockRepository.Verify(x => x.DeleteMany(filter), Times.Once);
    }

    [Fact]
    public async Task FindOneAsync_ShouldReturnSingleDocument()
    {
        Expression<Func<TestEntity, bool>> filter = x => x.Id == _testEntity.Id;
        _mockRepository.Setup(x => x.FindOneAsync(filter))
            .ReturnsAsync(_testEntity);

        var result = await _mockRepository.Object.FindOneAsync(filter);

        Assert.Equal(_testEntity.Id, result!.Id);
        _mockRepository.Verify(x => x.FindOneAsync(filter), Times.Once);
    }

    [Fact]
    public async Task FindByIdAsync_ShouldReturnDocument()
    {
        _mockRepository.Setup(x => x.FindByIdAsync(_testEntity.Id, null))
            .ReturnsAsync(_testEntity);

        var result = await _mockRepository.Object.FindByIdAsync(_testEntity.Id);

        Assert.Equal(_testEntity.Id, result!.Id);
        _mockRepository.Verify(x => x.FindByIdAsync(_testEntity.Id, null), Times.Once);
    }

    [Fact]
    public async Task InsertOneAsync_ShouldCallRepository()
    {
        _mockRepository.Setup(x => x.InsertOneAsync(It.IsAny<TestEntity>(), null))
            .Returns(Task.CompletedTask);

        await _mockRepository.Object.InsertOneAsync(_testEntity);

        _mockRepository.Verify(x => x.InsertOneAsync(_testEntity, null), Times.Once);
    }

    [Fact]
    public async Task InsertManyAsync_ShouldCallRepository()
    {
        var options = new InsertManyOptions();
        _mockRepository.Setup(x => x.InsertManyAsync(_testEntities, options))
            .Returns(Task.CompletedTask);

        await _mockRepository.Object.InsertManyAsync(_testEntities, options);

        _mockRepository.Verify(x => x.InsertManyAsync(_testEntities, options), Times.Once);
    }

    [Fact]
    public async Task AsyncCount_ShouldReturnCorrectCount()
    {
        Expression<Func<TestEntity, bool>> filter = x => x.Value > 150;
        _mockRepository.Setup(x => x.AsyncCount(filter))
            .ReturnsAsync(2);

        var result = await _mockRepository.Object.AsyncCount(filter);

        Assert.Equal(2, result);
        _mockRepository.Verify(x => x.AsyncCount(filter), Times.Once);
    }

    [Fact]
    public async Task FindOneAndReplaceAsync_ShouldReplaceDocument()
    {
        var options = new FindOneAndReplaceOptions<TestEntity>();
        _mockRepository.Setup(x => x.FindOneAndReplaceAsync(_testEntity, options))
            .ReturnsAsync(_testEntity);

        var result = await _mockRepository.Object.FindOneAndReplaceAsync(_testEntity, options);

        Assert.Equal(_testEntity.Id, result.Id);
        _mockRepository.Verify(x => x.FindOneAndReplaceAsync(_testEntity, options), Times.Once);
    }

    [Fact]
    public async Task DeleteOneAsync_ShouldCallRepository()
    {
        Expression<Func<TestEntity, bool>> filter = x => x.Id == _testEntity.Id;
        var options = new FindOneAndDeleteOptions<TestEntity>();

        _mockRepository.Setup(x => x.DeleteOneAsync(filter, options))
            .Returns(Task.CompletedTask);

        await _mockRepository.Object.DeleteOneAsync(filter, options);

        _mockRepository.Verify(x => x.DeleteOneAsync(filter, options), Times.Once);
    }

    [Fact]
    public async Task DeleteByIdAsync_ShouldCallRepository()
    {
        var options = new FindOneAndDeleteOptions<TestEntity>();
        _mockRepository.Setup(x => x.DeleteByIdAsync(_testEntity.Id, options))
            .Returns(Task.CompletedTask);

        await _mockRepository.Object.DeleteByIdAsync(_testEntity.Id, options);

        _mockRepository.Verify(x => x.DeleteByIdAsync(_testEntity.Id, options), Times.Once);
    }

    [Fact]
    public async Task DeleteManyAsync_ShouldCallRepository()
    {
        Expression<Func<TestEntity, bool>> filter = x => x.Value > 150;
        _mockRepository.Setup(x => x.DeleteManyAsync(filter))
            .Returns(Task.CompletedTask);

        await _mockRepository.Object.DeleteManyAsync(filter);

        _mockRepository.Verify(x => x.DeleteManyAsync(filter), Times.Once);
    }

    [Fact]
    public async Task WithTransactionAsync_ShouldExecuteTransaction()
    {
        var expectedResult = "transaction-result";
        _mockRepository.Setup(x => x.WithTransactionAsync(It.IsAny<Func<IClientSessionHandle, Task<string>>>()))
            .ReturnsAsync(expectedResult);

        var result = await _mockRepository.Object.WithTransactionAsync(
            session => Task.FromResult(expectedResult));

        Assert.Equal(expectedResult, result);
        _mockRepository.Verify(x => x.WithTransactionAsync(It.IsAny<Func<IClientSessionHandle, Task<string>>>()), Times.Once);
    }

    // Testes para casos de borda

    [Fact]
    public void FindById_ShouldReturnNullWhenNotFound()
    {
        var nonExistentId = ObjectId.GenerateNewId();
        _mockRepository.Setup(x => x.FindById(nonExistentId, null))
            .Returns((TestEntity)null!);

        var result = _mockRepository.Object.FindById(nonExistentId);

        Assert.Null(result);
        _mockRepository.Verify(x => x.FindById(nonExistentId, null), Times.Once);
    }

    [Fact]
    public async Task FindByIdAsync_ShouldReturnNullWhenNotFound()
    {
        var nonExistentId = ObjectId.GenerateNewId();
        _mockRepository.Setup(x => x.FindByIdAsync(nonExistentId, null))
            .ReturnsAsync((TestEntity)null!);

        var result = await _mockRepository.Object.FindByIdAsync(nonExistentId);

        Assert.Null(result);
        _mockRepository.Verify(x => x.FindByIdAsync(nonExistentId, null), Times.Once);
    }

    [Fact]
    public void FindOne_ShouldReturnNullWhenNotFound()
    {
        Expression<Func<TestEntity, bool>> filter = x => x.Id == ObjectId.GenerateNewId();
        _mockRepository.Setup(x => x.FindOne(filter))
            .Returns((TestEntity)null!);

        var result = _mockRepository.Object.FindOne(filter);

        Assert.Null(result);
        _mockRepository.Verify(x => x.FindOne(filter), Times.Once);
    }

    [Fact]
    public async Task FindOneAsync_ShouldReturnNullWhenNotFound()
    {
        Expression<Func<TestEntity, bool>> filter = x => x.Id == ObjectId.GenerateNewId();
        _mockRepository.Setup(x => x.FindOneAsync(filter))
            .ReturnsAsync((TestEntity)null!);

        var result = await _mockRepository.Object.FindOneAsync(filter);

        Assert.Null(result);
        _mockRepository.Verify(x => x.FindOneAsync(filter), Times.Once);
    }

    [Fact]
    public void FilterBy_ShouldReturnEmptyWhenNoMatches()
    {
        Expression<Func<TestEntity, bool>> filter = x => x.Value > 1000;
        _mockRepository.Setup(x => x.FilterBy(filter))
            .Returns(new List<TestEntity>());

        var result = _mockRepository.Object.FilterBy(filter);

        Assert.Empty(result);
        _mockRepository.Verify(x => x.FilterBy(filter), Times.Once);
    }
}