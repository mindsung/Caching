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
        Task<bool> Add(string key, T value, TimeSpan? expiry = null);
        Task Set(string key, T value, TimeSpan? expiry = null);
        Task<ICacheValue<T>> Get(string key);
        Task<bool> Delete(string key);
        Task<Guid> Subscribe(string key, Action<string> onSet, Action<string> onDelete);
        Task<Guid> Subscribe(string key, Func<string, Task> onSet, Func<string, Task> onDelete);
        Task Unsubscribe(string key, Guid subscriptionId);
        Task QueuePush(string queueName, T value);
        Task<ICacheValue<T>> QueuePop(string queueName, TimeSpan? timeout = null);
        Task QueueClear(string queueName);
        Task Synchronize(string context, Action action, TimeSpan? timeout = null, int maxConcurrent = 1);
        Task Synchronize(string context, Func<Task> action, TimeSpan? timeout = null, int maxConcurrent = 1);
        Task<T> SynchronizeGetOrAdd(string key, Func<T> valueFactory, TimeSpan? expiry = null, TimeSpan? syncTimeout = null);
        Task<T> SynchronizeGetOrAdd(string key, Func<Task<T>> valueFactory, TimeSpan? expiry = null, TimeSpan? syncTimeout = null);
        Task ResetSynchronizationContext(string context);
    }

    public interface ICacheProvider : ICacheProvider<string>
    {
    }
}
```
## Implementations
Implementations in this repository include:
* **MindSung.Caching.Providers.InProcess.InProcessCacheProvider**
 * A light-weight in-process memory cache, ideal for testing and single-instance server applications
* **MindSung.Caching.Providers.Redis.RedisProvider**
 * An interface to the popular [Redis](https://redis.io/) cache
