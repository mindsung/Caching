using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MindSung.Caching.Providers
{
    public class CacheSynchronizationHelper
    {
        public CacheSynchronizationHelper(ICacheProvider<string> cacheProvider)
        {
            this.cacheProvider = cacheProvider;
        }

        private ICacheProvider<string> cacheProvider;

        public Task Synchronize(string context, Action action, TimeSpan? timeout = null, int maxConcurrent = 1)
        {
            return Synchronize(context, () => { action(); return Task.FromResult(true); }, timeout, maxConcurrent);
        }

        public async Task Synchronize(string context, Func<Task> action, TimeSpan? timeout = null, int maxConcurrent = 1)
        {
            var ctxKey = $"syncctx/{context}";
            var qKey = $"syncq/{context}";
            if (await cacheProvider.Add(ctxKey, maxConcurrent.ToString(), TimeSpan.MaxValue))
            {
                // We're the lucky one that gets to initialized the semaphore.
                for (int i = 0; i < maxConcurrent - 1; i++)
                {
                    var nowait = cacheProvider.Enqueue(qKey, "1");
                }
            }
            else
            {
                var qval = await cacheProvider.Dequeue(qKey, timeout);
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
                var nowait = cacheProvider.Enqueue(qKey, "1");
            }
        }

        public async Task ResetContext(string context)
        {
            var ctxKey = $"syncctx/{context}";
            var qKey = $"syncq/{context}";
            await cacheProvider.Delete(ctxKey);
            await cacheProvider.ClearQueue(qKey);
        }
    }
}
