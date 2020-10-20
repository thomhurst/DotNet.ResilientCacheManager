using System.Threading.Tasks;

namespace TomLonghurst.ResilientCache
{
    public interface IResilientCacheManager<T>
    {
        Task<T> GetValue();
    }
}
