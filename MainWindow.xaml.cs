// MainWindow.xaml.cs
using System;
using System.IO;
using System.Windows;
using System.Threading;
using System.Threading.Tasks;
using Rover.Service;
using WinForms = System.Windows.Forms;

namespace Rover
{
    public partial class MainWindow : Window
    {
        private ytdlpService _ytdlpService;
        private DownloadOptions _downloadOptions;
        private CancellationTokenSource? _cancellationTokenSource;

        public MainWindow()
        {
            InitializeComponent();
            InitializeServices();
        }

        private void InitializeServices()
        {
            _ytdlpService = new ytdlpService();
            _downloadOptions = new DownloadOptions();
            
            // Set initial path display
            OutputPathTextBox.Text = _downloadOptions.OutputPath;
            
            // Set up initial UI state
            UpdateDownloadButtonState();
        }

        private void UpdateDownloadButtonState()
        {
            DownloadButton.IsEnabled = !string.IsNullOrWhiteSpace(UrlTextBox.Text);
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            using (var folderDialog = new WinForms.FolderBrowserDialog())
            {
                folderDialog.Description = "Select Download Folder";
                folderDialog.ShowNewFolderButton = true;
                
                // Set initial directory to current output path if it exists
                if (Directory.Exists(_downloadOptions.OutputPath))
                {
                    folderDialog.SelectedPath = Path.GetFullPath(_downloadOptions.OutputPath);
                }

                if (folderDialog.ShowDialog() == WinForms.DialogResult.OK)
                {
                    string selectedPath = folderDialog.SelectedPath;
                    
                    // Update the download options
                    _downloadOptions.OutputPath = selectedPath;
                    
                    // Update UI display
                    OutputPathTextBox.Text = selectedPath;
                    
                    // Optional: Save to user preferences/settings
                    try
                    {
                        Properties.Settings.Default.DownloadPath = selectedPath;
                        Properties.Settings.Default.Save();
                    }
                    catch
                    {
                        // Ignore settings save errors
                    }
                }
            }
        }

        // Format radio button change handler
        private void FormatRadio_Checked(object sender, RoutedEventArgs e)
        {
            // Ensure _downloadOptions is initialized
            if (_downloadOptions == null)
                return;
    
            if (sender is System.Windows.Controls.RadioButton radioButton)
            {
                // Use the Name property to identify which radio button was clicked
                switch (radioButton.Name)
                {
                    case "VideoRadio":
                        _downloadOptions.Format = DownloadFormat.VideoMP4;
                        // Show video quality options, hide audio-only options
                        VideoQualityPanel.Visibility = Visibility.Visible;
                        break;
                    case "AudioRadio":
                        _downloadOptions.Format = DownloadFormat.AudioM4A;
                        // Hide video quality options for audio-only downloads
                        VideoQualityPanel.Visibility = Visibility.Collapsed;
                        break;
                }
            }
        }

        // Video quality radio button change handler
        private void VideoQualityRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (_downloadOptions == null)
                return;

            if (sender is System.Windows.Controls.RadioButton radioButton && radioButton.Tag != null)
            {
                _downloadOptions.VideoResolution = radioButton.Tag.ToString() switch
                {
                    "SD480" => VideoResolution.SD480,
                    "HD720" => VideoResolution.HD720,
                    "HD1080" => VideoResolution.HD1080,
                    "UHD1440" => VideoResolution.UHD1440,
                    "UHD2160" => VideoResolution.UHD2160,
                    _ => VideoResolution.HD720
                };
            }
        }

        // Audio quality radio button change handler
        private void AudioQualityRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (_downloadOptions == null)
                return;

            if (sender is System.Windows.Controls.RadioButton radioButton && radioButton.Tag != null)
            {
                _downloadOptions.AudioBitrate = radioButton.Tag.ToString() switch
                {
                    "Low" => AudioBitrate.Low,
                    "Medium" => AudioBitrate.Medium,
                    "High" => AudioBitrate.High,
                    "VeryHigh" => AudioBitrate.VeryHigh,
                    _ => AudioBitrate.Medium
                };
            }
        }

        // URL TextBox change handler
        private void UrlTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            UpdateDownloadButtonState();
        }

        // Cancel button click handler
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Cancel the token source
                _cancellationTokenSource?.Cancel();
                
                // Kill the yt-dlp process
                _ytdlpService.CancelDownload();
                
                // Update UI immediately
                CancelButton.IsEnabled = false;
                DownloadButton.IsEnabled = true;
                
                StatusTextBlock.Text = "Cancelling download...";
                ProgressTextBlock.Text = "Cancelling...";
                ProgressBar.Value = 0;
                ProgressBar.IsIndeterminate = false;
                
                // Add to log
                LogTextBlock.Text += $"{DateTime.Now:HH:mm:ss} - Download cancelled by user\n";
                LogScrollViewer.ScrollToBottom();
            }
            catch (Exception ex)
            {
                LogTextBlock.Text += $"{DateTime.Now:HH:mm:ss} - Error cancelling download: {ex.Message}\n";
                LogScrollViewer.ScrollToBottom();
            }
        }

        // Download button click handler
        private async void DownloadButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string youtubeLink = UrlTextBox.Text.Trim();
                
                if (string.IsNullOrWhiteSpace(youtubeLink))
                {
                    System.Windows.MessageBox.Show("Please enter a YouTube URL", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Basic URL validation
                if (!IsValidYouTubeUrl(youtubeLink))
                {
                    System.Windows.MessageBox.Show("Please enter a valid YouTube URL", "Invalid URL", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Ensure output directory exists
                if (!Directory.Exists(_downloadOptions.OutputPath))
                {
                    Directory.CreateDirectory(_downloadOptions.OutputPath);
                }

                // Set download options based on UI selections
                _downloadOptions.Format = AudioRadio?.IsChecked == true ? 
                    DownloadFormat.AudioM4A : DownloadFormat.VideoMP4;
                
                _downloadOptions.AudioBitrate = GetSelectedAudioBitrate();
                _downloadOptions.VideoResolution = GetSelectedVideoResolution();

                // Subscribe to progress events
                _ytdlpService.ProgressChanged += OnProgressChanged;
                _ytdlpService.DownloadCompleted += OnDownloadCompleted;
                _ytdlpService.DownloadFailed += OnDownloadFailed;

                // Set up cancellation
                _cancellationTokenSource = new CancellationTokenSource();

                // Start download
                DownloadButton.IsEnabled = false;
                CancelButton.IsEnabled = true;
                StatusTextBlock.Text = "Starting download...";
                ProgressTextBlock.Text = "Initializing...";
                ProgressBar.IsIndeterminate = true;
                ProgressBar.Value = 0;
                ProgressPercentageTextBlock.Text = "";
                SpeedTextBlock.Text = "";
                ETATextBlock.Text = "";
                
                // Clear log and add initial entry
                LogTextBlock.Text = $"{DateTime.Now:HH:mm:ss} - Starting download for: {youtubeLink}\n";
                LogTextBlock.Text += $"{DateTime.Now:HH:mm:ss} - Format: {_downloadOptions.Format}\n";
                LogTextBlock.Text += $"{DateTime.Now:HH:mm:ss} - Quality: {(_downloadOptions.Format == DownloadFormat.VideoMP4 ? _downloadOptions.VideoResolution.ToString() : _downloadOptions.AudioBitrate.ToString())}\n";
                LogTextBlock.Text += $"{DateTime.Now:HH:mm:ss} - Output: {_downloadOptions.OutputPath}\n";
                LogScrollViewer.ScrollToBottom();
                
                await _ytdlpService.DownloadYoutubeAsync(youtubeLink, _downloadOptions, _cancellationTokenSource.Token);
            }
            catch (OperationCanceledException)
            {
                // Handle cancellation
                StatusTextBlock.Text = "Download cancelled";
                ProgressTextBlock.Text = "Cancelled";
                LogTextBlock.Text += $"{DateTime.Now:HH:mm:ss} - Download was cancelled\n";
                LogScrollViewer.ScrollToBottom();
                ResetUIAfterDownload();
            }
            catch (Exception ex)
            {
                LogTextBlock.Text += $"{DateTime.Now:HH:mm:ss} - Error: {ex.Message}\n";
                LogScrollViewer.ScrollToBottom();
                System.Windows.MessageBox.Show($"Error: {ex.Message}", "Download Error", MessageBoxButton.OK, MessageBoxImage.Error);
                ResetUIAfterDownload();
            }
        }

        private bool IsValidYouTubeUrl(string url)
        {
            // Basic YouTube URL validation
            return url.Contains("youtube.com/watch") || 
                   url.Contains("youtu.be/") || 
                   url.Contains("youtube.com/playlist") ||
                   url.Contains("youtube.com/shorts/");
        }

        private void ResetUIAfterDownload()
        {
            DownloadButton.IsEnabled = !string.IsNullOrWhiteSpace(UrlTextBox.Text);
            CancelButton.IsEnabled = false;
            ProgressBar.IsIndeterminate = false;
            
            // Unsubscribe from events to prevent memory leaks
            _ytdlpService.ProgressChanged -= OnProgressChanged;
            _ytdlpService.DownloadCompleted -= OnDownloadCompleted;
            _ytdlpService.DownloadFailed -= OnDownloadFailed;
            
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }

        // Get selected audio bitrate from radio buttons
        private AudioBitrate GetSelectedAudioBitrate()
        {
            if (AudioQuality128?.IsChecked == true) return AudioBitrate.Low;
            if (AudioQuality192?.IsChecked == true) return AudioBitrate.Medium;
            if (AudioQuality256?.IsChecked == true) return AudioBitrate.High;
            if (AudioQuality320?.IsChecked == true) return AudioBitrate.VeryHigh;
            
            return AudioBitrate.Medium; // Default
        }

        // Get selected video resolution from radio buttons
        private VideoResolution GetSelectedVideoResolution()
        {
            if (VideoQuality480?.IsChecked == true) return VideoResolution.SD480;
            if (VideoQuality720?.IsChecked == true) return VideoResolution.HD720;
            if (VideoQuality1080?.IsChecked == true) return VideoResolution.HD1080;
            if (VideoQuality1440?.IsChecked == true) return VideoResolution.UHD1440;
            if (VideoQuality2160?.IsChecked == true) return VideoResolution.UHD2160;
            
            return VideoResolution.HD720; // Default
        }

        // Event handlers for ytdlp service
        private void OnProgressChanged(object? sender, DownloadProgress progress)
        {
            Dispatcher.Invoke(() =>
            {
                try
                {
                    if (progress.Percentage >= 0)
                    {
                        // Ensure percentage is between 0-100
                        double percentage = Math.Min(100, Math.Max(0, progress.Percentage));
                        
                        ProgressBar.Value = percentage;
                        ProgressBar.IsIndeterminate = false;
                        ProgressTextBlock.Text = progress.Status ?? "Downloading...";
                        ProgressPercentageTextBlock.Text = $"{percentage:F1}%";
                        
                        if (!string.IsNullOrEmpty(progress.Speed))
                            SpeedTextBlock.Text = $"Speed: {progress.Speed}";
                        
                        if (!string.IsNullOrEmpty(progress.ETA))
                            ETATextBlock.Text = $"ETA: {progress.ETA}";
                    }
                    else
                    {
                        ProgressTextBlock.Text = progress.Status ?? "Processing...";
                        ProgressBar.IsIndeterminate = true;
                    }

                    // Update status
                    StatusTextBlock.Text = progress.Status ?? "Downloading...";
                    
                    // Add to log (avoid duplicating percentage info)
                    if (!string.IsNullOrEmpty(progress.Status))
                    {
                        string logEntry = $"{DateTime.Now:HH:mm:ss} - {progress.Status}";
                        if (progress.Percentage >= 0)
                        {
                            double percentage = Math.Min(100, Math.Max(0, progress.Percentage));
                            logEntry += $" ({percentage:F1}%)";
                        }
                        LogTextBlock.Text += logEntry + "\n";
                        
                        // Auto-scroll log
                        LogScrollViewer.ScrollToBottom();
                    }
                }
                catch (Exception ex)
                {
                    LogTextBlock.Text += $"{DateTime.Now:HH:mm:ss} - Progress update error: {ex.Message}\n";
                    LogScrollViewer.ScrollToBottom();
                }
            });
        }

        private void OnDownloadCompleted(object? sender, string message)
        {
            Dispatcher.Invoke(() =>
            {
                try
                {
                    ProgressBar.Value = 100;
                    ProgressBar.IsIndeterminate = false;
                    ProgressTextBlock.Text = "Download completed!";
                    ProgressPercentageTextBlock.Text = "100%";
                    StatusTextBlock.Text = "Download completed successfully";
                    SpeedTextBlock.Text = "";
                    ETATextBlock.Text = "";
                    
                    // Add to log
                    LogTextBlock.Text += $"{DateTime.Now:HH:mm:ss} - Download completed successfully!\n";
                    if (!string.IsNullOrEmpty(message))
                    {
                        LogTextBlock.Text += $"{DateTime.Now:HH:mm:ss} - {message}\n";
                    }
                    LogScrollViewer.ScrollToBottom();
                    
                    ResetUIAfterDownload();
                    
                    System.Windows.MessageBox.Show($"Download completed successfully!\n\nSaved to: {_downloadOptions.OutputPath}", 
                        "Download Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    LogTextBlock.Text += $"{DateTime.Now:HH:mm:ss} - Completion handler error: {ex.Message}\n";
                    LogScrollViewer.ScrollToBottom();
                }
            });
        }

        private void OnDownloadFailed(object? sender, string error)
        {
            Dispatcher.Invoke(() =>
            {
                try
                {
                    ProgressBar.IsIndeterminate = false;
                    ProgressTextBlock.Text = "Download failed";
                    StatusTextBlock.Text = "Download failed";
                    ProgressPercentageTextBlock.Text = "";
                    SpeedTextBlock.Text = "";
                    ETATextBlock.Text = "";
                    
                    // Add to log
                    LogTextBlock.Text += $"{DateTime.Now:HH:mm:ss} - Download failed: {error}\n";
                    LogScrollViewer.ScrollToBottom();
                    
                    ResetUIAfterDownload();
                    
                    // Only show message box if it's not a cancellation
                    if (!error.Contains("cancel", StringComparison.OrdinalIgnoreCase) && 
                        !error.Contains("abort", StringComparison.OrdinalIgnoreCase))
                    {
                        System.Windows.MessageBox.Show($"Download failed:\n\n{error}", "Download Error", 
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                catch (Exception ex)
                {
                    LogTextBlock.Text += $"{DateTime.Now:HH:mm:ss} - Error handler error: {ex.Message}\n";
                    LogScrollViewer.ScrollToBottom();
                }
            });
        }

        // Load saved settings on startup
        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // Load saved download path from user settings
                if (!string.IsNullOrEmpty(Properties.Settings.Default.DownloadPath))
                {
                    _downloadOptions.OutputPath = Properties.Settings.Default.DownloadPath;
                    OutputPathTextBox.Text = _downloadOptions.OutputPath;
                }
                
                // Ensure the default download directory exists
                if (!Directory.Exists(_downloadOptions.OutputPath))
                {
                    try
                    {
                        Directory.CreateDirectory(_downloadOptions.OutputPath);
                    }
                    catch
                    {
                        // If we can't create the saved path, fall back to a default
                        _downloadOptions.OutputPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Downloads");
                        OutputPathTextBox.Text = _downloadOptions.OutputPath;
                        
                        if (!Directory.Exists(_downloadOptions.OutputPath))
                        {
                            Directory.CreateDirectory(_downloadOptions.OutputPath);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // If all else fails, use a temp directory
                _downloadOptions.OutputPath = Path.Combine(Path.GetTempPath(), "RoverDownloads");
                OutputPathTextBox.Text = _downloadOptions.OutputPath;
                
                LogTextBlock.Text += $"{DateTime.Now:HH:mm:ss} - Settings load error: {ex.Message}\n";
                LogScrollViewer.ScrollToBottom();
            }

            // Set up URL textbox event handler
            UrlTextBox.TextChanged += UrlTextBox_TextChanged;
            
            // Set initial focus to URL textbox
            UrlTextBox.Focus();
        }

        // Clean up resources when window is closing
        protected override void OnClosed(EventArgs e)
        {
            try
            {
                // Cancel any ongoing downloads
                _cancellationTokenSource?.Cancel();
                
                // Clean up service
                _ytdlpService?.Dispose();
                
                // Clean up cancellation token
                _cancellationTokenSource?.Dispose();
            }
            catch
            {
                // Ignore cleanup errors
            }
            
            base.OnClosed(e);
        }
    }
}