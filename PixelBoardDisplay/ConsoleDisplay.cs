using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Timers;

namespace PixelBoard
{
    public class ConsoleDisplay : IDisplay
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool SetConsoleMode(IntPtr hConsoleHandle, int mode);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GetConsoleMode(IntPtr handle, out int mode);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr GetStdHandle(int handle);

        private DisplayHelper dh = new DisplayHelper();

        private bool refreshing = false;       

        /// <summary>
        /// Construct a display with the default size (20x10)
        /// </summary>
        public ConsoleDisplay()
        {
            //this.dh.SetFramerate(50);
            initBoard();
            ElapsedEventHandler dtfr = drawToFramerate;
            this.dh.MakeTimer(dtfr);
        }

        /// <summary>
        /// Construct a display with a custom size and optional framerate
        /// </summary>
        public ConsoleDisplay(sbyte height, sbyte width, sbyte framerate = 60)
        {
            this.dh.SetFramerate(framerate);
            this.dh.SetSize(height, width);
            initBoard();

            ElapsedEventHandler dtfr = drawToFramerate;
            this.dh.MakeTimer(dtfr);
        }
        public void DrawBatch(IEnumerable<ILocatedPixel> pixels)
        {
            foreach (var pixel in pixels)
            {
                Draw(pixel);
            }
        }
        private void initBoard()
        {
            this.dh.currentBoard = new Pixel[this.dh.height, this.dh.width];
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("000000");

            var handle = GetStdHandle(-11);
            int mode;
            GetConsoleMode(handle, out mode);
            SetConsoleMode(handle, mode | 0x4);

            for (sbyte i = 0; i < this.dh.height; i++)
            {
                for (sbyte j = 0; j < this.dh.width; j++)
                {
                        Console.Write(" ");
                        Console.Write("\x1b[38;2;" + 0 + ";" + 0 + ";" + 0 + "m" + "■");
                }
                Console.WriteLine("");
            }
        }



        private void drawToFramerate(Object source, ElapsedEventArgs e)
        {
            if (!refreshing)
            {
                refreshing = true;
                Pixel[,] toDraw = new Pixel[this.dh.height, this.dh.width];
                Array.Copy(this.dh.currentBoard, toDraw, this.dh.currentBoard.Length);

                if (this.dh.currentLCDNumber != this.dh.lastLCDNumber)
                {
                    Console.SetCursorPosition(0, 0);
                    string paddedNum = this.dh.currentLCDNumber.ToString();
                    this.dh.lastLCDNumber = this.dh.currentLCDNumber;
                    if (paddedNum.Length < 6)
                    {
                        for (int i = 0; i < 6 - this.dh.currentLCDNumber.ToString().Length; i++)
                        {
                            paddedNum = paddedNum.Insert(0, "0");
                        }

                    }
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine(paddedNum);
                }

                for (sbyte i = 0; i < this.dh.height; i++)
                {
                    int spacer = 1;
                    for (sbyte j = 0; j < this.dh.width; j++)
                    {
                        if (toDraw[i, j] != null)
                        {
                            if (this.dh.lastBoard == null || !toDraw[i,j].Equals(this.dh.lastBoard[i,j]))
                            {
                                Console.SetCursorPosition(j + spacer, i + 1);
                                Console.Write("\x1b[38;2;" + toDraw[i, j].Red + ";" + toDraw[i, j].Green + ";" + toDraw[i, j].Blue + "m" + "■");
                            }
                        }
                        spacer++;
                    }
                }
                if (Console.CursorTop != this.dh.height + 1 && Console.CursorLeft != 0)
                {
                    Console.SetCursorPosition(0, this.dh.height + 1);
                }
                this.dh.lastBoard = toDraw;
                refreshing = false;
            }
        }



        public void DisplayInt(int value)
        {
            this.dh.DisplayInt(value);

        }

        public void DisplayInt(int value, bool? leftAligned)
        {
            this.dh.DisplayInt(value, leftAligned);
        }

        public void DisplayInts(int leftValue, int rightValue)
        {
            this.dh.DisplayInts(leftValue, rightValue);
        }

        public void Draw(IPixel[,] pixels)
        {
            this.dh.Draw(pixels);
        }

        public void Draw(ILocatedPixel pixel)
        {
            this.dh.Draw(pixel);
        }

        public void DisplayText(string text)
        {
            // Update local state first
            this.dh.currentLCDNumber = text ?? "";
        }
    }
}
