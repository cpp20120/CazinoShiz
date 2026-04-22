using System.Collections.Concurrent;

namespace CasinoShiz.Host.Pages.Admin;

// Holds the GIF bytes from the most recent RunRaceAsync call so the admin UI
// can embed it after a POST→redirect. HorseResultStore persists only the last
// frame (PNG), not the full animation. Ephemeral; survives until restart.
public sealed class HorseGifCache
{
    private readonly ConcurrentDictionary<string, byte[]> _byDate = new();

    public void Put(string raceDate, byte[] gif) => _byDate[raceDate] = gif;

    public byte[]? Get(string raceDate) => _byDate.TryGetValue(raceDate, out var v) ? v : null;

    public IEnumerable<string> Dates => _byDate.Keys;
    
    public async Task<byte[]?> GetAsync(string raceDate, CancellationToken ct = default)
    {
        return await Task.FromResult(Get(raceDate));
    }
}
