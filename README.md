# MindSung.Caching
MindSung.Caching is a library that defines an injectable interface and implementations for providing shared cache services.
## **ICacheProvider** Interface
The interface currently includes:
* Basic (CRUD) cache operations
* Key-change subscription
* Awaitable queue operations
* Thread and process synchronization using the shared queue
```C#
namespace MindSung.Caching
{
    public interface ICacheProvider<T> : IDisposable
    {
        bool Add(string key, T value, TimeSpan? expiry = null);
        Task<bool> AddAsync(string key, T value, TimeSpan? expiry = null);
        void Set(string key, T value, TimeSpan? expiry = null);
        Task SetAsync(string key, T value, TimeSpan? expiry = null);
        ICacheValue<T> Get(string key);
        Task<ICacheValue<T>> GetAsync(string key);
        bool Delete(string key);
        Task<bool> DeleteAsync(string key);
        Task<Guid> SubscribeAsync(string key, Action<string> onSet, Action<string> onDelete);
        Task<Guid> SubscribeAsync(string key, Func<string, Task> onSet, Func<string, Task> onDelete);
        Task UnsubscribeAsync(string key, Guid subscriptionId);
        Task QueuePushAsync(string queueName, T value);
        Task<ICacheValue<T>> QueuePopAsync(string queueName, TimeSpan? timeout = null);
        Task QueueClearAsync(string queueName);
        Task Synchronize(string context, Action action, TimeSpan? timeout = null, int maxConcurrent = 1);
        Task Synchronize(string context, Func<Task> action, TimeSpan? timeout = null, int maxConcurrent = 1);
        Task<T> GetOrAddAsync(string key, Func<T> valueFactory, TimeSpan? expiry = null, TimeSpan? syncTimeout = null);
        Task<T> GetOrAddAsync(string key, Func<Task<T>> valueFactory, TimeSpan? expiry = null, TimeSpan? syncTimeout = null);
        Task ResetSynchronizationContext(string context);
    }

    public interface ICacheProvider : ICacheProvider<string>
    {
    }
}
```
## Implementations
Implementations in this repository include:
* **MindSung.Caching.Providers.InProcess.InProcessCacheProvider** - A light-weight in-process memory cache, ideal for testing and single-instance server applications
* **MindSung.Caching.Providers.Redis.RedisProvider** - An interface to the popular [Redis](https://redis.io/) cache
