#nullable enable

using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;

namespace PixelCatsClient
{
    public record ScoreRecord(int id, string name, int score, string created_at, string gameName);

    public sealed class PixelCatsApiClient
    {
        private readonly HttpClient _http;

        public PixelCatsApiClient(HttpClient http)
        {
            _http = http ?? throw new ArgumentNullException(nameof(http));
        }

        // Submit a code to the server. `gameCode` is the developer-facing token (game_code) the server expects.
        public async Task<bool> SubmitCodeAsync(string code, int score, string gameCode, string? apiKey = null)
        {
            if (string.IsNullOrEmpty(code)) throw new ArgumentException("code required", nameof(code));
            if (string.IsNullOrEmpty(gameCode)) throw new ArgumentException("gameCode required", nameof(gameCode));

            if (!string.IsNullOrEmpty(apiKey))
            {
                if (_http.DefaultRequestHeaders.Contains("x-api-key"))
                    _http.DefaultRequestHeaders.Remove("x-api-key");
                _http.DefaultRequestHeaders.Add("x-api-key", apiKey);
            }

            // Use the `game_code` key so the server recognizes the developer token
            var payload = new { code, score, game_code = gameCode };

            try
            {
                var resp = await _http.PostAsJsonAsync("/api/codes", payload).ConfigureAwait(false);
                return resp.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        // Fixes CS8603 by returning a non-null array always
        public async Task<ScoreRecord[]> GetTopScoresAsync(int limit = 8)
        {
            HttpResponseMessage resp;
            try
            {
                resp = await _http.GetAsync($"/api/leaderboard?limit={limit}").ConfigureAwait(false);
            }
            catch
            {
                return Array.Empty<ScoreRecord>();
            }

            if (!resp.IsSuccessStatusCode)
                return Array.Empty<ScoreRecord>();

            try
            {
                var data = await resp.Content.ReadFromJsonAsync<ScoreRecord[]>().ConfigureAwait(false);
                return data ?? Array.Empty<ScoreRecord>();
            }
            catch
            {
                return Array.Empty<ScoreRecord>();
            }
        }
    }
}
