#nullable enable

using PixelBoard;
using System;

namespace ConsoleTest.Games
{
    public interface IGame
    {
        void Initialize(IPixel[,] pixels);
        void Update(IPixel[,] pixels);
        void DrawTitle(IPixel[,] pixels);
        void HandleInput(ConsoleKey key, ref bool stateChanged);
        int GetScore();
        bool IsGameOver();
        string? GetGameOverCode();
        void SetGameOverCode(string? code);
        string GameId { get; }
    }
}