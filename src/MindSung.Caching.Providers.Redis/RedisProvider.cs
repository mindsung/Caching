﻿using System;
using System.Threading;
using System.Threading.Tasks;

using StackExchange.Redis;

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
            this.keyPrepend = keyPrepend + "/";
            this.slidingExpiry = slidingExpiry;
            keySetChannel = this.keyPrepend + "provider_keyset";
            keyDelChannel = this.keyPrepend + "provider_keydel";

            keySetHandler = (_, keySet) => subHelper.PublishSet(keySet);
            keyDelHandler = (_, keyDel) => subHelper.PublishDelete(keyDel);

            syncHelper = new CacheSynchronizationHelper<string>(this);
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
        private CacheSynchronizationHelper<string> syncHelper;

        private string FullKey(string key)
        {
            return keyPrepend + key;
        }

        private string ExpiryKey(string key)
        {
            return FullKey(key) + "/ttl";
        }

        private bool SetOrAdd(string key, string value, TimeSpan? expiry, bool isAdd)
        {
            var setResult = Database.StringSet(FullKey(key), value, expiry, isAdd ? When.NotExists : When.Always);
            if (slidingExpiry && expiry.HasValue)
            {
                Database.StringSet(ExpiryKey(key), (long)expiry.Value.TotalMilliseconds, expiry, isAdd ? When.NotExists : When.Always);
            }
            if (setResult)
            {
                PublishSet(key);
            }
            return setResult;
        }

        public bool Add(string key, string value, TimeSpan? expiry)
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

        public void Set(string key, string value, TimeSpan? expiry)
        {
            if (key == null)
            {
                throw new ArgumentException("Cache key cannot be null.", nameof(key));
            }
            if (value == null)
            {
                throw new ArgumentException("Cache value cannot be null.", nameof(value));
            }
            SetOrAdd(key, value, expiry, false);
        }

        private async Task<bool> SetOrAddAsync(string key, string value, TimeSpan? expiry, bool isAdd)
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

        public Task<bool> AddAsync(string key, string value, TimeSpan? expiry)
        {
            if (key == null)
            {
                throw new ArgumentException("Cache key cannot be null.", nameof(key));
            }
            if (value == null)
            {
                throw new ArgumentException("Cache value cannot be null.", nameof(value));
            }
            return SetOrAddAsync(key, value, expiry, true);
        }

        public Task SetAsync(string key, string value, TimeSpan? expiry)
        {
            if (key == null)
            {
                throw new ArgumentException("Cache key cannot be null.", nameof(key));
            }
            if (value == null)
            {
                throw new ArgumentException("Cache value cannot be null.", nameof(value));
            }
            return SetOrAddAsync(key, value, expiry, false);
        }

        public ICacheValue<string> Get(string key)
        {
            if (key == null)
            {
                throw new ArgumentException("Cache key cannot be null.", nameof(key));
            }
            var getResult = Database.StringGet(FullKey(key));
            var ttlResult = slidingExpiry ? Database.StringGet(ExpiryKey(key)) : RedisValue.Null;
            TimeSpan? expiry = null;
            if (getResult.HasValue && ttlResult.HasValue)
            {
                expiry = TimeSpan.FromMilliseconds((long)ttlResult);
            }
            if (getResult.HasValue && expiry.HasValue)
            {
                Database.KeyExpire(FullKey(key), expiry);
                Database.KeyExpire(ExpiryKey(key), expiry);
            }
            var value = new CacheValue<string>();
            if (getResult.HasValue)
            {
                value.HasValue = true;
                value.Value = getResult;
            }
            return value;
        }

        public async Task<ICacheValue<string>> GetAsync(string key)
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
            var value = new CacheValue<string>();
            if (results[0].HasValue)
            {
                value.HasValue = true;
                value.Value = results[0];
            }
            return value;
        }

        public bool Delete(string key)
        {
            if (key == null)
            {
                throw new ArgumentException("Cache key cannot be null.", nameof(key));
            }
            var delResult = Database.KeyDelete(FullKey(key));
            if (slidingExpiry)
            {
                Database.KeyDelete(ExpiryKey(key));
            }
            if (delResult)
            {
                PublishDelete(key);
            }
            return delResult;
        }

        public async Task<bool> DeleteAsync(string key)
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
                PublishDelete(key);
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

        protected void PublishDelete(string key)
        {
            if (sub != null)
            {
                var nowait = sub.PublishAsync(keyDelChannel, key);
            }
        }

        public Task<Guid> SubscribeAsync(string key, Action<string> onSet, Action<string> onDelete)
        {
            return SubscribeAsync(key, async k => { onSet(k); await Task.FromResult(true); }, async k => { onDelete(k); await Task.FromResult(true); });
        }

        public async Task<Guid> SubscribeAsync(string key, Func<string, Task> onSet, Func<string, Task> onDelete)
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

        public Task UnsubscribeAsync(string key, Guid subscriptionId)
        {
            if (key == null)
            {
                throw new ArgumentException("Cache key cannot be null.", nameof(key));
            }
            subHelper.RemoveSubscription(key, subscriptionId);
            return Task.FromResult(true);
        }

        private string QueueKey(string queueName)
        {
            return queueName + "/Q";
        }

        private string FullQueueKey(string queueName)
        {
            return FullKey(QueueKey(queueName));
        }

        public Task QueuePushAsync(string queueName, string value)
        {
            var task = Database.ListLeftPushAsync(FullQueueKey(queueName), value);
            PublishSet(QueueKey(queueName));
            return task;
        }

        public async Task<ICacheValue<string>> QueuePopAsync(string queueName, TimeSpan? timeout = null)
        {
            if (!timeout.HasValue || timeout.Value == TimeSpan.Zero)
            {
                var value = await Database.ListRightPopAsync(FullQueueKey(queueName));
                return new CacheValue<string> { HasValue = value.HasValue, Value = value };
            }

            // Just because we get a notification that something has been pushed
            // doesn't mean we'll get something from the queue, someone else may get
            // it before we do. To avoid a timing window in which something has been
            // place on the queue but we never get the notification, we always need to
            // have an active, listening subscription prior to each attempt to pop.
            // That is why we need to have a new task completion source ready to be
            // set by the subsriber before each attempt to pop.
            var pushed = new TaskCompletionSource<bool>();
            var subId = await SubscribeAsync(QueueKey(queueName), _ => pushed.TrySetResult(true), null);
            try
            {
                var value = await Database.ListRightPopAsync(FullQueueKey(queueName));
                if (value.HasValue)
                {
                    return new CacheValue<string> { HasValue = true, Value = value };
                }

                var wait = Task.Delay(timeout.Value);
                while (await Task.WhenAny(pushed.Task, wait) == pushed.Task)
                {
                    pushed = new TaskCompletionSource<bool>();
                    value = await Database.ListRightPopAsync(FullQueueKey(queueName));
                    if (value.HasValue)
                    {
                        return new CacheValue<string> { HasValue = true, Value = value };
                    }
                }

                return new CacheValue<string>();
            }
            finally
            {
                var nowait = UnsubscribeAsync(QueueKey(queueName), subId);
            }
        }

        public Task QueueClearAsync(string queueName)
        {
            return DeleteAsync(QueueKey(queueName));
        }

        public Task Synchronize(string context, Action action, TimeSpan? timeout = null, int maxConcurrent = 1)
        {
            return syncHelper.Synchronize(context, action, "1", timeout, maxConcurrent);
        }

        public Task Synchronize(string context, Func<Task> action, TimeSpan? timeout = null, int maxConcurrent = 1)
        {
            return syncHelper.Synchronize(context, action, "1", timeout, maxConcurrent);
        }

        public Task<string> GetOrAddAsync(string key, Func<string> valueFactory, TimeSpan? expiry = null, TimeSpan? syncTimeout = null)
        {
            return syncHelper.SynchronizeGetOrAdd(key, valueFactory, "1", expiry, syncTimeout);
        }

        public Task<string> GetOrAddAsync(string key, Func<Task<string>> valueFactory, TimeSpan? expiry = null, TimeSpan? syncTimeout = null)
        {
            return syncHelper.SynchronizeGetOrAdd(key, valueFactory, "1", expiry, syncTimeout);
        }

        public Task ResetSynchronizationContext(string context)
        {
            return syncHelper.ResetSynchronizationContext(context);
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
