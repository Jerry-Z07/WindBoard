namespace WindBoard.Models.InkV2
{
    public readonly struct InkPoint
    {
        public InkPoint(double xDip, double yDip, float pressure = 0.5f, long timestampTicks = 0)
        {
            XDip = xDip;
            YDip = yDip;
            Pressure = pressure;
            TimestampTicks = timestampTicks;
        }

        public double XDip { get; }
        public double YDip { get; }
        public float Pressure { get; }
        public long TimestampTicks { get; }
    }
}

