#nullable enable

using Microsoft.Extensions.Configuration;
using PixelBoard;
using System;
using System.Collections.Generic;
using System.Threading;
using System.IO;
using System.Text.Json;
using ConsoleTest.Games;
using System.Threading.Tasks;

namespace SnakeGame
{
    internal class Program
    {
        public enum State { Title, Playing, GameOver }
        public static State state = State.Title;

        public enum GameChoiceState { Snake, Tetris, Education }
        public static GameChoiceState game = GameChoiceState.Snake;

        // Fix CS8625: don't assign null to non-nullable
        public static IConfiguration? _config;

        private static readonly IPixel[,] pixels = new IPixel[20, 10];

        // Fix CS8618: these are initialized in Main, so make them nullable (or initialize here)
        private static Dictionary<GameChoiceState, IGame>? games;
        private static IGame? currentGame;

        private static async Task Main(string[] args)
        {
            // Fix filename typo: "appsettings. json" -> "appsettings.json"
            _config = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables()
                .Build();

            bool useEmulator = true;
            var useEmulatorValue = _config["UseEmulator"];
            if (!string.IsNullOrEmpty(useEmulatorValue))
            {
                _ = bool.TryParse(useEmulatorValue, out useEmulator);
            }
            var baseUrl = _config["Leaderboard:BaseUrl"] ?? "http://127.0.0.1:3000";

            string GetSecretForGame(GameChoiceState g) => g switch
            {
                GameChoiceState.Snake => _config["LEADERBOARD_HMAC_SNAKE"] ?? "",
                GameChoiceState.Tetris => _config["LEADERBOARD_HMAC_TETRIS"] ?? "",
                GameChoiceState.Education => _config["LEADERBOARD_HMAC_EDU"] ?? "",
                _ => ""
            };

            IDisplay emulatorDisplay = new ConsoleDisplay();
            // IDisplay hardwareDisplay = new ArduinoDisplay(); // Uncomment when hardware display is available

            // Initialize games
            games = new Dictionary<GameChoiceState, IGame>
            {
                { GameChoiceState.Snake, new Snake() },
                { GameChoiceState.Tetris, new Tetris() },
                { GameChoiceState.Education, new Education() }
            };

            currentGame = games[game];

            // Score export setup
            int lastExportedScore = int.MinValue;
            string sharedDir;
            try
            {
                sharedDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "shared"));
                Directory.CreateDirectory(sharedDir);
            }
            catch
            {
                sharedDir = Path.Combine(AppContext.BaseDirectory, "shared");
                Directory.CreateDirectory(sharedDir);
            }

            string scoreFilePath = Path.Combine(sharedDir, "latest_score.json");
            Console.WriteLine($"[ConsoleTest] Score file: {scoreFilePath}");

            // Ensure the file exists at startup with the current game's score (safe fallback to 0)
            try
            {
                // Fix CS8604: currentGame is nullable, so guard it once and keep a non-null local
                var gameLocal = currentGame ?? throw new InvalidOperationException("Current game was not initialized.");

                int initialScore;
                try { initialScore = gameLocal.GetScore(); } catch { initialScore = 0; }

                // Write initial file (no code - server will generate and store it)
                WriteScoreFileAtomic(scoreFilePath, initialScore, gameLocal, state: "Startup");

                lastExportedScore = initialScore;
                Console.WriteLine($"[ConsoleTest] Initialized score file '{scoreFilePath}' with score {initialScore}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ConsoleTest] Failed to initialize score file '{scoreFilePath}': {ex.GetType().Name}: {ex.Message}");
            }

            State previousState = state;
            state = State.Title;

            // Fix CS8600: declare as nullable
            string? lastGameOverCode = null;

            while (true)
            {
                // Fix CS8600/CS8602 patterns by working with non-null locals inside the loop
                var gameLocal = currentGame ?? throw new InvalidOperationException("Current game was not initialized.");

                if (state == State.Title)
                {
                    if (Console.KeyAvailable)
                    {
                        var key = Console.ReadKey(true).Key;

                        switch (key)
                        {
                            case ConsoleKey.S:
                                state = State.Playing;
                                gameLocal.Initialize(pixels);
                                lastGameOverCode = null;
                                gameLocal.SetGameOverCode(null);
                                break;

                            case ConsoleKey.A:
                                game = (GameChoiceState)(((int)game + 2) % 3);
                                currentGame = games![game];
                                break;

                            case ConsoleKey.D:
                                game = (GameChoiceState)(((int)game + 1) % 3);
                                currentGame = games![game];
                                break;
                        }
                    }

                    gameLocal.DrawTitle(pixels);
                }

                if (state == State.Playing)
                {
                    Thread.Sleep(100);

                    if (Console.KeyAvailable)
                    {
                        var key = Console.ReadKey(true).Key;
                        bool stateChanged = false;
                        gameLocal.HandleInput(key, ref stateChanged);

                        if (stateChanged)
                        {
                            state = State.Title;
                        }
                    }

                    gameLocal.Update(pixels);

                    if (gameLocal.IsGameOver())
                    {
                        state = State.GameOver;
                    }
                }

                if (state == State.GameOver)
                {
                    // keep updating/drawing while in GameOver visual state
                    gameLocal.Update(pixels);

                    // Write final score first to avoid race with server-side generation.
                    int finalScore = gameLocal.GetScore();
                    try
                    {
                        string gameCode = gameLocal.GameId;

                        string secret = GetSecretForGame(game);
                        if (string.IsNullOrWhiteSpace(secret))
                            throw new Exception($"Missing env var for {game}. Set LEADERBOARD_HMAC_{game.ToString().ToUpperInvariant()}");

                        var leaderboard = new ConsoleTest.LeaderboardClient(baseUrl, secret);
                        string claimCode = await leaderboard.MintClaimCodeAsync(gameCode, finalScore);

                        lastGameOverCode = claimCode;
                        gameLocal.SetGameOverCode(claimCode);

                        WriteScoreFileAtomic(scoreFilePath, finalScore, gameLocal, state: "GameOver", code: claimCode);
                        Console.WriteLine($"[ConsoleTest] Final score: {finalScore}, server code: {claimCode}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[ConsoleTest] Failed to mint claim code: {ex.Message}");
                    }

                    Thread.Sleep(1000);

                    Console.WriteLine($"[ConsoleTest] Returning to title screen with code {lastGameOverCode}");
                    state = State.Title;
                }

                if (previousState != state)
                {
                    Console.WriteLine($"[ConsoleTest] State changed {previousState} -> {state}");
                    previousState = state;
                }

                if (!string.IsNullOrEmpty(lastGameOverCode) && state == State.Title)
                {
                    if (int.TryParse(lastGameOverCode, out int codeInt))
                    {
                        emulatorDisplay.DisplayInt(codeInt);
                        // hardwareDisplay.DisplayInt(codeInt);
                    }
                }
                else
                {
                    if (gameLocal is ConsoleTest.Games.Tetris tetris && state == State.Playing)
                    {
                        // Emulator 
                        emulatorDisplay.DisplayText(tetris.GetHudText());

                        //// Hardware
                        //if (hardwareDisplay is PixelBoard.ArduinoDisplay arduino)
                        //{
                        //    byte dividerMask = 1 << (7 - 1);

                        //    byte holdMask = tetris.GetHoldMaskForHud();
                        //    byte nextMask = tetris.GetNextMaskForHud();

                        //    var holdCol = tetris.GetHoldColorForHud();
                        //    var divCol = ((byte)60, (byte)60, (byte)60);
                        //    var nextCol = tetris.GetNextColorForHud();

                        //    arduino.Display7SegHud(
                        //        holdMask,
                        //        dividerMask,
                        //        nextMask,
                        //        holdCol,
                        //        divCol,
                        //        nextCol,
                        //        tetris.GetScore()
                        //    );
                        //}
                        //else
                        //{
                        //    // fallback if not arduino
                        //    //hardwareDisplay.DisplayInt(tetris.GetScore());
                        //}
                    }
                    else
                    {
                        emulatorDisplay.DisplayInt(gameLocal.GetScore());
                        ///hardwareDisplay.DisplayInt(gameLocal.GetScore());
                    }
                }

                emulatorDisplay.Draw(pixels);
                // hardwareDisplay.Draw(pixels);
            }
        }

        // Write score file WITHOUT the code — server stores the code when it receives the score.
        // If 'code' is provided, include it in the JSON (written atomically).
        private static void WriteScoreFileAtomic(string path, int score, IGame? game, string? state = null, string? code = null)
        {
            // Use a dictionary so we only include the 'code' property when it is non-null.
            var payload = new Dictionary<string, object?>
            {
                ["score"] = score,
                ["state"] = state,
                ["gameName"] = game?.ToString() ?? string.Empty,
                ["timestamp"] = DateTimeOffset.UtcNow.ToString("O")
            };

            if (!string.IsNullOrEmpty(code))
            {
                payload["code"] = code;
            }

            var json = JsonSerializer.Serialize(payload);

            var dir = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(dir))
                dir = AppContext.BaseDirectory;

            Directory.CreateDirectory(dir);

            var tmp = Path.Combine(dir, $"{Guid.NewGuid():N}.tmp");
            File.WriteAllText(tmp, json);

            try
            {
                File.Copy(tmp, path, overwrite: true);
            }
            finally
            {
                try { File.Delete(tmp); } catch { }
            }
        }
    }
}
