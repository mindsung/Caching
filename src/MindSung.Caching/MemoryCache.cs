using System;
using System.Collections.Generic;

using ProtoBuf;

namespace MindSung.Caching
{
    public interface ICache<T>
    {
        IList<bool> Add(IList<CacheItem<T>> items);
        bool Add(CacheItem<T> item);
        void Set(IList<CacheItem<T>> items);
        void Set(CacheItem<T> item);
        IList<CacheResult<T>> Get(IList<string> keys);
        CacheResult<T> Get(string key);
        IList<CacheResult<T>> GetOrAdd(IList<CacheItem<T>> items);
        CacheResult<T> GetOrAdd(CacheItem<T> item);
        IList<bool> Remove(IList<string> keys);
        bool Remove(string key);
        IList<CacheResult<T>> GetAndRemove(IList<string> keys);
        CacheResult<T> GetAndRemove(string key);
    }

    public interface ILongCache : ICache<long>
    {
        IList<CacheResult<long>> AddValue(IList<CacheItem<long>> addValues);
        CacheResult<long> AddValue(CacheItem<long> addValue);
    }

    public interface IListCache<T> : ICache<IList<T>>
    {
        void Append(IList<CacheItem<IList<T>>> itemLists);
        void Append(CacheItem<IList<T>> itemList);
        void Append(IList<CacheItem<T>> items);
        void Append(CacheItem<T> item);
    }

    public static class MemoryCache
    {
        public static ICache<T> Create<T>(long maxSize = long.MaxValue, int defaultTtlMs = int.MaxValue, bool defaultSlidingTtl = true, bool allowEviction = true, Func<T, int> valueSizeHelper = null)
        {
            return MemoryCacheSet.Create(maxSize).GetNamedCache<T>("", defaultTtlMs, defaultSlidingTtl, allowEviction, valueSizeHelper);
        }
    }

    #region Related Public Entities

    [ProtoContract]
    public class CacheItem<T>
    {
        public CacheItem() { }
        public CacheItem(string key, T value, int ttlMs = 0, bool slidingTtl = true)
        {
            this.key = key;
            this.value = value;
            this.ttlMs = ttlMs;
            this.slidingTtl = slidingTtl;
        }
        [ProtoMember(1)]
        public string key;
        [ProtoMember(2)]
        public T value;
        [ProtoMember(3)]
        public int ttlMs; // 0 = default, max = don't expire
        [ProtoMember(4)]
        public bool slidingTtl = true;
    }

    [ProtoContract]
    public class CacheResult<T>
    {
        public CacheResult() { }
        public CacheResult(T value) { this.value = value; this.found = true; }
        public static CacheResult<T> NotFound() { return new CacheResult<T>(); }
        [ProtoMember(1)]
        public bool found;
        [ProtoMember(2)]
        public T value;
    }

    #endregion
}
