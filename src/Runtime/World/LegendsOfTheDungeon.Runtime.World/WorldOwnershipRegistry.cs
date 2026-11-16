using LegendsOfTheDungeon.Entities.Core;
using LegendsOfTheDungeon.Kernel;

namespace LegendsOfTheDungeon.Runtime.World;

/// <summary>
/// 显式注入的进程内世界与实体归属协调器；所有并行 WorldInstance 必须共享同一实例。
/// 这里只保存 Owner 引用与不可变身份，不保存可变实体能力或世界状态。
/// </summary>
public sealed class WorldOwnershipRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<WorldId, WorldInstance> _worlds = [];
    private readonly Dictionary<EntityId, Ownership> _owners = [];

    /// <summary>仍注册的世界数量。</summary>
    public int RegisteredWorlds
    {
        get
        {
            lock (_gate)
            {
                return _worlds.Count;
            }
        }
    }

    /// <summary>当前仍活跃或已附加实体的唯一归属记录数量。</summary>
    public int ClaimedEntities
    {
        get
        {
            lock (_gate)
            {
                return _owners.Count;
            }
        }
    }

    /// <summary>按值查询此 EntityId 的单一归属版本；不返回 WorldInstance 或实体引用。</summary>
    /// <param name="id">待查询的实体 ID。</param>
    /// <param name="identity">存在时输出世界归属版本。</param>
    /// <returns>是否存在占用。</returns>
    public bool TryGetOwner(EntityId id, out EntityIdentity identity)
    {
        lock (_gate)
        {
            if (_owners.TryGetValue(id, out var current))
            {
                identity = current.Identity;
                return true;
            }
        }

        identity = default;
        return false;
    }

    internal void RegisterWorld(WorldInstance owner)
    {
        lock (_gate)
        {
            if (!_worlds.TryAdd(owner.World, owner))
            {
                throw new InvalidOperationException("Only one registered WorldInstance may own a WorldId.");
            }
        }
    }

    internal bool TryClaim(WorldInstance owner, EntityIdentity identity)
    {
        lock (_gate)
        {
            if (!_worlds.TryGetValue(owner.World, out var registered) ||
                !ReferenceEquals(registered, owner))
            {
                throw new InvalidOperationException("WorldInstance is not registered.");
            }

            if (_owners.ContainsKey(identity.Id))
            {
                return false;
            }

            _owners.Add(identity.Id, new Ownership(owner, identity));
            return true;
        }
    }

    internal void Release(WorldInstance owner, EntityIdentity identity)
    {
        lock (_gate)
        {
            if (!_owners.TryGetValue(identity.Id, out var existing) ||
                !ReferenceEquals(existing.Owner, owner) || existing.Identity != identity)
            {
                throw new InvalidOperationException("World ownership release did not match the live claim.");
            }

            _owners.Remove(identity.Id);
        }
    }

    internal void UnregisterWorld(WorldInstance owner)
    {
        lock (_gate)
        {
            if (!_worlds.TryGetValue(owner.World, out var current) ||
                !ReferenceEquals(current, owner) ||
                _owners.Values.Any(claim => ReferenceEquals(claim.Owner, owner)))
            {
                throw new InvalidOperationException("Cannot unregister a world with live ownership claims.");
            }

            _worlds.Remove(owner.World);
        }
    }

    private sealed record Ownership(WorldInstance Owner, EntityIdentity Identity);
}
