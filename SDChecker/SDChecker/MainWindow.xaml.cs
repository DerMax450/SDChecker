using log4net;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SDChecker
{
    public partial class MainWindow : Window
    {
        private static readonly ILog log = LogManager.GetLogger(typeof(MainWindow));
        private CancellationTokenSource cancellationTokenSource;

        public MainWindow()
        {
            InitializeComponent();
            LoadDrives();
        }

        private void LoadDrives()
        {
            DriveComboBox.Items.Clear();
            var drives = DriveInfo.GetDrives()
                .Where(d => d.DriveType == DriveType.Removable && d.IsReady);

            foreach (var drive in drives)
                DriveComboBox.Items.Add(drive.Name);

            if (DriveComboBox.Items.Count > 0)
                DriveComboBox.SelectedIndex = 0;
        }

        private async void StartButton_Click(object sender, RoutedEventArgs e)
        {
            if (DriveComboBox.SelectedItem == null)
            {
                MessageBox.Show("Bitte ein Laufwerk auswählen.");
                return;
            }

            string drive = DriveComboBox.SelectedItem.ToString();
            string testType = ((ComboBoxItem)TestComboBox.SelectedItem).Content.ToString();

            cancellationTokenSource = new CancellationTokenSource();
            CancelButton.IsEnabled = true;
            StartButton.IsEnabled = false;
            ProgressBar.Value = 0;
            StatusText.Text = "Test läuft...";
            StatusText.Background = Brushes.Gold;
            OutputTextBox.Clear();

            try
            {
                if (testType == "Alle Tests" || testType == "Größenprüfung")
                    AppendOutput($"Größe: {FormatBytes(new DriveInfo(drive).TotalSize)}\n");

                if ((testType == "Alle Tests" || testType == "Hardwareprüfung") && !cancellationTokenSource.IsCancellationRequested)
                    await Task.Run(() => PrintHardwareInfo(drive), cancellationTokenSource.Token);

                if ((testType == "Alle Tests" || testType == "Geschwindigkeitstest") && !cancellationTokenSource.IsCancellationRequested)
                {
                    string testFile = Path.Combine(drive, "sdtest.tmp");
                    if (IncludeWriteTestCheckBox.IsChecked == true || IncludeReadTestCheckBox.IsChecked == true)
                    {
                        var (write, read) = await MeasureSpeedAsync(testFile, IncludeWriteTestCheckBox.IsChecked == true, IncludeReadTestCheckBox.IsChecked == true);
                        if (write >= 0) AppendOutput($"Schreibgeschwindigkeit: {write:F2} MB/s\n");
                        if (read >= 0) AppendOutput($"Lesegeschwindigkeit: {read:F2} MB/s\n");
                    }
                }

                StatusText.Text = "Test abgeschlossen";
                StatusText.Background = Brushes.LightGreen;
                LastTestTimeText.Text = $"Letzter Test: {DateTime.Now}";
            }
            catch (OperationCanceledException)
            {
                AppendOutput("Test abgebrochen.\n");
                StatusText.Text = "Test abgebrochen";
                StatusText.Background = Brushes.OrangeRed;
            }
            catch (Exception ex)
            {
                AppendOutput($"Fehler: {ex.Message}\n");
                StatusText.Text = "Fehler";
                StatusText.Background = Brushes.Red;
                log.Error("Fehler beim Test", ex);
            }
            finally
            {
                CancelButton.IsEnabled = false;
                StartButton.IsEnabled = true;
                ProgressBar.Value = 100;
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            cancellationTokenSource?.Cancel();
        }

        private void SaveResultButton_Click(object sender, RoutedEventArgs e)
        {
            Microsoft.Win32.SaveFileDialog dlg = new Microsoft.Win32.SaveFileDialog
            {
                FileName = "Testergebnis",
                DefaultExt = ".txt",
                Filter = "Textdateien (*.txt)|*.txt"
            };
            if (dlg.ShowDialog() == true)
            {
                File.WriteAllText(dlg.FileName, OutputTextBox.Text);
            }
        }

        private async Task<(double writeMBps, double readMBps)> MeasureSpeedAsync(string filePath, bool write, bool read)
        {
            byte[] data = new byte[100 * 1024 * 1024];
            new Random().NextBytes(data);
            double writeSpeed = -1, readSpeed = -1;

            if (write)
            {
                var sw = Stopwatch.StartNew();
                await File.WriteAllBytesAsync(filePath, data);
                sw.Stop();
                writeSpeed = data.Length / 1024.0 / 1024.0 / sw.Elapsed.TotalSeconds;
            }

            if (read)
            {
                var sw = Stopwatch.StartNew();
                byte[] readData = await File.ReadAllBytesAsync(filePath);
                sw.Stop();
                readSpeed = readData.Length / 1024.0 / 1024.0 / sw.Elapsed.TotalSeconds;
            }

            File.Delete(filePath);
            return (writeSpeed, readSpeed);
        }

        private void PrintHardwareInfo(string driveLetter)
        {
            string letter = driveLetter.TrimEnd('\\');
            try
            {
                var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_LogicalDiskToPartition");
                foreach (var result in searcher.Get())
                {
                    var antecedent = result["Antecedent"].ToString();
                    var dependent = result["Dependent"].ToString();
                    if (dependent.Contains($"{letter}"))
                    {
                        var diskIndex = antecedent.Split(new[] { "Disk #", "," }, StringSplitOptions.None)[1];
                        var diskSearcher = new ManagementObjectSearcher($"SELECT * FROM Win32_DiskDrive WHERE Index = {diskIndex.Trim()}");
                        foreach (var disk in diskSearcher.Get())
                        {
                            AppendOutput($"Modell: {disk["Model"]}\n");
                            AppendOutput($"Hersteller: {disk["Manufacturer"]}\n");
                            AppendOutput($"Seriennummer: {disk["SerialNumber"]}\n");
                        }
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                AppendOutput($"Hardwareinfo-Fehler: {ex.Message}\n");
                log.Warn("Fehler bei Hardwareinfo", ex);
            }
        }

        private void AppendOutput(string text)
        {
            Dispatcher.Invoke(() => OutputTextBox.AppendText(text));
        }

        private string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }
    }
}
