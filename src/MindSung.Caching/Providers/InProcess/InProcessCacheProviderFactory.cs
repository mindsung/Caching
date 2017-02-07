using System;
using System.Threading.Tasks;

namespace MindSung.Caching.Providers.InProcess
{
    public class InProcessCacheProviderFactory<T> : ICacheProviderFactory<T>
    {
        public Task<ICacheProvider<T>> GetNamedCacheProvider(string name, bool slidingExpiry)
        {
            return Task.FromResult<ICacheProvider<T>>(new InProcessCacheProvider<T>(name, slidingExpiry));
        }
    }

    public class InProcessCacheProviderFactory : ICacheProviderFactory
    {
        public Task<ICacheProvider> GetNamedCacheProvider(string name, bool slidingExpiry)
        {
            return Task.FromResult<ICacheProvider>(new InProcessCacheProvider(name, slidingExpiry));
        }

        async Task<ICacheProvider<string>> ICacheProviderFactory<string>.GetNamedCacheProvider(string name, bool slidingExpiry)
        {
            return await GetNamedCacheProvider(name, slidingExpiry);
        }
    }
}
