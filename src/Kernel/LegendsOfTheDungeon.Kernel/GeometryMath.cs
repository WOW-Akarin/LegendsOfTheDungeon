namespace LegendsOfTheDungeon.Kernel;

internal static class GeometryMath
{
    internal static double RequireFinite(double value, string paramName)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(paramName, value, "几何值必须是有限数。");
        }

        return value;
    }

    internal static double RequireNonNegativeFinite(double value, string paramName)
    {
        RequireFinite(value, paramName);

        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(paramName, value, "几何值不能为负数。");
        }

        return value;
    }

    internal static double RequireFiniteResult(double value, string operation)
    {
        if (!double.IsFinite(value))
        {
            throw new OverflowException(operation + " 的结果超出有限 Double 可表示范围。");
        }

        return value;
    }

    internal static double Add(double left, double right, string operation)
    {
        return RequireFiniteResult(left + right, operation);
    }

    internal static double Subtract(double left, double right, string operation)
    {
        return RequireFiniteResult(left - right, operation);
    }

    internal static double Multiply(double left, double right, string operation)
    {
        return RequireFiniteResult(left * right, operation);
    }

    internal static double Hypot3(double x, double y, double z, string operation)
    {
        var absoluteX = Math.Abs(x);
        var absoluteY = Math.Abs(y);
        var absoluteZ = Math.Abs(z);
        var maximum = Math.Max(absoluteX, Math.Max(absoluteY, absoluteZ));

        if (maximum == 0)
        {
            return 0;
        }

        var scaledX = x / maximum;
        var scaledY = y / maximum;
        var scaledZ = z / maximum;
        var scaledLength = Math.Sqrt(
            (scaledX * scaledX)
            + (scaledY * scaledY)
            + (scaledZ * scaledZ));

        return RequireFiniteResult(maximum * scaledLength, operation);
    }

    internal static double NormalizeRadians(double radians)
    {
        RequireFinite(radians, nameof(radians));

        var normalized = radians % Math.Tau;
        if (normalized < 0)
        {
            normalized += Math.Tau;
        }

        return normalized == 0 ? 0 : normalized;
    }

    internal static bool DifferenceWithin(double left, double right, double tolerance)
    {
        if (left == right)
        {
            return true;
        }

        var difference = Math.Abs(left - right);
        return double.IsFinite(difference) && difference <= tolerance;
    }
}
