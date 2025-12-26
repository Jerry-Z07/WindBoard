using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Media;

namespace WindBoard.Services
{
    public class StrokeService
    {
        private readonly InkCanvas _canvas;
        private double _baseThickness;

        public StrokeService(InkCanvas canvas, double baseThickness = 3.0)
        {
            _canvas = canvas;
            _baseThickness = baseThickness;
        }

        public double BaseThickness => _baseThickness;

        public void SetBaseThickness(double thickness, double currentZoom)
        {
            _baseThickness = thickness;
            UpdatePenThickness(currentZoom);
        }

        public void UpdatePenThickness(double zoom)
        {
            if (zoom <= 0) zoom = 1;

            double newThickness = _baseThickness / zoom;

            var da = _canvas.DefaultDrawingAttributes;
            da.Width = newThickness;
            da.Height = newThickness;
        }

        public void SetColor(Color color)
        {
            _canvas.DefaultDrawingAttributes.Color = color;
        }
    }
}
