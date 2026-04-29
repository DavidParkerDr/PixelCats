using System;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using PixelCatsClient;
namespace ConsoleTest
{
    public static class CodeGenerator
    {
        public static string GenerateSixDigitCode()
        {
            Span<byte> bytes = stackalloc byte[4];
            RandomNumberGenerator.Fill(bytes);
            uint value = BitConverter.ToUInt32(bytes) % 1_000_000;
            return value.ToString("D6");
        }

        /// <summary>
        /// Requests a unique six-digit code from the server by posting the score and gameId so
        /// the server can store the score and generate the code. If the request fails and allowFallback is true,
        /// the method returns a locally-generated code. Returns the exact string received from the server when successful.
        /// </summary>
        public static async Task<string> GenerateSixDigitCodeAsync(int score, string gameId, bool allowFallback = true, int timeoutSeconds = 3)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
                var code = await ApiClient.CreateScoreAndGetCodeAsync(score, gameId, cts.Token).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(code)) return code;
            }
            catch
            {
                // swallow; fallback below if allowed
            }

            if (allowFallback)
                return GenerateSixDigitCode();

            throw new InvalidOperationException("Failed to obtain a generated code from the server.");
        }
    }
}