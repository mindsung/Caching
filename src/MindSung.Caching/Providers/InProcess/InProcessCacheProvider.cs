using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace MindSung.Caching.Providers.InProcess
{
    using MemoryCacheBase;
    using System.Threading;

    public class InProcessCacheProvider<T> : ICacheProvider<T>
    {
        private static MemoryCacheSet cacheSet = MemoryCacheSet.Create();

        internal InProcessCacheProvider(string cacheName, bool slidingExpiry)
        {
            cache = cacheSet.GetNamedCache<T>(cacheName, int.MaxValue, slidingExpiry);
            this.slidingExpiry = slidingExpiry;
        }

        private ICache<T> cache;
        private bool slidingExpiry;
        private CacheSubscriptionHelper subs = new CacheSubscriptionHelper();

        private CacheItem<T> ToCacheItem(string key, T value, TimeSpan? expiry)
        {
            return new CacheItem<T>(key, value, expiry.HasValue ? (int)expiry.Value.TotalMilliseconds : 0, slidingExpiry);
        }

        public Task<bool> Add(string key, T value, TimeSpan? expiry)
        {
            if (key == null)
            {
                throw new ArgumentException("Cache key cannot be null.", nameof(key));
            }
            if (cache.Add(ToCacheItem(key, value, expiry)))
            {
                subs.PublishSet(key);
                return Task.FromResult(true);
            }
            return Task.FromResult(false);
        }

        public Task Set(string key, T value, TimeSpan? expiry)
        {
            if (key == null)
            {
                throw new ArgumentException("Cache key cannot be null.", nameof(key));
            }
            cache.Set(ToCacheItem(key, value, expiry));
            subs.PublishSet(key);
            return Task.FromResult(true);
        }

        public Task<ICacheValue<T>> Get(string key)
        {
            if (key == null)
            {
                throw new ArgumentException("Cache key cannot be null.", nameof(key));
            }
            CacheValue<T> value = new CacheValue<T>();
            var item = cache.Get(key);
            if (item.found)
            {
                value.HasValue = true;
                value.Value = item.value;
            }
            return Task.FromResult<ICacheValue<T>>(value);
        }

        public Task<bool> Delete(string key)
        {
            if (key == null)
            {
                throw new ArgumentException("Cache key cannot be null.", nameof(key));
            }
            if (cache.Remove(key))
            {
                subs.PublishDelete(key);
                return Task.FromResult(true);
            }
            return Task.FromResult(false);
        }

        public Task<Guid> Subscribe(string key, Action<string> onSet, Action<string> onDelete)
        {
            if (key == null)
            {
                throw new ArgumentException("Cache key cannot be null.", nameof(key));
            }
            return Task.FromResult(subs.AddSubscription(key, onSet, onDelete));
        }

        public Task<Guid> Subscribe(string key, Func<string, Task> onSet, Func<string, Task> onDelete)
        {
            if (key == null)
            {
                throw new ArgumentException("Cache key cannot be null.", nameof(key));
            }
            return Task.FromResult(subs.AddSubscription(key, onSet, onDelete));
        }

        public Task Unsubscribe(string key, Guid subscriptionId)
        {
            if (key == null)
            {
                throw new ArgumentException("Cache key cannot be null.", nameof(key));
            }
            subs.RemoveSubscription(key, subscriptionId);
            return Task.FromResult(true);
        }

        private class QueueInfo
        {
            public Queue<T> queue = new Queue<T>();
            public TaskCompletionSource<bool> tcsNotEmpty = new TaskCompletionSource<bool>();
        }

        private ConcurrentDictionary<string, QueueInfo> queues = new ConcurrentDictionary<string, QueueInfo>();

        public Task QueuePush(string queueName, T value)
        {
            var qi = queues.GetOrAdd(queueName, new QueueInfo());
            lock (qi)
            {
                qi.queue.Enqueue(value);
                qi.tcsNotEmpty.TrySetResult(true);
                return Task.FromResult(true);
            }
        }

        private bool TryPop(QueueInfo qi, out T value)
        {
            lock (qi)
            {
                if (qi.queue.Count == 0)
                {
                    value = default(T);
                    return false;
                }
                value = qi.queue.Dequeue();
                if (qi.queue.Count == 0)
                {
                    qi.tcsNotEmpty = new TaskCompletionSource<bool>();
                }
                return true;
            }
        }

        public async Task<ICacheValue<T>> QueuePop(string queueName, TimeSpan? timeout = null)
        {
            var qi = queues.GetOrAdd(queueName, new QueueInfo());
            T value;
            if (!timeout.HasValue || timeout.Value == TimeSpan.Zero)
            {
                return new CacheValue<T> { HasValue = TryPop(qi, out value), Value = value };
            }

            Task notEmpty;
            lock (qi)
            {
                notEmpty = qi.tcsNotEmpty.Task;
            }
            if (TryPop(qi, out value))
            {
                return new CacheValue<T> { HasValue = true, Value = value };
            }
            var wait = Task.Delay(timeout.Value);
            while (await Task.WhenAny(notEmpty, wait) == notEmpty)
            {
                lock (qi)
                {
                    notEmpty = qi.tcsNotEmpty.Task;
                }
                if (TryPop(qi, out value))
                {
                    return new CacheValue<T> { HasValue = true, Value = value };
                }
            }
            return new CacheValue<T>();
        }

        public Task QueueClear(string queueName)
        {
            var qi = queues.GetOrAdd(queueName, new QueueInfo());
            lock (qi)
            {
                qi.queue.Clear();
                qi.tcsNotEmpty = new TaskCompletionSource<bool>();
            }
            return Task.FromResult(true);
        }

        private ConcurrentDictionary<string, SemaphoreSlim> semaphores = new ConcurrentDictionary<string, SemaphoreSlim>();

        public Task Synchronize(string context, Action action, TimeSpan? timeout = null, int maxConcurrent = 1)
        {
            return Synchronize(context, () => { action(); return Task.FromResult(true); }, timeout, maxConcurrent);
        }

        public async Task Synchronize(string context, Func<Task> action, TimeSpan? timeout = null, int maxConcurrent = 1)
        {
            var sem = semaphores.GetOrAdd(context, new SemaphoreSlim(maxConcurrent, maxConcurrent));
            if (!(await sem.WaitAsync(timeout.HasValue ? timeout.Value : TimeSpan.MaxValue)))
            {
                throw new TimeoutException($"Timeout waiting for synchronization context {context}.");
            }
            try
            {
                await action();
            }
            finally
            {
                sem.Release();
            }
        }

        public Task<T> SynchronizeGetOrAdd(string key, Func<T> valueFactory, TimeSpan? expiry = default(TimeSpan?), TimeSpan? syncTimeout = default(TimeSpan?))
        {
            return SynchronizeGetOrAdd(key, () => Task.FromResult(valueFactory()), expiry, syncTimeout);
        }

        public async Task<T> SynchronizeGetOrAdd(string key, Func<Task<T>> valueFactory, TimeSpan? expiry = default(TimeSpan?), TimeSpan? syncTimeout = default(TimeSpan?))
        {
            T value = default(T);

            await Synchronize(key, async () =>
            {
                var cacheVal = await Get(key);
                if (!cacheVal.HasValue)
                {
                    value = await valueFactory();
                    // This should always be adding, but do a Set instead of Add
                    // to ensure that even if something isn't working correctly,
                    // the returned value will be the last value in cache.
                    await Set(key, value, expiry);
                }
                else
                {
                    value = cacheVal.Value;
                }
            }, syncTimeout, 1);

            return value;
        }

        public Task ResetSynchronizationContext(string context)
        {
            SemaphoreSlim _;
            semaphores.TryRemove(context, out _);
            return Task.FromResult(true);
        }

        public void Dispose()
        {
        }
    }
}
