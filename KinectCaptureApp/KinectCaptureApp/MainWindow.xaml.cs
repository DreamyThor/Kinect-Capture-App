using Microsoft.Kinect;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Controls;

namespace KinectCaptureApp
{
    public partial class MainWindow : Window
    {
        private KinectSensor sensor;

        // Readers
        private ColorFrameReader colorReader;
        private DepthFrameReader depthReader;
        private InfraredFrameReader infraredReader;
        private BodyFrameReader bodyReader;

        // Bitmaps
        private WriteableBitmap colorBitmap;
        private WriteableBitmap depthBitmap;
        private WriteableBitmap infraredBitmap;

        private byte[] depthPixels;
        private byte[] infraredPixels;
        private Body[] bodies;

        private CoordinateMapper coordinateMapper;

        public MainWindow()
        {
            InitializeComponent();
            InitializeKinect();
        }

        private void InitializeKinect()
        {
            sensor = KinectSensor.GetDefault();

            if (sensor == null)
            {
                MessageBox.Show("Kinect sensor not detected.");
                return;
            }

            coordinateMapper = sensor.CoordinateMapper;

            // ----------- COLOR STREAM -----------
            var colorDesc = sensor.ColorFrameSource.CreateFrameDescription(ColorImageFormat.Bgra);
            colorBitmap = new WriteableBitmap(
                colorDesc.Width,
                colorDesc.Height,
                96, 96,
                PixelFormats.Bgra32,
                null);
            ColorImage.Source = colorBitmap;

            colorReader = sensor.ColorFrameSource.OpenReader();
            colorReader.FrameArrived += ColorReader_FrameArrived;

            // ----------- DEPTH STREAM -----------
            var depthDesc = sensor.DepthFrameSource.FrameDescription;
            depthPixels = new byte[depthDesc.Width * depthDesc.Height * 4];
            depthBitmap = new WriteableBitmap(
                depthDesc.Width,
                depthDesc.Height,
                96, 96,
                PixelFormats.Bgra32,
                null);
            DepthImage.Source = depthBitmap;

            depthReader = sensor.DepthFrameSource.OpenReader();
            depthReader.FrameArrived += DepthReader_FrameArrived;

            // ----------- INFRARED STREAM -----------
            var irDesc = sensor.InfraredFrameSource.FrameDescription;
            infraredPixels = new byte[irDesc.Width * irDesc.Height * 4];
            infraredBitmap = new WriteableBitmap(
                irDesc.Width,
                irDesc.Height,
                96, 96,
                PixelFormats.Bgra32,
                null);
            InfraredImage.Source = infraredBitmap;

            infraredReader = sensor.InfraredFrameSource.OpenReader();
            infraredReader.FrameArrived += InfraredReader_FrameArrived;

            // ----------- BODY TRACKING -----------
            bodyReader = sensor.BodyFrameSource.OpenReader();
            bodies = new Body[sensor.BodyFrameSource.BodyCount];
            bodyReader.FrameArrived += BodyReader_FrameArrived;

            sensor.Open();
            MessageBox.Show("All Kinect streams initialized successfully!");
        }

        // ----------- COLOR -----------
        private void ColorReader_FrameArrived(object sender, ColorFrameArrivedEventArgs e)
        {
            using (var frame = e.FrameReference.AcquireFrame())
            {
                if (frame == null) return;

                var desc = frame.FrameDescription;
                colorBitmap.Lock();
                frame.CopyConvertedFrameDataToIntPtr(
                    colorBitmap.BackBuffer,
                    (uint)(desc.Width * desc.Height * 4),
                    ColorImageFormat.Bgra);
                colorBitmap.AddDirtyRect(new Int32Rect(0, 0, desc.Width, desc.Height));
                colorBitmap.Unlock();
            }
        }

        // ----------- DEPTH -----------
        private void DepthReader_FrameArrived(object sender, DepthFrameArrivedEventArgs e)
        {
            using (var frame = e.FrameReference.AcquireFrame())
            {
                if (frame == null) return;

                var desc = frame.FrameDescription;
                ushort minDepth = frame.DepthMinReliableDistance;
                ushort maxDepth = frame.DepthMaxReliableDistance;

                ushort[] depthData = new ushort[desc.Width * desc.Height];
                frame.CopyFrameDataToArray(depthData);

                for (int i = 0; i < depthData.Length; i++)
                {
                    ushort depth = depthData[i];
                    byte intensity = (byte)(depth >= minDepth && depth <= maxDepth
                        ? depth / 32
                        : 0);

                    int index = i * 4;
                    depthPixels[index] = intensity;
                    depthPixels[index + 1] = intensity;
                    depthPixels[index + 2] = intensity;
                    depthPixels[index + 3] = 255;
                }

                depthBitmap.WritePixels(
                    new Int32Rect(0, 0, desc.Width, desc.Height),
                    depthPixels,
                    desc.Width * 4,
                    0);
            }
        }

        // ----------- INFRARED -----------
        private void InfraredReader_FrameArrived(object sender, InfraredFrameArrivedEventArgs e)
        {
            using (var frame = e.FrameReference.AcquireFrame())
            {
                if (frame == null) return;

                var desc = frame.FrameDescription;
                ushort[] irData = new ushort[desc.Width * desc.Height];
                frame.CopyFrameDataToArray(irData);

                const float InfraredSourceValueMaximum = ushort.MaxValue;
                const float InfraredOutputValueMinimum = 0.01f;
                const float InfraredOutputValueMaximum = 1.0f;

                for (int i = 0; i < irData.Length; i++)
                {
                    float intensityRatio = irData[i] / InfraredSourceValueMaximum;
                    intensityRatio = Math.Min(InfraredOutputValueMaximum,
                                     Math.Max(InfraredOutputValueMinimum, intensityRatio));

                    byte intensity = (byte)(intensityRatio * 255);
                    int index = i * 4;

                    infraredPixels[index] = intensity;
                    infraredPixels[index + 1] = intensity;
                    infraredPixels[index + 2] = intensity;
                    infraredPixels[index + 3] = 255;
                }

                infraredBitmap.WritePixels(
                    new Int32Rect(0, 0, desc.Width, desc.Height),
                    infraredPixels,
                    desc.Width * 4,
                    0);
            }
        }

        // ----------- BODY / SKELETON -----------
        private void DrawJoint(float x, float y)
        {
            var ellipse = new System.Windows.Shapes.Ellipse
            {
                Width = 8,
                Height = 8,
                Fill = System.Windows.Media.Brushes.Lime
            };

            System.Windows.Controls.Canvas.SetLeft(ellipse, x - ellipse.Width / 2);
            System.Windows.Controls.Canvas.SetTop(ellipse, y - ellipse.Height / 2);

            SkeletonCanvas.Children.Add(ellipse);
        }

        private void DrawBone(Body body, JointType jointType1, JointType jointType2)
        {
            var joint1 = body.Joints[jointType1];
            var joint2 = body.Joints[jointType2];

            if (joint1.TrackingState == TrackingState.NotTracked ||
                joint2.TrackingState == TrackingState.NotTracked)
                return;

            DepthSpacePoint point1 =
                coordinateMapper.MapCameraPointToDepthSpace(joint1.Position);
            DepthSpacePoint point2 =
                coordinateMapper.MapCameraPointToDepthSpace(joint2.Position);

            if (float.IsInfinity(point1.X) || float.IsInfinity(point1.Y) ||
                float.IsInfinity(point2.X) || float.IsInfinity(point2.Y))
                return;

            var line = new System.Windows.Shapes.Line
            {
                X1 = point1.X,
                Y1 = point1.Y,
                X2 = point2.X,
                Y2 = point2.Y,
                Stroke = System.Windows.Media.Brushes.Yellow,
                StrokeThickness = 2
            };

            SkeletonCanvas.Children.Add(line);
        }

        private void BodyReader_FrameArrived(object sender, BodyFrameArrivedEventArgs e)
        {
            using (var frame = e.FrameReference.AcquireFrame())
            {
                if (frame == null) return;

                frame.GetAndRefreshBodyData(bodies);

                SkeletonCanvas.Children.Clear();

                foreach (var body in bodies)
                {
                    if (body == null || !body.IsTracked)
                        continue;

                    foreach (var joint in body.Joints)
                    {
                        if (joint.Value.TrackingState == TrackingState.NotTracked)
                            continue;

                        // Map joint to depth space
                        DepthSpacePoint point =
                            coordinateMapper.MapCameraPointToDepthSpace(joint.Value.Position);

                        if (float.IsInfinity(point.X) || float.IsInfinity(point.Y))
                            continue;

                        DrawJoint(point.X, point.Y);
                    }

                    // Draw bones between joints
                    DrawBone(body, JointType.Head, JointType.Neck);
                    DrawBone(body, JointType.Neck, JointType.SpineShoulder);
                    DrawBone(body, JointType.SpineShoulder, JointType.SpineMid);
                    DrawBone(body, JointType.SpineMid, JointType.SpineBase);

                    DrawBone(body, JointType.SpineShoulder, JointType.ShoulderLeft);
                    DrawBone(body, JointType.ShoulderLeft, JointType.ElbowLeft);
                    DrawBone(body, JointType.ElbowLeft, JointType.WristLeft);
                    DrawBone(body, JointType.WristLeft, JointType.HandLeft);

                    DrawBone(body, JointType.SpineShoulder, JointType.ShoulderRight);
                    DrawBone(body, JointType.ShoulderRight, JointType.ElbowRight);
                    DrawBone(body, JointType.ElbowRight, JointType.WristRight);
                    DrawBone(body, JointType.WristRight, JointType.HandRight);

                    DrawBone(body, JointType.SpineBase, JointType.HipLeft);
                    DrawBone(body, JointType.HipLeft, JointType.KneeLeft);
                    DrawBone(body, JointType.KneeLeft, JointType.AnkleLeft);
                    DrawBone(body, JointType.AnkleLeft, JointType.FootLeft);

                    DrawBone(body, JointType.SpineBase, JointType.HipRight);
                    DrawBone(body, JointType.HipRight, JointType.KneeRight);
                    DrawBone(body, JointType.KneeRight, JointType.AnkleRight);
                    DrawBone(body, JointType.AnkleRight, JointType.FootRight);
                }
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            colorReader?.Dispose();
            depthReader?.Dispose();
            infraredReader?.Dispose();
            bodyReader?.Dispose();
            sensor?.Close();
            base.OnClosed(e);
        }
    }
}