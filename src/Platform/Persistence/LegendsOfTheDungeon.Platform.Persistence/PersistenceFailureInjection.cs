namespace LegendsOfTheDungeon.Platform.Persistence;

/// <summary>P65.F 可控数据库故障点；仅供独立集成环境显式使用。</summary>
public enum PersistenceFailurePoint
{
    /// <summary>数据库 COMMIT 发出之前，仍可安全回滚。</summary>
    BeforeCommit,
    /// <summary>数据库 COMMIT 已成功返回，但向业务层确认之前。</summary>
    AfterCommit,
    /// <summary>数据库 ROLLBACK 已成功返回，但向业务层确认之前。</summary>
    AfterRollback
}

/// <summary>测试注入契约；正式生产连接工厂默认不携带任何故障点。</summary>
public interface IPersistenceFailureInjector
{
    /// <summary>在命名的持久化阶段触发可控故障，不得泄漏连接串或参数值。</summary>
    /// <param name="point">当前明确的数据库边界。</param>
    void Trigger(PersistenceFailurePoint point);
}
