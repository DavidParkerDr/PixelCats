using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Timers;

namespace PixelBoard
{
    internal class DisplayHelper
    {
        internal sbyte height = 20;
        internal sbyte width = 10;
        internal sbyte framerate = 60;
        private volatile bool boardDirty = false;
        internal static System.Timers.Timer displayRefreshTimer;
        private readonly object boardLock = new object();

        internal IPixel[,] lastBoard;
        internal IPixel[,] currentBoard;

        internal string lastLCDNumber = "";
        internal string currentLCDNumber = "";

        internal void SetFramerate(sbyte framerate)
        {
        //    if (framerate > 0 && framerate <= 60)
        //        this.framerate = framerate;
        //    else if (framerate > 60)
        //        this.framerate = 60;
        //    else
        //        this.framerate = 1;

        //    if (displayRefreshTimer != null)
        //        displayRefreshTimer.Interval = 1000.0 / this.framerate;
        }

        internal void SetSize(sbyte height, sbyte width)
        {
            this.height = height;
            this.width = width;
            this.currentBoard = new IPixel[height, width];
            this.lastBoard = new IPixel[height, width];
        }

        internal void MakeTimer(ElapsedEventHandler handler)
        {
            displayRefreshTimer = new System.Timers.Timer(1000.0 / this.framerate);
            displayRefreshTimer.Elapsed += handler;
            displayRefreshTimer.AutoReset = true;
            displayRefreshTimer.Enabled = true;
        }

        internal void ValidateLCDValue(int value, int max, string argumentName)
        {
            if (value > max)
                throw new ArgumentOutOfRangeException(argumentName, "Int to display was over the allowed digit limit.");
            else if (value < 0)
                throw new ArgumentOutOfRangeException(argumentName, "Int to display was negative, which is not valid.");
        }

        internal void Draw(IPixel[,] pixels)
        {
            if (pixels.GetLength(0) != this.height || pixels.GetLength(1) != this.width)
                throw new ArgumentOutOfRangeException("pixels", "Pixel dimensions do not match the defined display size.");

            lock (boardLock)
            {
                for (int row = 0; row < height; row++)
                {
                    for (int col = 0; col < width; col++)
                    {
                        currentBoard[row, col] = pixels[row, col];
                    }
                }
            }
            boardDirty = true;
        }

        internal void Draw(ILocatedPixel pixel)
        {
            if (pixel.Row >= this.height || pixel.Column >= this.width)
                throw new ArgumentOutOfRangeException("pixel", "Pixel position is out of bounds.");

            lock (boardLock)
            {
                this.currentBoard[pixel.Row, pixel.Column] = (IPixel)pixel;
            }
            boardDirty = true;
        }

        internal void DisplayInt(int value)
        {
            ValidateLCDValue(value, 999999, "value");
            this.currentLCDNumber = value.ToString();
        }

        internal void DisplayInt(int value, bool? leftAligned)
        {
            ValidateLCDValue(value, 999999, "value");
            string paddedValue = value.ToString();

            if (leftAligned == true)
            {
                while (value < 99999 && value != 0)
                {
                    value *= 10;
                    paddedValue += " ";
                }
            }

            this.currentLCDNumber = paddedValue;
        }

        internal void DisplayInts(int leftValue, int rightValue)
        {
            ValidateLCDValue(leftValue, 999, "leftValue");
            ValidateLCDValue(rightValue, 999, "rightValue");

            string paddedLeft = leftValue.ToString();
            string paddedRight = rightValue.ToString();

            while (leftValue < 99 && leftValue != 0)
            {
                leftValue *= 10;
                paddedLeft += " ";
            }

            while (rightValue < 99 && rightValue != 0)
            {
                rightValue *= 10;
                paddedRight = " " + paddedRight;
            }

            if (leftValue == 0) paddedLeft = "0  ";
            if (rightValue == 0) paddedRight = "  0";

            this.currentLCDNumber = paddedLeft + paddedRight;
        }

        /// <summary>
        /// Redraws only changed pixels to reduce flickering.
        /// </summary>
        internal void RefreshDisplay(IDisplay display)
        {
            if (!boardDirty) return;
            List<ILocatedPixel> changedPixels = new List<ILocatedPixel>();
            lock (boardLock)
            {
                for (int row = 0; row < height; row++)
                {
                    for (int col = 0; col < width; col++)
                    {
                        IPixel current = currentBoard[row, col];
                        IPixel last = lastBoard[row, col];

                        if (current != null && last != null &&
                            (current.Red != last.Red || current.Green != last.Green || current.Blue != last.Blue))
                        {
                            changedPixels.Add(new LocatedPixel(
                                current.Red, current.Green, current.Blue,
                                (sbyte)col, (sbyte)row));

                            lastBoard[row, col] = current;
                        }
                    }
                }
            }
            if (changedPixels.Count > 0)
            {
                display.DrawBatch(changedPixels);
            }
            boardDirty = false;
        }
    }
}
