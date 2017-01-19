﻿using System.Threading.Tasks;

namespace MindSung.Caching.Providers.InProcess
{
    public class InProcessCacheProviderFactory<T> : ICacheProviderFactory<T>
    {
        public Task<ICacheProvider<T>> GetNamedCacheProvider(string cacheName, bool slidingExpiry)
        {
            return Task.FromResult<ICacheProvider<T>>(new InProcessCacheProvider<T>(cacheName, slidingExpiry));
        }
    }
}
