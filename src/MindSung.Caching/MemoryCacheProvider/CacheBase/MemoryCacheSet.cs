using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace MindSung.Caching.MemoryCacheProvider.CacheBase
{
    public partial class MemoryCacheSet
    {
        private MemoryCacheSet(int numPartitions, long maxSize)
        {
            this.maxSize = maxSize;
            this.numPartitions = numPartitions;
        }

        public static MemoryCacheSet Create(long maxSize = long.MaxValue)
        {
            return new MemoryCacheSet(1, maxSize);
        }

        public static MemoryCacheSet CreatePartitioned(int numPartitions, long maxSize = long.MaxValue)
        {
            return new MemoryCacheSet(numPartitions, maxSize);
        }

        long maxSize;
        int numPartitions;
        LinkedList<ICacheEntry> lruSet = new LinkedList<ICacheEntry>();
        SortedSet<ICacheEntry> expirySet = new SortedSet<ICacheEntry>(new ExpiryComparer());
        protected object sync = new object();
        int uidNext = 0;
        long currentSize = 0;

        class ExpiryComparer : IComparer<ICacheEntry>
        {
            public int Compare(ICacheEntry x, ICacheEntry y)
            {
                if (x.expiry > y.expiry) return 1;
                if (x.expiry < y.expiry) return -1;
                if (x.uid > y.uid) return 1;
                if (x.uid < y.uid) return -1;
                return 0;
            }
        }

        ConcurrentDictionary<string, ICacheInfo> namedCaches = new ConcurrentDictionary<string, ICacheInfo>(StringComparer.CurrentCultureIgnoreCase);

        interface ICacheInfo
        {
            Type CacheValueType { get; }
        }

        interface ICacheEntry
        {
            string key { get; }
            int uid { get; set; }
            int ttlMs { get; set; }
            long expiry { get; set; }
            LinkedListNode<ICacheEntry> lruNode { get; }
            int size { get; }
        }

        public ICache<T> GetNamedCache<T>(string name, int defaultTtlMs = int.MaxValue, bool defaultSlidingTtl = true, bool allowEviction = true, Func<T, int> valueSizeHelper = null)
        {
            var cache = namedCaches.GetOrAdd(name, _ => new Cache<T>(this, defaultTtlMs, defaultSlidingTtl, allowEviction, valueSizeHelper));
            if (!cache.CacheValueType.Equals(typeof(T)))
                throw new InvalidOperationException("An existing named cache was created with a different type parameter.");
            return (ICache<T>)cache;
        }

        public static int GetPartitionIndex(string key, int numPartitions)
        {
            int sum = 0;
            int len = key.Length;
            int n;
            for (int i = 0; i < len; i++)
            {
                n = (int)key[i];
                sum += n + (n << i % 9);
            }
            if (sum < 0) return -sum % numPartitions;
            return sum % numPartitions;
        }

        public int GetPartitionIndex(string key)
        {
            return GetPartitionIndex(key, numPartitions);
        }

        class Cache<T> : ICacheInfo, ICache<T>
        {
            #region Class Setup

            public Cache(MemoryCacheSet cacheSet, int defaultTtlMs, bool defaultSlidingTtl, bool allowEviction, Func<T, int> valueSizeHelper)
            {
                this.cacheSet = cacheSet;
                this.defaultTtlMs = defaultTtlMs;
                this.defaultSlidingTtl = defaultSlidingTtl;
                this.allowEviction = allowEviction;
                this.valueSizeHelper = valueSizeHelper;
                partitions = new Partition[cacheSet.numPartitions];
                for (int i = 0; i < cacheSet.numPartitions; i++)
                {
                    partitions[i] = new Partition();
                }
            }

            MemoryCacheSet cacheSet;
            int defaultTtlMs;
            bool defaultSlidingTtl;
            bool allowEviction;
            Func<T, int> valueSizeHelper;
            Partition[] partitions;

            Partition GetPartition(string key)
            {
                return partitions[cacheSet.GetPartitionIndex(key)];
            }

            public Type CacheValueType { get { return typeof(T); } }

            class CacheEntry : ICacheEntry
            {
                public string key { get; set; }
                public int uid { get; set; }
                public int ttlMs { get; set; }
                public long expiry { get; set; }
                public LinkedListNode<ICacheEntry> lruNode { get; set; }
                public T value { get; set; }
                public int size { get; set; }
            }

            class Partition
            {
                public Dictionary<string, CacheEntry> keySet = new Dictionary<string, CacheEntry>();
            }

            #endregion

            #region Protected Helpers

            int GetItemSize(string key, T value)
            {
                var valueSize = valueSizeHelper != null ? valueSizeHelper(value) : 0;
                if (valueSize > 0) return valueSize + ApproxItemSizeOverhead + (key.Length * 2);
                else return 1; // Enforcing item count, not size.
            }

            static readonly int ApproxItemSizeOverhead = IntPtr.Size == 4 ? 160 : 285; // from mem tests

            CacheEntry CreateEntry(DateTime now, CacheItem<T> item)
            {
                var entry = new CacheEntry
                {
                    key = item.key,
                    ttlMs = item.slidingTtl ? -item.ttlMs : item.ttlMs,
                    value = item.value,
                    size = GetItemSize(item.key, item.value)
                };
                SetExpiry(now, entry);
                return entry;
            }

            CacheEntry GetEntry(DateTime now, Partition part, string key, out bool isExpired)
            {
                CacheEntry entry;
                if (part.keySet.TryGetValue(key, out entry))
                {
                    isExpired = IsExpired(now, entry);
                    return entry;
                }
                else
                {
                    isExpired = false;
                    return null;
                }
            }

            void SetExpiry(DateTime now, ICacheEntry entry)
            {
                var xpms = entry.ttlMs;
                entry.expiry = xpms == int.MaxValue ? long.MaxValue : DateTime.UtcNow.AddMilliseconds(xpms < 0 ? -xpms : xpms).Ticks;
                entry.uid = cacheSet.uidNext++;
                if (cacheSet.uidNext == int.MaxValue) cacheSet.uidNext = 0;
            }

            bool IsExpired(DateTime now, ICacheEntry entry)
            {
                return entry.expiry != long.MaxValue && now.Ticks > entry.expiry;
            }

            void RemoveEntry(Partition part, ICacheEntry entry)
            {
                part.keySet.Remove(entry.key);
                cacheSet.expirySet.Remove(entry);
                if (allowEviction) cacheSet.lruSet.Remove(entry.lruNode);
                cacheSet.currentSize -= entry.size;
            }

            bool TryRecoverOne(DateTime now)
            {
                // Recover one if one is expired.
                var expire = cacheSet.expirySet.FirstOrDefault();
                if (expire != null && IsExpired(now, expire))
                {
                    RemoveEntry(GetPartition(expire.key), expire);
                    return true;
                }
                return false;
            }

            CacheEntry AddItem(DateTime now, CacheItem<T> item)
            {
                var entry = CreateEntry(now, item);
                // Always try to recover an expired entry first.
                TryRecoverOne(now);
                // If this addition will exceed the max size, recover or evict until there is enough room.
                while (cacheSet.maxSize < (cacheSet.currentSize + entry.size) && cacheSet.expirySet.Count > 0)
                {
                    if (!TryRecoverOne(now) && allowEviction)
                    {
                        var evict = cacheSet.lruSet.FirstOrDefault();
                        if (evict == null) break;
                        RemoveEntry(GetPartition(evict.key), evict);
                    }
                }
                if (allowEviction)
                {
                    entry.lruNode = cacheSet.lruSet.AddLast(entry);
                }
                if (entry.expiry != long.MaxValue)
                {
                    cacheSet.expirySet.Add(entry);
                }
                GetPartition(item.key).keySet.Add(item.key, entry);
                cacheSet.currentSize += entry.size;
                return entry;
            }

            void AccessEntry(DateTime now, CacheEntry entry, bool forceUpdateExpiry)
            {
                if (entry.ttlMs < 0 || forceUpdateExpiry)
                {
                    // Sliding expiration or expired
                    if (entry.expiry != long.MaxValue) cacheSet.expirySet.Remove(entry);
                    SetExpiry(now, entry);
                    if (entry.expiry != long.MaxValue) cacheSet.expirySet.Add(entry);
                }
                if (allowEviction)
                {
                    // Move to the end of LRU
                    cacheSet.lruSet.Remove(entry.lruNode);
                    cacheSet.lruSet.AddLast(entry.lruNode);
                }
            }

            CacheItem<T> ApplyDefaults(CacheItem<T> item)
            {
                if (item.ttlMs == 0)
                {
                    item.ttlMs = defaultTtlMs;
                    item.slidingTtl = defaultSlidingTtl;
                }
                return item;
            }

            IEnumerable<CacheItem<T>> ApplyDefaults(IEnumerable<CacheItem<T>> items)
            {
                return items.Select(i => ApplyDefaults(i));
            }

            #endregion

            #region Public API

            public IList<bool> Add(IList<CacheItem<T>> items)
            {
                var now = DateTime.UtcNow;
                var results = new List<bool>();
                lock (cacheSet.sync)
                {
                    foreach (var item in ApplyDefaults(items))
                    {
                        bool expired;
                        var entry = GetEntry(now, GetPartition(item.key), item.key, out expired);
                        if (entry != null)
                        {
                            if (!expired)
                            {
                                results.Add(false);
                            }
                            else
                            {
                                // Exists but expired, update.
                                entry.value = item.value;
                                entry.ttlMs = item.ttlMs;
                                AccessEntry(now, entry, true);
                                results.Add(true);
                            }
                        }
                        else
                        {
                            AddItem(now, item);
                            results.Add(true);
                        }
                    }
                }
                return results;
            }

            public bool Add(CacheItem<T> item)
            {
                return Add(new[] { item }).Single();
            }

            public void Set(IList<CacheItem<T>> items)
            {
                var now = DateTime.UtcNow;
                lock (cacheSet.sync)
                {
                    foreach (var item in ApplyDefaults(items))
                    {
                        bool expired;
                        var entry = GetEntry(now, GetPartition(item.key), item.key, out expired);
                        if (entry != null)
                        {
                            // Exists, update even if expired.
                            entry.value = item.value;
                            entry.ttlMs = item.ttlMs;
                            AccessEntry(now, entry, expired);
                        }
                        else
                        {
                            // Doesn't exist, add
                            AddItem(now, item);
                        }
                    }
                }
            }

            public void Set(CacheItem<T> item)
            {
                Set(new[] { item });
            }

            public IList<CacheResult<T>> Get(IList<string> keys)
            {
                var now = DateTime.UtcNow;
                var results = new List<CacheResult<T>>();
                lock (cacheSet.sync)
                {
                    foreach (var key in keys)
                    {
                        bool expired;
                        var entry = GetEntry(now, GetPartition(key), key, out expired);
                        if (entry != null && !expired)
                        {
                            AccessEntry(now, entry, false);
                            results.Add(new CacheResult<T>(entry.value));
                        }
                        else
                        {
                            results.Add(CacheResult<T>.NotFound());
                        }
                    }
                }
                return results;
            }

            public CacheResult<T> Get(string key)
            {
                return Get(new[] { key }).Single();
            }

            public IList<CacheResult<T>> GetOrAdd(IList<CacheItem<T>> items)
            {
                throw new NotImplementedException();
            }

            public CacheResult<T> GetOrAdd(CacheItem<T> item)
            {
                throw new NotImplementedException();
            }

            public IList<bool> Remove(IList<string> keys)
            {
                var now = DateTime.UtcNow;
                var results = new List<bool>();
                lock (cacheSet.sync)
                {
                    foreach (var key in keys)
                    {
                        var part = GetPartition(key);
                        bool expired;
                        var entry = GetEntry(now, part, key, out expired);
                        if (entry != null)
                        {
                            RemoveEntry(part, entry);
                            results.Add(!expired);
                        }
                        else
                        {
                            results.Add(false);
                        }
                    }
                }
                return results;
            }

            public bool Remove(string key)
            {
                return Remove(new[] { key }).Single();
            }

            public IList<CacheResult<T>> GetAndRemove(IList<string> keys)
            {
                throw new NotImplementedException();
            }

            public CacheResult<T> GetAndRemove(string key)
            {
                throw new NotImplementedException();
            }

            #endregion
        }
    }
}
