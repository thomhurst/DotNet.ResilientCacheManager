using System;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using TomLonghurst.ResilientCache.UnitTests.Fakes;

namespace TomLonghurst.ResilientCache.UnitTests
{
    [Parallelizable(ParallelScope.None)]
    public class ResilientCacheManagerTests
    {
        private Mock<IFakeLogger> _logger;
        private Mock<IFakeRepository> _fakeRepository;

        [SetUp]
        public void Setup()
        {
            _logger = new Mock<IFakeLogger>();
            _fakeRepository = new Mock<IFakeRepository>();
        }

        [Test]
        public async Task
            When_Successful_And_CalledMultipleTimesWithinCachePeriod_Then_ResponseIsCachedAndDelegateCalledOnce()
        {
            var manager = new ResilientCacheManager<string>(TimeSpan.FromMinutes(5), GetAlwaysSuccessfulDelegate(),
                e => _logger.Object.WriteException(e));

            var result1 = await manager.GetValue();
            var result2 = await manager.GetValue();
            var result3 = await manager.GetValue();
            var result4 = await manager.GetValue();

            _fakeRepository.Verify(x => x.Get(), Times.Once);
            _logger.Verify(x => x.WriteException(It.IsAny<Exception>()), Times.Never);
        }

        [TestCase(TestExceptionType.RawException)]
        [TestCase(TestExceptionType.ExceptionWrappedInTask)]
        public void When_InitialLoadThrowsException_Then_NextCallToCacheKeyRetries(TestExceptionType testExceptionType)
        {
            var manager = new ResilientCacheManager<string>(TimeSpan.FromMinutes(5), GetAlwaysFailingDelegate(testExceptionType),
                e => _logger.Object.WriteException(e));

            Assert.ThrowsAsync<Exception>(() => manager.GetValue());
            Assert.ThrowsAsync<Exception>(() => manager.GetValue());
            Assert.ThrowsAsync<Exception>(() => manager.GetValue());
            Assert.ThrowsAsync<Exception>(() => manager.GetValue());

            _fakeRepository.Verify(x => x.Get(), Times.AtLeast(4));
            _logger.Verify(x => x.WriteException(It.IsAny<Exception>()), Times.Never);
        }

        [TestCase(TestExceptionType.RawException)]
        [TestCase(TestExceptionType.ExceptionWrappedInTask)]
        public async Task
            When_SuccessfulHttpResponse_And_NextCallFailsOnBackgroundRefresh_Then_ReturnPreviouslySuccessfulCacheAndDontThrowException(TestExceptionType testExceptionType)
        {
            var manager = new ResilientCacheManager<string>(TimeSpan.FromMilliseconds(1), GetAlwaysSuccessfulDelegate(),
                e => _logger.Object.WriteException(e));

            var result1 = await manager.GetValue();

            SetupExceptionResult(testExceptionType);

            await Task.Delay(50);

            Assert.DoesNotThrowAsync(() => manager.GetValue());

            _fakeRepository.Verify(x => x.Get(), Times.AtLeast(2));
            _logger.Verify(x => x.WriteException(It.IsAny<Exception>()), Times.AtLeast(1));
        }

        [Test]
        public async Task When_LeftAlone_Then_SuccessfullyRefreshesCacheInBackground()
        {
            var manager = new ResilientCacheManager<string>(TimeSpan.FromMilliseconds(1), GetAlwaysSuccessfulDelegate(),
                e => _logger.Object.WriteException(e));

            var result1 = await manager.GetValue();
            
            var tcs = new TaskCompletionSource<string>();
            _fakeRepository.Setup(x => x.Get()).Returns(() =>
            {
                tcs.SetResult("Blah");
                return tcs.Task;
            });
            
            await tcs.Task;

            _fakeRepository.Verify(x => x.Get(), Times.AtLeast(2));
            _logger.Verify(x => x.WriteException(It.IsAny<Exception>()), Times.Never);
        }

        [Test]
        public async Task When_RefreshingCache_Then_ReturnPreviousValue()
        {
            var manager = new ResilientCacheManager<string>(TimeSpan.FromMilliseconds(1), GetAlwaysSuccessfulDelegate(),
                e => _logger.Object.WriteException(e));

            var result1 = await manager.GetValue();

            var currentlyRefreshingCacheTask = new TaskCompletionSource<string>();
            var testReadyToContinueTask = new TaskCompletionSource<string>();
            _fakeRepository.Setup(x => x.Get()).Returns(() =>
            {
                testReadyToContinueTask.SetResult("Blah");
                return currentlyRefreshingCacheTask.Task;
            });

            await testReadyToContinueTask.Task;

            var result2 = await manager.GetValue();

            Assert.AreEqual(result1, result2);

            _fakeRepository.Verify(x => x.Get(), Times.AtLeast(2));
            _logger.Verify(x => x.WriteException(It.IsAny<Exception>()), Times.Never);
        }

        [Test]
        public void When_DoNotCallGet_Then_BackgroundRefreshIsNotCalledBeforeInterval()
        {
            var manager =
                new ResilientCacheManager<string>(TimeSpan.FromMinutes(5), GetAlwaysSuccessfulDelegate(), null);

            _fakeRepository.Verify(x => x.Get(), Times.Never);
        }

        [Test]
        public async Task When_DoNotCallGet_Then_BackgroundRefreshIsTriggeredAfterInterval()
        {
            var tcs = new TaskCompletionSource<string>();
            _fakeRepository.Setup(x => x.Get()).Returns(tcs.Task);
            
            var manager =
                new ResilientCacheManager<string>(TimeSpan.FromMilliseconds(1), () =>
                {
                    var task = _fakeRepository.Object.Get();
                    tcs.SetResult("Blah");
                    return task;
                }, null);
            
            await tcs.Task;

            _fakeRepository.Verify(x => x.Get(), Times.AtLeastOnce);
        }

        [Test]
        public void When_RetryingWhileExistingTaskIsPending_Then_ReturnSameSuccessfulTask()
        {
            var pendingTaskSource = new TaskCompletionSource<string>();

            _fakeRepository.Setup(x => x.Get()).Returns(pendingTaskSource.Task);

            var manager = new ResilientCacheManager<string>(TimeSpan.FromMinutes(1),
                () => _fakeRepository.Object.Get(), e => _logger.Object.WriteException(e));

            var task1 = manager.GetValue();
            var task2 = manager.GetValue();

            pendingTaskSource.SetResult("Blah");

            Assert.DoesNotThrowAsync(() => Task.WhenAll(task1, task2));

            Assert.True(task1.IsCompleted);
            Assert.True(task2.IsCompleted);

            _fakeRepository.Verify(x => x.Get(), Times.Once);
        }

        [Test]
        public void When_RetryingWhileExistingTaskIsPending_Then_ReturnSameFailedTask()
        {
            var pendingTaskSource1 = new TaskCompletionSource<string>();
            var pendingTaskSource2 = new TaskCompletionSource<string>();

            _fakeRepository.Setup(x => x.Get()).Returns(pendingTaskSource1.Task);

            var manager = new ResilientCacheManager<string>(TimeSpan.FromMinutes(5),
                () => _fakeRepository.Object.Get(), e => _logger.Object.WriteException(e));

            var task1 = manager.GetValue();

            _fakeRepository.Setup(x => x.Get()).Returns(pendingTaskSource2.Task);

            var task2 = manager.GetValue();

            pendingTaskSource1.SetException(new TaskCanceledException());
            pendingTaskSource2.SetResult("Blah");

            Assert.ThrowsAsync<TaskCanceledException>(() => Task.WhenAll(task1, task2));

            Assert.True(task1.IsCanceled);
            Assert.True(task2.IsCanceled);

            _fakeRepository.Verify(x => x.Get(), Times.Once);
        }

        private Func<Task<string>> GetAlwaysSuccessfulDelegate()
        {
            _fakeRepository.Setup(x => x.Get()).ReturnsAsync("Blah");
            return () => _fakeRepository.Object.Get();
        }

        private Func<Task<string>> GetAlwaysFailingDelegate(TestExceptionType testException)
        {
            SetupExceptionResult(testException);
            return () => _fakeRepository.Object.Get();
        }

        private void SetupExceptionResult(TestExceptionType testException)
        {
            if (testException == TestExceptionType.RawException)
            {
                _fakeRepository.Setup(x => x.Get()).Throws<Exception>();
            }
            else
            {
                _fakeRepository.Setup(x => x.Get()).Returns(Task.FromException<string>(new Exception()));
            }
        }
    }

    public enum TestExceptionType
    {
        RawException,
        ExceptionWrappedInTask
    }
}
