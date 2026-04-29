using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PixelCatsClient
{
    public static class ApiClient
    {
        private static readonly string ApiBase = "http://10.31.228.19:3000";
        private static readonly HttpClient _http = new() { BaseAddress = new Uri(ApiBase) };

        // Simple dedupe cache to avoid accidental duplicate POSTs in quick succession
        private static readonly object _postLock = new object();
        private static string? _lastPostedPayload;
        private static DateTime _lastPostedAt = DateTime.MinValue;
        private static string? _lastReturnedCode;
        private static readonly TimeSpan _dedupeWindow = TimeSpan.FromSeconds(5);

        // Optionally: set an API key from environment here
        static ApiClient()
        {
            var apiKey = Environment.GetEnvironmentVariable("API_KEY");
            if (!string.IsNullOrEmpty(apiKey))
                _http.DefaultRequestHeaders.Add("x-api-key", apiKey);
        }

        private sealed record CodeResponse(string code);

        /// <summary>
        /// Calls GET /api/generate_code and returns the server-generated code or null on failure.
        /// </summary>
        public static async Task<string?> GetGeneratedCodeAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                using var resp = await _http.GetAsync("/api/generate_code", cancellationToken).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) return null;

                var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var payload = await resp.Content.ReadFromJsonAsync<CodeResponse>(opts, cancellationToken).ConfigureAwait(false);
                return payload?.code;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Posts score and gameId to /api/codes and returns the server-generated code (or null on failure).
        /// This matches the server behavior where it stores the score and game identifier and generates the code.
        /// This method now includes a short dedupe window to avoid accidental duplicate submissions.
        /// </summary>
        public static async Task<string?> CreateScoreAndGetCodeAsync(int score, string gameId, CancellationToken cancellationToken = default)
        {
            try
            {
                // Build payload and serialized representation for dedupe
                var payloadObj = new { score, game_code = gameId };
                string? payloadJson;
                try { payloadJson = JsonSerializer.Serialize(payloadObj); } catch { payloadJson = null; }

                // Dedupe short-circuit
                lock (_postLock)
                {
                    if (!string.IsNullOrEmpty(payloadJson) && _lastPostedPayload == payloadJson && (DateTime.UtcNow - _lastPostedAt) <= _dedupeWindow)
                    {
                        // Return cached code if available, otherwise avoid reposting
                        try
                        {
                            Console.WriteLine($"[ApiClient] Suppressing duplicate POST /api/codes; returning cached code.");
                        }
                        catch { }
                        return _lastReturnedCode;
                    }
                }

                try
                {
                    Console.WriteLine($"[ApiClient] POST /api/codes payload: {JsonSerializer.Serialize(payloadObj)}");
                }
                catch { }

                using var resp = await _http.PostAsJsonAsync("/api/codes", payloadObj, cancellationToken).ConfigureAwait(false);

                if (!resp.IsSuccessStatusCode)
                {
                    try
                    {
                        var body = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                        Console.WriteLine($"[ApiClient] POST /api/codes failed: {(int)resp.StatusCode} {resp.ReasonPhrase} - {body}");
                    }
                    catch { }
                    return null;
                }

                var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var cr = await resp.Content.ReadFromJsonAsync<CodeResponse>(opts, cancellationToken).ConfigureAwait(false);

                // Update dedupe cache with the successful result
                lock (_postLock)
                {
                    try
                    {
                        _lastPostedPayload = payloadJson;
                        _lastPostedAt = DateTime.UtcNow;
                        _lastReturnedCode = cr?.code;
                    }
                    catch { }
                }

                return cr?.code;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ApiClient] Exception while posting to /api/codes: {ex}");
                return null;
            }
        }

        public static async Task<bool> SubmitCodeAsync(string code, int score, string gameName)
        {
            try
            {
                // Use game_code key to identify the developer token; if caller has a gameName instead
                // they should pass the developer token here. Keep backward-compatible property too.
                var payload = new { code, score, game_code = gameName };

                try
                {
                    Console.WriteLine($"[ApiClient] POST /api/codes payload (submit): {JsonSerializer.Serialize(payload)}");
                }
                catch { }

                var resp = await _http.PostAsJsonAsync("/api/codes", payload);
                if (!resp.IsSuccessStatusCode)
                {
                    try
                    {
                        var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                        Console.WriteLine($"[ApiClient] SubmitCode POST /api/codes failed: {(int)resp.StatusCode} {resp.ReasonPhrase} - {body}");
                    }
                    catch { }

                    // optionally read and log
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ApiClient] Exception in SubmitCodeAsync: {ex}");
                return false;
            }
        }

        public static async Task<ScoreRecord[]> GetTopScoresAsync(int limit = 8)
        {
            try
            {
                var resp = await _http.GetAsync($"/api/leaderboard?limit={limit}");
                if (!resp.IsSuccessStatusCode) return Array.Empty<ScoreRecord>();
                var arr = await resp.Content.ReadFromJsonAsync<ScoreRecord[]>();
                return arr ?? Array.Empty<ScoreRecord>();
            }
            catch
            {
                return Array.Empty<ScoreRecord>();
            }
        }
        public static async Task<Dictionary<string, ScoreRecord[]>> GetTopScoresByGamesAsync(string[] games, int perGame = 3, int fetchLimit = 200)
        {
            var result = new Dictionary<string, ScoreRecord[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var g in games) result[g] = Array.Empty<ScoreRecord>();

            try
            {
                var resp = await _http.GetAsync($"/api/leaderboard?limit={fetchLimit}");
                if (!resp.IsSuccessStatusCode) return result;

                var json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array) return result;

                var list = new System.Collections.Generic.List<ScoreRecord>(capacity: 64);

                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    int id = 0;
                    if (el.TryGetProperty("id", out var idProp) && idProp.ValueKind == System.Text.Json.JsonValueKind.Number)
                        idProp.TryGetInt32(out id);

                    string name = el.TryGetProperty("name", out var nameProp) && nameProp.ValueKind == System.Text.Json.JsonValueKind.String
                        ? nameProp.GetString()! : "";

                    int score = 0;
                    if (el.TryGetProperty("score", out var scoreProp) && scoreProp.ValueKind == System.Text.Json.JsonValueKind.Number)
                        scoreProp.TryGetInt32(out score);

                    string created_at = el.TryGetProperty("created_at", out var caProp) && caProp.ValueKind == System.Text.Json.JsonValueKind.String
                        ? caProp.GetString()! : "";

                    // Accept either "game" or "gameName" or "gameId"
                    string game = "";
                    if (el.TryGetProperty("game", out var gameProp) && gameProp.ValueKind == System.Text.Json.JsonValueKind.String)
                        game = gameProp.GetString()!;
                    else if (el.TryGetProperty("gameName", out var gnProp) && gnProp.ValueKind == System.Text.Json.JsonValueKind.String)
                        game = gnProp.GetString()!;
                    else if (el.TryGetProperty("gameId", out var gidProp) && gidProp.ValueKind == System.Text.Json.JsonValueKind.String)
                        game = gidProp.GetString()!;

                    // Construct ScoreRecord using your existing record signature (id, name, score, created_at, game)
                    list.Add(new ScoreRecord(id, name, score, created_at, game));
                }

                // Group and select top per game
                var comparer = System.StringComparer.OrdinalIgnoreCase;
                foreach (var g in games)
                {
                    var top = list
                        .Where(x => !string.IsNullOrEmpty(x.gameName) && comparer.Equals(x.gameName, g))
                        .OrderByDescending(x => x.score)
                        .ThenBy(x => x.name, comparer)
                        .Take(perGame)
                        .ToArray();

                    result[g] = top;
                }

                return result;
            }
            catch
            {
                return result;
            }
        }
    }

}