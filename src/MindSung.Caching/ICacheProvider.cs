using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

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
    }

    public interface ICacheProvider : ICacheProvider<string>
    {
    }
}
