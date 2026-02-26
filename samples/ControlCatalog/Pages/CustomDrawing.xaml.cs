using System;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace ControlCatalog.Pages
{
    public partial class CustomDrawing : UserControl
    {
        public CustomDrawing()
        {
            InitializeComponent();
        }

        private CustomDrawingExampleControl DrawingControl =>
            CustomDrawingControl ?? throw new InvalidOperationException("Named control CustomDrawingControl was not initialized.");

        private void InitializeComponent()
        {
            InitializeComponent(true);
        }

        private void RotateMinus(object? sender, RoutedEventArgs e)
        {
            DrawingControl.Rotation -= Math.PI / 20.0d;
        }

        private void RotatePlus(object? sender, RoutedEventArgs e)
        {
            DrawingControl.Rotation += Math.PI / 20.0d;
        }

        private void ZoomIn(object? sender, RoutedEventArgs e)
        {
            DrawingControl.Scale *= 1.2d;
        }

        private void ZoomOut(object? sender, RoutedEventArgs e)
        {
            DrawingControl.Scale /= 1.2d;
        }
    }
}
