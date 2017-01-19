using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MindSung.Caching.Providers
{
    public class CacheSynchronizationHelper<T>
    {
        public CacheSynchronizationHelper(ICacheProvider<T> cacheProvider)
        {
            this.cacheProvider = cacheProvider;
        }

        private ICacheProvider<T> cacheProvider;

        public Task Synchronize(string context, Action action, T anyValue, TimeSpan? timeout = null, int maxConcurrent = 1)
        {
            return Synchronize(context, () => { action(); return Task.FromResult(true); }, anyValue, timeout, maxConcurrent);
        }

        public async Task Synchronize(string context, Func<Task> action, T anyValue, TimeSpan? timeout = null, int maxConcurrent = 1)
        {
            var ctxKey = $"syncctx/{context}";
            var qKey = $"syncq/{context}";
            if (await cacheProvider.Add(ctxKey, anyValue, TimeSpan.MaxValue))
            {
                // We're the lucky one that gets to initialized the semaphore.
                for (int i = 0; i < maxConcurrent - 1; i++)
                {
                    var nowait = cacheProvider.QueuePush(qKey, anyValue);
                }
            }
            else
            {
                var qval = await cacheProvider.QueuePop(qKey, timeout);
                if (!qval.HasValue)
                {
                    throw new TimeoutException($"Timeout waiting for synchronization context {context}.");
                }
            }
            try
            {
                await action();
            }
            finally
            {
                var nowait = cacheProvider.QueuePush(qKey, anyValue);
            }
        }

        public Task<T> SynchronizeGetOrAdd(string key, Func<T> valueFactory, T anyValue, TimeSpan? expiry = null, TimeSpan? syncTimeout = null)
        {
            return SynchronizeGetOrAdd(key, () => Task.FromResult(valueFactory()), anyValue, expiry, syncTimeout);
        }

        public async Task<T> SynchronizeGetOrAdd(string key, Func<Task<T>> valueFactory, T anyValue, TimeSpan? expiry = null, TimeSpan? syncTimeout = null)
        {
            T value = default(T);

            await Synchronize(key, async () =>
            {
                var cacheVal = await cacheProvider.Get(key);
                if (!cacheVal.HasValue)
                {
                    value = await valueFactory();
                    // This should always be adding, but do a Set instead of Add
                    // to ensure that even if something isn't working correctly,
                    // the returned value will be the last value in cache.
                    await cacheProvider.Set(key, value, expiry);
                }
                else
                {
                    value = cacheVal.Value;
                }
            },
            anyValue, syncTimeout, 1);

            return value;
        }

        public async Task ResetSynchronizationContext(string context)
        {
            var ctxKey = $"syncctx/{context}";
            var qKey = $"syncq/{context}";
            await cacheProvider.Delete(ctxKey);
            await cacheProvider.QueueClear(qKey);
        }
    }
}
