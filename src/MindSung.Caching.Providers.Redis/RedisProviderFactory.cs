using System;
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

        private ConnectionMultiplexer GetConnection()
        {
            if (connection == null)
            {
                connectionSync.Wait();
                try
                {
                    if (connection == null)
                    {
                        var cn = $"{networkAddress}:{(port > 0 ? port : (ssl ? 6380 : 6379))},abortConnect=false";
                        if (ssl) { cn += ",ssl=true"; }
                        if (password != null) { cn += $",password={password}"; }
                        connection = ConnectionMultiplexer.Connect(cn);
                    }
                }
                finally
                {
                    connectionSync.Release();
                }
            }
            return connection;
        }

        public ICacheProvider GetNamedCacheProvider(string name, bool slidingExpiry)
        {
            return new RedisProvider(GetConnection(), name, slidingExpiry);
        }

        ICacheProvider<string> ICacheProviderFactory<string>.GetNamedCacheProvider(string name, bool slidingExpiry)
        {
            return GetNamedCacheProvider(name, slidingExpiry);
        }

        private async Task<ConnectionMultiplexer> GetConnectionAsync()
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

        public async Task<ICacheProvider> GetNamedCacheProviderAsync(string name, bool slidingExpiry)
        {
            return new RedisProvider(await GetConnectionAsync(), name, slidingExpiry);
        }

        async Task<ICacheProvider<string>> ICacheProviderFactory<string>.GetNamedCacheProviderAsync(string name, bool slidingExpiry)
        {
            return await GetNamedCacheProviderAsync(name, slidingExpiry);
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
