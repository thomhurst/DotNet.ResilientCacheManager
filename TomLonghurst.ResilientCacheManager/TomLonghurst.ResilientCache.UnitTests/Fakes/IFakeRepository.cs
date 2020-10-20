using System.Threading.Tasks;

namespace TomLonghurst.ResilientCache.UnitTests.Fakes
{
    public interface IFakeRepository
    {
        Task<string> Get();
    }
}