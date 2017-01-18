using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MindSung.Caching.Providers.InProcess
{
    using MemoryCacheBase;

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

        class CacheValue : ICacheValue<T>
        {
            public bool HasValue { get; set; }
            public T Value { get; set; }
        }

        public Task<ICacheValue<T>> Get(string key)
        {
            if (key == null)
            {
                throw new ArgumentException("Cache key cannot be null.", nameof(key));
            }
            CacheValue value = new CacheValue();
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

        public void Dispose()
        {
        }
    }
}
