using PixelBoard;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace ConsoleTest.Games
{
    public class Snake : IGame
    {

        private Queue<(int x, int y)> snake = new Queue<(int x, int y)>();
        private int snakeLength = 5;
        private int headX = 10;
        private int headY = 5;

        private int directionX = 0;
        private int directionY = 1;

        private int nextDirectionX = 0;
        private int nextDirectionY = 1;

        private Random rand = new Random();
        private (int x, int y) food;

        private int score = 0;
        private float rainbowShift = 0f;

        private bool gameOver = false;
        private bool wallsAreDeadly = false;

        private int moveCounter = 0;
        private string gameOverCode = null;

        // ======================================================
        // TODO
        // ======================================================

        public void Initialize(IPixel[,] pixels)
        {
            // TODO
        }

        public void Update(IPixel[,] pixels)
        {
            // TODO
        }

        public void HandleInput(ConsoleKey key, ref bool stateChanged)
        {
            // TODO
        }

        public int GetScore()
        {
            // TODO
            return 0;
        }

        // Optional helpers used by the main program / scoring screen
        public bool IsGameOver()
        {
            // TODO
            return false;
        }

        public string GetGameOverCode()
        {
            // TODO
            return null;
        }

        // Private helpers (students implement)
        private int GetMoveSpeed()
        {
            // TODO
            return 0;
        }

        private void DrawGame(IPixel[,] pixels)
        {
            // TODO
        }

        private void DrawGameOver(IPixel[,] pixels)
        {
            // TODO
        }

        // ======================================================
        // HELPERS
        // ======================================================

        public void DrawTitle(IPixel[,] pixels)
        {
            rainbowShift += 0.0001f;
            for (sbyte i = 0; i < 20; i++)
            {
                for (sbyte j = 0; j < 10; j++)
                {
                    float hue = (rainbowShift + (i + j) * 0.05f) % 1f;
                    Color color = ColorFromHSV(hue * 360, 1f, 1f);
                    pixels[i, j] = new Pixel(color.R, color.G, color.B);
                }
            }

            int[,] sShape = new int[,]
            {
                {1, 1, 1, 0, 1},
                {1, 0, 1, 0, 1},
                {1, 0, 1, 0, 1},
                {1, 0, 1, 0, 1},
                {1, 0, 1, 1, 1}
            };
            int shapeWidth = sShape.GetLength(1);
            int shapeHeight = sShape.GetLength(0);
            int offsetX = (20 - shapeWidth) / 2;
            int offsetY = (10 - shapeHeight) / 2;

            for (int y = 0; y < shapeHeight; y++)
            {
                for (int x = 0; x < shapeWidth; x++)
                {
                    if (sShape[y, x] == 1)
                    {
                        pixels[offsetX + x, offsetY + y] = new Pixel(0, 0, 0);
                    }
                }
            }
        }

        public static Color ColorFromHSV(double hue, double saturation, double value)
        {
            int hi = Convert.ToInt32(Math.Floor(hue / 60)) % 6;
            double f = hue / 60 - Math.Floor(hue / 60);

            value = value * 255;
            int v = Convert.ToInt32(value);
            int p = Convert.ToInt32(value * (1 - saturation));
            int q = Convert.ToInt32(value * (1 - f * saturation));
            int t = Convert.ToInt32(value * (1 - (1 - f) * saturation));

            return hi switch
            {
                0 => Color.FromArgb(255, v, t, p),
                1 => Color.FromArgb(255, q, v, p),
                2 => Color.FromArgb(255, p, v, t),
                3 => Color.FromArgb(255, p, q, v),
                4 => Color.FromArgb(255, t, p, v),
                _ => Color.FromArgb(255, v, p, q),
            };
        }
    }
}
