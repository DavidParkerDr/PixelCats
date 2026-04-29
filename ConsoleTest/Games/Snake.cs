using PixelBoard;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;

namespace ConsoleTest.Games
{
    public class Snake : IGame
    {
        // Unique identifier for this game. Replace with the real ID provided by the site.
        public string GameId { get; } = "VTwLvlyoHGw";

        // Game variables
        private Queue<(int x, int y)> snake = new Queue<(int x, int y)>();
        private int snakeLength = 5;
        private int headX = 10;
        private int headY = 5;
        private int directionX = 0;
        private int directionY = 1;
        private int nextDirectionX = 0;
        private int nextDirectionY = 1;
        private readonly Random rand = new Random();
        private (int x, int y) food;
        private int score = 0;
        private float rainbowShift = 0f;
        private bool gameOver = false;
        private bool wallsAreDeadly = false;
        private int moveCounter = 0;
        private string? gameOverCode;

        private TaskCompletionSource<string?>? codeTcs;

        public void Initialize(IPixel[,] pixels)
        {
            // Reset to original values
            snake.Clear();
            snakeLength = 5;
            headX = 10;
            headY = 5;
            directionX = 0;
            directionY = 1;
            nextDirectionX = 0;
            nextDirectionY = 1;
            food = (rand.Next(20), rand.Next(10));
            score = 0;
            gameOver = false;
            moveCounter = 0;
            gameOverCode = null;  // Reset code
            codeTcs = null;

            // Initialize snake body
            for (int i = 0; i < snakeLength; i++)
            {
                snake.Enqueue((headX, headY - i));
            }
        }

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

        public void Update(IPixel[,] pixels)
        {
            if (gameOver)
            {
                DrawGameOver(pixels);
                return;
            }

            moveCounter++;

            if (moveCounter < GetMoveSpeed())
            {
                DrawGame(pixels);
                return;
            }

            moveCounter = 0;

            directionX = nextDirectionX;
            directionY = nextDirectionY;

            // Move snake
            headX += directionX;
            headY += directionY;

            // Check wall collision
            if (wallsAreDeadly)
            {
                // Walls are deadly - game over
                if (headX < 0 || headX >= 20 || headY < 0 || headY >= 10)
                {
                    gameOver = true;
                    DrawGameOver(pixels);
                    return;
                }
            }
            else
            {
                // Walls wrap around
                if (headX >= 20) headX = 0;
                if (headX < 0) headX = 19;
                if (headY >= 10) headY = 0;
                if (headY < 0) headY = 9;
            }

            // Check self-collision (classic Snake game over condition)
            if (snake.Contains((headX, headY)))
            {
                gameOver = true;
                DrawGameOver(pixels);
                return;
            }

            // Check if snake eats food
            if (headX == food.x && headY == food.y)
            {
                score++;
                snakeLength++;

                // Spawn food in empty location
                do
                {
                    food = (rand.Next(20), rand.Next(10));
                } while (snake.Contains(food) || (food.x == headX && food.y == headY));
            }

            // Update snake body
            snake.Enqueue((headX, headY));
            if (snake.Count > snakeLength)
                snake.Dequeue();

            // Draw game
            DrawGame(pixels);
        }

        public void HandleInput(ConsoleKey key, ref bool stateChanged)
        {
            if (gameOver)
            {
                // If game is over, pressing any key could be used to return to title or restart.
                // We do not change state here; Program handles transitions via stateChanged or IsGameOver check.
                if (key == ConsoleKey.Escape)
                {
                    stateChanged = true;
                }
                return;
            }

            // Buffer input to prevent reversing into yourself mid-frame
            switch (key)
            {
                case ConsoleKey.W:
                case ConsoleKey.UpArrow:
                    // Can't reverse direction
                    if (directionX != 1)
                    {
                        nextDirectionX = -1;
                        nextDirectionY = 0;
                    }
                    break;
                case ConsoleKey.S:
                case ConsoleKey.DownArrow:
                    if (directionX != -1)
                    {
                        nextDirectionX = 1;
                        nextDirectionY = 0;
                    }
                    break;
                case ConsoleKey.A:
                case ConsoleKey.LeftArrow:
                    if (directionY != 1)
                    {
                        nextDirectionX = 0;
                        nextDirectionY = -1;
                    }
                    break;
                case ConsoleKey.D:
                case ConsoleKey.RightArrow:
                    if (directionY != -1)
                    {
                        nextDirectionX = 0;
                        nextDirectionY = 1;
                    }
                    break;
                case ConsoleKey.Escape:
                    // Signal Program that user requested to leave the playing state
                    stateChanged = true;
                    break;
            }
        }

        // Expose game-over status so Program can reliably transition to GameOver and export final score. 
        public bool IsGameOver() => gameOver;

        public string GetGameOverCode() => gameOverCode;  // Return the generated code (may be null)

        public int GetScore() => score;

        public void SetGameOverCode(string? code)
        {
            gameOverCode = code;
            codeTcs?.TrySetResult(code);
        }

        /// <summary>
        /// Awaitable helper that waits up to <paramref name="timeoutMs"/> for the server-provided code.
        /// Returns the code if available, otherwise returns whatever value is currently in <see cref="gameOverCode"/> (may be null).
        /// Callers may block on the returned Task if they can't be async.
        /// </summary>
        public async Task<string?> WaitForGameOverCodeAsync(int timeoutMs = 3000)
        {
            if (!string.IsNullOrEmpty(gameOverCode)) return gameOverCode;
            codeTcs ??= new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);

            var completed = await Task.WhenAny(codeTcs.Task, Task.Delay(timeoutMs)).ConfigureAwait(false);
            if (completed == codeTcs.Task)
                return await codeTcs.Task.ConfigureAwait(false);

            // timeout -> return current value (may be null or fallback code)
            return gameOverCode;
        }

        private int GetMoveSpeed()
        {
            if (score < 5) return 3;       // Medium start - more engaging
            if (score < 10) return 2;      // Fast
            if (score < 20) return 1;      // Very fast - challenging
            return 1;                       // Keep at very fast
        }

        private void DrawGame(IPixel[,] pixels)
        {
            // Clear background
            for (sbyte i = 0; i < 20; i++)
            {
                for (sbyte j = 0; j < 10; j++)
                {
                    pixels[i, j] = new Pixel(20, 20, 40);
                }
            }

            // Draw snake
            foreach (var (x, y) in snake.Take(snake.Count - 1))
            {
                pixels[x, y] = new Pixel(0, 180, 0);
            }

            // Draw snake head
            if (snake.Count > 0)
            {
                var head = snake.Last();
                pixels[head.x, head.y] = new Pixel(0, 255, 0);
            }

            // Draw food
            pixels[food.x, food.y] = new Pixel(255, 0, 0);
        }

        private void DrawGameOver(IPixel[,] pixels)
        {
            // Draw the final game state 
            DrawGame(pixels);

            // Flash red overlay
            for (int i = 0; i < 20; i++)
            {
                for (int j = 0; j < 10; j++)
                {
                    // Red tint overlay
                    pixels[i, j] = new Pixel(150, 0, 0);
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