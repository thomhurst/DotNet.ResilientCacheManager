using System.Collections.Generic;
using System.Threading.Tasks;

namespace TomLonghurst.ResilientCache.Interfaces
{
    internal interface IResilientCacheManager<in TKey, TValue>
    {
        Task<TValue> Get(TKey key);
    }
}
