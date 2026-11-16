using System.Text.Json;
using LegendsOfTheDungeon.Kernel;
using LegendsOfTheDungeon.Platform;
using LegendsOfTheDungeon.Security.Contracts;
using Xunit;

namespace LegendsOfTheDungeon.UnitTests;

public sealed class PlatformTests
{
    private static readonly Guid Correlation = Guid.Parse("00000000-0000-4000-8000-000000000001");

    [Fact]
    public void ValidIndependentOptionsEnterRunningWithStableOrderIndependentHash()
    {
        var first = new ConfigurationStartup();
        first.Register(new WorkerOptions("platform.workers", 256));
        first.Register(new LoggingOptions(32, LogSeverity.Warning));
        first.Register(new SecretsOptions(
            new SensitiveValue("sentinel-token-dont-log"),
            new SensitiveValue("Host=db;Password=sentinel-conn-dont-log")));

        var second = new ConfigurationStartup();
        second.Register(new SecretsOptions(
            new SensitiveValue("different-private-token"),
            new SensitiveValue("Host=other;Password=different-password")));
        second.Register(new LoggingOptions(32, LogSeverity.Warning));
        second.Register(new WorkerOptions("platform.workers", 256));

        var fingerprint = first.Start();
        Assert.Equal(ConfigurationState.Running, first.State);
        Assert.Equal(64, fingerprint.Length);
        Assert.Equal(fingerprint, second.Start());
        Assert.Equal(fingerprint, first.Fingerprint);
        Assert.DoesNotContain("sentinel", fingerprint, StringComparison.OrdinalIgnoreCase);

        var changed = new ConfigurationStartup();
        changed.Register(new LoggingOptions(33, LogSeverity.Warning));
        changed.Register(new WorkerOptions("platform.workers", 256));
        changed.Register(new SecretsOptions(
            new SensitiveValue("different-private-token"),
            new SensitiveValue("Host=other;Password=different-password")));
        Assert.NotEqual(fingerprint, changed.Start());
        Assert.Throws<InvalidOperationException>(() => first.Start());
    }

    [Fact]
    public void InvalidOptionsFailBeforeRunningWithChineseRedactedDiagnostics()
    {
        const string token = "sentinel-token-should-never-appear";
        const string connection = "Host=db;Password=sentinel-password";

        var startup = new ConfigurationStartup();
        startup.Register(new LoggingOptions(0, (LogSeverity)999));
        startup.Register(new SecretsOptions(new SensitiveValue(token), new SensitiveValue(null)));
        startup.Register(new WorkerOptions("platform.workers", -1));

        var error = Assert.Throws<InvalidOperationException>(() => startup.Start());

        Assert.Equal(ConfigurationState.Faulted, startup.State);
        Assert.Contains("启动配置验证失败", error.Message, StringComparison.Ordinal);
        Assert.Contains("日志级别配置无效", error.Message, StringComparison.Ordinal);
        Assert.Contains("缺少必需的敏感配置", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(token, error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(connection, error.Message, StringComparison.Ordinal);
        Assert.NotEmpty(startup.Diagnostics);
        Assert.Throws<InvalidOperationException>(() => _ = startup.Fingerprint);
        Assert.Throws<InvalidOperationException>(() => startup.Start());
    }

    [Fact]
    public void InvalidModuleIdAndDuplicateOptionsCannotRegister()
    {
        var startup = new ConfigurationStartup();
        Assert.Throws<ArgumentException>(
            () => startup.Register(new WorkerOptions("Host=db;Password=secret", 1)));
        startup.Register(new LoggingOptions(1, LogSeverity.Debug));
        Assert.Throws<InvalidOperationException>(
            () => startup.Register(new LoggingOptions(2, LogSeverity.Warning)));
    }

    [Fact]
    public void MissingAllModulesCannotEnterRunning()
    {
        var startup = new ConfigurationStartup();

        var error = Assert.Throws<InvalidOperationException>(() => startup.Start());

        Assert.Equal(ConfigurationState.Faulted, startup.State);
        Assert.Contains("未注册任何技术模块配置", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void StructuredOperationalAuditAndPerformanceEventsCarryFullContext()
    {
        var sink = new RecordingSink();
        var logger = new StructuredLogger(sink);
        var context = new LogContext(
            "platform.logging",
            Correlation,
            new WorldId(7),
            new EntityId(9));

        logger.Write(context, LogEventId.OperationCompleted, LogSeverity.Information, LogCategory.Operation, true);
        logger.Write(context, LogEventId.AuthorizationDenied, LogSeverity.Warning, LogCategory.Audit, false);
        logger.Write(context, LogEventId.PerformanceSample, LogSeverity.Debug, LogCategory.Performance, true, 125);

        Assert.Equal(3, sink.Entries.Count);
        var json = StructuredLogFormatter.ToJson(sink.Entries[2]);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal("platform.logging", root.GetProperty("module").GetString());
        Assert.Equal(7UL, root.GetProperty("world").GetUInt64());
        Assert.Equal(9UL, root.GetProperty("entity").GetUInt64());
        Assert.Equal(Correlation.ToString("D"), root.GetProperty("correlation").GetString());
        Assert.Equal("Performance", root.GetProperty("category").GetString());
        Assert.Equal(125, root.GetProperty("elapsedMilliseconds").GetInt64());
        Assert.Equal("AuthorizationDenied", sink.Entries[1].EventId.ToString());
    }

    [Fact]
    public void LoggingRejectsUnsafeFreeTextAndInvalidDuration()
    {
        var sink = new RecordingSink();
        var logger = new StructuredLogger(sink);
        Assert.Throws<ArgumentException>(
            () => new LogContext("password=sentinel", Correlation));
        Assert.Throws<ArgumentException>(
            () => new LogContext("platform.logging", Guid.Empty));
        Assert.Throws<ArgumentException>(
            () => new LogContext("platform.logging", Correlation, default(WorldId)));

        var context = new LogContext("platform.logging", Correlation);
        Assert.Throws<ArgumentOutOfRangeException>(() => logger.Write(
            context, LogEventId.PerformanceSample, LogSeverity.Warning,
            LogCategory.Performance, false, -1));
        Assert.Empty(sink.Entries);
    }

    [Fact]
    public void SensitiveValuesAreRedactedAndCannotEnterStructuredLogContract()
    {
        const string token = "super-secret-token-sentinel";
        const string connection = "Host=database;Password=super-secret-connection-sentinel";
        var tokenValue = new SensitiveValue(token);
        var connectionValue = new SensitiveValue(connection);

        Assert.True(tokenValue.IsConfigured);
        Assert.True(connectionValue.IsConfigured);
        Assert.DoesNotContain(token, tokenValue.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(connection, connectionValue.ToString(), StringComparison.Ordinal);

        var sink = new RecordingSink();
        var logger = new StructuredLogger(sink);
        logger.Write(new LogContext("platform.secrets", Correlation),
            LogEventId.ConfigurationAccepted, LogSeverity.Information, LogCategory.Audit, true);
        var report = StructuredLogFormatter.ToJson(Assert.Single(sink.Entries));
        Assert.DoesNotContain(token, report, StringComparison.Ordinal);
        Assert.DoesNotContain(connection, report, StringComparison.Ordinal);
        Assert.DoesNotContain("Password=", report, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnconfiguredProductionIdentityAndAuthorizationDenyByDefault()
    {
        const string token = "dangerous-bearer-credential-sentinel";
        var binder = new DenyByDefaultIdentityBinder();
        var result = await binder.BindAsync(new AuthenticationEvidence(token), default);
        Assert.True(result.IsFailure);
        Assert.Equal("security.identity.denied", result.Error.Code);
        Assert.DoesNotContain(token, result.Error.Message, StringComparison.Ordinal);

        var authorizer = new DenyByDefaultCommandAuthorizer();
        var decision = authorizer.Authorize(null, "world.command.execute");
        Assert.True(decision.IsFailure);
        Assert.Equal("security.authorization.denied", decision.Error.Code);
    }

    [Fact]
    public void LocalTestPrincipalCannotBecomeProductionIdentity()
    {
        var identity = BoundIdentity.CreateLocalTest("local.player");
        Assert.Equal(IdentityOrigin.LocalTest, identity.Origin);
        Assert.Equal("local.player", identity.SubjectKey);
        Assert.Empty(typeof(BoundIdentity).GetConstructors());

        var result = ProductionIdentityGuard.RequireProduction(identity);
        Assert.True(result.IsFailure);
        Assert.Equal("security.identity.production_required", result.Error.Code);
        Assert.True(ProductionIdentityGuard.RequireProduction(null).IsFailure);

        var authorizer = new DenyByDefaultCommandAuthorizer();
        Assert.True(authorizer.Authorize(identity, "world.command.execute").IsFailure);
    }

    private sealed class RecordingSink : ILogSink
    {
        public List<StructuredLogEntry> Entries { get; } = [];

        public void Write(StructuredLogEntry entry)
        {
            Entries.Add(entry);
        }
    }
}
