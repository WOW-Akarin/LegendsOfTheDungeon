using LegendsOfTheDungeon.Entities.Core;
using LegendsOfTheDungeon.Kernel;

namespace LegendsOfTheDungeon.Runtime.World;

/// <summary>三维整数 Cell 坐标；对负坐标使用 floor 而不是截断。</summary>
/// <param name="X">X 轴 Cell 序号。</param>
/// <param name="Y">Y 轴 Cell 序号。</param>
/// <param name="Z">Z 轴 Cell 序号。</param>
public readonly record struct SpatialCellKey(int X, int Y, int Z);

/// <summary>单个 Cell 的生命周期；Unloaded 拒绝实体进入直到显式激活。</summary>
public enum SpatialCellLifecycle
{
    /// <summary>允许空间索引实体进入。</summary>
    Active,
    /// <summary>已卸载，不再接受实体；仅允许空 Cell 卸载。</summary>
    Unloaded
}

/// <summary>Cell 激活/卸载请求的明确状态。</summary>
public enum SpatialCellResult
{
    /// <summary>已激活。</summary>
    Activated,
    /// <summary>已卸载。</summary>
    Unloaded,
    /// <summary>此前已经处于所需状态。</summary>
    AlreadyCompleted,
    /// <summary>没有该 Cell 的记录。</summary>
    NotFound,
    /// <summary>仍有实体占用，不允许卸载。</summary>
    Occupied,
    /// <summary>超过总 Cell 预算。</summary>
    CapacityExceeded,
    /// <summary>非 Running 生命周期不能修改 Grid。</summary>
    InvalidState
}

/// <summary>只读 Cell 快照，不暴露内部桶和可变实体。</summary>
/// <param name="Key">Cell 坐标。</param>
/// <param name="Lifecycle">当前生命周期。</param>
/// <param name="Count">此 Cell 内附加/活动实体数量。</param>
public readonly record struct SpatialCellSnapshot(
    SpatialCellKey Key, SpatialCellLifecycle Lifecycle, int Count);

/// <summary>
/// 仅由 WorldInstance 在自身同步 _gate 之下修改的派生索引；从不拥有 Transform。
/// 只保存 EntityId 到 Cell 的映射。任意空间索引失败须由 WorldInstance fail-closed。
/// </summary>
internal sealed class WorldSpatialIndex
{
    internal const int MaximumCells = 16_384;
    private Dictionary<SpatialCellKey, Bucket> _cells = [];
    private Dictionary<EntityId, SpatialCellKey> _reverse = [];
    private readonly double _size;

    internal WorldSpatialIndex(double cellSize)
    {
        if (!double.IsFinite(cellSize) || cellSize is < 1 or > 4096)
        {
            throw new ArgumentOutOfRangeException(nameof(cellSize),
                "Cell size must be finite and between 1 and 4096 metres.");
        }

        _size = cellSize;
    }

    internal double CellSize => _size;
    internal int IndexedCount => _reverse.Count;
    internal int CellCount => _cells.Count;

    internal bool TryCell(Position position, out SpatialCellKey key)
    {
        var x = Math.Floor(position.X / _size);
        var y = Math.Floor(position.Y / _size);
        var z = Math.Floor(position.Z / _size);
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(z) ||
            x < int.MinValue || x > int.MaxValue ||
            y < int.MinValue || y > int.MaxValue ||
            z < int.MinValue || z > int.MaxValue)
        {
            key = default;
            return false;
        }

        key = new SpatialCellKey((int)x, (int)y, (int)z);
        return true;
    }

    internal WorldEntityResult ValidateDestination(Position position, out SpatialCellKey key)
    {
        if (!TryCell(position, out key))
        {
            return WorldEntityResult.SpatialOutOfBounds;
        }

        if (_cells.TryGetValue(key, out var bucket))
        {
            return bucket.Lifecycle == SpatialCellLifecycle.Unloaded
                ? WorldEntityResult.CellUnloaded : WorldEntityResult.Added;
        }

        return _cells.Count >= MaximumCells
            ? WorldEntityResult.CapacityExceeded : WorldEntityResult.Added;
    }

    internal void Add(EntityIdentity identity, SpatialCellKey key)
    {
        var bucket = ActiveBucket(key);
        if (!_reverse.TryAdd(identity.Id, key))
        {
            throw new InvalidOperationException("Spatial reverse index already owns EntityId.");
        }

        if (!bucket.Members.Add(identity.Id))
        {
            throw new InvalidOperationException("Spatial bucket already owns EntityId.");
        }
    }

    internal void Move(EntityIdentity identity, SpatialCellKey expected, SpatialCellKey destination)
    {
        if (!_reverse.TryGetValue(identity.Id, out var actual) || actual != expected)
        {
            throw new InvalidOperationException("Spatial reverse index does not match Transform.");
        }

        if (expected == destination)
        {
            return;
        }

        var source = ActiveBucket(expected);
        var target = ActiveBucket(destination);
        if (!source.Members.Contains(identity.Id) || !target.Members.Add(identity.Id))
        {
            throw new InvalidOperationException("Spatial move source/target index inconsistency.");
        }

        if (!source.Members.Remove(identity.Id))
        {
            throw new InvalidOperationException("Spatial move lost its source Cell.");
        }

        _reverse[identity.Id] = destination;
    }

    internal void Remove(EntityIdentity identity, Position oldPosition)
    {
        if (!TryCell(oldPosition, out var expected) ||
            !_reverse.TryGetValue(identity.Id, out var actual) || actual != expected ||
            !_cells.TryGetValue(expected, out var bucket) ||
            !bucket.Members.Remove(identity.Id) || !_reverse.Remove(identity.Id))
        {
            throw new InvalidOperationException("Spatial index removal does not match canonical Transform.");
        }
    }

    internal bool TrySnapshot(SpatialCellKey key, out SpatialCellSnapshot snapshot)
    {
        if (_cells.TryGetValue(key, out var bucket))
        {
            snapshot = new SpatialCellSnapshot(key, bucket.Lifecycle, bucket.Members.Count);
            return true;
        }

        snapshot = default;
        return false;
    }

    internal SpatialCellResult Activate(SpatialCellKey key)
    {
        if (_cells.TryGetValue(key, out var found))
        {
            if (found.Lifecycle == SpatialCellLifecycle.Active)
            {
                return SpatialCellResult.AlreadyCompleted;
            }

            found.Lifecycle = SpatialCellLifecycle.Active;
            return SpatialCellResult.Activated;
        }

        if (_cells.Count >= MaximumCells)
        {
            return SpatialCellResult.CapacityExceeded;
        }

        _cells.Add(key, new Bucket());
        return SpatialCellResult.Activated;
    }

    internal SpatialCellResult Unload(SpatialCellKey key)
    {
        if (!_cells.TryGetValue(key, out var found))
        {
            return SpatialCellResult.NotFound;
        }

        if (found.Lifecycle == SpatialCellLifecycle.Unloaded)
        {
            return SpatialCellResult.AlreadyCompleted;
        }

        if (found.Members.Count != 0)
        {
            return SpatialCellResult.Occupied;
        }

        found.Lifecycle = SpatialCellLifecycle.Unloaded;
        return SpatialCellResult.Unloaded;
    }

    internal IReadOnlyList<EntityId> Candidates(Bounds bounds)
    {
        var results = new List<EntityId>();
        foreach (var (key, bucket) in _cells)
        {
            if (bucket.Lifecycle != SpatialCellLifecycle.Active || !Overlaps(key, bounds))
            {
                continue;
            }

            results.AddRange(bucket.Members);
        }

        results.Sort(static (a, b) => a.Value.CompareTo(b.Value));
        return results;
    }

    internal IReadOnlyList<EntityId> Candidates(Position center, Distance radius)
    {
        var results = new List<EntityId>();
        foreach (var (key, bucket) in _cells)
        {
            if (bucket.Lifecycle != SpatialCellLifecycle.Active || !Overlaps(key, center, radius))
            {
                continue;
            }

            results.AddRange(bucket.Members);
        }

        results.Sort(static (a, b) => a.Value.CompareTo(b.Value));
        return results;
    }

    internal void Rebuild(IEnumerable<(EntityIdentity Identity, Position Position)> entities)
    {
        // 两阶段重建：保留当前空 Cell 的 Loaded/Unloaded 状态，完全验证后才发布新索引。
        var replacement = new WorldSpatialIndex(_size);
        foreach (var (key, old) in _cells)
        {
            replacement._cells.Add(key, new Bucket { Lifecycle = old.Lifecycle });
        }

        foreach (var (identity, position) in entities)
        {
            var validity = replacement.ValidateDestination(position, out var cell);
            if (validity != WorldEntityResult.Added)
            {
                throw new InvalidOperationException("Rebuild cannot index canonical Transform.");
            }

            replacement.Add(identity, cell);
        }

        _cells = replacement._cells;
        _reverse = replacement._reverse;
    }

    internal void Clear()
    {
        _cells.Clear();
        _reverse.Clear();
    }

    private Bucket ActiveBucket(SpatialCellKey key)
    {
        if (!_cells.TryGetValue(key, out var bucket))
        {
            if (_cells.Count >= MaximumCells)
            {
                throw new InvalidOperationException("Spatial Grid exhausted.");
            }

            bucket = new Bucket();
            _cells.Add(key, bucket);
        }

        if (bucket.Lifecycle != SpatialCellLifecycle.Active)
        {
            throw new InvalidOperationException("Unloaded Spatial Cell rejected a mutation.");
        }

        return bucket;
    }

    private bool Overlaps(SpatialCellKey key, Bounds bounds)
    {
        var x = (double)key.X * _size;
        var y = (double)key.Y * _size;
        var z = (double)key.Z * _size;
        return x <= bounds.Maximum.X && x + _size >= bounds.Minimum.X &&
               y <= bounds.Maximum.Y && y + _size >= bounds.Minimum.Y &&
               z <= bounds.Maximum.Z && z + _size >= bounds.Minimum.Z;
    }

    private bool Overlaps(SpatialCellKey key, Position center, Distance radius)
    {
        var x = (double)key.X * _size;
        var y = (double)key.Y * _size;
        var z = (double)key.Z * _size;
        var dx = AxisDistance(center.X, x, x + _size);
        var dy = AxisDistance(center.Y, y, y + _size);
        var dz = AxisDistance(center.Z, z, z + _size);
        return Math.Hypot(Math.Hypot(dx, dy), dz) <= radius.Meters;
    }

    private static double AxisDistance(double value, double min, double max) =>
        value < min ? min - value : value > max ? value - max : 0;

    private sealed class Bucket
    {
        internal SpatialCellLifecycle Lifecycle { get; set; } = SpatialCellLifecycle.Active;
        internal HashSet<EntityId> Members { get; } = [];
    }
}
