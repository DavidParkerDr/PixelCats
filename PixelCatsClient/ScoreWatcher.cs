using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PixelCatsClient
{
    // Simple container for a detected score
    public record ScoreEvent(string Code, int Score, string GameName, DateTimeOffset Timestamp);

    // Lightweight watcher that mirrors the logic in your console app but raises events
    public class ScoreWatcher
    {
        private readonly string _filePath;
        private CancellationTokenSource? _cts;

        public event Action<ScoreEvent>? NewScoreDetected;

        public ScoreWatcher(string filePath)
        {
            _filePath = filePath;
        }

        public void Start()
        {
            if (_cts != null) return;
            _cts = new CancellationTokenSource();

            // Small, safe log so you can confirm the watcher path without changing behavior
            //Console.WriteLine($"[ScoreWatcher] Starting watcher for: {_filePath} (exists: {File.Exists(_filePath)})");

            _ = RunAsync(_cts.Token);
        }

        public void Stop()
        {
            _cts?.Cancel();
            _cts = null;
        }

        private async Task RunAsync(CancellationToken ct)
        {
            // We'll mimic your poller as a simple loop here.
            long lastProcessed = 0;
            try
            {
                if (File.Exists(_filePath))
                {
                    lastProcessed = new DateTimeOffset(File.GetLastWriteTimeUtc(_filePath)).ToUnixTimeMilliseconds();
                }
            }
            catch (Exception)
            {
                // don't fail startup if timestamp read fails
            }
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    if (File.Exists(_filePath))
                    {
                        var (score, timestamp, state, code, gameName) = await ProgramHelpers.TryReadScoreWithTimestampAsync(_filePath, ct);
                        if (score.HasValue && timestamp.HasValue && string.Equals(state, "GameOver", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(code))
                        {
                            var tsMs = timestamp.Value.ToUnixTimeMilliseconds();
                            if (tsMs > lastProcessed)
                            {
                                lastProcessed = tsMs;
                                var ev = new ScoreEvent(code, score.Value, string.IsNullOrWhiteSpace(gameName) ? "Unknown" : gameName, timestamp.Value);
                                NewScoreDetected?.Invoke(ev);

                                // Small log so you can see events in the console
                                Console.WriteLine($"[ScoreWatcher] Emitted ScoreEvent Code={ev.Code} Score={ev.Score} Timestamp={ev.Timestamp:u}");
                            }
                        }
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    // Log exception so we're not silently swallowing issues
                    Console.WriteLine($"[ScoreWatcher] Error reading file: {ex.Message}");
                }

                await Task.Delay(800, ct);
            }
        }
    }
}