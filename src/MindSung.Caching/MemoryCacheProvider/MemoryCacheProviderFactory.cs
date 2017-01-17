using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MindSung.Caching.MemoryCacheProvider
{
    public class MemoryCacheProviderFactory : ICacheProviderFactory
    {
        public Task<ICacheProvider<T>> GetNamedCacheProvider<T>(string cacheName, bool slidingExpiry)
        {
            return Task.FromResult<ICacheProvider<T>>(new MemoryCacheProvider<T>(cacheName, slidingExpiry));
        }
    }
}
