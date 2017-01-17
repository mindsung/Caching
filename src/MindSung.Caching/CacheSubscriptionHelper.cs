using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MindSung.Caching
{
    public class CacheSubscriptionHelper
    {
        private class Subscription
        {
            public Func<string, Task> onSet;
            public Func<string, Task> onDelete;
        }

        private ConcurrentDictionary<string, ConcurrentDictionary<Guid, Subscription>> subscriptions = new ConcurrentDictionary<string, ConcurrentDictionary<Guid, Subscription>>();

        public Guid AddSubscription(string key, Action<string> onSet, Action<string> onDelete)
        {
            return AddSubscription(key, k => { onSet(k); return Task.FromResult(true); }, k => { onDelete(k); return Task.FromResult(true); });
        }

        public Guid AddSubscription(string key, Func<string, Task> onSet, Func<string, Task> onDelete)
        {
            var id = Guid.NewGuid();
            subscriptions.GetOrAdd(key, new ConcurrentDictionary<Guid, Subscription>())[id] = new Subscription { onSet = onSet, onDelete = onDelete };
            return id;
        }

        public void RemoveSubscription(string key, Guid subscriptionId)
        {
            ConcurrentDictionary<Guid, Subscription> keySubs;
            Subscription _;
            if (!subscriptions.TryGetValue(key, out keySubs) || !keySubs.TryRemove(subscriptionId, out _))
            {
                throw new Exception("Subscription not found.");
            }
        }

        public void PublishSet(string key)
        {
            ConcurrentDictionary<Guid, Subscription> keySubs;
            if (subscriptions.TryGetValue(key, out keySubs))
            {
                foreach (var sub in keySubs.Values)
                {
                    Task.Run(async () =>
                    {
                        try
                        {
                            if (sub.onSet != null) { await sub.onSet(key); }
                        }
                        catch { }
                    });
                }
            }
        }

        public void PublishDelete(string key)
        {
            ConcurrentDictionary<Guid, Subscription> keySubs;
            if (subscriptions.TryGetValue(key, out keySubs))
            {
                foreach (var sub in keySubs.Values)
                {
                    Task.Run(async () =>
                    {
                        try
                        {
                            if (sub.onSet != null) { await sub.onDelete(key); }
                        }
                        catch { }
                    });
                }
            }
        }
    }
}
