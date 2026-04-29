using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ConsoleTest
{
    public sealed class LeaderboardClient
    {
        private readonly HttpClient _http;
        private readonly string _baseUrl;
        private readonly byte[] _hmacKey;

        public LeaderboardClient(string baseUrl, string deviceHmacSecret)
        {
            _http = new HttpClient();
            _baseUrl = (baseUrl ?? throw new ArgumentNullException(nameof(baseUrl))).TrimEnd('/');
            _hmacKey = Encoding.UTF8.GetBytes(deviceHmacSecret ?? throw new ArgumentNullException(nameof(deviceHmacSecret)));
        }

        public async System.Threading.Tasks.Task<string> MintClaimCodeAsync(string gameCode, int score)
        {
            if (string.IsNullOrWhiteSpace(gameCode))
                throw new ArgumentException("gameCode is required", nameof(gameCode));

            long ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            string nonce = CreateNonce(16);

            // MUST match Python exactly:
            // game_code=<game_code>&score=<int>&ts=<int>&nonce=<nonce>
            string canonical = $"game_code={gameCode}&score={score}&ts={ts}&nonce={nonce}";
            string sig = ComputeSigBase64Url(_hmacKey, canonical);

            var payload = new
            {
                game_code = gameCode,
                score = score,
                ts = ts,
                nonce = nonce,
                sig = sig,
            };

            using var resp = await _http.PostAsJsonAsync($"{_baseUrl}/api/codes", payload).ConfigureAwait(false);
            string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (!resp.IsSuccessStatusCode)
                throw new InvalidOperationException($"POST /api/codes failed: {(int)resp.StatusCode} {resp.ReasonPhrase}\n{body}");

            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("code", out var codeProp))
                throw new InvalidOperationException($"POST /api/codes response missing 'code'\n{body}");

            var code = codeProp.GetString();
            if (string.IsNullOrWhiteSpace(code))
                throw new InvalidOperationException($"POST /api/codes returned empty 'code'\n{body}");

            return code;
        }

        private static string CreateNonce(int numBytes)
        {
            Span<byte> bytes = stackalloc byte[numBytes];
            RandomNumberGenerator.Fill(bytes);
            return Base64UrlEncode(bytes);
        }

        private static string ComputeSigBase64Url(byte[] key, string canonical)
        {
            byte[] msg = Encoding.UTF8.GetBytes(canonical);
            byte[] mac = HMACSHA256.HashData(key, msg);
            return Base64UrlEncode(mac);
        }

        private static string Base64UrlEncode(ReadOnlySpan<byte> data)
        {
            string b64 = Convert.ToBase64String(data);
            return b64.Replace("+", "-").Replace("/", "_").TrimEnd('=');
        }
    }
}