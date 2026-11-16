using System.Text.Json;
using LegendsOfTheDungeon.Kernel;
using Xunit;

namespace LegendsOfTheDungeon.UnitTests;

public sealed class GeometryTests
{
    [Fact]
    public void DefaultGeometryValuesAreValidMathematicalZeros()
    {
        Assert.Equal(new Position(0, 0, 0), default(Position));
        Assert.Equal(new Vector(0, 0, 0), default(Vector));
        Assert.Equal(new Distance(0), default(Distance));
        Assert.Equal(new Angle(0), default(Angle));
        Assert.Equal(new Transform(default, default), default(Transform));
        Assert.Equal(new Bounds(default, default), default(Bounds));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(6.283185307179586, 0)]
    [InlineData(-1.5707963267948966, 4.71238898038469)]
    [InlineData(15.707963267948966, 3.141592653589793)]
    public void AngleNormalizesToZeroInclusiveTauExclusive(double input, double expected)
    {
        var angle = new Angle(input);

        Assert.Equal(expected, angle.Radians, 14);
        Assert.InRange(angle.Radians, 0, Math.Tau);
        Assert.True(angle.Radians < Math.Tau);
    }

    [Fact]
    public void YawUsesRightHandedZUpConvention()
    {
        Assert.True(
            Vector.FromYaw(new Angle(0))
                .NearlyEquals(new Vector(1, 0, 0)));

        Assert.True(
            Vector.FromYaw(new Angle(Math.PI / 2))
                .NearlyEquals(new Vector(0, 1, 0)));

        Assert.True(
            Vector.FromYaw(new Angle(Math.PI))
                .NearlyEquals(new Vector(-1, 0, 0)));
    }

    [Fact]
    public void PositionDistanceAndRangeUseClosedBoundary()
    {
        var origin = new Position(0, 0, 0);
        var target = new Position(3, 4, 12);

        Assert.Equal(13, origin.DistanceTo(target).Meters, 12);
        Assert.True(origin.IsWithin(target, new Distance(13)));
        Assert.False(origin.IsWithin(target, new Distance(12.999)));
        Assert.Equal(new Vector(3, 4, 12), origin.VectorTo(target));
    }

    [Fact]
    public void VectorNormalizesNonZeroAndRejectsZeroLength()
    {
        var normalized = new Vector(3, 4, 0).Normalize();

        Assert.True(normalized.NearlyEquals(new Vector(0.6, 0.8, 0)));
        Assert.Equal(1, normalized.Length, 12);
        Assert.Throws<InvalidOperationException>(() => default(Vector).Normalize());
    }

    [Fact]
    public void BoundsContainTheirSurfaceAndMeasureOutsideDistance()
    {
        var bounds = new Bounds(
            new Position(0, 0, 0),
            new Position(10, 10, 10));

        Assert.True(bounds.Contains(new Position(0, 10, 5)));
        Assert.True(bounds.Contains(new Position(10, 10, 10)));
        Assert.False(bounds.Contains(new Position(10.001, 10, 10)));
        Assert.Equal(0, bounds.DistanceTo(new Position(5, 5, 5)).Meters);
        Assert.Equal(5, bounds.DistanceTo(new Position(13, 14, 10)).Meters, 12);
        Assert.True(bounds.IsWithin(new Position(13, 14, 10), new Distance(5)));
        Assert.False(bounds.IsWithin(new Position(13, 14, 10), new Distance(4.999)));
    }

    [Fact]
    public void BoundsRejectReversedAxes()
    {
        Assert.Throws<ArgumentException>(
            () => new Bounds(
                new Position(1, 0, 0),
                new Position(0, 1, 1)));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void GeometryRejectsNonFiniteInputs(double invalid)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Position(invalid, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Vector(0, invalid, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Distance(invalid));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Angle(invalid));
    }

    [Fact]
    public void DistanceRejectsNegativeValuesAndInvalidTolerance()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Distance(-0.001));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new Distance(1).NearlyEquals(new Distance(1), -1));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new Vector(1, 0, 0).NearlyEquals(new Vector(1, 0, 0), double.NaN));
    }

    [Fact]
    public void FiniteArithmeticThatWouldBecomeInfinityIsRejected()
    {
        var maximum = double.MaxValue;

        Assert.Throws<OverflowException>(
            () => new Position(maximum, 0, 0).Translate(new Vector(maximum, 0, 0)));

        Assert.Throws<OverflowException>(
            () => new Vector(maximum, 0, 0).Scale(2));

        Assert.Throws<OverflowException>(
            () => new Position(-maximum, 0, 0).DistanceTo(new Position(maximum, 0, 0)));

        Assert.Throws<OverflowException>(
            () => new Vector(maximum, maximum, maximum).Length);
    }

    [Fact]
    public void TransformForwardMatchesNormalizedYaw()
    {
        var transform = new Transform(
            new Position(2, 3, 4),
            new Angle(-Math.PI / 2));

        Assert.Equal(new Position(2, 3, 4), transform.Position);
        Assert.True(transform.Forward().NearlyEquals(new Vector(0, -1, 0)));
        Assert.Equal(3 * Math.PI / 2, transform.Yaw.Radians, 12);
    }

    [Fact]
    public void AdapterStyleJsonRoundTripRebuildsValidatedGeometry()
    {
        var originalTransform = new Transform(
            new Position(12.5, -8.25, 3),
            new Angle(7.5));
        var originalBounds = new Bounds(
            new Position(-10, -20, -30),
            new Position(10, 20, 30));

        var wire = new GeometryWire(
            originalTransform.Position.X,
            originalTransform.Position.Y,
            originalTransform.Position.Z,
            originalTransform.Yaw.Radians,
            originalBounds.Minimum.X,
            originalBounds.Minimum.Y,
            originalBounds.Minimum.Z,
            originalBounds.Maximum.X,
            originalBounds.Maximum.Y,
            originalBounds.Maximum.Z);

        var json = JsonSerializer.Serialize(wire);
        var restoredWire = JsonSerializer.Deserialize<GeometryWire>(json);
        Assert.NotNull(restoredWire);

        var restoredTransform = new Transform(
            new Position(restoredWire.X, restoredWire.Y, restoredWire.Z),
            new Angle(restoredWire.Yaw));
        var restoredBounds = new Bounds(
            new Position(restoredWire.MinX, restoredWire.MinY, restoredWire.MinZ),
            new Position(restoredWire.MaxX, restoredWire.MaxY, restoredWire.MaxZ));

        Assert.Equal(originalTransform, restoredTransform);
        Assert.Equal(originalBounds, restoredBounds);
    }

    private sealed record GeometryWire(
        double X,
        double Y,
        double Z,
        double Yaw,
        double MinX,
        double MinY,
        double MinZ,
        double MaxX,
        double MaxY,
        double MaxZ);
}
