#nullable enable

using PixelBoard;
using System;
using System.Threading.Tasks;

namespace ConsoleTest.Games
{
    public class Education : IGame
    {
        // Unique identifier for this game. Replace with the real ID provided by the site.
        public string GameId { get; } = "VJhH-GChBH8";

        private int score = 0;
        private int lives = 3;
        private readonly int rows = 20;
        private readonly int cols = 10;
        private int[,] board = new int[20, 10]; // 0 = empty, 1 = falling block
        private int bucketX = 4; // bucket center column (0..cols-1)
        private readonly Random rand = new Random();
        private int frameCounter = 0;

        // Speed tuning: base interval and minimum interval (frames)
        private int dropIntervalBase = 6;   // starting frames between downward moves
        private int minDropInterval = 2;    // fastest allowed (lower = faster)
        private int speedupScoreStep = 3;   // every `speedupScoreStep` points reduce interval by 1

        private int spawnInterval = 18; // frames between new block spawns
        private int spawnCounter = 0;
        private bool gameOver = false;
        private string? gameOverCode;

        private TaskCompletionSource<string?>? codeTcs;

        public void Initialize(IPixel[,] pixels)
        {
            score = 0;
            lives = 3;
            board = new int[rows, cols];
            bucketX = cols / 2;
            frameCounter = 0;
            spawnCounter = 0;
            gameOver = false;
            gameOverCode = null;
            codeTcs = null;
        }

        public void DrawTitle(IPixel[,] pixels)
        {
            for (sbyte i = 0; i < 20; i++)
            {
                for (sbyte j = 0; j < 10; j++)
                {
                    pixels[i, j] = new Pixel(200, 50, 150);
                }
            }
            int[,] sShape = new int[,]
           {
                {1, 1, 1, 1, 1},
                {1, 0, 1, 0, 1},
                {1, 0, 1, 0, 1},
                {1, 0, 1, 0, 1},
                {1, 0, 1, 0, 1}
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
                        pixels[offsetX + x, offsetY + y] = new Pixel(0, 0, 0); // Black pixel
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

            frameCounter++;
            spawnCounter++;

            // Spawn new block at intervals
            if (spawnCounter >= spawnInterval)
            {
                spawnCounter = 0;
                int col = rand.Next(cols);
                if (board[0, col] == 0)
                {
                    board[0, col] = 1;
                }
            }

            int currentDropInterval = GetCurrentDropInterval();

            // Move blocks down at drop interval
            if (frameCounter >= currentDropInterval)
            {
                frameCounter = 0;

                // FIRST: move blocks down from top to bottom-2 -> this handles blocks moving into bottom row in the same tick
                for (int r = rows - 2; r >= 0; r--)
                {
                    for (int c = 0; c < cols; c++)
                    {
                        if (board[r, c] == 1)
                        {
                            int newR = r + 1;

                            // If moving into bottom row, resolve immediately (catch or miss)
                            if (newR == rows - 1)
                            {
                                // moving into bottom row -> check bucket catch
                                if (IsBucketCoveringColumn(c))
                                {
                                    score += 1;
                                }
                                else
                                {
                                    lives -= 1;
                                    if (lives <= 0)
                                    {
                                        gameOver = true;
                                    }
                                }
                                board[r, c] = 0; // remove source cell (block consumed / removed)
                            }
                            else
                            {
                                // Normal downward move if target is empty
                                if (board[newR, c] == 0)
                                {
                                    board[newR, c] = 1;
                                    board[r, c] = 0;
                                }
                                // if target occupied, keep block in place (stack)
                            }

                            // if game ended due to lives reaching zero, bail out early
                            if (gameOver)
                                break;
                        }
                    }
                    if (gameOver)
                        break;
                }

                if (!gameOver)
                {
                    // THEN: process any blocks that were already on bottom row (could be leftover from previous tick)
                    for (int c = 0; c < cols; c++)
                    {
                        if (board[rows - 1, c] == 1)
                        {
                            if (IsBucketCoveringColumn(c))
                            {
                                score += 1;
                            }
                            else
                            {
                                lives -= 1;
                                if (lives <= 0)
                                {
                                    gameOver = true;
                                }
                            }
                            board[rows - 1, c] = 0;
                            if (gameOver) break;
                        }
                    }
                }
            }

            DrawBoard(pixels);
        }

        public void HandleInput(ConsoleKey key, ref bool stateChanged)
        {
            if (gameOver)
            {
                // Allow escape to return to title
                if (key == ConsoleKey.Escape)
                {
                    stateChanged = true;
                }
                return;
            }

            switch (key)
            {
                case ConsoleKey.A:
                case ConsoleKey.LeftArrow:
                    if (bucketX > 0) bucketX--;
                    break;
                case ConsoleKey.D:
                case ConsoleKey.RightArrow:
                    if (bucketX < cols - 1) bucketX++;
                    break;
                case ConsoleKey.Escape:
                    stateChanged = true;
                    break;
                case ConsoleKey.Spacebar:
                case ConsoleKey.S:
                case ConsoleKey.DownArrow:
                    // Fast-forward: immediately advance one drop step using current interval
                    frameCounter = GetCurrentDropInterval();
                    break;
            }
        }

        public bool IsGameOver() => gameOver;

        public string? GetGameOverCode() => gameOverCode;

        public int GetScore() => score;

        public void SetGameOverCode(string? code)
        {
            gameOverCode = code;
            codeTcs?.TrySetResult(code);
        }

        private bool IsBucketCoveringColumn(int col)
        {
            // Bucket is drawn centered at bucketX and covers up to 3 columns if possible
            int left = Math.Max(0, bucketX - 1);
            int right = Math.Min(cols - 1, bucketX + 1);
            return col >= left && col <= right;
        }

        private void DrawBoard(IPixel[,] pixels)
        {
            // Background
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    pixels[r, c] = new Pixel(10, 10, 40);
                }
            }

            // Draw falling blocks
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    if (board[r, c] == 1)
                    {
                        pixels[r, c] = new Pixel(200, 40, 40); // red block
                    }
                }
            }

            // Draw bucket on bottom row (three-pixel wide when possible)
            int left = Math.Max(0, bucketX - 1);
            int right = Math.Min(cols - 1, bucketX + 1);
            for (int c = left; c <= right; c++)
            {
                pixels[rows - 1, c] = new Pixel(0, 180, 0); // green bucket
            }

            // Draw HUD: lives and score on top row (simple)
            // Lives: draw small red pixels on top-left
            for (int i = 0; i < 3; i++)
            {
                int col = Math.Min(cols - 1, i);
                if (i < lives)
                    pixels[0, col] = new Pixel(220, 40, 40);
                else
                    pixels[0, col] = new Pixel(30, 30, 30);
            }

            // Score indicator: show one yellow pixel per point (capped to available columns)
            int scoreBars = Math.Min(cols - 3, score); // now increases each point
            for (int i = 0; i < scoreBars; i++)
            {
                int col = cols - 1 - i;
                pixels[0, col] = new Pixel(180, 180, 40);
            }
        }

        private void DrawGameOver(IPixel[,] pixels)
        {
            // Red background to indicate game over, with a black center stripe
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    pixels[r, c] = new Pixel(80, 0, 0);
                }
            }

            // Draw a simple "score stripe" across the middle
            int mid = rows / 2;
            for (int c = 2; c < cols - 2; c++)
            {
                pixels[mid, c] = new Pixel(0, 0, 0);
            }
        }

        private int GetCurrentDropInterval()
        {
            // Reduce interval by 1 for each `speedupScoreStep` points, down to `minDropInterval`.
            int decreased = score / speedupScoreStep;
            int interval = dropIntervalBase - decreased;
            return Math.Max(minDropInterval, interval);
        }
    }
}