#nullable enable

using PixelBoard;
using System;
using System.Drawing;
using System.Threading.Tasks;

namespace ConsoleTest.Games
{
    public class Tetris : IGame
    {
        public string GameId { get; } = "bCbC7cpanUA";

        // ===== Arduino-matching tuning =====
        private const int BASE_FALL_MS = 550;
        private const int LINES_PER_LEVEL = 10;
        private const int FALL_DECREMENT = 40;
        private const int MIN_FALL_MS = 80;

        private const int PER_LINE_FALL_DECREMENT_MS = 10;

        private const int SCORE_STEP_POINTS = 1000;
        private const int SCORE_STEP_FALL_DECREMENT_MS = 20;

        private const int SOFT_DROP_MIN_MS = 55;
        private const int SOFT_DROP_DIVISOR = 4;

        private int? holdPieceIndex = null;
        private bool holdLocked = false;
        private int nextPieceIndex = 0;
        private int score = 0;
        private int level = 0;
        private int totalLinesCleared = 0;
        private int fallDelayMs = BASE_FALL_MS;
        private int lastScoreSpeedStep = 0;

        private int[,] board = new int[20, 10]; // 0 = empty, 1+ = filled
        private Tetromino? currentPiece;
        private int pieceX;
        private int pieceY;
        private readonly Random rand = new Random();

        private bool gameOver = false;
        private int manualDropCooldown = 0;
        private int rotateCooldown = 0;  // Rotation cooldown
        private string gameOverCode = null;  // Game over code
        private DateTime ignoreDownUntil = DateTime.MinValue; // suppress repeated Down presses for a short time

        private TaskCompletionSource<string?>? codeTcs;

        // Time-based gravity to match Arduino behavior
        private DateTime lastFallTimeUtc = DateTime.UtcNow;
        private bool downHeld = false;

        private static readonly int[][,] SHAPES = new int[][,]
        {
            new int[,] { {1,1,1,1} },                 // I
            new int[,] { {1,1}, {1,1} },             // O
            new int[,] { {0,1,0}, {1,1,1} },         // T
            new int[,] { {0,1,1}, {1,1,0} },         // S
            new int[,] { {1,1,0}, {0,1,1} },         // Z
            new int[,] { {1,0,0}, {1,1,1} },         // J
            new int[,] { {0,0,1}, {1,1,1} }          // L
        };

        private static readonly Color[] PIECE_COLORS = new Color[]
        {
            Color.Cyan,     // I
            Color.Yellow,   // O
            Color.Purple,   // T
            Color.Green,    // S
            Color.Red,      // Z
            Color.Blue,     // J
            Color.Orange    // L
        };

        private class Tetromino
        {
            public int[,] Shape;
            public Color Color;
            public int ColorIndex;

            public Tetromino(int shapeIndex)
            {
                Shape = (int[,])SHAPES[shapeIndex].Clone();
                ColorIndex = shapeIndex;
                Color = PIECE_COLORS[shapeIndex];
            }

            public void Rotate()
            {
                int rows = Shape.GetLength(0);
                int cols = Shape.GetLength(1);
                int[,] rotated = new int[cols, rows];

                for (int r = 0; r < rows; r++)
                {
                    for (int c = 0; c < cols; c++)
                    {
                        rotated[c, rows - 1 - r] = Shape[r, c];
                    }
                }

                Shape = rotated;
            }
        }

        public void Initialize(IPixel[,] pixels)
        {
            score = 0;
            level = 0;
            totalLinesCleared = 0;
            fallDelayMs = BASE_FALL_MS;
            lastScoreSpeedStep = 0;

            board = new int[20, 10];
            gameOver = false;
            manualDropCooldown = 0;
            rotateCooldown = 0;
            gameOverCode = null;
            codeTcs = null;

            holdPieceIndex = null;
            holdLocked = false;
            nextPieceIndex = rand.Next(SHAPES.Length);

            downHeld = false;
            ignoreDownUntil = DateTime.MinValue;
            lastFallTimeUtc = DateTime.UtcNow;

            SpawnNewPiece();
        }

        public void DrawTitle(IPixel[,] pixels)
        {
            for (sbyte i = 0; i < 20; i++)
            {
                for (sbyte j = 0; j < 10; j++)
                {
                    pixels[i, j] = new Pixel(100, 75, 200);
                }
            }

            int[,] tShape = new int[,]
            {
                {1, 0, 0, 0, 0},
                {1, 0, 0, 0, 0},
                {1, 1, 1, 1, 1},
                {1, 0, 0, 0, 0},
                {1, 0, 0, 0, 0}
            };

            int shapeWidth = tShape.GetLength(1);
            int shapeHeight = tShape.GetLength(0);
            int offsetX = (20 - shapeWidth) / 2;
            int offsetY = (10 - shapeHeight) / 2;

            for (int y = 0; y < shapeHeight; y++)
            {
                for (int x = 0; x < shapeWidth; x++)
                {
                    if (tShape[y, x] == 1)
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

            if (manualDropCooldown > 0)
                manualDropCooldown--;

            if (rotateCooldown > 0)
                rotateCooldown--;

            // Approximate held-down behavior in console mode:
            // if there has been no recent down key activity, release downHeld
            if (downHeld && (DateTime.UtcNow - ignoreDownUntil).TotalMilliseconds > 150)
            {
                downHeld = false;
            }

            int currentDelay = CurrentFallDelay(downHeld);
            var now = DateTime.UtcNow;

            if ((now - lastFallTimeUtc).TotalMilliseconds >= currentDelay)
            {
                lastFallTimeUtc = now;

                bool movedDown = MovePiece(0, 1);
                if (movedDown)
                {
                    if (downHeld)
                    {
                        score += 1; // Arduino soft-drop scoring
                    }
                }
                else
                {
                    LockPiece();
                    ClearLinesAndApplyArduinoProgression();
                    SpawnNewPiece();

                    if (!IsValidPosition(pieceX, pieceY))
                    {
                        gameOver = true;
                    }
                }
            }

            DrawBoard(pixels);
        }

        public void HandleInput(ConsoleKey key, ref bool stateChanged)
        {
            if (gameOver)
            {
                if (key == ConsoleKey.Escape)
                    stateChanged = true;
                return;
            }

            ProcessKey(key, ref stateChanged);
        }

        private void ProcessKey(ConsoleKey key, ref bool stateChanged)
        {
            switch (key)
            {
                case ConsoleKey.A:
                case ConsoleKey.LeftArrow:
                    ignoreDownUntil = DateTime.MinValue;
                    downHeld = false;
                    MovePiece(-1, 0);
                    DrainInput();
                    break;

                case ConsoleKey.D:
                case ConsoleKey.RightArrow:
                    ignoreDownUntil = DateTime.MinValue;
                    downHeld = false;
                    MovePiece(1, 0);
                    DrainInput();
                    break;

                case ConsoleKey.S:
                case ConsoleKey.DownArrow:
                    downHeld = true;
                    ignoreDownUntil = DateTime.UtcNow;

                    if (DateTime.UtcNow < ignoreDownUntil)
                    {
                        DrainDownsAndProcessFirstNonDown(ref stateChanged);
                        break;
                    }

                    if (manualDropCooldown <= 0)
                    {
                        // Immediate manual move, still matching the Arduino rule of +1 if moved while down is held
                        if (MovePiece(0, 1))
                        {
                            score += 1;
                        }
                        else
                        {
                            LockPiece();
                            ClearLinesAndApplyArduinoProgression();
                            SpawnNewPiece();

                            if (!IsValidPosition(pieceX, pieceY))
                                gameOver = true;
                        }

                        manualDropCooldown = 2;
                        lastFallTimeUtc = DateTime.UtcNow;
                    }

                    DrainDownsAndProcessFirstNonDown(ref stateChanged);
                    break;

                case ConsoleKey.W:
                    ignoreDownUntil = DateTime.MinValue;
                    downHeld = false;
                    DoHold();
                    DrainInput();
                    break;

                case ConsoleKey.Q:
                    ignoreDownUntil = DateTime.MinValue;
                    downHeld = false;
                    if (rotateCooldown <= 0)
                    {
                        RotateLeft();
                        rotateCooldown = 5;
                    }
                    DrainInput();
                    break;

                case ConsoleKey.E:
                    ignoreDownUntil = DateTime.MinValue;
                    downHeld = false;
                    if (rotateCooldown <= 0)
                    {
                        RotateRight();
                        rotateCooldown = 5;
                    }
                    DrainInput();
                    break;

                case ConsoleKey.R:
                    score += 100;
                    break;

                case ConsoleKey.Escape:
                    ignoreDownUntil = DateTime.MinValue;
                    downHeld = false;
                    stateChanged = true;
                    score = 0;
                    DrainInput();
                    break;
            }
        }

        private void DrainDownsAndProcessFirstNonDown(ref bool stateChanged)
        {
            try
            {
                while (Console.KeyAvailable)
                {
                    var kr = Console.ReadKey(true);
                    if (kr.Key == ConsoleKey.S || kr.Key == ConsoleKey.DownArrow)
                        continue;

                    ProcessKey(kr.Key, ref stateChanged);
                    return;
                }
            }
            catch (InvalidOperationException)
            {
            }
        }

        private void DoHold()
        {
            if (holdLocked || currentPiece == null)
                return;

            holdLocked = true;
            int curIndex = currentPiece.ColorIndex;

            if (holdPieceIndex == null)
            {
                holdPieceIndex = curIndex;
                SpawnNewPiece();
                return;
            }

            int swap = holdPieceIndex.Value;
            holdPieceIndex = curIndex;

            currentPiece = new Tetromino(swap);
            pieceX = 5 - currentPiece.Shape.GetLength(1) / 2;
            pieceY = 0;

            if (!IsValidPosition(pieceX, pieceY))
                gameOver = true;

            lastFallTimeUtc = DateTime.UtcNow;
        }

        private void RotateRight()
        {
            if (currentPiece == null) return;
            RotatePieceClockwise();
        }

        private static char PieceChar(int index) => index switch
        {
            0 => '│',
            1 => '▄',
            2 => '┤',
            3 => 'S',
            4 => 'Z',
            5 => 'J',
            6 => 'L',
            _ => ' '
        };

        public string GetHudText()
        {
            char hold = holdPieceIndex is int h ? PieceChar(h) : ' ';
            char next = PieceChar(nextPieceIndex);

            int last3 = Math.Abs(score) % 1000;
            string s3 = last3.ToString("D3");

            return $"{hold}-{next}{s3}";
        }

        private void RotateLeft()
        {
            if (currentPiece == null) return;

            RotatePieceClockwise();
            RotatePieceClockwise();
            RotatePieceClockwise();
        }

        private void RotatePieceClockwise()
        {
            if (currentPiece == null) return;

            currentPiece.Rotate();

            if (!IsValidPosition(pieceX, pieceY))
            {
                if (IsValidPosition(pieceX - 1, pieceY)) pieceX--;
                else if (IsValidPosition(pieceX + 1, pieceY)) pieceX++;
                else
                {
                    for (int i = 0; i < 3; i++) currentPiece.Rotate();
                }
            }
        }

        public bool IsGameOver() => gameOver;

        public string? GetGameOverCode() => gameOverCode;

        public void SetGameOverCode(string? code)
        {
            gameOverCode = code;
            codeTcs?.TrySetResult(code);
        }

        public int GetScore() => score;

        private void SpawnNewPiece()
        {
            int curIndex = nextPieceIndex;
            nextPieceIndex = rand.Next(SHAPES.Length);

            currentPiece = new Tetromino(curIndex);

            pieceX = 5 - currentPiece.Shape.GetLength(1) / 2;
            pieceY = 0;

            holdLocked = false;
            lastFallTimeUtc = DateTime.UtcNow;

            try
            {
                while (Console.KeyAvailable) Console.ReadKey(true);
            }
            catch (InvalidOperationException)
            {
            }
        }

        private bool MovePiece(int dx, int dy)
        {
            int newX = pieceX + dx;
            int newY = pieceY + dy;

            if (IsValidPosition(newX, newY))
            {
                pieceX = newX;
                pieceY = newY;
                return true;
            }

            return false;
        }

        private int PieceToDigit(int pieceIndex)
        {
            if (pieceIndex < 0 || pieceIndex > 6) return 0;
            return pieceIndex + 1;
        }

        public int GetHudInt()
        {
            int holdDigit = holdPieceIndex.HasValue ? PieceToDigit(holdPieceIndex.Value) : 0;
            int divDigit = 8;
            int nextDigit = PieceToDigit(nextPieceIndex);
            int last3 = Math.Abs(score) % 1000;

            holdDigit = Math.Clamp(holdDigit, 0, 7);
            nextDigit = Math.Clamp(nextDigit, 0, 7);

            return holdDigit * 100000 + divDigit * 10000 + nextDigit * 1000 + last3;
        }

        private static byte SegById(int segId)
        {
            if (segId < 1 || segId > 7) return 0;
            return (byte)(1 << (segId - 1));
        }

        private static byte PieceMask(int type) => type switch
        {
            6 => (byte)(SegById(1) | SegById(6) | SegById(5)),
            2 => (byte)(SegById(1) | SegById(7) | SegById(6)),
            0 => (byte)(SegById(1) | SegById(6)),
            1 => (byte)(SegById(6) | SegById(7) | SegById(4) | SegById(5)),
            4 => (byte)(SegById(3) | SegById(7) | SegById(6)),
            3 => (byte)(SegById(1) | SegById(7) | SegById(4)),
            5 => (byte)(SegById(3) | SegById(4) | SegById(5)),
            _ => (byte)0
        };

        public byte GetHoldMaskForHud()
        {
            if (!holdPieceIndex.HasValue) return 0;
            return PieceMask(holdPieceIndex.Value);
        }

        public byte GetNextMaskForHud()
        {
            return PieceMask(nextPieceIndex);
        }

        public (byte r, byte g, byte b) GetHoldColorForHud()
        {
            if (!holdPieceIndex.HasValue) return (0, 0, 0);
            Color c = PIECE_COLORS[holdPieceIndex.Value];
            return ((byte)c.R, (byte)c.G, (byte)c.B);
        }

        public (byte r, byte g, byte b) GetNextColorForHud()
        {
            Color c = PIECE_COLORS[nextPieceIndex];
            return ((byte)c.R, (byte)c.G, (byte)c.B);
        }

        private bool IsValidPosition(int x, int y)
        {
            if (currentPiece == null) return false;

            int rows = currentPiece.Shape.GetLength(0);
            int cols = currentPiece.Shape.GetLength(1);

            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    if (currentPiece.Shape[r, c] == 1)
                    {
                        int boardX = x + c;
                        int boardY = y + r;

                        if (boardX < 0 || boardX >= 10 || boardY >= 20)
                            return false;

                        if (boardY >= 0 && board[boardY, boardX] != 0)
                            return false;
                    }
                }
            }

            return true;
        }

        private void DrainInput()
        {
            try
            {
                while (Console.KeyAvailable) Console.ReadKey(true);
            }
            catch (InvalidOperationException)
            {
            }
        }

        private void LockPiece()
        {
            if (currentPiece == null) return;

            int rows = currentPiece.Shape.GetLength(0);
            int cols = currentPiece.Shape.GetLength(1);

            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    if (currentPiece.Shape[r, c] == 1)
                    {
                        int boardX = pieceX + c;
                        int boardY = pieceY + r;

                        if (boardY >= 0 && boardY < 20 && boardX >= 0 && boardX < 10)
                        {
                            board[boardY, boardX] = currentPiece.ColorIndex + 1;
                        }
                    }
                }
            }
        }

        private void ClearLinesAndApplyArduinoProgression()
        {
            int linesCleared = 0;

            for (int row = 19; row >= 0; row--)
            {
                bool fullLine = true;
                for (int col = 0; col < 10; col++)
                {
                    if (board[row, col] == 0)
                    {
                        fullLine = false;
                        break;
                    }
                }

                if (!fullLine)
                    continue;

                linesCleared++;

                for (int r = row; r > 0; r--)
                {
                    for (int c = 0; c < 10; c++)
                    {
                        board[r, c] = board[r - 1, c];
                    }
                }

                for (int c = 0; c < 10; c++)
                {
                    board[0, c] = 0;
                }

                row++;
            }

            ApplyLineClearScoreAndLevel(linesCleared);
        }

        private static int ClassicLineClearScore(int lines, int lvl)
        {
            int baseScore = lines switch
            {
                1 => 40,
                2 => 100,
                3 => 300,
                4 => 1200,
                _ => 0
            };

            return baseScore * (lvl + 1);
        }

        private void UpdateLevelOnCleared(int cleared)
        {
            if (cleared == 0)
                return;

            totalLinesCleared += cleared;

            int newLevel = totalLinesCleared / LINES_PER_LEVEL;
            if (newLevel <= level)
                return;

            level = newLevel;

            int candidate = BASE_FALL_MS;
            if (level * FALL_DECREMENT >= candidate)
            {
                fallDelayMs = MIN_FALL_MS;
            }
            else
            {
                int v = candidate - (level * FALL_DECREMENT);
                fallDelayMs = Math.Max(MIN_FALL_MS, v);
            }
        }

        private void ApplyLineClearScoreAndLevel(int cleared)
        {
            if (cleared == 0)
                return;

            score += ClassicLineClearScore(cleared, level);

            UpdateLevelOnCleared(cleared);

            int perLineDec = cleared * PER_LINE_FALL_DECREMENT_MS;
            if (perLineDec > 0)
            {
                fallDelayMs = Math.Max(MIN_FALL_MS, fallDelayMs - perLineDec);
            }

            if (SCORE_STEP_POINTS > 0)
            {
                int curStep = score / SCORE_STEP_POINTS;
                if (curStep > lastScoreSpeedStep)
                {
                    int stepsToApply = curStep - lastScoreSpeedStep;
                    int totalDec = stepsToApply * SCORE_STEP_FALL_DECREMENT_MS;
                    fallDelayMs = Math.Max(MIN_FALL_MS, fallDelayMs - totalDec);
                    lastScoreSpeedStep = curStep;
                }
            }
        }

        private int CurrentFallDelay(bool isDownHeld)
        {
            if (!isDownHeld)
                return fallDelayMs;

            int div = fallDelayMs / SOFT_DROP_DIVISOR;
            int candidate = Math.Max(SOFT_DROP_MIN_MS, div);
            return candidate < fallDelayMs ? candidate : fallDelayMs;
        }

        private void DrawBoard(IPixel[,] pixels)
        {
            for (int row = 0; row < 20; row++)
            {
                for (int col = 0; col < 10; col++)
                {
                    if (board[row, col] == 0)
                    {
                        pixels[row, col] = new Pixel(10, 10, 20);
                    }
                    else
                    {
                        Color color = PIECE_COLORS[board[row, col] - 1];
                        pixels[row, col] = new Pixel(color.R, color.G, color.B);
                    }
                }
            }

            if (currentPiece != null)
            {
                int rows = currentPiece.Shape.GetLength(0);
                int cols = currentPiece.Shape.GetLength(1);

                for (int r = 0; r < rows; r++)
                {
                    for (int c = 0; c < cols; c++)
                    {
                        if (currentPiece.Shape[r, c] == 1)
                        {
                            int boardX = pieceX + c;
                            int boardY = pieceY + r;

                            if (boardY >= 0 && boardY < 20 && boardX >= 0 && boardX < 10)
                            {
                                Color color = currentPiece.Color;
                                pixels[boardY, boardX] = new Pixel(color.R, color.G, color.B);
                            }
                        }
                    }
                }
            }
        }

        private void DrawGameOver(IPixel[,] pixels)
        {
            for (int row = 0; row < 20; row++)
            {
                for (int col = 0; col < 10; col++)
                {
                    pixels[row, col] = new Pixel(50, 0, 0);
                }
            }
        }

        public async Task<string?> WaitForGameOverCodeAsync(int timeoutMs = 3000)
        {
            if (!string.IsNullOrEmpty(gameOverCode)) return gameOverCode;
            codeTcs ??= new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);

            var completed = await Task.WhenAny(codeTcs.Task, Task.Delay(timeoutMs)).ConfigureAwait(false);
            if (completed == codeTcs.Task)
                return await codeTcs.Task.ConfigureAwait(false);

            return gameOverCode;
        }
    }
}