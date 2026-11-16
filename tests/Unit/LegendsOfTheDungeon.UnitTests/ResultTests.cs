using System.Text.Json;
using LegendsOfTheDungeon.Kernel;
using Xunit;

namespace LegendsOfTheDungeon.UnitTests;

public sealed class ResultTests
{
    [Fact]
    public void NonGenericResultEnforcesSuccessAndFailureAccess()
    {
        var error = new KernelError("kernel.invalid_id", "标识无效。");
        var success = Result.Success();
        var failure = Result.Failure(error);

        Assert.True(success.IsSuccess);
        Assert.False(success.IsFailure);
        Assert.Throws<InvalidOperationException>(() => _ = success.Error);

        Assert.True(failure.IsFailure);
        Assert.False(failure.IsSuccess);
        Assert.Same(error, failure.Error);
    }

    [Fact]
    public void GenericResultEnforcesValueAndErrorAccess()
    {
        var error = new KernelError("kernel.not_found", "未找到目标。");
        var success = Result.Success(new EntityId(7));
        var failure = Result.Failure<EntityId>(error);

        Assert.True(success.IsSuccess);
        Assert.Equal(7UL, success.Value.Value);
        Assert.Throws<InvalidOperationException>(() => _ = success.Error);

        Assert.True(failure.IsFailure);
        Assert.Same(error, failure.Error);
        Assert.Throws<InvalidOperationException>(() => _ = failure.Value);
    }

    [Fact]
    public void DefaultResultsAreRejectedInsteadOfSilentlyBecomingFailure()
    {
        var result = default(Result);
        var genericResult = default(Result<EntityId>);

        Assert.Throws<InvalidOperationException>(() => _ = result.IsSuccess);
        Assert.Throws<InvalidOperationException>(() => _ = result.IsFailure);
        Assert.Throws<InvalidOperationException>(() => _ = genericResult.IsSuccess);
        Assert.Throws<InvalidOperationException>(() => _ = genericResult.Value);
    }

    [Fact]
    public void GenericSuccessRejectsNullReferencePayload()
    {
        Assert.Throws<ArgumentNullException>(() => Result.Success<string>(null!));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Kernel.Invalid")]
    [InlineData("kernel invalid")]
    [InlineData("kernel/invalid")]
    public void ErrorRejectsUnstableMachineCodes(string code)
    {
        Assert.Throws<ArgumentException>(() => new KernelError(code, "错误。"));
    }

    [Fact]
    public void ErrorRejectsMachineCodeLongerThanContractLimit()
    {
        Assert.Throws<ArgumentException>(() => new KernelError(new string('a', 97), "错误。"));
    }

    [Fact]
    public void ErrorCodeSerializesAsStableExactMachineString()
    {
        var error = new KernelError(
            "kernel.invalid_id",
            "标识无效。",
            [new ValidationIssue("required", "entityId", "实体标识不能为空。")]);

        var json = JsonSerializer.Serialize(error);
        using var document = JsonDocument.Parse(json);

        Assert.Equal("kernel.invalid_id", document.RootElement.GetProperty("Code").GetString());
        Assert.Equal("required", document.RootElement.GetProperty("ValidationIssues")[0].GetProperty("Code").GetString());
    }

    [Fact]
    public void ErrorDefensivelyCopiesValidationIssues()
    {
        var issues = new List<ValidationIssue>
        {
            new("required", "entityId", "实体标识不能为空。")
        };

        var error = new KernelError("kernel.validation", "输入验证失败。", issues);
        issues.Add(new ValidationIssue("range", "worldId", "世界标识超出范围。"));

        Assert.Single(error.ValidationIssues);
        Assert.Equal("required", error.ValidationIssues[0].Code);
    }
}
