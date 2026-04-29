#nullable enable

using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PixelCatsClient;
using Xunit;

namespace ConsoleTest.Tests
{
    // Very small fake handler for deterministic responses
    internal sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
            => _responder = responder ?? throw new ArgumentNullException(nameof(responder));

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_responder(request));
    }

    public sealed class PixelCatsApiClientTests
    {
        [Fact]
        public async Task SubmitCodeAsync_ReturnsTrue_On200()
        {
            var handler = new FakeHttpMessageHandler(req =>
            {
                Assert.Equal(HttpMethod.Post, req.Method);

                // Avoid nullable warning by asserting non-null before using it
                Assert.NotNull(req.RequestUri);
                Assert.Equal("/api/codes", req.RequestUri!.PathAndQuery);

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"ok\":true}")
                };
            });

            using var http = new HttpClient(handler) { BaseAddress = new Uri("http://api.test") };
            var client = new PixelCatsApiClient(http);

            var ok = await client.SubmitCodeAsync("ABC123", 9001, "Tetris", "key");
            Assert.True(ok);
        }

        [Fact]
        public async Task SubmitCodeAsync_ReturnsFalse_OnServerError()
        {
            var handler = new FakeHttpMessageHandler(_ =>
                new HttpResponseMessage(HttpStatusCode.InternalServerError));

            using var http = new HttpClient(handler) { BaseAddress = new Uri("http://api.test") };
            var client = new PixelCatsApiClient(http);

            var ok = await client.SubmitCodeAsync("X", 1, "Tetris");
            Assert.False(ok);
        }

        [Fact]
        public async Task SubmitCodeAsync_Throws_OnMissingCode()
        {
            using var http = new HttpClient(new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)))
            {
                BaseAddress = new Uri("http://api.test")
            };
            var client = new PixelCatsApiClient(http);

            await Assert.ThrowsAsync<ArgumentException>(() => client.SubmitCodeAsync("", 1, "Tetris"));
        }

        [Fact]
        public async Task SubmitCodeAsync_Throws_OnMissingGameCode()
        {
            using var http = new HttpClient(new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)))
            {
                BaseAddress = new Uri("http://api.test")
            };
            var client = new PixelCatsApiClient(http);

            await Assert.ThrowsAsync<ArgumentException>(() => client.SubmitCodeAsync("ABC", 1, ""));
        }

        [Fact]
        public async Task SubmitCodeAsync_SetsApiKeyHeader_WhenProvided()
        {
            int call = 0;

            var handler = new FakeHttpMessageHandler(req =>
            {
                call++;
                string expected = call == 1 ? "key1" : "key2";

                Assert.True(req.Headers.Contains("x-api-key"));
                Assert.Equal(expected, string.Join(",", req.Headers.GetValues("x-api-key")));

                return new HttpResponseMessage(HttpStatusCode.OK);
            });

            using var http = new HttpClient(handler) { BaseAddress = new Uri("http://api.test") };
            var client = new PixelCatsApiClient(http);

            _ = await client.SubmitCodeAsync("ABC", 1, "Tetris", apiKey: "key1");
            _ = await client.SubmitCodeAsync("DEF", 2, "Tetris", apiKey: "key2");

            Assert.Equal(2, call);
        }

        [Fact]
        public async Task GetTopScoresAsync_ParsesJson_On200()
        {
            var payload = new[]
            {
                new { id = 1, name = "Alice", score = 100, created_at = "2026-01-01T00:00:00Z", gameName = "Tetris" },
                new { id = 2, name = "Bob", score = 80, created_at = "2026-01-02T00:00:00Z", gameName = "Tetris" }
            };
            var json = JsonSerializer.Serialize(payload);

            var handler = new FakeHttpMessageHandler(req =>
            {
                Assert.Equal(HttpMethod.Get, req.Method);

                Assert.NotNull(req.RequestUri);
                Assert.StartsWith("/api/leaderboard?limit=2", req.RequestUri!.PathAndQuery, StringComparison.Ordinal);

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
                };
            });

            using var http = new HttpClient(handler) { BaseAddress = new Uri("http://api.test") };
            var client = new PixelCatsApiClient(http);

            var results = await client.GetTopScoresAsync(2);

            Assert.Equal(2, results.Length);
            Assert.Equal("Alice", results[0].name);
            Assert.Equal(100, results[0].score);
        }

        [Fact]
        public async Task GetTopScoresAsync_ReturnsEmpty_OnNonSuccess()
        {
            var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest));

            using var http = new HttpClient(handler) { BaseAddress = new Uri("http://api.test") };
            var client = new PixelCatsApiClient(http);

            var results = await client.GetTopScoresAsync(5);

            Assert.NotNull(results);
            Assert.Empty(results);
        }

        [Fact]
        public async Task GetTopScoresAsync_ReturnsEmpty_OnInvalidJson()
        {
            var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{not-json", System.Text.Encoding.UTF8, "application/json")
            });

            using var http = new HttpClient(handler) { BaseAddress = new Uri("http://api.test") };
            var client = new PixelCatsApiClient(http);

            var results = await client.GetTopScoresAsync(5);

            Assert.NotNull(results);
            Assert.Empty(results);
        }
    }
}