﻿using log4net;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Security.Cryptography;
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
                var di = new DriveInfo(drive);

                if (testType == "Alle Tests" || testType == "Größenprüfung")
                {
                    AppendOutput($"Größe: {FormatBytes(di.TotalSize)}\n");
                    AppendOutput($"Freier Speicher: {FormatBytes(di.TotalFreeSpace)}\n");
                }

                if ((testType == "Alle Tests" || testType == "Hardwareprüfung") && !cancellationTokenSource.IsCancellationRequested)
                    await Task.Run(() => PrintHardwareInfo(drive), cancellationTokenSource.Token);

                double write = -1, read = -1;

                if ((testType == "Alle Tests" || testType == "Geschwindigkeitstest") && !cancellationTokenSource.IsCancellationRequested)
                {
                    string testFile = Path.Combine(drive, "sdtest.tmp");
                    if (IncludeWriteTestCheckBox.IsChecked == true || IncludeReadTestCheckBox.IsChecked == true)
                    {
                        (write, read) = await MeasureSpeedAsync(testFile, IncludeWriteTestCheckBox.IsChecked == true, IncludeReadTestCheckBox.IsChecked == true);
                        if (write >= 0) AppendOutput($"Schreibgeschwindigkeit: {write:F2} MB/s\n");
                        if (read >= 0) AppendOutput($"Lesegeschwindigkeit: {read:F2} MB/s\n");
                    }
                }

                if ((testType == "Alle Tests" || testType == "Validierung") && !cancellationTokenSource.IsCancellationRequested)
                {
                    AppendOutput("Starte Validierung mit Hashprüfung...\n");
                    string testFile = Path.Combine(drive, "sdvalidate.tmp");
                    byte[] data = new byte[10 * 1024 * 1024];
                    new Random().NextBytes(data);
                    var sha1 = SHA1.Create();
                    var expected = sha1.ComputeHash(data);
                    await File.WriteAllBytesAsync(testFile, data);
                    var readData = await File.ReadAllBytesAsync(testFile);
                    var actual = sha1.ComputeHash(readData);
                    File.Delete(testFile);
                    bool valid = expected.SequenceEqual(actual);
                    AppendOutput(valid ? "Validierung erfolgreich: Daten konsistent\n" : "Warnung: Daten nicht identisch – mögliche Fälschung\n");
                }

                if ((testType == "Alle Tests" || testType == "Fragmentierungsanalyse") && !cancellationTokenSource.IsCancellationRequested)
                {
                    AppendOutput("Führe Fragmentierungsanalyse durch...\n");
                    var fragOutput = await RunDefragAnalysis(drive);
                    AppendOutput(fragOutput);
                }

                // Heuristik für Fake-Karten
                if (!cancellationTokenSource.IsCancellationRequested)
                {
                    AppendOutput("Führe Heuristik zur Fake-Erkennung durch...\n");
                    if (di.TotalSize > 64L * 1024 * 1024 * 1024 && write > 0 && write < 5.0)
                    {
                        AppendOutput("Verdacht: Große Kapazität aber langsame Schreibgeschwindigkeit – evtl. Fälschung\n");
                    }

                    var model = GetDriveModelFromLetter(drive);
                    if (!string.IsNullOrEmpty(model) && !model.ToLower().Contains("sandisk") && !model.ToLower().Contains("samsung") && !model.ToLower().Contains("kingston"))
                    {
                        AppendOutput("Warnung: Modellname nicht als Markenhersteller erkennbar\n");
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

        private string GetDriveModelFromLetter(string driveLetter)
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
                            return disk["Model"]?.ToString() ?? string.Empty;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                log.Warn("Fehler bei Modellermittlung", ex);
            }
            return string.Empty;
        }

        private async Task<string> RunDefragAnalysis(string drive)
        {
            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "defrag.exe",
                        Arguments = $"{drive.TrimEnd('\\')} /A /V",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                string output = await process.StandardOutput.ReadToEndAsync();
                process.WaitForExit();
                return output;
            }
            catch (Exception ex)
            {
                log.Warn("Fehler bei Fragmentierungsanalyse", ex);
                return $"Fehler bei Fragmentierungsanalyse: {ex.Message}\n";
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
                            AppendOutput($"DeviceID: {disk["DeviceID"]}\n");
                            AppendOutput($"PNPDeviceID: {disk["PNPDeviceID"]}\n");
                            AppendOutput($"Interface: {disk["InterfaceType"]}\n");
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

        private async void InspectFlashButton_Click(object sender, RoutedEventArgs e)
        {
            await RunFlashInspectorAsync();
        }

        private async Task RunFlashInspectorAsync()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "FlashChipInspector.exe", // Pfad ggf. absolut angeben
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = new Process { StartInfo = psi };
                process.Start();

                string output = await process.StandardOutput.ReadToEndAsync();
                process.WaitForExit();

                AppendOutput("FlashChipInspector output:\n" + output);
            }
            catch (Exception ex)
            {
                AppendOutput("Error running FlashChipInspector: " + ex.Message);
            }
        }

    }
}
