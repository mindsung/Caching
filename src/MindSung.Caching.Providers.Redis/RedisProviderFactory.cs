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

        public async Task<ICacheProvider> GetNamedCacheProvider(string name, bool slidingExpiry)
        {
            return new RedisProvider(await GetConnection(), name, slidingExpiry);
        }

        async Task<ICacheProvider<string>> ICacheProviderFactory<string>.GetNamedCacheProvider(string name, bool slidingExpiry)
        {
            return await GetNamedCacheProvider(name, slidingExpiry);
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
