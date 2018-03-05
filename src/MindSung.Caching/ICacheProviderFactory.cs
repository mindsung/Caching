using System.Threading.Tasks;

namespace MindSung.Caching
{
    public interface ICacheProviderFactory<T>
    {
        ICacheProvider<T> GetNamedCacheProvider(string name, bool slidingExpiry);
        Task<ICacheProvider<T>> GetNamedCacheProviderAsync(string name, bool slidingExpiry);
    }

    public interface ICacheProviderFactory : ICacheProviderFactory<string>
    {
        new ICacheProvider GetNamedCacheProvider(string name, bool slidingExpiry);
        new Task<ICacheProvider> GetNamedCacheProviderAsync(string name, bool slidingExpiry);
    }
}
