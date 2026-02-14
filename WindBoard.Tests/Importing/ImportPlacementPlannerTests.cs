using System.Numerics;
using WindBoard.Board.Elements;
using WindBoard.Importing;
using WindBoard.Tests;

namespace WindBoard.Tests.Importing;

public sealed class ImportPlacementPlannerTests
{
    [Fact]
    public void PlaceElementAtViewportCenterGrid_Zoom1_PlacesAsExpected()
    {
        var element = new BoardFileElement();
        Vector2 cameraWorld = new(100, 200);
        float zoom = 1.0f;

        ImportPlacementPlanner.PlaceElementAtViewportCenterGrid(element, sizeDip: new Vector2(360, 160), index: 0, cameraWorld, zoom);
        AssertEx.Equal(new Vector2(-80, 120), element.PositionWorld);
        AssertEx.Equal(new Vector2(360, 160), element.SizeWorld);

        ImportPlacementPlanner.PlaceElementAtViewportCenterGrid(element, sizeDip: new Vector2(360, 160), index: 1, cameraWorld, zoom);
        AssertEx.Equal(new Vector2(364, 120), element.PositionWorld);
    }

    [Fact]
    public void PlaceElementAtViewportCenterGrid_Zoom2_ScalesOffsetsAndSize()
    {
        var element = new BoardFileElement();
        Vector2 cameraWorld = new(100, 200);
        float zoom = 2.0f;

        ImportPlacementPlanner.PlaceElementAtViewportCenterGrid(element, sizeDip: new Vector2(360, 160), index: 1, cameraWorld, zoom);

        // sizeWorld = (180, 80); offsetWorld = (444,0)/2 = (222,0); pos = camera - size/2 + offset = (100,200) - (90,40) + (222,0)
        AssertEx.Equal(new Vector2(232, 160), element.PositionWorld);
        AssertEx.Equal(new Vector2(180, 80), element.SizeWorld);
    }
}
