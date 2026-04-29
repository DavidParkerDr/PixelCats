// I pulled TryReadScoreWithTimestampAsync out as a helper so ScoreWatcher can reuse it.
// This is mostly your original logic simplified for reuse.
using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PixelCatsClient
{
    public static class ProgramHelpers
    {
        public static async Task<(int? score, DateTimeOffset? timestamp, string? state, string? code, string? gameName)>
            TryReadScoreWithTimestampAsync(string path, CancellationToken ct)
        {
            for (int attempt = 0; attempt < 6; attempt++)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    if (!File.Exists(path))
                        return (null, null, null, null, null);

                    using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var sr = new StreamReader(fs);
                    var json = await sr.ReadToEndAsync();
                    if (string.IsNullOrWhiteSpace(json))
                        return (null, null, null, null, null);

                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    int? score = null;
                    DateTimeOffset? timestamp = null;
                    string? state = null;
                    string? code = null;
                    string? gameName = null;

                    if (root.TryGetProperty("score", out var scoreProp) && scoreProp.ValueKind == JsonValueKind.Number)
                    {
                        if (scoreProp.TryGetInt32(out int v))
                            score = v;
                    }

                    if (root.TryGetProperty("state", out var stateProp) && stateProp.ValueKind == JsonValueKind.String)
                    {
                        state = stateProp.GetString();
                    }

                    if (root.TryGetProperty("code", out var codeProp) && codeProp.ValueKind == JsonValueKind.String)
                    {
                        code = codeProp.GetString();
                    }

                    if (root.TryGetProperty("gameName", out var gnProp) && gnProp.ValueKind == JsonValueKind.String)
                    {
                        gameName = gnProp.GetString();

                        if (!string.IsNullOrWhiteSpace(gameName))
                        {
                            gameName = gameName.Trim();
                            int lastDot = gameName.LastIndexOf('.');
                            if (lastDot >= 0 && lastDot < gameName.Length - 1)
                                gameName = gameName.Substring(lastDot + 1);
                        }
                    }

                    string[] tsCandidates = { "timestamp", "time", "written_at", "created_at" };
                    foreach (var key in tsCandidates)
                    {
                        if (!root.TryGetProperty(key, out var p)) continue;

                        if (p.ValueKind == JsonValueKind.String)
                        {
                            if (DateTimeOffset.TryParse(p.GetString(), out var dto))
                            {
                                timestamp = dto.ToUniversalTime();
                                break;
                            }
                        }
                        else if (p.ValueKind == JsonValueKind.Number)
                        {
                            if (p.TryGetInt64(out long n))
                            {
                                if (n > 1_000_000_000_000L)
                                    timestamp = DateTimeOffset.FromUnixTimeMilliseconds(n);
                                else
                                    timestamp = DateTimeOffset.FromUnixTimeSeconds(n);
                                break;
                            }
                        }
                    }

                    if (!timestamp.HasValue)
                    {
                        timestamp = File.GetLastWriteTimeUtc(path);
                    }

                    return (score, timestamp, state, code, gameName);
                }
                catch (JsonException) { /* retry */ }
                catch (IOException) { /* retry */ }
                catch (Exception) { return (null, null, null, null, null); }

                await Task.Delay(200, ct);
            }

            return (null, null, null, null, null);
        }
    }
}