using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MindSung.Caching.Providers
{
    public class CacheValue<T> : ICacheValue<T>
    {
        public bool HasValue { get; set; }
        public T Value { get; set; }
    }
}
