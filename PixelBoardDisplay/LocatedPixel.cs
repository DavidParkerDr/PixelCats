#nullable enable
using System;
using System.Collections.Generic;
using System.Text;

namespace PixelBoard
{
    public class LocatedPixel : Pixel, ILocatedPixel
    {
        private sbyte column;
        private sbyte row;

        public sbyte Column { get => column; set => column = value; }
        public sbyte Row { get => row; set => row = value; }

        public LocatedPixel(byte r, byte g, byte b, sbyte column, sbyte row)
            : base(r, g, b)
        {
            this.column = column;
            this.row = row;
        }

        public override bool Equals(object? obj)
        {
            if (obj is LocatedPixel other)
            {
                return base.Equals(other) && this.Column == other.Column && this.Row == other.Row;
            }
            return false;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(base.GetHashCode(), Column, Row);
        }
    }
}
