﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using HuskyRobotics.Utilities;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace HuskyRobotics.UI
{
    /// <summary>
    /// Interaction logic for MapDisplay.xaml
    /// </summary>
    public partial class MapDisplay : UserControl
    {
        private const double RESET_PADDING = 10;
        public MapDisplay()
        {
            InitializeComponent();
        }

        public void DisplayMap(string mapSetFile)
        {
            ClearCanvas();
            // load in individual images
            // TODO get the path from the settings
            if (File.Exists(Directory.GetCurrentDirectory() + @"\Images\" + mapSetFile))
            {
                using (StreamReader file = new StreamReader(Directory.GetCurrentDirectory() + @"\Images\" + mapSetFile))
                {
                    string line = file.ReadLine();
                    string[] config = line.Split('|');
                    string[] imgDim = config[0].Split('x');
                    string[] centerCoords = config[1].Split(',');

                    ImageWidth = 1;
                    ImageHeight = 1;
                    double MapCenterLat = 0;
                    double MapCenterLong = 0;
                    Zoom = 1;

                    Int32.TryParse(config[2], out Zoom);
                    Int32.TryParse(imgDim[0], out ImageWidth);
                    Int32.TryParse(imgDim[1], out ImageHeight);
                    Double.TryParse(centerCoords[0], out MapCenterLat);
                    Double.TryParse(centerCoords[1], out MapCenterLong);

                    Tuple<int, int> PixelCoords = MapConversion.LatLongToPixelXY(MapCenterLat, MapCenterLong, Zoom);
                    CenterPixelX = PixelCoords.Item1;
                    CenterPixelY = PixelCoords.Item2;

                    while ((line = file.ReadLine()) != null)
                    {
                        string[] parts = line.Split('|');
                        string[] location = parts[0].Split(',');
                        int x = 0;
                        int y = 0;
                        Int32.TryParse(location[0], out x);
                        Int32.TryParse(location[1], out y);
                        AddImage(Directory.GetCurrentDirectory() + @"\Images\" + parts[1] + ".jpg", x, y, ImageWidth, ImageHeight);
                    }
                }
            }
        }

        // adds an image to the canvas with the given file location and the coords of where
        // on the canvas it goes
        private void AddImage(String location, int x, int y, int width, int height)
        {
            var uri = new Uri(location, UriKind.Absolute);
            var bitmap = new BitmapImage(uri);
            var image = new Image { Source = bitmap, Width = width, Height = height };
            Canvas.SetLeft(image, x * (width - 1));
            Canvas.SetTop(image, y * (height - 1));
            MapCanvas.Children.Add(image);
            allImages.Add(image);
        }

        // removes all images from the map display canvas
        private void ClearCanvas()
        {
            MapCanvas.Children.Clear();
            allImages.Clear();
        }

        private Point mousePosition;
        private List<Image> allImages = new List<Image>();
        private List<Image> waypointIcons= new List<Image>();
        private bool dragging = false;
        private int ImageWidth;
        private int ImageHeight;
        private int CenterPixelX;
        private int CenterPixelY;
        private int Zoom;

        private ObservableCollection<Waypoint> _waypoints = new ObservableCollection<Waypoint>();
        public ObservableCollection<Waypoint> Waypoints { get => _waypoints;
            set {
                _waypoints.CollectionChanged -= WaypointsChanged;
                _waypoints = value;
                _waypoints.CollectionChanged += WaypointsChanged;
            }
        }

        private void WaypointsChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            foreach (var oldIcon in waypointIcons) {
                MapCanvas.Children.Remove(oldIcon);
            }
            
            foreach (var waypoint in Waypoints)
            {
                var waypointIcon = new Image { Source = new BitmapImage(new Uri(Directory.GetCurrentDirectory() + @"/waypoint.png", UriKind.Absolute)) };
                Tuple<int, int> pixelCoords = MapConversion.LatLongToPixelXY(waypoint.Lat, waypoint.Long, Zoom);
                Canvas.SetLeft(waypointIcon, (ImageWidth / 2) + (pixelCoords.Item1 - CenterPixelX));
                Canvas.SetTop(waypointIcon, (ImageHeight / 2) + (pixelCoords.Item2 - CenterPixelY));
                MapCanvas.Children.Add(waypointIcon);
                waypointIcons.Add(waypointIcon);
            }            
        }

        private void CanvasMouseButtonDown(object sender, MouseButtonEventArgs e)
        {
            mousePosition = e.GetPosition(OuterCanvas);
            e.MouseDevice.Capture(MapCanvas);
        }

        private void CanvasMouseButtonUp(object sender, MouseButtonEventArgs e)
        {
            e.MouseDevice.Capture(null); // Release capture
        }

        private void CanvasMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                var position = e.GetPosition(OuterCanvas);
                var offset = position - mousePosition;
                mousePosition = position;

                var matrix = MapCanvas.RenderTransform.Value;
                matrix.Translate(offset.X, offset.Y);
                MapCanvas.RenderTransform = new MatrixTransform(matrix);
            }
        }

        private void CanvasMouseWheel(object sender, MouseWheelEventArgs e)
        {
            double scale = Math.Pow(1.1, -e.Delta/20.0);
            var position = e.GetPosition(MapCanvas);
            var matrix = MapCanvas.RenderTransform.Value;
            matrix.ScaleAtPrepend(scale, scale, position.X, position.Y);
            MapCanvas.RenderTransform = new MatrixTransform(matrix);
        }

        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            MapCanvas.RenderTransform = new MatrixTransform();
        }
    }
}
