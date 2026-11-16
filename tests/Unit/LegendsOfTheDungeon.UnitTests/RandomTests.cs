using LegendsOfTheDungeon.Kernel;
using Xunit;

namespace LegendsOfTheDungeon.UnitTests;

public sealed class RandomTests
{
    [Fact]
    public void Seed42MatchesLockedXoshiro256StarStarV1Vector()
    {
        var random = new DeterministicRandom(42);
        ulong[] expected =
        [
            1546998764402558742UL,
            6990951692964543102UL,
            12544586762248559009UL,
            17057574109182124193UL,
            18295552978065317476UL,
        ];

        var actual = expected.Select(_ => random.NextUInt64()).ToArray();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void SameSeedProducesSameSequence()
    {
        var first = new DeterministicRandom(123456789);
        var second = new DeterministicRandom(123456789);

        for (var index = 0; index < 128; index++)
        {
            Assert.Equal(first.NextUInt64(), second.NextUInt64());
        }
    }

    [Fact]
    public void ExportedStateResumesExactlyWhereSequenceStopped()
    {
        var original = new DeterministicRandom(987654321);

        for (var index = 0; index < 17; index++)
        {
            _ = original.NextUInt64();
        }

        var state = original.ExportState();
        var restored = DeterministicRandom.FromState(state);

        Assert.Equal(DeterministicRandom.AlgorithmId, state.AlgorithmId);
        Assert.Equal(DeterministicRandom.AlgorithmVersion, state.AlgorithmVersion);

        for (var index = 0; index < 128; index++)
        {
            Assert.Equal(original.NextUInt64(), restored.NextUInt64());
        }
    }

    [Fact]
    public void RestoreRejectsDefaultWrongVersionAndAllZeroState()
    {
        Assert.Throws<ArgumentException>(
            () => DeterministicRandom.FromState(default));

        var wrongVersion = new RandomState(
            DeterministicRandom.AlgorithmId,
            DeterministicRandom.AlgorithmVersion + 1,
            1,
            2,
            3,
            4);
        Assert.Throws<ArgumentException>(
            () => DeterministicRandom.FromState(wrongVersion));

        var allZero = new RandomState(
            DeterministicRandom.AlgorithmId,
            DeterministicRandom.AlgorithmVersion,
            0,
            0,
            0,
            0);
        Assert.Throws<ArgumentException>(
            () => DeterministicRandom.FromState(allZero));
    }

    [Fact]
    public void BoundedRandomStaysInsideRangeAndRejectsZeroBound()
    {
        var random = new DeterministicRandom(7);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => random.NextUInt64(0));

        for (var index = 0; index < 10_000; index++)
        {
            Assert.InRange(random.NextUInt64(37), 0UL, 36UL);
        }

        Assert.Equal(0UL, random.NextUInt64(1));
    }

    [Fact]
    public void ProbabilityZeroAndOneDoNotConsumeRandomState()
    {
        var random = new DeterministicRandom(99);
        var before = random.ExportState();

        Assert.False(new Probability(0m).Roll(random));
        Assert.True(new Probability(1m).Roll(random));

        Assert.Equal(before, random.ExportState());
    }

    [Fact]
    public void ProbabilityRejectsOutOfRangeAndUsesNearestEvenQuantization()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new Probability(-0.0001m));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new Probability(1.0001m));

        Assert.Equal(
            Probability.Resolution / 2,
            new Probability(0.5m).Units);

        var halfUnit = 0.5m / Probability.Resolution;
        var oneAndHalfUnits = 1.5m / Probability.Resolution;

        Assert.Equal(0UL, new Probability(halfUnit).Units);
        Assert.Equal(2UL, new Probability(oneAndHalfUnits).Units);
    }

    [Fact]
    public void InteriorProbabilityConsumesRandomAndIsDeterministic()
    {
        var first = new DeterministicRandom(1234);
        var second = new DeterministicRandom(1234);
        var probability = new Probability(0.375m);

        for (var index = 0; index < 128; index++)
        {
            Assert.Equal(probability.Roll(first), probability.Roll(second));
        }

        Assert.NotEqual(
            new DeterministicRandom(1234).ExportState(),
            first.ExportState());
    }

    [Fact]
    public void WeightedSelectorUsesIntegerBoundariesDeterministically()
    {
        WeightedOption<string>[] options =
        [
            new("common", 5),
            new("rare", 3),
            new("legendary", 2),
        ];

        Assert.Equal(
            "common",
            WeightedSelector.Select(new FixedBoundRandom(0), options));
        Assert.Equal(
            "common",
            WeightedSelector.Select(new FixedBoundRandom(4), options));
        Assert.Equal(
            "rare",
            WeightedSelector.Select(new FixedBoundRandom(5), options));
        Assert.Equal(
            "rare",
            WeightedSelector.Select(new FixedBoundRandom(7), options));
        Assert.Equal(
            "legendary",
            WeightedSelector.Select(new FixedBoundRandom(8), options));
        Assert.Equal(
            "legendary",
            WeightedSelector.Select(new FixedBoundRandom(9), options));
    }

    [Fact]
    public void WeightedSelectorRejectsInvalidWeightsAndOverflow()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new WeightedOption<string>("invalid", 0));

        Assert.Throws<ArgumentException>(
            () => WeightedSelector.Select(
                new DeterministicRandom(1),
                Array.Empty<WeightedOption<string>>()));

        WeightedOption<string>[] containsDefault =
        [
            new("valid", 1),
            default,
        ];
        Assert.Throws<ArgumentException>(
            () => WeightedSelector.Select(
                new DeterministicRandom(1),
                containsDefault));

        WeightedOption<string>[] overflow =
        [
            new("large", ulong.MaxValue),
            new("one", 1),
        ];
        Assert.Throws<OverflowException>(
            () => WeightedSelector.Select(
                new DeterministicRandom(1),
                overflow));
    }

    [Fact]
    public void NamedStreamsAreStableDistinctAndInjectableAsIRandom()
    {
        const ulong rootSeed = 20261116;
        var combat = RandomStreams.Create(rootSeed, RandomStreamKind.Combat);
        var loot = RandomStreams.Create(rootSeed, RandomStreamKind.Loot);
        var ai = RandomStreams.Create(rootSeed, RandomStreamKind.AI);

        Assert.IsAssignableFrom<IRandom>(combat);
        Assert.IsAssignableFrom<IRandom>(loot);
        Assert.IsAssignableFrom<IRandom>(ai);

        var combatFirst = combat.NextUInt64();
        var lootFirst = loot.NextUInt64();
        var aiFirst = ai.NextUInt64();

        Assert.NotEqual(combatFirst, lootFirst);
        Assert.NotEqual(combatFirst, aiFirst);
        Assert.NotEqual(lootFirst, aiFirst);

        Assert.Equal(
            combatFirst,
            RandomStreams.Create(rootSeed, RandomStreamKind.Combat).NextUInt64());
        Assert.Equal(
            lootFirst,
            RandomStreams.Create(rootSeed, RandomStreamKind.Loot).NextUInt64());
        Assert.Equal(
            aiFirst,
            RandomStreams.Create(rootSeed, RandomStreamKind.AI).NextUInt64());
    }

    [Fact]
    public void AiSamplingDoesNotAdvanceLootStream()
    {
        const ulong rootSeed = 88;

        var baselineLoot = RandomStreams.Create(rootSeed, RandomStreamKind.Loot);
        var expected =
            Enumerable.Range(0, 32)
                .Select(_ => baselineLoot.NextUInt64())
                .ToArray();

        var ai = RandomStreams.Create(rootSeed, RandomStreamKind.AI);
        for (var index = 0; index < 100; index++)
        {
            _ = ai.NextUInt64();
        }

        var actualLoot = RandomStreams.Create(rootSeed, RandomStreamKind.Loot);
        var actual =
            Enumerable.Range(0, expected.Length)
                .Select(_ => actualLoot.NextUInt64())
                .ToArray();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void UnknownNamedStreamIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RandomStreams.Create(
                1,
                (RandomStreamKind)999));
    }

    private sealed class FixedBoundRandom(ulong value) : IRandom
    {
        public ulong NextUInt64() => value;

        public ulong NextUInt64(ulong exclusiveUpperBound)
        {
            ArgumentOutOfRangeException.ThrowIfZero(exclusiveUpperBound);

            if (value >= exclusiveUpperBound)
            {
                throw new InvalidOperationException("测试随机值必须小于调用方提供的上界。");
            }

            return value;
        }
    }
}
