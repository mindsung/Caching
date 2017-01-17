using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MindSung.Caching
{
    public interface ICacheProviderFactory
    {
        Task<ICacheProvider<T>> GetNamedCacheProvider<T>(string name, bool slidingExpiry);
    }
}
