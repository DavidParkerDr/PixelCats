#nullable enable

using PixelBoard;
using System;
using System.Drawing;
using System.Threading.Tasks;

namespace ConsoleTest.Games
{
    public class Tetris : IGame
    {
        // Unique identifier for this game.
        public string GameId { get; } = "bCbC7cpanUA";
        private int? holdPieceIndex = null;
        private bool holdLocked = false;
        private int nextPieceIndex = 0;
        private int score = 0;
        private int level = 0;  // Level for authentic scoring
        private int[,] board = new int[20, 10]; // 0 = empty, 1+ = filled
        private Tetromino? currentPiece;
        private int pieceX;
        private int pieceY;
        private Random rand = new Random();
        private int frameCounter = 0;
        private int dropSpeed = 10; // Frames before piece drops
        private bool gameOver = false;
        private int manualDropCooldown = 0;
        private int rotateCooldown = 0;  // Rotation cooldown
        private string? gameOverCode;  // Game over code
        private DateTime ignoreDownUntil = DateTime.MinValue; // suppress repeated Down presses for a short time

        private TaskCompletionSource<string?>? codeTcs;

        // Tetromino shapes (7 classic pieces)
        private static readonly int[][,] SHAPES = new int[][,]
        {
            // I piece
            new int[,] { {1,1,1,1} },
            
            // O piece
            new int[,] { {1,1}, {1,1} },
            
            // T piece
            new int[,] { {0,1,0}, {1,1,1} },
            
            // S piece
            new int[,] { {0,1,1}, {1,1,0} },
            
            // Z piece
            new int[,] { {1,1,0}, {0,1,1} },
            
            // J piece
            new int[,] { {1,0,0}, {1,1,1} },
            
            // L piece
            new int[,] { {0,0,1}, {1,1,1} }
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
            board = new int[20, 10];
            gameOver = false;
            frameCounter = 0;
            manualDropCooldown = 0;
            rotateCooldown = 0;
            gameOverCode = null;  // Reset code
            codeTcs = null;
            holdPieceIndex = null;
            holdLocked = false;
            nextPieceIndex = rand.Next(SHAPES.Length);
            SpawnNewPiece();
        }

        public void DrawTitle(IPixel[,] pixels)
        {
            // Purple background with "T" shape
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

            frameCounter++;

            // Decrease manual drop cooldown
            if (manualDropCooldown > 0)
            {
                manualDropCooldown--;
            }

            // Decrease rotate cooldown
            if (rotateCooldown > 0)
            {
                rotateCooldown--;
            }

            // Auto-drop piece
            if (frameCounter >= dropSpeed)
            {
                frameCounter = 0;
                if (!MovePiece(0, 1))
                {
                    // Piece can't move down, lock it
                    LockPiece();
                    ClearLines();
                    SpawnNewPiece();

                    // Check game over
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

            // Dispatch to shared processor which can also consume extra queued keys when necessary
            ProcessKey(key, ref stateChanged);
        }

        // Central key processor so we can consume queued keys (e.g., drop autorepeat) and immediately handle a subsequent non-drop key
        private void ProcessKey(ConsoleKey key, ref bool stateChanged)
        {
            switch (key)
            {
                case ConsoleKey.A:
                case ConsoleKey.LeftArrow:
                    // pressing any horizontal input clears Down suppression so movement is responsive
                    ignoreDownUntil = DateTime.MinValue;
                    MovePiece(-1, 0);
                    DrainInput();
                    break;

                case ConsoleKey.D:
                case ConsoleKey.RightArrow:
                    ignoreDownUntil = DateTime.MinValue;
                    MovePiece(1, 0);
                    DrainInput();
                    break;

                case ConsoleKey.S:
                case ConsoleKey.DownArrow:
                    // If we're in a short suppression window, drain further Down repeats and process the next non-Down key immediately (if any).
                    if (DateTime.UtcNow < ignoreDownUntil)
                    {
                        DrainDownsAndProcessFirstNonDown(ref stateChanged);
                        break;
                    }

                    if (manualDropCooldown <= 0)
                    {
                        if (MovePiece(0, 1))
                        {
                            score += 1;
                        }
                        else
                        {
                            LockPiece();
                            ClearLines();
                            SpawnNewPiece();
                            if (!IsValidPosition(pieceX, pieceY))
                                gameOver = true;
                        }
                        manualDropCooldown = 2; // small cooldown to reduce OS repeat sensitivity
                    }
                    else
                    {
                        // start a short suppression window so repeated OS events are ignored instead of queued
                        ignoreDownUntil = DateTime.UtcNow.AddMilliseconds(200);
                    }

                    // After handling one Down event, aggressively collapse any immediate Down repeats in the OS buffer
                    // and, if present, handle the first queued non-Down key right away so it doesn't get stuck behind repeats.
                    DrainDownsAndProcessFirstNonDown(ref stateChanged);
                    break;

                case ConsoleKey.W: // HOLD
                    ignoreDownUntil = DateTime.MinValue;
                    DoHold();
                    DrainInput();
                    break;

                case ConsoleKey.Q: // Rotate left
                    ignoreDownUntil = DateTime.MinValue;
                    if (rotateCooldown <= 0)
                    {
                        RotateLeft();
                        rotateCooldown = 5;
                    }
                    DrainInput();
                    break;

                case ConsoleKey.E: // Rotate right
                    ignoreDownUntil = DateTime.MinValue;
                    if (rotateCooldown <= 0)
                    {
                        RotateRight();
                        rotateCooldown = 5;
                    }
                    DrainInput();
                    break;
                case ConsoleKey.R:
                    // debug add 100 points
                    score += 100;
                    break;

                case ConsoleKey.Escape:
                    ignoreDownUntil = DateTime.MinValue;
                    stateChanged = true;
                    score = 0; // reset score so it doesn't carry over if we return to title and start a new game
                    DrainInput();
                    break;
            }
        }

        // Drain consecutive Down/S repeats from the Console buffer and immediately handle the first non-Down key (if any).
        private void DrainDownsAndProcessFirstNonDown(ref bool stateChanged)
        {
            try
            {
                while (Console.KeyAvailable)
                {
                    var kr = Console.ReadKey(true);
                    if (kr.Key == ConsoleKey.S || kr.Key == ConsoleKey.DownArrow)
                        continue; // drop repeat - skip

                    // Found a non-Down key queued after repeats: process it right away.
                    ProcessKey(kr.Key, ref stateChanged);
                    return;
                }
            }
            catch (InvalidOperationException) { }
        }
        private void DoHold()
        {
            if (holdLocked) return;
            if (currentPiece == null) return;

            holdLocked = true;

            int curIndex = currentPiece.ColorIndex;

            if (holdPieceIndex == null)
            {
                holdPieceIndex = curIndex;
                SpawnNewPiece(); // take the next piece
                return;
            }

            int swap = holdPieceIndex.Value;
            holdPieceIndex = curIndex;

            currentPiece = new Tetromino(swap);
            pieceX = 5 - currentPiece.Shape.GetLength(1) / 2;
            pieceY = 0;

            if (!IsValidPosition(pieceX, pieceY))
                gameOver = true;
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

            // digit0=hold, digit1=divider, digit2=next, digit3-5=score
            return $"{hold}-{next}{s3}";
        }
        private void RotateLeft()
        {
            if (currentPiece == null) return;

            // Rotate CCW = rotate CW 3 times
            RotatePieceClockwise();
            RotatePieceClockwise();
            RotatePieceClockwise();
        }

        private void RotatePieceClockwise()
        {
            if (currentPiece == null) return;

            currentPiece.Rotate();

            // same kick logic you already had
            if (!IsValidPosition(pieceX, pieceY))
            {
                if (IsValidPosition(pieceX - 1, pieceY)) pieceX--;
                else if (IsValidPosition(pieceX + 1, pieceY)) pieceX++;
                else
                {
                    // rotate back
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
            // If first spawn, initialize next

            int curIndex = nextPieceIndex;
            nextPieceIndex = rand.Next(SHAPES.Length);

            currentPiece = new Tetromino(curIndex);

            pieceX = 5 - currentPiece.Shape.GetLength(1) / 2;
            pieceY = 0;
            frameCounter = 0;

            holdLocked = false;

            try
            {
                while (Console.KeyAvailable) Console.ReadKey(true);
            }
            catch (InvalidOperationException) { }
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
            // I=1..L=7
            if (pieceIndex < 0 || pieceIndex > 6) return 0;
            return pieceIndex + 1;
        }

        public int GetHudInt()
        {
            int holdDigit = holdPieceIndex.HasValue ? PieceToDigit(holdPieceIndex.Value) : 0;
            int divDigit = 8;
            int nextDigit = PieceToDigit(nextPieceIndex);
            int last3 = Math.Abs(score) % 1000;

            // enforce bounds
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
            6 => (byte)(SegById(1) | SegById(6) | SegById(5)),                 // L
            2 => (byte)(SegById(1) | SegById(7) | SegById(6)),                 // T
            0 => (byte)(SegById(1) | SegById(6)),                              // I
            1 => (byte)(SegById(6) | SegById(7) | SegById(4) | SegById(5)),    // O
            4 => (byte)(SegById(3) | SegById(7) | SegById(6)), // Z ("2")
            3 => (byte) (SegById(1) | SegById(7) | SegById(4)), // S ("5")
            5 => (byte)(SegById(3) | SegById(4) | SegById(5)),                 // J
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

                        // Check boundaries
                        if (boardX < 0 || boardX >= 10 || boardY >= 20)
                            return false;

                        // Check collision with existing blocks (allow spawning above board)
                        if (boardY >= 0 && board[boardY, boardX] != 0)
                            return false;
                    }
                }
            }
            return true;
        }

        // Drain any pending console input (used to discard repeated Down key repeats)
        private void DrainInput()
        {
            try
            {
                while (Console.KeyAvailable) Console.ReadKey(true);
            }
            catch (InvalidOperationException) { }
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

        private void ClearLines()
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

                if (fullLine)
                {
                    linesCleared++;
                    // Move all rows above down
                    for (int r = row; r > 0; r--)
                    {
                        for (int c = 0; c < 10; c++)
                        {
                            board[r, c] = board[r - 1, c];
                        }
                    }
                    // Clear top row
                    for (int c = 0; c < 10; c++)
                    {
                        board[0, c] = 0;
                    }
                    row++; // Check this row again
                }
            }

            // Classic Tetris scoring (NES style)
            if (linesCleared > 0)
            {
                int points = linesCleared switch
                {
                    1 => 40,      // Single
                    2 => 100,     // Double
                    3 => 300,     // Triple
                    4 => 1200,    // Tetris!   
                    _ => 0
                };
                score += points * (level + 1);
            }
        }

        private void DrawBoard(IPixel[,] pixels)
        {
            // Draw background and locked pieces
            for (int row = 0; row < 20; row++)
            {
                for (int col = 0; col < 10; col++)
                {
                    if (board[row, col] == 0)
                    {
                        // Empty cell - dark background
                        pixels[row, col] = new Pixel(10, 10, 20);
                    }
                    else
                    {
                        // Locked piece
                        Color color = PIECE_COLORS[board[row, col] - 1];
                        pixels[row, col] = new Pixel(color.R, color.G, color.B);
                    }
                }
            }

            // Draw current falling piece
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
            // Flash red
            for (int row = 0; row < 20; row++)
            {
                for (int col = 0; col < 10; col++)
                {
                    pixels[row, col] = new Pixel(50, 0, 0);
                }
            }
        }

        /// <summary>
        /// Awaitable helper that waits up to <paramref name="timeoutMs"/> for the server-provided code.
        /// </summary>
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