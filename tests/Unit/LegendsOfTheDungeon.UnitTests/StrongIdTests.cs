using LegendsOfTheDungeon.Kernel;
using Xunit;

namespace LegendsOfTheDungeon.UnitTests;

public sealed class StrongIdTests
{
    [Fact]
    public void AllStrongValuesRejectZeroConstruction()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new EntityId(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ActorId(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new WorldId(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AbilityId(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ItemDefinitionId(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ItemInstanceId(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TaskId(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EntityGeneration(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new WorldEpoch(0));
    }

    [Fact]
    public void DefaultStrongValuesAreEmptyAndCannotCrossValueBoundary()
    {
        Assert.True(default(EntityId).IsEmpty);
        Assert.True(default(ActorId).IsEmpty);
        Assert.True(default(WorldId).IsEmpty);
        Assert.True(default(AbilityId).IsEmpty);
        Assert.True(default(ItemDefinitionId).IsEmpty);
        Assert.True(default(ItemInstanceId).IsEmpty);
        Assert.True(default(TaskId).IsEmpty);
        Assert.True(default(EntityGeneration).IsEmpty);
        Assert.True(default(WorldEpoch).IsEmpty);

        Assert.Throws<InvalidOperationException>(() => _ = default(EntityId).Value);
        Assert.Throws<InvalidOperationException>(() => _ = default(ItemInstanceId).ToString());
        Assert.Throws<InvalidOperationException>(() => _ = default(WorldEpoch).Value);
    }

    [Fact]
    public void CanonicalTextRoundTripsWithInvariantUnsignedFormat()
    {
        var identifier = new EntityId(ulong.MaxValue);

        Assert.Equal("18446744073709551615", identifier.ToString());
        Assert.Equal(identifier, EntityId.Parse(identifier.ToString()));
        Assert.True(EntityId.TryParse("42", out var parsed));
        Assert.Equal(42UL, parsed.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("00")]
    [InlineData("01")]
    [InlineData("-1")]
    [InlineData("+1")]
    [InlineData(" 1")]
    [InlineData("1 ")]
    [InlineData("18446744073709551616")]
    public void CanonicalParserRejectsEmptySignedWhitespaceLeadingZeroAndOverflow(string? text)
    {
        Assert.False(EntityId.TryParse(text, out var parsed));
        Assert.True(parsed.IsEmpty);
    }

    [Fact]
    public void ParseRejectsInvalidCanonicalText()
    {
        Assert.Throws<FormatException>(() => EntityId.Parse("0"));
        Assert.Throws<FormatException>(() => WorldEpoch.Parse("18446744073709551616"));
    }

    [Fact]
    public void DefinitionInstanceGenerationAndEpochAreDistinctClrTypes()
    {
        Assert.NotEqual(typeof(ItemDefinitionId), typeof(ItemInstanceId));
        Assert.NotEqual(typeof(EntityId), typeof(EntityGeneration));
        Assert.NotEqual(typeof(WorldId), typeof(WorldEpoch));
    }
}
