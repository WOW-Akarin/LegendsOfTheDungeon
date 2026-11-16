using System.Data;
using System.Data.Common;
using LegendsOfTheDungeon.Platform.Persistence;
using Xunit;

namespace LegendsOfTheDungeon.UnitTests;

public sealed class PersistenceTests
{
    [Fact]
    public void ParameterNamesAndTypesAreValidated()
    {
        Assert.Throws<ArgumentException>(() => new SqlParameterValue("id", DbType.Int64, 1));
        Assert.Throws<ArgumentException>(() => new SqlParameterValue("@Bad", DbType.Int64, 1));
        Assert.Throws<ArgumentException>(() => new SqlParameterValue("@x;drop", DbType.Int64, 1));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new SqlParameterValue("@id", (DbType)9999, 1));
        var value = new SqlParameterValue("@entity_id", DbType.Int64, 12L);
        Assert.Equal("@entity_id", value.Name);
        Assert.Equal(DbType.Int64, value.DbType);
        Assert.Equal(12L, value.Value);
    }

    [Fact]
    public void SqlParameterListIsFrozenAndDuplicateNamesFail()
    {
        var source = new List<SqlParameterValue> { new("@id", DbType.Int64, 12L) };
        var command = new ParameterizedCommand("SELECT @id", source);
        source.Clear();

        Assert.Single(command.Parameters);
        Assert.Equal("SELECT @id", command.Sql);
        Assert.Throws<ArgumentException>(() => new ParameterizedCommand("SELECT @id", [
            new SqlParameterValue("@id", DbType.Int64, 1),
            new SqlParameterValue("@id", DbType.Int64, 2)
        ]));
        Assert.Throws<ArgumentException>(() => new ParameterizedCommand(" ", []));
    }

    [Fact]
    public void ConnectionFactoryRejectsAbsentConfiguration()
    {
        Assert.Throws<ArgumentNullException>(() => new DatabaseConnectionFactory(null!, "Host=db"));
        Assert.Throws<ArgumentException>(() => new DatabaseConnectionFactory(StubProvider.Instance, " "));
    }

    private sealed class StubProvider : DbProviderFactory
    {
        public static StubProvider Instance { get; } = new();
    }
}
