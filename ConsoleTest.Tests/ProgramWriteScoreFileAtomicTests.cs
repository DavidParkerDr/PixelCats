#nullable enable

using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using ConsoleTest.Games;
using PixelBoard;
using Xunit;

namespace ConsoleTest.Tests
{
    public sealed class ProgramWriteScoreFileAtomicTests
    {
        private sealed class FakeGame : IGame
        {
            public string GameId => "fake";

            public void Initialize(IPixel[,] pixels) { }
            public void Update(IPixel[,] pixels) { }
            public void DrawTitle(IPixel[,] pixels) { }
            public void HandleInput(ConsoleKey key, ref bool stateChanged) { }
            public int GetScore() => 0;
            public bool IsGameOver() => false;
            public string GetGameOverCode() => "";
            public void SetGameOverCode(string? code) { }
            public override string ToString() => "FakeGame";
        }

        private static MethodInfo GetWriteScoreMethod()
        {
            var asm = typeof(IGame).Assembly;
            var programType = asm.GetType("SnakeGame.Program", throwOnError: true);
            return programType!.GetMethod("WriteScoreFileAtomic", BindingFlags.NonPublic | BindingFlags.Static)
                   ?? throw new MissingMethodException(programType.FullName, "WriteScoreFileAtomic");
        }

        [Fact]
        public void WriteScoreFileAtomic_WritesJson_WithoutCode_WhenNull()
        {
            var mi = GetWriteScoreMethod();

            var dir = Path.Combine(Path.GetTempPath(), "PixelCatsFork.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);

            var path = Path.Combine(dir, "latest_score.json");

            mi.Invoke(null, new object?[] { path, 42, new FakeGame(), "Startup", null });

            Assert.True(File.Exists(path));

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;

            Assert.Equal(42, root.GetProperty("score").GetInt32());
            Assert.Equal("Startup", root.GetProperty("state").GetString());
            Assert.Equal("FakeGame", root.GetProperty("gameName").GetString());

            Assert.True(root.TryGetProperty("timestamp", out var tsProp));
            Assert.Equal(JsonValueKind.String, tsProp.ValueKind);
            Assert.False(string.IsNullOrWhiteSpace(tsProp.GetString()));

            Assert.False(root.TryGetProperty("code", out _));
        }

        [Fact]
        public void WriteScoreFileAtomic_WritesJson_WithCode_WhenProvided()
        {
            var mi = GetWriteScoreMethod();

            var dir = Path.Combine(Path.GetTempPath(), "PixelCatsFork.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);

            var path = Path.Combine(dir, "latest_score.json");

            mi.Invoke(null, new object?[] { path, 9001, new FakeGame(), "GameOver", "ABC123" });

            Assert.True(File.Exists(path));

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;

            Assert.Equal(9001, root.GetProperty("score").GetInt32());
            Assert.Equal("GameOver", root.GetProperty("state").GetString());
            Assert.Equal("ABC123", root.GetProperty("code").GetString());
        }
    }
}