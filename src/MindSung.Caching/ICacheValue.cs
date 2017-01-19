namespace MindSung.Caching
{
    public interface ICacheValue<T>
    {
        bool HasValue { get; }
        T Value { get; }
    }
}
