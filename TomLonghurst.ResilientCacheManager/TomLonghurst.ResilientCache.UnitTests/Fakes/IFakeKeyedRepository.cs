using System.Threading.Tasks;

namespace TomLonghurst.ResilientCache.UnitTests.Fakes
{
    public interface IFakeKeyedRepository
    {
        Task<string> Get(string key);
    }
}