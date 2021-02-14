using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Kroeg.EntityStore.Models;

namespace Kroeg.EntityStore.Store
{
  public class StagingEntityStore : IEntityStore
  {
    private readonly Dictionary<string, APEntity> _entities = new();

    public StagingEntityStore(IEntityStore next)
    {
      Bypass = next;
    }

    public async Task<APEntity> GetEntity(string id, bool doRemote)
    {
      if (_entities.ContainsKey(id)) return _entities[id];

      return await Bypass.GetEntity(id, doRemote);
    }

    public Task<APEntity> StoreEntity(APEntity entity)
    {
      _entities[entity.Id] = entity;
      return Task.FromResult(entity);
    }

    public async Task CommitChanges()
    {
      foreach (var item in _entities.ToList())
      {
        await Bypass.StoreEntity(item.Value);
      }

      _entities.Clear();

      await Bypass.CommitChanges();
    }

    public void TrimDown(string prefix)
    {
      foreach (var item in _entities.Keys.ToList())
      {
        if (prefix != null && item.StartsWith(prefix)) continue;
        _entities.Remove(item);
      }

    }

    public IEntityStore Bypass { get; }
  }
}
