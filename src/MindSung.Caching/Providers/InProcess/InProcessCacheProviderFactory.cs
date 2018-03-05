using System;
using System.Threading.Tasks;

namespace MindSung.Caching.Providers.InProcess
{
    public class InProcessCacheProviderFactory<T> : ICacheProviderFactory<T>
    {
        public ICacheProvider<T> GetNamedCacheProvider(string name, bool slidingExpiry)
        {
            return new InProcessCacheProvider<T>(name, slidingExpiry);
        }

        public Task<ICacheProvider<T>> GetNamedCacheProviderAsync(string name, bool slidingExpiry)
        {
            return Task.FromResult<ICacheProvider<T>>(new InProcessCacheProvider<T>(name, slidingExpiry));
        }
    }

    public class InProcessCacheProviderFactory : ICacheProviderFactory
    {
        public ICacheProvider GetNamedCacheProvider(string name, bool slidingExpiry)
        {
            return new InProcessCacheProvider(name, slidingExpiry);
        }

        ICacheProvider<string> ICacheProviderFactory<string>.GetNamedCacheProvider(string name, bool slidingExpiry)
        {
            return GetNamedCacheProvider(name, slidingExpiry);
        }

        public Task<ICacheProvider> GetNamedCacheProviderAsync(string name, bool slidingExpiry)
        {
            return Task.FromResult<ICacheProvider>(new InProcessCacheProvider(name, slidingExpiry));
        }

        async Task<ICacheProvider<string>> ICacheProviderFactory<string>.GetNamedCacheProviderAsync(string name, bool slidingExpiry)
        {
            return await GetNamedCacheProviderAsync(name, slidingExpiry);
        }
    }
}
