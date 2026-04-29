using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace PixelCatsClient
{
    public class Game1 : Game
    {
        private GraphicsDeviceManager _graphics;
        private SpriteBatch? _spriteBatch;
        private SpriteFont? _font;
        private Texture2D? _white;
        private Texture2D? _pixelCats;
        private Texture2D? _bgGradient; 
        private ScoreWatcher? _watcher;
        private readonly ConcurrentQueue<Action> _mainThreadQueue = new();
        private System.Collections.Generic.Dictionary<string, ScoreRecord[]> _topScoresByGame = new(System.StringComparer.OrdinalIgnoreCase);
        private ScoreRecord[] _topScores = Array.Empty<ScoreRecord>();
        private string _statusLine = "Starting...";

        private Color _bg = new Color(73, 17, 175);

        public Game1()
        {
            _graphics = new GraphicsDeviceManager(this);
            IsMouseVisible = true;
            Window.Title = "PixelCats Leaderboard";
            // Configure default window size
            _graphics.PreferredBackBufferWidth = 900;
            _graphics.PreferredBackBufferHeight = 600;
        }

        protected override void Initialize()
        {
            base.Initialize();
        }

        protected override void LoadContent()
        {
            _spriteBatch = new SpriteBatch(GraphicsDevice);
            _font = Content.Load<SpriteFont>("Content/DefaultFont");

            _white = new Texture2D(GraphicsDevice, 1, 1);
            _white.SetData(new[] { Color.Orange });

            _pixelCats = Content.Load<Texture2D>("Content/PixelCats");
            string watcherPath;
            try
            {
                watcherPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "shared", "latest_score.json"));
            }
            catch
            {
                watcherPath = "latest_score.json";
            }


            var topColor = Color.Plum;
            var bottomColor = Color.DarkSlateBlue;
            int vpWidth = GraphicsDevice.Viewport.Width;
            int vpHeight = Math.Max(2, GraphicsDevice.Viewport.Height);
            _bgGradient = CreateVerticalGradientTexture(GraphicsDevice, vpWidth, vpHeight, topColor, bottomColor);

            _watcher = new ScoreWatcher(watcherPath);
            _watcher.NewScoreDetected += OnNewScoreDetected;
            _watcher.Start();

            _ = RefreshTopScoresAsync();
            //_statusLine = "Listening for scores...";
        }

        protected override void UnloadContent()
        {
            _watcher?.Stop();
            _white?.Dispose();
            _bgGradient?.Dispose();
            base.UnloadContent();
        }

        private void OnNewScoreDetected(ScoreEvent ev)
        {
            // Marshal to main thread via queue so we can update UI safely
            _mainThreadQueue.Enqueue(() =>
            {
                _statusLine = $"Detected: {ev.Score} from {ev.Code} ({ev.GameName}) at {ev.Timestamp:u}";
                // Submit in background, update status when done (enqueue result)
                _ = Task.Run(async () =>
                {
                    _mainThreadQueue.Enqueue(() => _statusLine = "Submitting...");
                    var ok = await ApiClient.SubmitCodeAsync(ev.Code, ev.Score, ev.GameName);
                    _mainThreadQueue.Enqueue(() =>
                    {
                        _statusLine = ok ? "Submitted OK" : "Submit failed";
                    });

                    // Refresh leaderboard after submission
                    var scores = await ApiClient.GetTopScoresAsync(10);
                    _mainThreadQueue.Enqueue(() =>
                    {
                        _topScores = scores;
                        _statusLine += " - Leaderboard updated";
                    });
                });
            });
        }

        protected override void Update(GameTime gameTime)
        {
            // Exit on escape
            if (Keyboard.GetState().IsKeyDown(Keys.Escape))
                Exit();

            // Execute queued actions from background threads
            while (_mainThreadQueue.TryDequeue(out var action))
            {
                try { action?.Invoke(); } catch (Exception ex) { _statusLine = $"UI update error: {ex.Message}"; }
            }

            base.Update(gameTime);
        }

        protected override void Draw(GameTime gameTime)
        {
            // Ensure content is loaded
            if (_spriteBatch == null || _font == null || _white == null)
                return;

            // Draw gradient background if available, otherwise fallback to solid clear
            if (_bgGradient != null)
            {
                _spriteBatch.Begin();
                _spriteBatch.Draw(_bgGradient, new Rectangle(0, 0, GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height), Color.White);
                _spriteBatch.End();
            }
            else
            {
                GraphicsDevice.Clear(_bg);
            }

            _spriteBatch.Begin();

            var padding = 20;
           
            if (_pixelCats != null)
            {
                float maxW = 600f;
                float scale = Math.Min(1f, maxW / _pixelCats.Width);

                float extraRightInset = 160f; 
                var pos = new Vector2(
                    GraphicsDevice.Viewport.Width - (_pixelCats.Width * scale) - padding - extraRightInset,
                    5);

                _spriteBatch.Draw(_pixelCats, pos, null, Color.White, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
            }


            // Leaderboard area 
            var lbX = padding * 1.5f;
            var lbY = 210;
            _spriteBatch.DrawString(_font, "Top Scores:", new Vector2(lbX, lbY), Color.White);

            var yStart = lbY + 28;

            // Layout: three columns (left-to-right)
            float colWidth = 280f;    // pixel width per column (tweak to taste)
            float colGap = 24f;
            float col0X = lbX;
            float col1X = col0X + colWidth + colGap;
            float col2X = col1X + colWidth + colGap;

            var games = new[] { "Education", "Snake", "Tetris" };

            for (int col = 0; col < 3; col++)
            {
                var gx = games[col];
                // Column header (game name)
                _spriteBatch.DrawString(_font, gx, new Vector2(col == 0 ? col0X : col == 1 ? col1X : col2X, yStart), Color.White);

                // entries for this game
                var entries = _topScoresByGame.TryGetValue(gx, out var arr) ? arr : Array.Empty<ScoreRecord>();

                float y = yStart + 28;
                int rank = 1;
                foreach (var s in entries)
                {
                    // Rank badge-like number (just draw number)
                    var rankText = rank.ToString();
                    _spriteBatch.DrawString(_font, rankText, new Vector2(col == 0 ? col0X : col == 1 ? col1X : col2X, y), Color.Gold);

                    // Name (slightly indented)
                    var nameX = (col == 0 ? col0X : col == 1 ? col1X : col2X) + 32f;
                    var nameToDraw = TruncateToWidth($"{s.name}", colWidth - 32f - 60f);
                    _spriteBatch.DrawString(_font, nameToDraw, new Vector2(nameX, y), Color.White);

                    // Score (right-aligned within column)
                    var scoreText = s.score.ToString();
                    var scoreSize = _font.MeasureString(scoreText);
                    float scoreRight = (col == 0 ? col0X : col == 1 ? col1X : col2X) + colWidth - 8f;
                    float scoreX = scoreRight - scoreSize.X;
                    _spriteBatch.DrawString(_font, scoreText, new Vector2(nameX, y + 28), Color.Goldenrod);
                   

                    y += 72; // spacing between cards; tweak as needed to match visual spacing
                    rank++;
                }

            }

            // Status box
            _spriteBatch.DrawString(_font, _statusLine, new Vector2(padding + 8, GraphicsDevice.Viewport.Height - 60), Color.LightGray);

            // Footer
            var footer = "Press Esc to exit.";
            var footerSize = _font.MeasureString(footer);
            _spriteBatch.DrawString(_font, footer, new Vector2(padding, GraphicsDevice.Viewport.Height - 30), Color.Gray);

            _spriteBatch.End();

            base.Draw(gameTime);
        }

        private string TruncateToWidth(string text, float maxWidth)
        {
            if (_font == null) return text;
            if (_font.MeasureString(text).X <= maxWidth) return text;

            const string ellipsis = "...";
            int low = 0;
            int high = text.Length;

            while (low < high)
            {
                int mid = (low + high + 1) / 2;
                var candidate = text.Substring(0, mid) + ellipsis;
                if (_font.MeasureString(candidate).X <= maxWidth)
                    low = mid;
                else
                    high = mid - 1;
            }

            var final = text.Substring(0, Math.Max(0, low)) + ellipsis;
            return final;
        }

        private Texture2D CreateVerticalGradientTexture(GraphicsDevice graphicsDevice, int width, int height, Color topColor, Color bottomColor)
        {
            // Create a 1xheight texture and set per-row colors, then stretch when drawing
            var tex = new Texture2D(graphicsDevice, 1, height);
            var data = new Color[height];
            for (int y = 0; y < height; y++)
            {
                float t = height <= 1 ? 0f : (float)y / (height - 1);
                byte r = (byte)(topColor.R + (bottomColor.R - topColor.R) * t);
                byte g = (byte)(topColor.G + (bottomColor.G - topColor.G) * t);
                byte b = (byte)(topColor.B + (bottomColor.B - topColor.B) * t);
                byte a = (byte)(topColor.A + (bottomColor.A - topColor.A) * t);
                data[y] = new Color(r, g, b, a);
            }
            tex.SetData(data);
            return tex;
        }

        private async Task RefreshTopScoresAsync()
        {
            // The three games in the website layout — change names if your backend uses different game identifiers
            var games = new[] { "Education", "Snake", "Tetris" };

            // Fetch grouped top-3 per game (ApiClient.GetTopScoresByGamesAsync is the new method above)
            var grouped = await ApiClient.GetTopScoresByGamesAsync(games, perGame: 3, fetchLimit: 200);

            _mainThreadQueue.Enqueue(() =>
            {
                // store result for rendering
                _topScoresByGame = grouped;
                // also populate _topScores for backward compatibility if any other code uses it
                // (just flattening top scores in the order of games)
                _topScores = games.SelectMany(g => grouped.TryGetValue(g, out var arr) ? arr : Array.Empty<ScoreRecord>()).ToArray();
            });
        }
    }
}