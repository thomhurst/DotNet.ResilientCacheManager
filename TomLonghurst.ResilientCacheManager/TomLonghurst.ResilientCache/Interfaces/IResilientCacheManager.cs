using System.Threading.Tasks;

namespace TomLonghurst.ResilientCache.Interfaces
{
    public interface IResilientCacheManager<T>
    {
        Task<T> GetValue();
    }
}
