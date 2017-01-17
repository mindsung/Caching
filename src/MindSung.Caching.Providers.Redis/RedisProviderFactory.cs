using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using StackExchange.Redis;

namespace MindSung.Caching.Providers.Redis
{
    public class RedisProviderFactory : ICacheProviderFactory, IDisposable
    {
        public RedisProviderFactory(string networkAddress, string password = null, bool ssl = false, int port = 0)
        {
            this.networkAddress = networkAddress;
            this.password = password;
            this.ssl = ssl;
            this.port = port;
        }

        private string networkAddress;
        private string password;
        private bool ssl;
        private int port;
        private ConnectionMultiplexer connection;
        private SemaphoreSlim connectionSync = new SemaphoreSlim(1, 1);

        private async Task<ConnectionMultiplexer> GetConnection()
        {
            if (connection == null)
            {
                await connectionSync.WaitAsync();
                try
                {
                    if (connection == null)
                    {
                        var cn = $"{networkAddress}:{(port > 0 ? port : (ssl ? 6380 : 6379))},abortConnect=false";
                        if (ssl) { cn += ",ssl=true"; }
                        if (password != null) { cn += $",password={password}"; }
                        connection = await ConnectionMultiplexer.ConnectAsync(cn);
                    }
                }
                finally
                {
                    connectionSync.Release();
                }
            }
            return connection;
        }

        public async Task<ICacheProvider<T>> GetNamedCacheProvider<T>(string cacheName, bool slidingExpiry)
        {
            if (typeof(T) != typeof(string))
            {
                throw new NotSupportedException("Type parameter T for RedisProviderFactory method must be type System.String.");
            }
            return (ICacheProvider<T>)(new RedisProvider(await GetConnection(), cacheName, slidingExpiry));
        }

        public void Dispose()
        {
            if (connection != null)
            {
                connection.Dispose();
            }
        }
    }
}
