using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using TomLonghurst.ResilientCache.Interfaces;

namespace TomLonghurst.ResilientCache
{
    public class ResilientCacheManager<TKey, TValue> : IResilientCacheManager<TKey, TValue>, IDisposable
    {
        private readonly TimeSpan _refreshInterval;
        private readonly Func<TKey, Task<TValue>> _dataRetrieverDelegate;
        private readonly Action<Exception> _onBackgroundException;
        private ImmutableDictionary<TKey, Task<TValue>> _getValueTasks = ImmutableDictionary<TKey, Task<TValue>>.Empty;

        private readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();

        public ResilientCacheManager(TimeSpan refreshInterval, Func<TKey, Task<TValue>> dataRetrieverDelegate, Action<Exception> onBackgroundException)
        {
            _refreshInterval = refreshInterval;
            _dataRetrieverDelegate = dataRetrieverDelegate;
            _onBackgroundException = onBackgroundException;

            ValidateSettings();
            
            Task.Factory.StartNew(SetupBackgroundRefresh);
        }

        private async Task SetupBackgroundRefresh()
        {
            while (!_cancellationTokenSource.IsCancellationRequested)
            {
                await Task.Delay(_refreshInterval, _cancellationTokenSource.Token);

                foreach (var cacheKey in _getValueTasks.Keys)
                {
                    try
                    {
                        var newDataTask = TryRefreshValue(cacheKey);

                        // Let the task finish successfully before we update the dictionary
                        await newDataTask;

                        await ImmutableInterlocked.AddOrUpdate(ref _getValueTasks, cacheKey, newDataTask,
                            (s, task) => newDataTask);
                    }
                    catch (Exception exception)
                    {
                        // Don't error - We want this thread to keep going
                        _onBackgroundException?.Invoke(exception);
                    }
                }
            }
        }

        public async Task<TValue> Get(TKey key)
        {
            try
            {
                return await ImmutableInterlocked.GetOrAdd(ref _getValueTasks, key,
                    _ => TryRefreshValue(key));
            }
            catch(Exception)
            {
                ImmutableInterlocked.TryRemove(ref _getValueTasks, key, out _);
                throw;
            }
        }

        public void Dispose()
        {
            _cancellationTokenSource.Cancel();
        }

        private Task<TValue> TryRefreshValue(TKey key)
        {
            return _dataRetrieverDelegate(key);
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
