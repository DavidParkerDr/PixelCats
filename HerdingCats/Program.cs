using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Microsoft.Extensions.Configuration;
using PixelBoard;

namespace HerdingCats
{
    class Program
    {
        public static IConfiguration _config = null;

        public enum State { Title, Playing, GameOver };
        public static State state = State.Playing;
        public static IPixel[,] board = new IPixel[20, 10];
        public static IPixel[,] background = new IPixel[20, 10];
        public static IPixel[,] title = null;
        public static IPixel[,] gameOver = null;
        public static Random rng = new Random();


        public static IPixel[] brightRainbow = {
                new Pixel(230, 0, 2), new Pixel(255, 106, 0),
                new Pixel(252, 154, 3), new Pixel(253, 220, 89),
                new Pixel(162, 207, 0), new Pixel(35, 169, 74),
                new Pixel(35, 169, 204), new Pixel(2, 82, 141),
                new Pixel(151, 14, 128), new Pixel(236, 19, 132) };

        public static IPixel[] dullRainbow =
        {
                new Pixel(127, 0, 1), new Pixel(140, 58, 0),
                new Pixel(139, 85, 2), new Pixel(139, 121, 49),
                new Pixel(89, 114, 0), new Pixel(19, 93, 41),
                new Pixel(19, 93, 112), new Pixel(1, 45, 78),
                new Pixel(83, 8, 70), new Pixel(130, 10, 73)
        };

        public static IPixel[] dullerRainbow =
        {
                new Pixel(44, 0, 0), new Pixel(49, 20, 0),
                new Pixel(49, 30, 1), new Pixel(49, 42, 17),
                new Pixel(31, 40, 0), new Pixel(7, 33, 14),
                new Pixel(7, 33, 39), new Pixel(0, 16, 27),
                new Pixel(29, 3, 24), new Pixel(45, 4, 26)
        };

        static int score = 0;

        public static int[] cats = { 1, 1, 1, 1, 0, 1, 1, 1, 1, 1 };

        public class Coord
        {
            public int Row;
            public int Col;
            public Pixel Colour;
        }

        public class Bat
        {
            public int Col;
            public int shootFrameDelay;
            public int framesTillShoot;

            public Bat()
            {
                shootFrameDelay = 10;
                Col = 5;
            }
        }

        public static Bat bat = new Bat();

        public class Laser
        {
            public Coord[] Bolt;

            public Laser(int pCol)
            {
                Bolt = new Coord[4];

                for (int i = 0; i < Bolt.Length; i++)
                {
                    Bolt[i] = new Coord();
                    Bolt[i].Col = pCol;
                    Bolt[i].Row = board.GetLength(0) - 3 + i;
                }

                Bolt[0].Colour = new Pixel(255, 255, 255);
                Bolt[1].Colour = new Pixel(200, 200, 225);
                Bolt[2].Colour = new Pixel(150, 150, 225);
                Bolt[3].Colour = new Pixel(75, 75, 255);
            }
        }

        public static List<Coord> Falling = new List<Coord>();

        public static List<Laser> Lasers = new List<Laser>();

        public class FrameTimer
        {
            public int frameDelay;
            public int timeTillThing;

            public bool DoThing()
            {
                timeTillThing--;
                if(timeTillThing <= 0)
                {
                    timeTillThing = frameDelay;
                    return true;
                }
                return false;
            }

            public void ModifyPeriod(int pInt)
            {
                frameDelay += pInt;
            }

            public FrameTimer(int pDelay)
            {
                frameDelay = pDelay;
                timeTillThing = pDelay;
            }
        }

        static FrameTimer spawnTimer = new FrameTimer(45);
        static FrameTimer dropTimer = new FrameTimer(30);
        static FrameTimer catTimer = new FrameTimer(60);

        #region
        
        // https://stackoverflow.com/questions/1335426/is-there-a-built-in-c-net-system-api-for-hsv-to-rgb

        public static void HsvToRgb(double h, double S, double V, out int r, out int g, out int b)
        {
            double H = h;
            while (H < 0) { H += 360; };
            while (H >= 360) { H -= 360; };
            double R, G, B;
            if (V <= 0)
            { R = G = B = 0; }
            else if (S <= 0)
            {
                R = G = B = V;
            }
            else
            {
                double hf = H / 60.0;
                int i = (int)Math.Floor(hf);
                double f = hf - i;
                double pv = V * (1 - S);
                double qv = V * (1 - S * f);
                double tv = V * (1 - S * (1 - f));
                switch (i)
                {

                    // Red is the dominant color

                    case 0:
                        R = V;
                        G = tv;
                        B = pv;
                        break;

                    // Green is the dominant color

                    case 1:
                        R = qv;
                        G = V;
                        B = pv;
                        break;
                    case 2:
                        R = pv;
                        G = V;
                        B = tv;
                        break;

                    // Blue is the dominant color

                    case 3:
                        R = pv;
                        G = qv;
                        B = V;
                        break;
                    case 4:
                        R = tv;
                        G = pv;
                        B = V;
                        break;

                    // Red is the dominant color

                    case 5:
                        R = V;
                        G = pv;
                        B = qv;
                        break;

                    // Just in case we overshoot on our math by a little, we put these here. Since its a switch it won't slow us down at all to put these here.

                    case 6:
                        R = V;
                        G = tv;
                        B = pv;
                        break;
                    case -1:
                        R = V;
                        G = pv;
                        B = qv;
                        break;

                    // The color is not defined, we should throw an error.

                    default:
                        //LFATAL("i Value error in Pixel conversion, Value is %d", i);
                        R = G = B = V; // Just pretend its black/white
                        break;
                }
            }
            r = Clamp((int)(R * 255.0));
            g = Clamp((int)(G * 255.0));
            b = Clamp((int)(B * 255.0));
        }

        /// <summary>
        /// Clamp a value to 0-255
        /// </summary>
        static int Clamp(int i)
        {
            if (i < 0) return 0;
            if (i > 255) return 255;
            return i;
        }

        #endregion

        static void UpdateBackground()
        {
            for (int i = 0; i < board.GetLength(0); i++)
            {
                for (int j = 0; j < board.GetLength(1); j++)
                {
                    board[i, j] = background[i, j];
                }
            }
        }

        static void UpdateCats()
        {
            if(catTimer.DoThing())
            {
                for (int i = 0; i < cats.Length; i++)
                {
                    if (cats[i] != 0)
                    {
                        int move = rng.Next(3);
                        switch (move)
                        {
                            case 1:
                                if (i > 0 && cats[i - 1] == 0)
                                {
                                    cats[i - 1] = 1;
                                    cats[i] = 0;
                                }
                                break;
                            case 2:
                                if (i != 9 && cats[i + 1] == 0)
                                {
                                    cats[i + 1] = 1;
                                    cats[i] = 0;
                                }
                                break;
                        }
                    }
                }
            }

            for (int i = 0; i < cats.Length; i++)
            {
                if (cats[i] == 1)
                {
                    board[board.GetLength(0) - 1, i] = new Pixel(0, 255, 0);
                }
                else
                {
                    board[board.GetLength(0) - 1, i] = new Pixel(0, 0, 0);
                }
            }
        }

        static void UpdateBat()
        {
            int batRow = board.GetLength(0) - 2;

            bat.framesTillShoot = bat.framesTillShoot > 0 ? bat.framesTillShoot - 1 : 0;

            while (Console.KeyAvailable)
            {
                char ch = Console.ReadKey(true).KeyChar;

                switch (ch)
                {
                    case 'a':
                        board[batRow, bat.Col] = background[batRow, bat.Col];
                        bat.Col = bat.Col != 0 ? bat.Col - 1 : 0;
                        //board[batRow, bat.Col] = new Pixel(255, 255, 255);
                        break;
                    case 'd':
                        board[batRow, bat.Col] = background[batRow, bat.Col];
                        bat.Col = bat.Col != 9 ? bat.Col + 1 : 9;
                        //board[batRow, bat.Col] = new Pixel(255, 255, 255);
                        break;
                    case 's':
                        if (bat.framesTillShoot <= 0)
                        {
                            bat.framesTillShoot = bat.shootFrameDelay;
                            Laser laser = new Laser(bat.Col);
                            Lasers.Add(laser);
                        }

                        break;
                }
            }
            board[batRow, bat.Col] = new Pixel(255, 255, 255);
        }

        static void UpdateLasers()
        {
            foreach (Laser l in Lasers)
            {
                if (l.Bolt[3].Row < board.GetLength(0) - 2 && l.Bolt[3].Row >= 0)
                {
                    board[l.Bolt[3].Row, l.Bolt[3].Col] = background[l.Bolt[3].Row, l.Bolt[3].Col];
                }

                for (int i = 0; i < l.Bolt.Length; i++)
                {
                    l.Bolt[i].Row--;
                }
            }


            foreach (Laser l in Lasers)
            {
                foreach (Coord c in l.Bolt)
                {
                    if (c.Row < board.GetLength(0) - 2 && c.Row >= 0)
                    {
                        board[c.Row, c.Col] = c.Colour;
                    }
                }
            }

            CheckLaserCollisions();
        }

        private static void CheckLaserCollisions()
        {
            for (int laserIndex = Lasers.Count - 1; laserIndex >= 0; laserIndex--)
            {
                for (int fallerIndex = Falling.Count - 1; fallerIndex >= 0; fallerIndex--)
                {
                    if (Lasers[laserIndex].Bolt[0].Col == Falling[fallerIndex].Col &&
                        Lasers[laserIndex].Bolt[0].Row == Falling[fallerIndex].Row)
                    {
                        score++;
                        if (score % 2 == 0)
                        {
                            if (rng.Next(2) == 1)
                            {
                                spawnTimer.ModifyPeriod(-1);
                            }
                            else
                            {
                                dropTimer.ModifyPeriod(-1);
                            }
                        }
                        for (int i = 0; i < Lasers[laserIndex].Bolt.Length; i++)
                        {
                            Laser l = Lasers[laserIndex];
                            if (l.Bolt[i].Row < board.GetLength(0) && l.Bolt[i].Row >= 0)
                            {
                                board[l.Bolt[i].Row, l.Bolt[i].Col] = background[l.Bolt[i].Row, l.Bolt[i].Col];
                            }
                        }
                        Lasers.RemoveAt(laserIndex);
                        Falling.RemoveAt(fallerIndex);
                        break;
                    }
                }
            }
        }

        static void UpdateFallingStuff()
        {
            if (dropTimer.DoThing())
            {
                for (int i = 0; i < Falling.Count; i++)
                {
                    board[Falling[i].Row, Falling[i].Col] = background[Falling[i].Row, Falling[i].Col];
                    Falling[i].Row++;
                }

                CheckLaserCollisions();
                for (int i = Falling.Count - 1; i >= 0; i--)
                {
                    if (Falling[i].Row == board.GetLength(0))
                    {
                        if (cats[Falling[i].Col] == 1)
                        {
                            cats[Falling[i].Col] = 0;
                        }

                        Falling.RemoveAt(i);
                    }
                }

                foreach (Coord c in Falling)
                {
                    board[c.Row, c.Col] = c.Colour;
                }
            }

            if (spawnTimer.DoThing())
            {
                Coord faller = new Coord();
                faller.Col = rng.Next(0, board.GetLength(1));
                faller.Row = 0;
                int i = rng.Next(9);
                while (Math.Abs(i - faller.Col) < 2)
                {
                    i = rng.Next(9);
                }
                faller.Colour = (Pixel)brightRainbow[i];
                Falling.Add(faller);
                board[faller.Row, faller.Col] = faller.Colour;
            }
        }

        static FrameTimer titleScrollTimer = new FrameTimer(3000);

        static int titleOffset = 0;

        static void ReadBMP(string pFileName, ref IPixel[,] array)
        {
            StreamReader reader = new StreamReader(pFileName);

            string line = reader.ReadLine();
            string[] values = line.Split(',');
            
            int w = int.Parse(values[0]);
            int h = int.Parse(values[1]);

            array = new IPixel[w, h];

            for(int i = 0; i < array.GetLength(1); i++)
            {
                for(int j = 0; j < array.GetLength(0); j++)
                {
                    values = reader.ReadLine().Split(',');
                    array[j, i] = new Pixel(byte.Parse(values[0]), byte.Parse(values[1]), byte.Parse(values[2]));
                }
            }

            reader.Close();
        }

        static void UpdateTitle()
        {
            if (titleScrollTimer.DoThing())
            {
                titleOffset += 1;

                if (titleOffset > title.GetLength(0) - 10)
                {
                    titleOffset = 0;
                }
            }

            for (int i = 0; i < 10; i++)
            {
                for (int j = 0; j < 20; j++)
                {
                    board[j, i] = title[i + titleOffset, j];
                }
            }

            while (Console.KeyAvailable)
            {
                char ch = Console.ReadKey(true).KeyChar;

                switch (ch)
                {
                    case 'a':
                    case 'd':
                    case 's':
                        UpdateBackground();
                        board[board.GetLength(0) - 2, bat.Col] = new Pixel(255, 255, 255);
                        state = State.Playing;
                        break;
                }
            }
        }

        public static void UpdateGameOver()
        {
            if (titleScrollTimer.DoThing())
            {
                titleOffset += 1;

                if (titleOffset > gameOver.GetLength(0) - 10)
                {
                    titleOffset = 0;
                    titleScrollTimer = new FrameTimer(3000);
                    state = State.Title;
                }
            }

            for (int i = 0; i < 10; i++)
            {
                for (int j = 0; j < 20; j++)
                {
                    board[j, i] = gameOver[i + titleOffset, j];
                }
            }

            while (Console.KeyAvailable)
            {
                char ch = Console.ReadKey(true).KeyChar;

                switch (ch)
                {
                    case 'a':
                    case 'd':
                    case 's':
                        titleOffset = 0;
                        titleScrollTimer = new FrameTimer(3000);
                        state = State.Title;
                        break;
                }
            }
        }

        static void Main(string[] args)
        {
            _config = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .Build();
            var useEmulator = bool.Parse(_config.GetValue(Type.GetType("System.String"), "UseEmulator").ToString());

            ReadBMP("hello.txt", ref title);
            ReadBMP("gameOver.txt", ref gameOver);

            for (int i = 0; i < background.GetLength(0); i++)
            {
                for (int j = 0; j < background.GetLength(1); j++)
                {
                    if (i < background.GetLength(0) - 2)
                    {
                        background[i, j] = brightRainbow[j];
                        board[i, j] = brightRainbow[j];
                    }
                    else
                    {
                        background[i, j] = new Pixel(0,0,0);
                        board[i, j] = new Pixel(0, 0, 0);
                    }                
                }
            }

            IDisplay display = new ConsoleDisplay();
            if (!useEmulator)
            {
                display = new ArduinoDisplay();
            }

            state = State.Title;

            while (true)
            {
                if (state == State.Playing)
                {
                    UpdateCats();
                    UpdateLasers();
                    UpdateFallingStuff();
                    UpdateBat();

                    Thread.Sleep(50);

                    int catsAlive = 0;

                    foreach(int c in cats)
                    {
                        if(c == 1) { catsAlive++; }
                    }

                    display.DisplayInts(catsAlive, score);

                    bool allCatsDead = true;

                    foreach (int i in cats)
                    {
                        if (i == 1)
                        {
                            allCatsDead = false;
                            break;
                        }
                    }

                    if (allCatsDead)
                    {
                        state = State.GameOver;
                    }
                }
                else if(state == State.Title)
                {
                    UpdateTitle();
                }
                else if(state == State.GameOver)
                {
                    UpdateGameOver();
                }
                display.Draw(board);
            }
        }
    }
}