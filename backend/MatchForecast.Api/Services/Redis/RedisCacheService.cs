using System.Text.Json;
using MatchForecast.Api.Options;
using MatchForecast.Models.Common;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace MatchForecast.Api.Services.Redis;

public interface IRedisCacheService
{
    Task<T?> GetAsync<T>(string key);

    Task SetAsync<T>(string key, T value, TimeSpan? expiration = null);

    Task RemoveAsync(string key);

    Task<bool> ExistsAsync(string key);

    // Uygulamanın önekiyle (Redis:InstanceName) başlayan tüm key'leri siler, silinen key sayısını döner.
    Task<long> ClearAsync();
}

// Okuma/yazma hataları (Redis kapalı, timeout, bozuk JSON) cache miss sayılır; böylece Redis düşse de API kaynaktan cevap vermeye devam eder.
public sealed class RedisCacheService(
    IConnectionMultiplexer connection,
    IOptions<RedisOptions> options,
    ILogger<RedisCacheService> logger) : IRedisCacheService
{
    private const int ScanPageSize = 500;
    private readonly RedisOptions _opt = options.Value;

    private IDatabase Database => connection.GetDatabase();

    public async Task<T?> GetAsync<T>(string key)
    {
        try
        {
            var value = await Database.StringGetAsync(Prefixed(key));
            return value.HasValue ? JsonSerializer.Deserialize<T>(value.ToString()) : default;
        }
        catch (Exception ex) when (IsCacheFailure(ex))
        {
            logger.LogWarning(ex, "Redis read failed for key {Key}; falling back to source.", key);
            return default;
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? expiration = null)
    {
        try
        {
            var ttl = expiration ?? TimeSpan.FromMinutes(_opt.CacheMinutes);
            await Database.StringSetAsync(Prefixed(key), JsonSerializer.Serialize(value), new Expiration(ttl));
        }
        catch (Exception ex) when (IsCacheFailure(ex))
        {
            logger.LogWarning(ex, "Redis write failed for key {Key}; result is returned without caching.", key);
        }
    }

    // Silme işlemleri hata yutmaz: kullanıcı silme istediyse başarısızlığı görmeli.
    public async Task RemoveAsync(string key) =>
        await Database.KeyDeleteAsync(Prefixed(key));

    public async Task<bool> ExistsAsync(string key)
    {
        try
        {
            return await Database.KeyExistsAsync(Prefixed(key));
        }
        catch (Exception ex) when (IsCacheFailure(ex))
        {
            logger.LogWarning(ex, "Redis exists check failed for key {Key}.", key);
            return false;
        }
    }

    public async Task<long> ClearAsync()
    {
        var database = Database;
        var pattern = $"{_opt.InstanceName}*";
        var primaries = connection.GetEndPoints()
            .Select(e => connection.GetServer(e))
            .Where(s => s.IsConnected && !s.IsReplica)
            .ToList();

        if (primaries.Count == 0)
        {
            logger.LogError("Cache clear failed: no connected Redis primary.");
            throw new ForecastException("Redis'e bağlanılamadı, önbellek temizlenemedi.", StatusCodes.Status503ServiceUnavailable);
        }

        long deleted = 0;
        foreach (var server in primaries)
        {
            // KEYS yerine SCAN (KeysAsync sayfalı çalışır) kullanılır; Redis'i bloklamaz ve allowAdmin gerektirmez.
            var batch = new List<RedisKey>(ScanPageSize);
            await foreach (var key in server.KeysAsync(database.Database, pattern, ScanPageSize))
            {
                batch.Add(key);
                if (batch.Count < ScanPageSize) continue;
                deleted += await database.KeyDeleteAsync(batch.ToArray());
                batch.Clear();
            }

            if (batch.Count > 0)
                deleted += await database.KeyDeleteAsync(batch.ToArray());
        }

        logger.LogInformation("Cache cleared: {Count} keys deleted with pattern {Pattern}.", deleted, pattern);
        return deleted;
    }

    private string Prefixed(string key) => _opt.InstanceName + key;

    private static bool IsCacheFailure(Exception ex) =>
        ex is RedisException or RedisTimeoutException or JsonException;
}