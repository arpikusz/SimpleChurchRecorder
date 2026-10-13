using System.IO;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using NAudio.Wave;

namespace SimpleChurchRecorder
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private static string audioOutputPath { get; set; } = "C:/audio";
        private WaveInEvent? waveIn;
        private WaveFileWriter? writer;
        private Brush? originalBackground;
        private Brush? originalStatusForeground;
        private Brush? originalRecordIconForeground;
        private Brush? originalRecordLabelForeground;
        private bool isEditingOutput = false;

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
            RecordButton.Click += RecordButton_Click;
            EditOutputButton.Click += EditOutputButton_Click;
        }

        private void MainWindow_Loaded(object? sender, RoutedEventArgs e)
        {
            bool ok = true;

            try
            {
                LoadInputDevices();
                LoadDefaultOutputPath();

                OutputPathBox.Text = audioOutputPath;
                OutputPathBox.IsReadOnly = true; 
            }
            catch
            {
                ok = false;
            }

            StatusLabel.Text = ok ? "Ready!" : "Error on startup!";

            // Check command line args for autorecord option
            try
            {
                var args = Environment.GetCommandLineArgs();
                if (args != null)
                {
                    foreach (var a in args)
                    {
                        if (string.Equals(a, "autorecord", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(a, "--autorecord", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(a, "/autorecord", StringComparison.OrdinalIgnoreCase))
                        {
                            // start recording automatically
                            StartRecording();
                            break;
                        }
                    }
                }
            }
            catch
            {
                // ignore errors when checking args
            }
        }

        private void LoadInputDevices()
        {
            // Enumerate audio input devices
            int count = WaveInEvent.DeviceCount;
            for (int i = 0; i < count; i++)
            {
                var caps = WaveInEvent.GetCapabilities(i);
                InputDevicesCombo.Items.Add(caps.ProductName);
            }

            if (InputDevicesCombo.Items.Count > 0)
            {
                // select default device (first)
                InputDevicesCombo.SelectedIndex = 0;
            }
        }

        

        private void EditOutputButton_Click(object? sender, RoutedEventArgs e)
        {
            if (!isEditingOutput)
            {
                // Enter edit mode
                isEditingOutput = true;
                OutputPathBox.IsReadOnly = false;
                OutputPathBox.Focus();
                OutputPathBox.CaretIndex = OutputPathBox.Text.Length;
                EditOutputIcon.Text = "💾"; // save icon
                EditOutputButton.ToolTip = "Save";
            }
            else
            {
                // Save mode
                try
                {
                    string newPath = OutputPathBox.Text?.Trim() ?? audioOutputPath;
                    if (string.IsNullOrEmpty(newPath)) throw new InvalidOperationException("Invalid path");

                    // save to registry under HKCU\Software\SimpleChurchRecorder
                    try
                    {
                        using var rk = Registry.CurrentUser.CreateSubKey("Software\\SimpleChurchRecorder");
                        rk.SetValue("OutputPath", newPath, RegistryValueKind.String);
                    }
                    catch
                    {
                        // if registry write fails, surface error
                        throw;
                    }

                    audioOutputPath = newPath;

                    StatusLabel.Text = "Ready!";
                }
                catch
                {
                    StatusLabel.Text = "Error!";
                }
                finally
                {
                    // leave edit mode
                    isEditingOutput = false;
                    OutputPathBox.IsReadOnly = true;
                    EditOutputIcon.Text = "✎";
                    EditOutputButton.ToolTip = "Edit";
                }
            }
        }

        private void RecordButton_Click(object? sender, RoutedEventArgs e)
        {
            if (waveIn == null)
            {
                StartRecording();
            }
            else
            {
                try
                {
                    waveIn.StopRecording();
                }
                catch
                {
                    // force cleanup
                    DisposeRecording();
                    RestoreUiAfterStop();
                    StatusLabel.Text = "Error!";
                }
            }
        }

        // Starts recording. Safe to call from startup (autorecord) or from the Record button.
        private void StartRecording()
        {
            try
            {
                if (InputDevicesCombo.SelectedIndex < 0)
                {
                    StatusLabel.Text = "Error!";
                    return;
                }

                // Ensure folder exists
                if (!Directory.Exists(audioOutputPath))
                {
                    Directory.CreateDirectory(audioOutputPath);
                }

                string filename = DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss") + ".wav";
                string path = Path.Combine(audioOutputPath, filename);

                waveIn = new WaveInEvent();
                waveIn.DeviceNumber = InputDevicesCombo.SelectedIndex;
                waveIn.WaveFormat = new WaveFormat(44100, 2); // 44k stereo
                writer = new WaveFileWriter(path, waveIn.WaveFormat);
                waveIn.DataAvailable += WaveIn_DataAvailable;
                waveIn.RecordingStopped += WaveIn_RecordingStopped;
                waveIn.StartRecording();

                // update UI: change label and icon
                if (originalBackground == null) originalBackground = this.Background;
                if (originalStatusForeground == null) originalStatusForeground = StatusLabel.Foreground;
                if (originalRecordLabelForeground == null) originalRecordLabelForeground = RecordLabel.Foreground;

                RecordLabel.Text = "STOP";
                this.Background = Brushes.Red;
                StatusLabel.Foreground = Brushes.White;
                RecordLabel.Foreground = Brushes.White;
                StatusLabel.Text = "Recording...";
            }
            catch
            {
                StatusLabel.Text = "Error!";
            }
        }

        private void WaveIn_DataAvailable(object? sender, WaveInEventArgs e)
        {
            try
            {
                writer?.Write(e.Buffer, 0, e.BytesRecorded);
                writer?.Flush();
            }
            catch
            {
                // ignore write errors
            }
        }

        private void WaveIn_RecordingStopped(object? sender, StoppedEventArgs e)
        {
            DisposeRecording();
            Dispatcher.Invoke(() =>
            {
                RestoreUiAfterStop();
                StatusLabel.Text = "Ready!";
            });
        }

        private void DisposeRecording()
        {
            try
            {
                if (waveIn != null)
                {
                    waveIn.DataAvailable -= WaveIn_DataAvailable;
                    waveIn.RecordingStopped -= WaveIn_RecordingStopped;
                    waveIn.Dispose();
                    waveIn = null;
                }

                if (writer != null)
                {
                    writer.Dispose();
                    writer = null;
                }
            }
            catch
            {
                // ignore
            }
        }

        private void RestoreUiAfterStop()
        {
            RecordLabel.Text = "RECORD";
            if (originalBackground != null) this.Background = originalBackground;
            if (originalStatusForeground != null) StatusLabel.Foreground = originalStatusForeground;
            if (originalRecordLabelForeground != null) RecordLabel.Foreground = originalRecordLabelForeground;
        }

        private static void LoadDefaultOutputPath()
        {
            var rk = Registry.CurrentUser.CreateSubKey("Software\\SimpleChurchRecorder");
            audioOutputPath = rk.GetValue("OutputPath", RegistryValueKind.String) as string ?? audioOutputPath;
        }
    }
}
