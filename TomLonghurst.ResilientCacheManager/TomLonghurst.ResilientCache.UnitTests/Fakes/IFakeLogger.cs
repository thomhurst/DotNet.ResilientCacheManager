using System;

namespace TomLonghurst.ResilientCache.UnitTests.Fakes
{
    public interface IFakeLogger
    {
        void WriteException(Exception exception);
    }
}