using Microsoft.Azure.Kinect.Sensor;
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Monitor
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private WriteableBitmap _depthBmp;
        private Device _device = null;
        private DateTimeOffset _startTime = DateTimeOffset.Now;

        public MainWindow()
        {
            InitializeComponent();

            this.Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (Device.GetInstalledCount() > 0)
            {
                PlayButton.IsEnabled = true;
                StatusTextbox.Text = "Kinect found.";
            }
            else
            {
                PlayButton.IsEnabled = false;
                StatusTextbox.Text = "No kinect found.";
            }
        }

        private async Task<KinectCallResult> StartKinect()
        {
            KinectCallResult result = new KinectCallResult { Success = false };

            await Task.Run(() =>
            {
                try
                {
                    if (_device == null)
                    {
                        _device = Device.Open();

                        _device.StartCameras(new DeviceConfiguration
                        {
                            ColorResolution = ColorResolution.Off,
                            DepthMode = DepthMode.WFOV_Unbinned,
                            DisableStreamingIndicator = true,
                            CameraFPS = FPS.FPS15,
                        });

                        _device.StartImu();

                        result.Success = true;
                    }
                }
                catch (Exception ex)
                {
                    result.Success = false;
                    result.OptionalMessage = ex.GetBaseException().Message;
                }

            });

            return result;
        }

        private async Task<KinectCallResult> StartProcessingFramesAsync()
        {
            KinectCallResult result = new KinectCallResult { Success = false };
            TaskCompletionSource<bool> started = new TaskCompletionSource<bool>();
            var backgroundTask = Task.Run(async () =>
            {
                try
                {
                    bool firstFrameReceived = false;
                    byte[] rgb24 = null;

                    for (; ; )
                    {
                        var capture = _device.GetCapture(TimeSpan.FromSeconds(10));
                        Image depthImage = capture.Depth;
                        if (!firstFrameReceived)
                        {
                            started.SetResult(true);

                            await Dispatcher.InvokeAsync(() =>
                            {
                                _depthBmp = new WriteableBitmap(
                                    depthImage.WidthPixels / 2, depthImage.HeightPixels / 2, 96, 96,
                                    PixelFormats.Rgb24, null);
                                Depth.Source = _depthBmp;
                            });

                            rgb24 = new byte[(depthImage.WidthPixels / 2) * (depthImage.HeightPixels / 2) * 3];

                            firstFrameReceived = true;
                        }
                        else if (rgb24 != null)
                        {
                            ushort[] buffer = depthImage.GetPixels<ushort>().ToArray();

                            int rgbIndex = 0;
                            for (int y = 0; y < depthImage.HeightPixels; y += 2)
                            {
                                for (int x = 0; x < depthImage.WidthPixels; x += 2)
                                {
                                    byte rgbValue = (byte)(buffer[y * depthImage.WidthPixels + x] * (255 / 5_000.0f));
                                    rgb24[rgbIndex++] = rgbValue;
                                    rgb24[rgbIndex++] = rgbValue;
                                    rgb24[rgbIndex++] = rgbValue;
                                }
                            }
                            await Dispatcher.InvokeAsync(() =>
                            {
                                _depthBmp.WritePixels(
                                    new Int32Rect(0, 0, depthImage.WidthPixels / 2, depthImage.HeightPixels / 2),
                                    rgb24,
                                    (depthImage.WidthPixels / 2) * 3, 0);

                                DurationTextbox.Text = (DateTimeOffset.Now - _startTime).ToString();
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    result.OptionalMessage = ex.GetBaseException().Message;
                    started.SetResult(false);
                }
            });

            result.Success = backgroundTask.Status == TaskStatus.Running && await started.Task;
            return result;
        }

        private async void PlayButton_Click(object sender, RoutedEventArgs e)
        {
            var result = await StartKinect();
            if (result.Success)
            {
                await StartProcessingFramesAsync();
                StatusTextbox.Text = "Started playing the kinect.";
                PlayButton.IsEnabled = false;
                _startTime = DateTimeOffset.Now;
            }
        }
    }
}
