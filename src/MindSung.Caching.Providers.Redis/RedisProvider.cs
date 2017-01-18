using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using StackExchange.Redis;
using System.Threading;

namespace MindSung.Caching.Providers.Redis
{
    public class RedisProvider : ICacheProvider, IDisposable
    {
        // Must be instantiated by the factory.
        internal RedisProvider(ConnectionMultiplexer connection, string keyPrepend, bool slidingExpiry)
        {
            if (connection == null)
            {
                throw new ArgumentException("Redis cache connection cannot be null.", nameof(connection));
            }
            if (string.IsNullOrWhiteSpace(keyPrepend))
            {
                throw new ArgumentException("Redis cache provider requires a key-prepender value, it does not support multiple named caches.", nameof(keyPrepend));
            }

            this.connection = connection;
            Database = connection.GetDatabase();
            this.keyPrepend = keyPrepend + ".";
            this.slidingExpiry = slidingExpiry;
            keySetChannel = this.keyPrepend + "provider_keyset";
            keyDelChannel = this.keyPrepend + "provider_keydel";

            keySetHandler = (_, keySet) => subHelper.PublishSet(keySet);
            keyDelHandler = (_, keyDel) => subHelper.PublishDelete(keyDel);
        }

        protected readonly IDatabase Database;

        private ConnectionMultiplexer connection;
        private string keyPrepend;
        private bool slidingExpiry;
        private ISubscriber sub;
        private SemaphoreSlim subSync = new SemaphoreSlim(1, 1);
        private CacheSubscriptionHelper subHelper = new CacheSubscriptionHelper();
        private string keySetChannel;
        private string keyDelChannel;
        private Action<RedisChannel, RedisValue> keySetHandler;
        private Action<RedisChannel, RedisValue> keyDelHandler;

        private string FullKey(string key)
        {
            return keyPrepend + key;
        }

        private string ExpiryKey(string key)
        {
            return FullKey(key) + ".ttl_";
        }

        private async Task<bool> SetOrAdd(string key, string value, TimeSpan? expiry, bool isAdd)
        {
            var tasks = new Task<bool>[2];
            tasks[0] = Database.StringSetAsync(FullKey(key), value, expiry, isAdd ? When.NotExists : When.Always);
            tasks[1] = slidingExpiry && expiry.HasValue
                ? Database.StringSetAsync(ExpiryKey(key), (long)expiry.Value.TotalMilliseconds, expiry, isAdd ? When.NotExists : When.Always)
                : Task.FromResult(true);
            await Task.WhenAll(tasks);
            if (tasks[0].Result)
            {
                PublishSet(key);
            }
            return tasks[0].Result;
        }

        public Task<bool> Add(string key, string value, TimeSpan? expiry)
        {
            if (key == null)
            {
                throw new ArgumentException("Cache key cannot be null.", nameof(key));
            }
            if (value == null)
            {
                throw new ArgumentException("Cache value cannot be null.", nameof(value));
            }
            return SetOrAdd(key, value, expiry, true);
        }

        public Task Set(string key, string value, TimeSpan? expiry)
        {
            if (key == null)
            {
                throw new ArgumentException("Cache key cannot be null.", nameof(key));
            }
            if (value == null)
            {
                throw new ArgumentException("Cache value cannot be null.", nameof(value));
            }
            return SetOrAdd(key, value, expiry, false);
        }

        protected class CacheValue : ICacheValue<string>
        {
            public bool HasValue { get; set; }
            public string Value { get; set; }
        }

        public async Task<ICacheValue<string>> Get(string key)
        {
            if (key == null)
            {
                throw new ArgumentException("Cache key cannot be null.", nameof(key));
            }
            var tasks = new Task<RedisValue>[2];
            tasks[0] = Database.StringGetAsync(FullKey(key));
            tasks[1] = slidingExpiry ? Database.StringGetAsync(ExpiryKey(key)) : Task.FromResult(RedisValue.Null);
            var results = await Task.WhenAll(tasks);
            TimeSpan? expiry = null;
            if (results[0].HasValue && results[1].HasValue)
            {
                expiry = TimeSpan.FromMilliseconds((long)results[1]);
            }
            if (results[0].HasValue && expiry.HasValue)
            {
                var nowait = Database.KeyExpireAsync(FullKey(key), expiry);
                nowait = Database.KeyExpireAsync(ExpiryKey(key), expiry);
            }
            var value = new CacheValue();
            if (results[0].HasValue)
            {
                value.HasValue = true;
                value.Value = results[0];
            }
            return value;
        }

        public async Task<bool> Delete(string key)
        {
            if (key == null)
            {
                throw new ArgumentException("Cache key cannot be null.", nameof(key));
            }
            var tasks = new Task<bool>[2];
            tasks[0] = Database.KeyDeleteAsync(FullKey(key));
            tasks[1] = slidingExpiry ? Database.KeyDeleteAsync(ExpiryKey(key)) : Task.FromResult(true);
            await Task.WhenAll(tasks);
            if (tasks[0].Result)
            {
                PublishRemove(key);
            }
            return tasks[0].Result;
        }

        protected void PublishSet(string key)
        {
            if (sub != null)
            {
                var nowait = sub.PublishAsync(keySetChannel, key);
            }
        }

        protected void PublishRemove(string key)
        {
            if (sub != null)
            {
                var nowait = sub.PublishAsync(keyDelChannel, key);
            }
        }

        public Task<Guid> Subscribe(string key, Action<string> onSet, Action<string> onDelete)
        {
            return Subscribe(key, async k => { onSet(k); await Task.FromResult(true); }, async k => { onDelete(k); await Task.FromResult(true); });
        }

        public async Task<Guid> Subscribe(string key, Func<string, Task> onSet, Func<string, Task> onDelete)
        {
            if (key == null)
            {
                throw new ArgumentException("Cache key cannot be null.", nameof(key));
            }
            if (sub == null)
            {
                await subSync.WaitAsync();
                try
                {
                    // Check again once we have the lock.
                    if (sub == null)
                    {
                        sub = connection.GetSubscriber();
                        await sub.SubscribeAsync(keySetChannel, keySetHandler);
                        await sub.SubscribeAsync(keyDelChannel, keyDelHandler);
                    }
                }
                finally
                {
                    subSync.Release();
                }
            }
            return subHelper.AddSubscription(key, onSet, onDelete);
        }

        public Task Unsubscribe(string key, Guid subscriptionId)
        {
            if (key == null)
            {
                throw new ArgumentException("Cache key cannot be null.", nameof(key));
            }
            subHelper.RemoveSubscription(key, subscriptionId);
            return Task.FromResult(true);
        }

        public void Dispose()
        {
            if (sub != null)
            {
                sub.Unsubscribe(keySetChannel, keySetHandler);
                sub.Unsubscribe(keyDelChannel, keyDelHandler);
            }
        }
    }
}
