#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using ConsoleTest.Games;
using Xunit;
using PixelBoard;

namespace ConsoleTest.Tests
{
    // Minimal IPixel implementation used by all tests
    internal sealed class TestPixel : IPixel
    {
        public byte Red { get; set; }
        public byte Green { get; set; }
        public byte Blue { get; set; }
    }

    public sealed class GenericGameTests
    {
        private static IPixel[,] CreatePixelBuffer()
        {
            var pixels = new IPixel[20, 10];
            for (int r = 0; r < 20; r++)
                for (int c = 0; c < 10; c++)
                    pixels[r, c] = new TestPixel();
            return pixels;
        }

        // Finds all concrete IGame implementations in the same assembly as Tetris
        public static IEnumerable<object[]> GameTypes()
        {
            var asm = typeof(Tetris).Assembly;
            var gameInterface = typeof(IGame);

            foreach (var t in asm.GetTypes()
                                 .Where(x => gameInterface.IsAssignableFrom(x)
                                             && !x.IsAbstract
                                             && x.IsClass
                                             && x.GetConstructor(Type.EmptyTypes) != null))
            {
                yield return new object[] { t };
            }
        }

        [Theory]
        [MemberData(nameof(GameTypes))]
        public void Game_Lifecycle_NoExceptions(Type gameType)
        {
            // Arrange
            var pixels = CreatePixelBuffer();

            // Fixes CS8600/CS8602: CreateInstance can return null -> assert first
            var instance = Activator.CreateInstance(gameType);
            Assert.NotNull(instance);

            var game = (IGame)instance!;

            // Act - call title and initialize (should not throw)
            game.DrawTitle(pixels);
            game.Initialize(pixels);

            // Act - call Update a few times to exercise drawing/logic
            for (int i = 0; i < 10; i++)
                game.Update(pixels);

            // Act - send a few inputs (soft-drop, rotate, escape)
            bool stateChanged = false;
            game.HandleInput(ConsoleKey.S, ref stateChanged);
            game.HandleInput(ConsoleKey.UpArrow, ref stateChanged);
            game.HandleInput(ConsoleKey.Escape, ref stateChanged);

            // Assert basic invariants
            Assert.True(game.GetScore() >= 0);
            var code = game.GetGameOverCode();

            if (game.IsGameOver())
            {
                Assert.False(string.IsNullOrEmpty(code));
            }
            else
            {
                Assert.True(code is null || code.Length > 0);
            }
        }
    }
}
