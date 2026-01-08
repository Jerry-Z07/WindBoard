namespace WindBoard.Models.Ink
{
    public readonly record struct InkPoint(double X, double Y, float Pressure, long TimestampTicks);
}

