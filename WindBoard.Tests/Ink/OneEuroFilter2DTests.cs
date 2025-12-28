using System;
using System.Windows;
using WindBoard.Core.Ink;
using Xunit;

namespace WindBoard.Tests.Ink;

public sealed class OneEuroFilter2DTests
{
    [Fact]
    public void Update_FirstCall_ResetsAndReturnsInput()
    {
        var filter = new OneEuroFilter2D();
        var p = new Point(10, 20);

        var result = filter.Update(
            rawMm: p,
            dtSec: 0.016,
            minCutoffHz: 2,
            beta: 0.1,
            dCutoffHz: 1);

        Assert.Equal(p, result);
        Assert.Equal(0, filter.DerivativeMagnitudeMmPerSec, precision: 12);
    }

    [Fact]
    public void Update_DoesNotThrow_WhenCutoffClampReversed()
    {
        var filter = new OneEuroFilter2D();
        filter.Reset(new Point(0, 0));

        var ex = Record.Exception(() =>
        {
            filter.Update(
                rawMm: new Point(1, 0),
                dtSec: 0.0,
                minCutoffHz: 2.0,
                beta: 0.0,
                dCutoffHz: 1.0,
                cutoffMinClampHz: 10.0,
                cutoffMaxClampHz: 1.0);
        });

        Assert.Null(ex);
        Assert.True(filter.DerivativeMagnitudeMmPerSec >= 0);
        Assert.False(double.IsNaN(filter.DerivativeMagnitudeMmPerSec));
        Assert.False(double.IsInfinity(filter.DerivativeMagnitudeMmPerSec));
    }
}

