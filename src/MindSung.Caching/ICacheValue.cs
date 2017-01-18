using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MindSung.Caching
{
    public interface ICacheValue<T>
    {
        bool HasValue { get; }
        T Value { get; }
    }
}
