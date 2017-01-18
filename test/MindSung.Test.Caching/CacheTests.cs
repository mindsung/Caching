using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Xunit;

using MindSung.Caching;
using MindSung.Caching.Providers.InProcess;
using MindSung.Caching.Providers.Redis;

namespace MindSung.Test.Caching
{
    public class CacheTests : IDisposable
    {
        protected const int testConcurrency = 4;
        protected const int batchSize = 2500;
        protected const int queueSize = 2500;
        protected const int notifySize = 1250;
        protected const int blockingQueueConcurrency = 20;
        protected const int blockingQueueDelayMs = 4000;
        protected const int blockingQueueToleranceMs = 500;
        protected const int expirationMs = 500;
        protected const int expirationToleranceMs = 100;

        protected readonly TimeSpan cacheExpiry = TimeSpan.FromMinutes(5);

        protected async Task RunConcurrent(int num, Func<Task> action)
        {
            var tasks = new List<Task>();
            for (var i = 0; i < num; i++)
            {
                tasks.Add(action());
            }
            await Task.WhenAll(tasks);
        }

        protected async Task CacheCRUD(ICacheProviderFactory<string> factory, string cacheName)
        {
            var cache = await factory.GetNamedCacheProvider(cacheName, false);
            var key = "test1";
            // Clear the key in case a previous failed test left a value in it.
            await cache.Delete(key);
            // Cache add.
            var setVal = "hello";
            await cache.Add(key, setVal, cacheExpiry);
            var getVal = await cache.Get(key);
            Assert.True(getVal.HasValue && setVal == getVal.Value, $"Cache add failed. Expected {setVal}, got {getVal.Value}.");
            // Cache set.
            setVal = "changed";
            await cache.Set(key, setVal, cacheExpiry);
            getVal = await cache.Get(key);
            Assert.True(getVal.HasValue && setVal == getVal.Value, $"Cache set failed. Expected {setVal}, got {getVal.Value}.");
            // Cache delete.
            await cache.Delete(key);
            getVal = await cache.Get(key);
            Assert.True(!getVal.HasValue, $"Cache delete failed. Expected no value, got {getVal.Value}.");
        }

        protected async Task CacheBatch(ICacheProviderFactory<string> factory, string cacheName, int size)
        {
            var cache = await factory.GetNamedCacheProvider(cacheName, false);
            Action<Action<int>> repeat = action =>
            {
                for (var i = 0; i < size; i++)
                {
                    action(i);
                }
            };
            // Set the keys.
            var setTasks = new List<Task>();
            repeat(i => setTasks.Add(cache.Set("key" + i.ToString(), i.ToString(), cacheExpiry)));
            await Task.WhenAll(setTasks);
            // Get the keys.
            var getTasks = new List<Task<ICacheValue<string>>>();
            repeat(i => getTasks.Add(cache.Get("key" + i.ToString())));
            await Task.WhenAll(getTasks);
            // Try to delete the keys.
            var delTasks = new List<Task>();
            repeat(i => delTasks.Add(cache.Delete("key" + i.ToString())));
            await Task.WhenAll(delTasks);
            // Verify the keys we got back.
            repeat(i => Assert.True(getTasks[i].Result.HasValue && getTasks[i].Result.Value == i.ToString(), $"Cache batch get failed. Expected {i.ToString()}, got {getTasks[i].Result.Value}."));
        }

        protected async Task CacheExpiration(ICacheProviderFactory<string> factory, string cacheName)
        {
            // Test with static expiry.
            var cache = await factory.GetNamedCacheProvider(cacheName, false);
            var key = Guid.NewGuid().ToString();
            var setVal = "hello";
            // Will check before expiration time.
            var time1 = Task.Delay(expirationMs - expirationToleranceMs);
            // Will check after expiration time, should be expired.
            var time2 = Task.Delay(expirationMs + expirationToleranceMs);
            await cache.Add(key, setVal, TimeSpan.FromMilliseconds(expirationMs));
            await time1;
            var val = await cache.Get(key);
            Assert.True(val.HasValue && val.Value == setVal, $"Cache static expiration first check failed. Expected {setVal}, got {val.Value}.");
            await time2;
            val = await cache.Get(key);
            Assert.True(!val.HasValue, $"Cache static expiration second check failed. Expected no value, got {val.Value}.");
            // Test with sliding expiry.
            cache = await factory.GetNamedCacheProvider(cacheName + ".sliding", true);
            // Will check before expiration time.
            time1 = Task.Delay(expirationMs - expirationToleranceMs);
            // Will check after original expiration time, should still be there because of previous get.
            time2 = Task.Delay(expirationMs + expirationToleranceMs);
            // Will check expiration time plus tolerance after second get, should be expired.
            var time3 = Task.Delay(expirationMs + expirationToleranceMs + expirationMs + expirationToleranceMs);
            await cache.Add(key, setVal, TimeSpan.FromMilliseconds(expirationMs));
            await time1;
            val = await cache.Get(key);
            Assert.True(val.HasValue && val.Value == setVal, $"Cache sliding expiration first check failed. Expected {setVal}, got {val.Value}.");
            await time2;
            val = await cache.Get(key);
            Assert.True(val.HasValue && val.Value == setVal, $"Cache sliding expiration second check failed. Expected {setVal}, got {val.Value}.");
            await time3;
            val = await cache.Get(key);
            Assert.True(!val.HasValue, $"Cache sliding expiration third check failed. Expected no value, got {val.Value}.");
        }

        protected async Task CacheNotifications(ICacheProviderFactory<string> factory, string cacheName, int size)
        {
            var cache = await factory.GetNamedCacheProvider(cacheName, false);
            // Create items to be added and removed from cache.
            var items = new List<int>();
            for (var i = 0; i < size; i++)
            {
                items.Add(i);
            }
            var cacheItems = items.ToDictionary(i => "key" + i.ToString(), i => i.ToString());
            // Create dictionaries used to track notifications received.
            var set1Notify = cacheItems.Keys.ToDictionary(k => k);
            var set2Notify = cacheItems.Keys.ToDictionary(k => k);
            var delNotify = cacheItems.Keys.ToDictionary(k => k);
            // Create synchronization tasks.
            var taskReady = new TaskCompletionSource<bool>();
            var subsComplete = new TaskCompletionSource<bool>();
            // Run a task to create and respond to subscriptions.
            var subs = new Dictionary<string, Guid>();
            var nowait = Task.Run(async () =>
            {
                foreach (var key in cacheItems.Keys)
                {
                    subs.Add(key, await cache.Subscribe(key,
                        setKey =>
                        {
                            lock (set1Notify)
                            {
                                if (!set1Notify.Remove(setKey))
                                {
                                    lock (set2Notify)
                                    {
                                        set2Notify.Remove(setKey);
                                    }
                                }
                            }
                            if (set1Notify.Count == 0 && set2Notify.Count == 0 && delNotify.Count == 0)
                            {
                                subsComplete.SetResult(true);
                            }
                        },
                        delKey =>
                        {
                            lock (delNotify)
                            {
                                delNotify.Remove(delKey);
                            }
                            if (set1Notify.Count == 0 && set2Notify.Count == 0 && delNotify.Count == 0)
                            {
                                subsComplete.SetResult(true);
                            }
                        }));
                }
                taskReady.SetResult(true);
            });
            await taskReady.Task;
            // Add cache items to cause first group of set notifications.
            var tasks = new List<Task>();
            foreach (var kv in cacheItems)
            {
                tasks.Add(cache.Add(kv.Key, kv.Value, cacheExpiry));
            }
            // Set cache items to cause second group of set notifications.
            foreach (var kv in cacheItems)
            {
                tasks.Add(cache.Set(kv.Key, kv.Value + "B", cacheExpiry));
            }
            await Task.WhenAll(tasks);
            // Delete all keys to cause delete notifications.
            tasks.Clear();
            foreach (var key in cacheItems.Keys)
            {
                tasks.Add(cache.Delete(key));
            }
            await Task.WhenAll(tasks);
            // Wait for all notifications to be received.
            try
            {
                Assert.True(await Task.WhenAny(subsComplete.Task, Task.Delay(30000)) == subsComplete.Task,
                    "Failed cache subscriptions. Timeout waiting for all subscriptions to complete.");
            }
            finally
            {
                // Clean up subscriptions.
                foreach (var kv in subs)
                {
                    try { await cache.Unsubscribe(kv.Key, kv.Value); }
                    catch { }
                }
            }
        }

        public virtual void Dispose()
        {
        }
    }

    public class InProc : CacheTests
    {
        private InProcessCacheProviderFactory<string> inProcCacheFactory = new InProcessCacheProviderFactory<string>();

        [Fact]
        public Task CacheCRUD()
        {
            return RunConcurrent(testConcurrency, () => CacheCRUD(inProcCacheFactory, Guid.NewGuid().ToString()));
        }

        [Fact]
        public Task CacheBatch()
        {
            return RunConcurrent(testConcurrency, () => CacheBatch(inProcCacheFactory, Guid.NewGuid().ToString(), batchSize));
        }

        [Fact]
        public Task CacheExpiration()
        {
            return RunConcurrent(testConcurrency, () => CacheExpiration(inProcCacheFactory, Guid.NewGuid().ToString()));
        }

        [Fact]
        public Task CacheNotifications()
        {
            return RunConcurrent(testConcurrency, () => CacheNotifications(inProcCacheFactory, Guid.NewGuid().ToString(), notifySize));
        }
    }

    public class Redis : CacheTests
    {
        private RedisProviderFactory redisFactory = new RedisProviderFactory("localhost");

        public override void Dispose()
        {
            redisFactory.Dispose();
            base.Dispose();
        }

        [Fact]
        public Task CacheCRUD()
        {
            return RunConcurrent(testConcurrency, () => CacheCRUD(redisFactory, Guid.NewGuid().ToString()));
        }

        [Fact]
        public Task CacheBatch()
        {
            return RunConcurrent(testConcurrency, () => CacheBatch(redisFactory, Guid.NewGuid().ToString(), batchSize));
        }

        [Fact]
        public Task CacheExpiration()
        {
            return RunConcurrent(testConcurrency, () => CacheExpiration(redisFactory, Guid.NewGuid().ToString()));
        }

        [Fact]
        public Task CacheNotifications()
        {
            return RunConcurrent(testConcurrency, () => CacheNotifications(redisFactory, Guid.NewGuid().ToString(), notifySize));
        }
    }
}
