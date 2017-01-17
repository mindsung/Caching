using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MindSung.Caching
{
    public interface ICacheProvider<T>
    {
        Task<bool> Add(string key, T value, TimeSpan? expiry = null);
        Task Set(string key, T value, TimeSpan? expiry = null);
        Task<ICacheValue<T>> Get(string key);
        Task<bool> Delete(string key);
        Task<Guid> Subscribe(string key, Action<string> onSet, Action<string> onDelete);
        Task<Guid> Subscribe(string key, Func<string, Task> onSet, Func<string, Task> onDelete);
        Task Unsubscribe(string key, Guid subscriptionId);
    }

    public interface ICacheValue<T>
    {
        bool HasValue { get; }
        T Value { get; }
    }
}
