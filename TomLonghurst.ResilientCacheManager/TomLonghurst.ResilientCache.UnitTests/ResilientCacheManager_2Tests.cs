using System;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using TomLonghurst.ResilientCache.UnitTests.Fakes;

namespace TomLonghurst.ResilientCache.UnitTests
{
    [TestFixture]
    [Parallelizable(ParallelScope.None)]
    public class ResilientCacheManager2Tests
    {
        private Mock<IFakeLogger> _logger;
        private Mock<IFakeKeyedRepository> _fakeRepository;
        
        private ResilientCacheManager<string, string> _resilientCacheManager;

        [SetUp]
        public void Setup()
        {
            _logger = new Mock<IFakeLogger>();
            _fakeRepository = new Mock<IFakeKeyedRepository>();

            CreateCacheManager();
        }

        private void CreateCacheManager()
        {
            _resilientCacheManager = new ResilientCacheManager<string, string>(TimeSpan.FromMinutes(5), 
                GetAlwaysSuccessfulDelegate(), 
                exception => _logger.Object.WriteException(exception));
        }
        
        [TestCase("")]
        [TestCase("Key2")]
        public async Task WhenCalledOnce_ThenCallsDelegateOnce(string ksdv)
        {
            await _resilientCacheManager.Get(ksdv);
            
            _fakeRepository.Verify(x => x.Get(ksdv), Times.Once);
        }
        
        [TestCase("")]
        [TestCase("Key2")]
        public async Task WhenCalledTwice_WithinCachedPeriod_ThenCallsDelegateOnlyOnce(string ksdv)
        {
            var result1 = await _resilientCacheManager.Get(ksdv);
            var result2 = await _resilientCacheManager.Get(ksdv);
            
            _fakeRepository.Verify(x => x.Get(It.IsAny<string>()), Times.Once);
        }
        
        [TestCase("")]
        [TestCase("Key2")]
        public async Task WhenCalledOnce_AndAgainAfterCachedPeriod_ThenCallsDelegateTwice(string ksdv)
        {
            _resilientCacheManager = new ResilientCacheManager<string, string>(TimeSpan.FromMilliseconds(5), 
                GetAlwaysSuccessfulDelegate(), 
                exception => _logger.Object.WriteException(exception));

            await _resilientCacheManager.Get(ksdv);
            await Task.Delay(50);
            await _resilientCacheManager.Get(ksdv);
            
            _fakeRepository.Verify(x => x.Get(It.IsAny<string>()), Times.AtLeast(2));
        }
        
        [TestCase("")]
        [TestCase("Key2")]
        public async Task WhenCalledOnce_AndWaitForBackgroundRefresh_ThenCallsDelegateTwice(string ksdv)
        {
            _resilientCacheManager = new ResilientCacheManager<string, string>(TimeSpan.FromMilliseconds(5), 
                GetAlwaysSuccessfulDelegate(), 
                exception => _logger.Object.WriteException(exception));

            await _resilientCacheManager.Get(ksdv);
            await Task.Delay(50);

            _fakeRepository.Verify(x => x.Get(It.IsAny<string>()), Times.AtLeast(2));
        }
        
        [TestCase("")]
        [TestCase("Key2")]
        public async Task When_CalledDelegateThrowsExceptionInBackground_Then_CanStillRetrievePreviousData(string ksdv)
        {
            _resilientCacheManager = new ResilientCacheManager<string, string>(TimeSpan.FromMilliseconds(5), 
                GetAlwaysSuccessfulDelegate(), 
                exception => _logger.Object.WriteException(exception));
            
            await _resilientCacheManager.Get(ksdv);

            _fakeRepository.Setup(x => x.Get(It.IsAny<string>()))
                .Throws(new Exception());

            await Task.Delay(50);

            Assert.DoesNotThrowAsync(() => _resilientCacheManager.Get(ksdv));
            
            _fakeRepository.Verify(x => x.Get(It.IsAny<string>()), Times.AtLeast(2));
        }
        
        [Test]
        public async Task WhenCalledWithDifferentKeys_ThenDelegateIsCalledForEachKey()
        {
            await _resilientCacheManager.Get("Key");
            await _resilientCacheManager.Get("Key2");

            _fakeRepository.Verify(x => x.Get("Key"), Times.Exactly(1));
            _fakeRepository.Verify(x => x.Get("Key2"), Times.Exactly(1));
        }
        
        [Test]
        public async Task WhenCalledWithDifferentKeys_WithinCachedPeriod_ThenDelegateIsCalledOnceForEachKey()
        {
            await _resilientCacheManager.Get("Key");
            await _resilientCacheManager.Get("Key2");
            await _resilientCacheManager.Get("Key");
            await _resilientCacheManager.Get("Key2");
            await _resilientCacheManager.Get("Key");
            await _resilientCacheManager.Get("Key2");

            _fakeRepository.Verify(x => x.Get("Key"), Times.Exactly(1));
            _fakeRepository.Verify(x => x.Get("Key2"), Times.Exactly(1));
        }
        
        [Test]
        public async Task When_MakingMultipleCallsToCacheKeyBeforeInitialLoadCompletes_Then_ValueOnlyLoadedOnce()
        {
            var pendingTaskSource = new TaskCompletionSource<string>();
            _fakeRepository.Setup(x => x.Get(It.IsAny<string>()))
                .Returns(() => pendingTaskSource.Task);
            CreateCacheManager();
            
            var task1 = _resilientCacheManager.Get("Key");
            var task2 = _resilientCacheManager.Get("Key");
            var task3 = _resilientCacheManager.Get("Key");

            pendingTaskSource.SetResult("Hola");

            await Task.WhenAll(task1, task2, task3);
            
            _fakeRepository.Verify(x => x.Get("Key"), Times.Exactly(1));
        }
        
        [Test]
        public async Task When_RefreshingCacheKey_Then_PreviousValueReturnedUntilRefreshCompletes()
        {
            _fakeRepository.Setup(x => x.Get(It.IsAny<string>()))
                .ReturnsAsync(() => "1");

            _resilientCacheManager = new ResilientCacheManager<string, string>(TimeSpan.FromMilliseconds(5), 
                _fakeRepository.Object.Get, 
                exception => _logger.Object.WriteException(exception));

            var initialResult = await _resilientCacheManager.Get("Key");
            
            var pendingTaskSource = new TaskCompletionSource<string>();
            _fakeRepository.Setup(x => x.Get(It.IsAny<string>()))
                .Returns(() => pendingTaskSource.Task);

            await Task.Delay(50);
            
            var secondResult = await _resilientCacheManager.Get("Key");

            _fakeRepository.Verify(x => x.Get(It.IsAny<string>()), Times.AtLeast(2));
            
            Assert.That(secondResult, Is.EqualTo("1"));
        }
        
        [Test]
        public async Task When_InitialLoadThrowsException_Then_NextCallToCacheKeyRetries()
        {
            _fakeRepository.Setup(x => x.Get(It.IsAny<string>()))
                .Throws(new Exception());

            Assert.ThrowsAsync<Exception>(() => _resilientCacheManager.Get("Key"));
            
            _fakeRepository.Setup(x => x.Get(It.IsAny<string>()))
                .ReturnsAsync(() => "Yo1");
            
            var secondResult = await _resilientCacheManager.Get("Key");

            _fakeRepository.Verify(x => x.Get("Key"), Times.Exactly(2));

            Assert.That(secondResult, Is.EqualTo("Yo1"));
        }
        
        [Test]
        public void QueuedTasksAllFailTogether()
        {
            var pendingTaskSource = new TaskCompletionSource<string>();
            _fakeRepository
                .Setup(x => x.Get(It.IsAny<string>()))
                .Returns(pendingTaskSource.Task);

            //Queue up 3 loads;
            var task1 = _resilientCacheManager.Get("Key");
            
            //QueuedTasksAllFailTogether: Reset the mock to return a different value before the 2nd and 3rd calls
            //So this in effect proves that it still gets the task from the first calls (because it's still pending)
            _fakeRepository
                .Setup(x => x.Get(It.IsAny<string>()))
                .ReturnsAsync("Hallo1");
            
            var task2 = _resilientCacheManager.Get("Key");
            var task3 = _resilientCacheManager.Get("Key");

            pendingTaskSource.SetException(new TaskCanceledException());

            Assert.ThrowsAsync<TaskCanceledException>(() => task1);
            Assert.ThrowsAsync<TaskCanceledException>(() => task2);
            Assert.ThrowsAsync<TaskCanceledException>(() => task3);

            Assert.True(ReferenceEquals(task1.Exception, task2.Exception));
            Assert.True(ReferenceEquals(task1.Exception, task3.Exception));
            Assert.True(ReferenceEquals(task2.Exception, task3.Exception));
            
            _fakeRepository.Verify(x => x.Get("Key"), Times.Once);
        }

        [Test]
        public async Task When_RetryingWhileExistingTaskIsPending_Then_ReturnSameSuccessfulTask()
        {
            var pendingTaskSource = new TaskCompletionSource<string>();

            CreateCacheManager();
            
            _fakeRepository
                .Setup(x => x.Get(It.IsAny<string>()))
                .Returns(() => pendingTaskSource.Task);
            
            var task1 = _resilientCacheManager.Get("Key");
            var task2 = _resilientCacheManager.Get("Key");
            
            Assert.True(!task1.IsCompleted);
            Assert.True(!task2.IsCompleted);
            
            pendingTaskSource.SetResult("G'day");
            
            await Task.WhenAll(task1, task2);
            
            Assert.True(task1.IsCompleted);
            Assert.True(task2.IsCompleted);
            
            _fakeRepository.Verify(x => x.Get("Key"), Times.Once);
        }
        
        [Test]
        public void When_RetryingWhileExistingTaskIsPending_Then_ReturnSameFailedTask()
        {
            var pendingTaskSource1 = new TaskCompletionSource<string>();
            var pendingTaskSource2 = new TaskCompletionSource<string>();

            CreateCacheManager();
            
            _fakeRepository
                .Setup(x => x.Get(It.IsAny<string>()))
                .Returns(() => pendingTaskSource1.Task);
            var task1 = _resilientCacheManager.Get("Key");
            
            _fakeRepository
                .Setup(x => x.Get(It.IsAny<string>()))
                .Returns(() => pendingTaskSource2.Task);
            var task2 = _resilientCacheManager.Get("Key");
            
            pendingTaskSource1.SetException(new TaskCanceledException());
            pendingTaskSource2.SetResult("Guten tag");
            
            Assert.ThrowsAsync<TaskCanceledException>(() => Task.WhenAll(task1, task2));
            
            Assert.True(task1.IsCanceled);
            Assert.True(task2.IsCanceled);
            
            _fakeRepository.Verify(x => x.Get("Key"), Times.Once);
        }
        
        
        private Func<string, Task<string>> GetAlwaysSuccessfulDelegate()
        {
            _fakeRepository.Setup(x => x.Get(It.IsAny<string>())).ReturnsAsync("InitialValue");
            return key => _fakeRepository.Object.Get(key);
        }
    }
}
