using Microsoft.Kinect;
using System;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.Windows.Threading;

namespace KinectCaptureApp
{
    public partial class MainWindow : Window
    {
        private KinectSensor sensor;

        // Body tracking
        private BodyFrameReader bodyReader;
        private Body[] bodies;

        // RGB camera
        private ColorFrameReader colorReader;
        private WriteableBitmap colorBitmap;

        public MainWindow()
        {
            InitializeComponent();
            InitializeKinect();
        }

        private void InitializeKinect()
        {
            sensor = KinectSensor.GetDefault();

            if (sensor != null)
            {
                sensor.Open();

                // ---------------- BODY ----------------
                bodyReader = sensor.BodyFrameSource.OpenReader();
                bodies = new Body[sensor.BodyFrameSource.BodyCount];
                bodyReader.FrameArrived += BodyReader_FrameArrived;

                // ---------------- COLOR ----------------
                var colorFrameDesc = sensor.ColorFrameSource.CreateFrameDescription(ColorImageFormat.Bgra);

                colorBitmap = new WriteableBitmap(
                    colorFrameDesc.Width,
                    colorFrameDesc.Height,
                    96,
                    96,
                    PixelFormats.Bgra32,
                    null);

                ColorImage.Source = colorBitmap;

                colorReader = sensor.ColorFrameSource.OpenReader();
                colorReader.FrameArrived += ColorReader_FrameArrived;

                MessageBox.Show("Kinect initialized successfully!");
            }
            else
            {
                MessageBox.Show("Kinect sensor not detected.");
            }
        }

        // ---------------- RGB STREAM ----------------
        private void ColorReader_FrameArrived(object sender, ColorFrameArrivedEventArgs e)
        {
            using (var frame = e.FrameReference.AcquireFrame())
            {
                if (frame == null) return;

                var desc = frame.FrameDescription;

                using (var buffer = frame.LockRawImageBuffer())
                {
                    colorBitmap.Lock();

                    frame.CopyConvertedFrameDataToIntPtr(
                        colorBitmap.BackBuffer,
                        (uint)(desc.Width * desc.Height * 4),
                        ColorImageFormat.Bgra);

                    colorBitmap.AddDirtyRect(new Int32Rect(0, 0, desc.Width, desc.Height));
                    colorBitmap.Unlock();
                }
            }
        }

        // ---------------- BODY TRACKING ----------------
        private void BodyReader_FrameArrived(object sender, BodyFrameArrivedEventArgs e)
        {
            using (var frame = e.FrameReference.AcquireFrame())
            {
                if (frame == null) return;

                frame.GetAndRefreshBodyData(bodies);

                foreach (var body in bodies)
                {
                    if (body != null && body.IsTracked)
                    {
                        var head = body.Joints[JointType.Head].Position;

                        System.Diagnostics.Debug.WriteLine(
                            $"Head Position: X={head.X:F2}, Y={head.Y:F2}, Z={head.Z:F2}");
                    }
                }
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            bodyReader?.Dispose();
            colorReader?.Dispose();
            sensor?.Close();

            base.OnClosed(e);
        }
    }
}