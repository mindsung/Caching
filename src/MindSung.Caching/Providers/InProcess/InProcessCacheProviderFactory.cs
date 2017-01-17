using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MindSung.Caching.Providers.InProcess
{
    public class InProcessCacheProviderFactory : ICacheProviderFactory
    {
        public Task<ICacheProvider<T>> GetNamedCacheProvider<T>(string cacheName, bool slidingExpiry)
        {
            return Task.FromResult<ICacheProvider<T>>(new InProcessCacheProvider<T>(cacheName, slidingExpiry));
        }
    }
}
