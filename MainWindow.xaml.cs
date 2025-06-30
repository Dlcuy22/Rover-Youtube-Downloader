using System;
using System.IO;
using System.Windows;
using System.Threading;
using System.Threading.Tasks;
using Rover.Service;
using WinForms = System.Windows.Forms;
using Rover.Utils;

namespace Rover
{
    /// <summary>
    /// Main window for the Rover YouTube downloader application.
    /// Handles user interface interactions and coordinates download operations.
    /// </summary>
    public partial class MainWindow : Window
    {
        #region Private Fields

        private ytdlpService _ytdlpService;
        private DownloadOptions _downloadOptions;
        private CancellationTokenSource? _cancellationTokenSource;
        private ModifyFileDate _fileModifier;

        #endregion

        #region Constructor and Initialization

        public MainWindow()
        {
            InitializeComponent();
            InitializeServices();
        }

        /// <summary>
        /// Initializes all required services and sets up the initial UI state.
        /// </summary>
        private void InitializeServices()
        {
            _ytdlpService = new ytdlpService();
            _downloadOptions = new DownloadOptions();
            _fileModifier = new ModifyFileDate();
            
            // Display the current output path
            OutputPathTextBox.Text = _downloadOptions.OutputPath;
            
            // Configure initial button states
            UpdateDownloadButtonState();
        }

        #endregion

        #region UI State Management

        /// <summary>
        /// Updates the download button state based on whether a URL has been entered.
        /// </summary>
        private void UpdateDownloadButtonState()
        {
            DownloadButton.IsEnabled = !string.IsNullOrWhiteSpace(UrlTextBox.Text);
        }

        /// <summary>
        /// Resets the UI to its default state after a download completes or fails.
        /// </summary>
        private void ResetUIAfterDownload()
        {
            // Re-enable download button if URL is present
            DownloadButton.IsEnabled = !string.IsNullOrWhiteSpace(UrlTextBox.Text);
            CancelButton.IsEnabled = false;
            ProgressBar.IsIndeterminate = false;
            
            // Clean up event subscriptions to prevent memory leaks
            _ytdlpService.ProgressChanged -= OnProgressChanged;
            _ytdlpService.DownloadCompleted -= OnDownloadCompleted;
            _ytdlpService.DownloadFailed -= OnDownloadFailed;
            
            // Dispose of cancellation token resources
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }

        #endregion

        #region Event Handlers - UI Controls

        /// <summary>
        /// Opens a folder browser dialog to select the download destination.
        /// </summary>
        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            using (var folderDialog = new WinForms.FolderBrowserDialog())
            {
                folderDialog.Description = "Select Download Folder";
                folderDialog.ShowNewFolderButton = true;
                
                // Start from current output path if it exists
                if (Directory.Exists(_downloadOptions.OutputPath))
                {
                    folderDialog.SelectedPath = Path.GetFullPath(_downloadOptions.OutputPath);
                }

                if (folderDialog.ShowDialog() == WinForms.DialogResult.OK)
                {
                    string selectedPath = folderDialog.SelectedPath;
                    
                    // Update download configuration and UI
                    _downloadOptions.OutputPath = selectedPath;
                    OutputPathTextBox.Text = selectedPath;
                    
                    // Persist the selected path to user settings
                    SaveDownloadPathToSettings(selectedPath);
                }
            }
        }

        /// <summary>
        /// Handles changes to the download format selection (Video/Audio).
        /// Updates UI visibility and download options accordingly.
        /// </summary>
        private void FormatRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (_downloadOptions == null || sender is not System.Windows.Controls.RadioButton radioButton)
                return;

            switch (radioButton.Name)
            {
                case "VideoRadio":
                    _downloadOptions.Format = DownloadFormat.VideoMP4;
                    VideoQualityPanel.Visibility = Visibility.Visible;
                    break;
                    
                case "AudioRadio":
                    _downloadOptions.Format = DownloadFormat.AudioM4A;
                    VideoQualityPanel.Visibility = Visibility.Collapsed;
                    break;
            }
        }

        /// <summary>
        /// Handles video quality selection changes.
        /// </summary>
        private void VideoQualityRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (_downloadOptions == null || 
                sender is not System.Windows.Controls.RadioButton radioButton || 
                radioButton.Tag == null)
                return;

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

        /// <summary>
        /// Handles audio quality selection changes.
        /// </summary>
        private void AudioQualityRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (_downloadOptions == null || 
                sender is not System.Windows.Controls.RadioButton radioButton || 
                radioButton.Tag == null)
                return;

            _downloadOptions.AudioBitrate = radioButton.Tag.ToString() switch
            {
                "Low" => AudioBitrate.Low,
                "Medium" => AudioBitrate.Medium,
                "High" => AudioBitrate.High,
                "VeryHigh" => AudioBitrate.VeryHigh,
                _ => AudioBitrate.Medium
            };
        }

        /// <summary>
        /// Handles URL text box changes to update button states.
        /// </summary>
        private void UrlTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            UpdateDownloadButtonState();
        }

        #endregion

        #region Event Handlers - Download Operations

        /// <summary>
        /// Initiates the download process with the specified URL and options.
        /// </summary>
        private async void DownloadButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string youtubeLink = UrlTextBox.Text.Trim();
                
                // Validate input
                if (!ValidateInput(youtubeLink))
                    return;

                // Prepare download environment
                if (!PrepareDownloadEnvironment())
                    return;

                // Configure download options from UI selections
                ConfigureDownloadOptions();

                // Set up download monitoring
                SetupDownloadMonitoring();

                // Update UI for download start
                UpdateUIForDownloadStart(youtubeLink);

                // Start the download process
                await _ytdlpService.DownloadYoutubeAsync(youtubeLink, _downloadOptions, _cancellationTokenSource.Token);
            }
            catch (OperationCanceledException)
            {
                HandleDownloadCancellation();
            }
            catch (Exception ex)
            {
                HandleDownloadError(ex);
            }
        }

        /// <summary>
        /// Cancels the current download operation and kills the yt-dlp process.
        /// </summary>
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Signal cancellation
                _cancellationTokenSource?.Cancel();
                _ytdlpService.CancelDownload();
                
                // Update UI immediately
                UpdateUIForCancellation();
                
                // Log the cancellation
                LogMessage("Download cancelled by user");
            }
            catch (Exception ex)
            {
                LogMessage($"Error cancelling download: {ex.Message}");
            }
        }

        #endregion

        #region Event Handlers - Download Progress

        /// <summary>
        /// Handles progress updates from the download service.
        /// Updates progress bars, status text, and log entries.
        /// </summary>
        private void OnProgressChanged(object? sender, DownloadProgress progress)
        {
            Dispatcher.Invoke(() =>
            {
                try
                {
                    UpdateProgressDisplay(progress);
                    UpdateStatusDisplay(progress);
                    LogProgressUpdate(progress);
                }
                catch (Exception ex)
                {
                    LogMessage($"Progress update error: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// Handles successful download completion.
        /// Performs post-download file modifications and updates UI.
        /// </summary>
        private void OnDownloadCompleted(object? sender, string message)
        {
            Dispatcher.Invoke(() =>
            {
                try
                {
                    // Update UI to show completion
                    UpdateUIForCompletion();
                    
                    // Log completion
                    LogMessage("Download completed successfully!");
                    if (!string.IsNullOrEmpty(message))
                        LogMessage(message);
                    
                    // Perform post-download file modifications
                    PerformPostDownloadTasks();
                    
                    // Clean up and show completion message
                    ResetUIAfterDownload();
                    ShowCompletionMessage();
                }
                catch (Exception ex)
                {
                    LogMessage($"Completion handler error: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// Handles download failures and errors.
        /// </summary>
        private void OnDownloadFailed(object? sender, string error)
        {
            Dispatcher.Invoke(() =>
            {
                try
                {
                    // Update UI to show failure state
                    UpdateUIForFailure();
                    
                    // Log the error
                    LogMessage($"Download failed: {error}");
                    
                    // Clean up
                    ResetUIAfterDownload();
                    
                    // Show error message (unless it was a user cancellation)
                    if (!IsUserCancellation(error))
                    {
                        ShowErrorMessage(error);
                    }
                }
                catch (Exception ex)
                {
                    LogMessage($"Error handler error: {ex.Message}");
                }
            });
        }

        #endregion

        #region Helper Methods - Validation

        /// <summary>
        /// Validates the YouTube URL input.
        /// </summary>
        private bool ValidateInput(string youtubeLink)
        {
            if (string.IsNullOrWhiteSpace(youtubeLink))
            {
                System.Windows.MessageBox.Show("Please enter a YouTube URL", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (!IsValidYouTubeUrl(youtubeLink))
            {
                System.Windows.MessageBox.Show("Please enter a valid YouTube URL", "Invalid URL", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Performs basic YouTube URL validation.
        /// </summary>
        private bool IsValidYouTubeUrl(string url)
        {
            return url.Contains("youtube.com/watch") ||
                   url.Contains("youtu.be/") ||
                   url.Contains("youtube.com/playlist") ||
                   url.Contains("youtube.com/shorts/") ||
                   url.Contains("music.youtube.com/watch");
        }

        #endregion

        #region Helper Methods - Download Setup

        /// <summary>
        /// Ensures the output directory exists and is accessible.
        /// </summary>
        private bool PrepareDownloadEnvironment()
        {
            try
            {
                if (!Directory.Exists(_downloadOptions.OutputPath))
                {
                    Directory.CreateDirectory(_downloadOptions.OutputPath);
                }
                return true;
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Could not create output directory: {ex.Message}", 
                    "Directory Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        /// <summary>
        /// Configures download options based on current UI selections.
        /// </summary>
        private void ConfigureDownloadOptions()
        {
            _downloadOptions.Format = AudioRadio?.IsChecked == true ? 
                DownloadFormat.AudioM4A : DownloadFormat.VideoMP4;
            
            _downloadOptions.AudioBitrate = GetSelectedAudioBitrate();
            _downloadOptions.VideoResolution = GetSelectedVideoResolution();
        }

        /// <summary>
        /// Sets up event handlers for download monitoring.
        /// </summary>
        private void SetupDownloadMonitoring()
        {
            _ytdlpService.ProgressChanged += OnProgressChanged;
            _ytdlpService.DownloadCompleted += OnDownloadCompleted;
            _ytdlpService.DownloadFailed += OnDownloadFailed;
            
            _cancellationTokenSource = new CancellationTokenSource();
        }

        #endregion

        #region Helper Methods - UI Updates

        /// <summary>
        /// Updates the UI when a download starts.
        /// </summary>
        private void UpdateUIForDownloadStart(string youtubeLink)
        {
            DownloadButton.IsEnabled = false;
            CancelButton.IsEnabled = true;
            StatusTextBlock.Text = "Starting download...";
            ProgressTextBlock.Text = "Initializing...";
            ProgressBar.IsIndeterminate = true;
            ProgressBar.Value = 0;
            ProgressPercentageTextBlock.Text = "";
            SpeedTextBlock.Text = "";
            ETATextBlock.Text = "";
            
            // Clear log and add initial entries
            LogMessage($"Starting download for: {youtubeLink}");
            LogMessage($"Format: {_downloadOptions.Format}");
            LogMessage($"Quality: {GetQualityDisplayText()}");
            LogMessage($"Output: {_downloadOptions.OutputPath}");
        }

        /// <summary>
        /// Updates progress display elements.
        /// </summary>
        private void UpdateProgressDisplay(DownloadProgress progress)
        {
            if (progress.Percentage >= 0)
            {
                double percentage = Math.Min(100, Math.Max(0, progress.Percentage));
                ProgressBar.Value = percentage;
                ProgressBar.IsIndeterminate = false;
                ProgressPercentageTextBlock.Text = $"{percentage:F1}%";
                
                if (!string.IsNullOrEmpty(progress.Speed))
                    SpeedTextBlock.Text = $"Speed: {progress.Speed}";
                
                if (!string.IsNullOrEmpty(progress.ETA))
                    ETATextBlock.Text = $"ETA: {progress.ETA}";
            }
            else
            {
                ProgressBar.IsIndeterminate = true;
            }
        }

        /// <summary>
        /// Updates status display based on progress information.
        /// </summary>
        private void UpdateStatusDisplay(DownloadProgress progress)
        {
            StatusTextBlock.Text = progress.Status ?? "Downloading...";
            ProgressTextBlock.Text = progress.Status ?? (progress.Percentage >= 0 ? "Downloading..." : "Processing...");
        }

        /// <summary>
        /// Updates UI for download completion.
        /// </summary>
        private void UpdateUIForCompletion()
        {
            ProgressBar.Value = 100;
            ProgressBar.IsIndeterminate = false;
            ProgressTextBlock.Text = "Download completed!";
            ProgressPercentageTextBlock.Text = "100%";
            StatusTextBlock.Text = "Download completed successfully";
            SpeedTextBlock.Text = "";
            ETATextBlock.Text = "";
        }

        /// <summary>
        /// Updates UI for download failure.
        /// </summary>
        private void UpdateUIForFailure()
        {
            ProgressBar.IsIndeterminate = false;
            ProgressTextBlock.Text = "Download failed";
            StatusTextBlock.Text = "Download failed";
            ProgressPercentageTextBlock.Text = "";
            SpeedTextBlock.Text = "";
            ETATextBlock.Text = "";
        }

        /// <summary>
        /// Updates UI for download cancellation.
        /// </summary>
        private void UpdateUIForCancellation()
        {
            CancelButton.IsEnabled = false;
            DownloadButton.IsEnabled = true;
            StatusTextBlock.Text = "Cancelling download...";
            ProgressTextBlock.Text = "Cancelling...";
            ProgressBar.Value = 0;
            ProgressBar.IsIndeterminate = false;
        }

        #endregion

        #region Helper Methods - Settings and Quality

        /// <summary>
        /// Retrieves the currently selected audio bitrate.
        /// </summary>
        private AudioBitrate GetSelectedAudioBitrate()
        {
            if (AudioQuality128?.IsChecked == true) return AudioBitrate.Low;
            if (AudioQuality192?.IsChecked == true) return AudioBitrate.Medium;
            if (AudioQuality256?.IsChecked == true) return AudioBitrate.High;
            if (AudioQuality320?.IsChecked == true) return AudioBitrate.VeryHigh;
            
            return AudioBitrate.Medium; // Default fallback
        }

        /// <summary>
        /// Retrieves the currently selected video resolution.
        /// </summary>
        private VideoResolution GetSelectedVideoResolution()
        {
            if (VideoQuality480?.IsChecked == true) return VideoResolution.SD480;
            if (VideoQuality720?.IsChecked == true) return VideoResolution.HD720;
            if (VideoQuality1080?.IsChecked == true) return VideoResolution.HD1080;
            if (VideoQuality1440?.IsChecked == true) return VideoResolution.UHD1440;
            if (VideoQuality2160?.IsChecked == true) return VideoResolution.UHD2160;
            
            return VideoResolution.HD720; // Default fallback
        }

        /// <summary>
        /// Gets a human-readable quality description for logging.
        /// </summary>
        private string GetQualityDisplayText()
        {
            return _downloadOptions.Format == DownloadFormat.VideoMP4 
                ? _downloadOptions.VideoResolution.ToString() 
                : _downloadOptions.AudioBitrate.ToString();
        }

        /// <summary>
        /// Saves the selected download path to user settings.
        /// </summary>
        private void SaveDownloadPathToSettings(string path)
        {
            try
            {
                Properties.Settings.Default.DownloadPath = path;
                Properties.Settings.Default.Save();
            }
            catch
            {
                // Silently ignore settings save errors - not critical
            }
        }

        #endregion

        #region Helper Methods - Post-Download Tasks

        /// <summary>
        /// Performs file modification tasks after successful download.
        /// </summary>
        private void PerformPostDownloadTasks()
        {
            try
            {
                string? downloadedFilePath = _ytdlpService.GetDownloadedFilePath();
                
                if (!string.IsNullOrEmpty(downloadedFilePath) && File.Exists(downloadedFilePath))
                {
                    _fileModifier.ModifyFile(downloadedFilePath);
                    LogMessage($"File date modified successfully for: {Path.GetFileName(downloadedFilePath)}");
                }
                else
                {
                    LogMessage("Warning: Could not locate downloaded file for date modification");
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Error modifying file date: {ex.Message}");
            }
        }

        #endregion

        #region Helper Methods - Error Handling

        /// <summary>
        /// Handles download cancellation scenarios.
        /// </summary>
        private void HandleDownloadCancellation()
        {
            StatusTextBlock.Text = "Download cancelled";
            ProgressTextBlock.Text = "Cancelled";
            LogMessage("Download was cancelled");
            ResetUIAfterDownload();
        }

        /// <summary>
        /// Handles general download errors.
        /// </summary>
        private void HandleDownloadError(Exception ex)
        {
            LogMessage($"Error: {ex.Message}");
            System.Windows.MessageBox.Show($"Error: {ex.Message}", "Download Error", 
                MessageBoxButton.OK, MessageBoxImage.Error);
            ResetUIAfterDownload();
        }

        /// <summary>
        /// Determines if an error message indicates user cancellation.
        /// </summary>
        private bool IsUserCancellation(string error)
        {
            return error.Contains("cancel", StringComparison.OrdinalIgnoreCase) || 
                   error.Contains("abort", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Shows the download completion message to the user.
        /// </summary>
        private void ShowCompletionMessage()
        {
            System.Windows.MessageBox.Show($"Download completed successfully!\n\nSaved to: {_downloadOptions.OutputPath}", 
                "Download Complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        /// <summary>
        /// Shows an error message for download failures.
        /// </summary>
        private void ShowErrorMessage(string error)
        {
            System.Windows.MessageBox.Show($"Download failed:\n\n{error}", "Download Error", 
                MessageBoxButton.OK, MessageBoxImage.Error);
        }

        #endregion

        #region Helper Methods - Logging

        /// <summary>
        /// Adds a timestamped message to the log and auto-scrolls.
        /// </summary>
        private void LogMessage(string message)
        {
            LogTextBlock.Text += $"{DateTime.Now:HH:mm:ss} - {message}\n";
            LogScrollViewer.ScrollToBottom();
        }

        /// <summary>
        /// Logs progress updates with percentage information when available.
        /// </summary>
        private void LogProgressUpdate(DownloadProgress progress)
        {
            if (!string.IsNullOrEmpty(progress.Status))
            {
                string logEntry = progress.Status;
                if (progress.Percentage >= 0)
                {
                    double percentage = Math.Min(100, Math.Max(0, progress.Percentage));
                    logEntry += $" ({percentage:F1}%)";
                }
                LogMessage(logEntry);
            }
        }

        #endregion

        #region Window Lifecycle Events

        /// <summary>
        /// Handles window loaded event - initializes settings and UI state.
        /// </summary>
        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                LoadUserSettings();
                SetupEventHandlers();
                
                // Set initial focus to URL input
                UrlTextBox.Focus();
            }
            catch (Exception ex)
            {
                LogMessage($"Settings load error: {ex.Message}");
            }
        }

        /// <summary>
        /// Loads user settings and ensures download directory exists.
        /// </summary>
        private void LoadUserSettings()
        {
            // Load saved download path
            if (!string.IsNullOrEmpty(Properties.Settings.Default.DownloadPath))
            {
                _downloadOptions.OutputPath = Properties.Settings.Default.DownloadPath;
                OutputPathTextBox.Text = _downloadOptions.OutputPath;
            }
            
            // Ensure download directory exists, with fallback options
            EnsureDownloadDirectoryExists();
        }

        /// <summary>
        /// Ensures the download directory exists, creating fallback directories if necessary.
        /// </summary>
        private void EnsureDownloadDirectoryExists()
        {
            if (!Directory.Exists(_downloadOptions.OutputPath))
            {
                try
                {
                    Directory.CreateDirectory(_downloadOptions.OutputPath);
                }
                catch
                {
                    // Fallback to default video folder
                    _downloadOptions.OutputPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Downloads");
                    OutputPathTextBox.Text = _downloadOptions.OutputPath;
                    
                    if (!Directory.Exists(_downloadOptions.OutputPath))
                    {
                        try
                        {
                            Directory.CreateDirectory(_downloadOptions.OutputPath);
                        }
                        catch
                        {
                            // Last resort - use temp directory
                            _downloadOptions.OutputPath = Path.Combine(Path.GetTempPath(), "RoverDownloads");
                            OutputPathTextBox.Text = _downloadOptions.OutputPath;
                            Directory.CreateDirectory(_downloadOptions.OutputPath);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Sets up additional event handlers that couldn't be set in XAML.
        /// </summary>
        private void SetupEventHandlers()
        {
            UrlTextBox.TextChanged += UrlTextBox_TextChanged;
        }

        /// <summary>
        /// Handles window closing - performs cleanup of resources and ongoing operations.
        /// </summary>
        protected override void OnClosed(EventArgs e)
        {
            try
            {
                // Cancel any ongoing downloads
                _cancellationTokenSource?.Cancel();
                
                // Clean up services
                _ytdlpService?.Dispose();
                _cancellationTokenSource?.Dispose();
            }
            catch
            {
                // Ignore cleanup errors during shutdown
            }
            
            base.OnClosed(e);
        }

        #endregion
    }
}