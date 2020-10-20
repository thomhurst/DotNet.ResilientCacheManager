using System;
using System.Threading.Tasks;
using TomLonghurst.ResilientCache.Interfaces;

namespace TomLonghurst.ResilientCache
{
    public class ResilientCacheManager<TValue> : IResilientCacheManager<TValue>
    {
        private readonly object _delegateResponseSharedTaskObjectLock = new object();
        private Task<TValue> _delegateResponseSharedTask;
        
        private readonly TimeSpan _refreshInterval;
        private readonly Func<Task<TValue>> _dataRetrieverDelegate;
        private readonly Action<Exception> _onBackgroundException;

        public ResilientCacheManager(TimeSpan refreshInterval, Func<Task<TValue>> dataRetrieverDelegate, Action<Exception> onBackgroundException)
        {
            _refreshInterval = refreshInterval;
            _dataRetrieverDelegate = dataRetrieverDelegate;
            _onBackgroundException = onBackgroundException;
            
            ValidateSettings();

            Task.Factory.StartNew(SetupBackgroundRefresh);
        }

        public async Task<TValue> GetValue()
        {
            try
            {
                var thresholdsResponseSharedTask = GetOrCreateSharedTaskIfNotExists();

                return await thresholdsResponseSharedTask;
            }
            catch (Exception)
            {
                // If task failed, remove it, as we don't want new callers to immediately receive exceptions, we'd want them to retry instead
                lock (_delegateResponseSharedTaskObjectLock)
                {
                    _delegateResponseSharedTask = null;
                }

                throw;
            }
        }

        private async Task SetupBackgroundRefresh()
        {
            while (true)
            {
                await Task.Delay(_refreshInterval);

                try
                {
                    var dataTask = _dataRetrieverDelegate();
                    var successfulResponse = await dataTask;
                    // The await will throw an exception if something goes wrong
                    // So the assignment below will always only be on a successful state
                    lock (_delegateResponseSharedTaskObjectLock)
                    {
                        _delegateResponseSharedTask = dataTask;
                    }
                }
                catch (Exception e)
                {
                    // Don't error - We want this thread to keep going
                    _onBackgroundException?.Invoke(e);
                }
            }
        }

        private Task<TValue> GetOrCreateSharedTaskIfNotExists()
        {
            lock (_delegateResponseSharedTaskObjectLock)
            {
                if (_delegateResponseSharedTask == null)
                {
                    _delegateResponseSharedTask = _dataRetrieverDelegate();
                }
                
                return _delegateResponseSharedTask;
            }
        }

        private void ValidateSettings()
        {
            if (_refreshInterval == null)
            {
                throw new ArgumentNullException(nameof(_refreshInterval));
            }
            
            if (_dataRetrieverDelegate == null)
            {
                throw new ArgumentNullException(nameof(_dataRetrieverDelegate));
            }
        }
    }
}
