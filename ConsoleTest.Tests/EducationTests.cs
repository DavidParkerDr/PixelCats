#nullable enable

using System;
using System.Reflection;
using ConsoleTest.Games;
using PixelBoard;
using Xunit;

namespace ConsoleTest.Tests
{
    public sealed class EducationTests
    {
        private static IPixel[,] CreatePixelBuffer()
        {
            var pixels = new IPixel[20, 10];
            for (int r = 0; r < 20; r++)
                for (int c = 0; c < 10; c++)
                    pixels[r, c] = new Pixel(0, 0, 0);
            return pixels;
        }

        private static FieldInfo GetField(Type t, string name)
            => t.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
               ?? throw new MissingFieldException(t.FullName, name);

        private static T GetPrivate<T>(object instance, string fieldName)
        {
            var f = GetField(instance.GetType(), fieldName);
            return (T)f.GetValue(instance)!;
        }

        private static void SetPrivate<T>(object instance, string fieldName, T value)
        {
            var f = GetField(instance.GetType(), fieldName);
            f.SetValue(instance, value);
        }

        [Fact]
        public void Initialize_ResetsState()
        {
            var pixels = CreatePixelBuffer();
            var game = new Education();

            SetPrivate(game, "score", 123);
            SetPrivate(game, "lives", 1);
            SetPrivate(game, "gameOver", true);
            SetPrivate<string?>(game, "gameOverCode", "X");

            game.Initialize(pixels);

            Assert.Equal(0, game.GetScore());
            Assert.False(game.IsGameOver());
            Assert.Null(game.GetGameOverCode());

            var board = GetPrivate<int[,]>(game, "board");
            Assert.Equal(20, board.GetLength(0));
            Assert.Equal(10, board.GetLength(1));
        }

        [Fact]
        public void HandleInput_MovesBucketWithinBounds()
        {
            var pixels = CreatePixelBuffer();
            var game = new Education();
            game.Initialize(pixels);

            bool changed = false;

            for (int i = 0; i < 50; i++)
                game.HandleInput(ConsoleKey.LeftArrow, ref changed);

            int bucketX = GetPrivate<int>(game, "bucketX");
            Assert.Equal(0, bucketX);

            for (int i = 0; i < 50; i++)
                game.HandleInput(ConsoleKey.RightArrow, ref changed);

            bucketX = GetPrivate<int>(game, "bucketX");
            Assert.Equal(9, bucketX);
        }

        [Fact]
        public void Update_BlockCaught_IncrementsScore()
        {
            var pixels = CreatePixelBuffer();
            var game = new Education();
            game.Initialize(pixels);

            SetPrivate(game, "bucketX", 5);
            SetPrivate(game, "spawnCounter", 0);
            SetPrivate(game, "frameCounter", 5); // next Update increments to 6 -> drop tick at score=0

            var board = GetPrivate<int[,]>(game, "board");
            board[18, 5] = 1;

            game.Update(pixels);

            Assert.Equal(1, game.GetScore());
            Assert.False(game.IsGameOver());
            Assert.Equal(0, board[18, 5]);
            Assert.Equal(0, board[19, 5]);
        }

        [Fact]
        public void Update_BlockMissed_DecrementsLives_AndSetsGameOver_WhenLivesReachZero()
        {
            var pixels = CreatePixelBuffer();
            var game = new Education();
            game.Initialize(pixels);

            SetPrivate(game, "bucketX", 0);
            SetPrivate(game, "lives", 1);
            SetPrivate(game, "spawnCounter", 0);
            SetPrivate(game, "frameCounter", 5);

            var board = GetPrivate<int[,]>(game, "board");
            board[18, 9] = 1; // bucket at 0 covers cols 0..1 only

            game.Update(pixels);

            Assert.True(game.IsGameOver());
            Assert.Equal(0, GetPrivate<int>(game, "lives"));
        }

        [Fact]
        public void HandleInput_WhenGameOver_EscapeSetsStateChanged()
        {
            var pixels = CreatePixelBuffer();
            var game = new Education();
            game.Initialize(pixels);

            SetPrivate(game, "gameOver", true);

            bool stateChanged = false;
            game.HandleInput(ConsoleKey.Escape, ref stateChanged);

            Assert.True(stateChanged);
        }

        [Fact]
        public void SetGameOverCode_SetsAndReturns()
        {
            var pixels = CreatePixelBuffer();
            var game = new Education();
            game.Initialize(pixels);

            Assert.Null(game.GetGameOverCode());

            game.SetGameOverCode("12345");

            Assert.Equal("12345", game.GetGameOverCode());
        }
    }
}