using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MindSung.Caching
{
    public interface ICacheProviderFactory<T>
    {
        Task<ICacheProvider<T>> GetNamedCacheProvider(string name, bool slidingExpiry);
    }

    public interface ICacheProviderFactory : ICacheProviderFactory<string>
    {
        new Task<ICacheProvider> GetNamedCacheProvider(string name, bool slidingExpiry);
    }
}
