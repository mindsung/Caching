namespace MindSung.Caching.Providers
{
    public class CacheValue<T> : ICacheValue<T>
    {
        public bool HasValue { get; set; }
        public T Value { get; set; }
    }
}
