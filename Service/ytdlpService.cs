using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;

namespace Rover.Service
{
    /// <summary>
    /// Supported download formats for media content
    /// </summary>
    public enum DownloadFormat
    {
        AudioM4A,
        VideoMP4
    }

    /// <summary>
    /// Audio quality bitrate options in kbps
    /// </summary>
    public enum AudioBitrate
    {
        Low = 128,
        Medium = 192,
        High = 256,
        VeryHigh = 320
    }

    /// <summary>
    /// Video resolution options based on height in pixels
    /// </summary>
    public enum VideoResolution
    {
        SD480 = 480,
        HD720 = 720,
        HD1080 = 1080,
        UHD1440 = 1440,
        UHD2160 = 2160 
    }

    /// <summary>
    /// Configuration options for downloading media content
    /// </summary>
    public class DownloadOptions
    {
        /// <summary>
        /// The format to download (audio or video). Defaults to VideoMP4
        /// </summary>
        public DownloadFormat Format { get; set; } = DownloadFormat.VideoMP4;
        
        /// <summary>
        /// Audio quality bitrate. Defaults to Medium (192 kbps)
        /// </summary>
        public AudioBitrate AudioBitrate { get; set; } = AudioBitrate.Medium;
        
        /// <summary>
        /// Maximum video resolution to download. Defaults to HD720
        /// </summary>
        public VideoResolution VideoResolution { get; set; } = VideoResolution.HD720;
        
        /// <summary>
        /// Directory where downloaded files will be saved. Defaults to user's Downloads folder
        /// </summary>
        public string OutputPath { get; set; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads");
        
        
        /// <summary>
        /// Custom filename (without extension). If null, uses video title
        /// </summary>
        public string? CustomFilename { get; set; } = null;
    }

    /// <summary>
    /// Represents the current state of a download operation
    /// </summary>
    public class DownloadProgress
    {
        /// <summary>
        /// Download completion percentage (0-100)
        /// </summary>
        public double Percentage { get; set; }
        
        /// <summary>
        /// Current status message describing the download phase
        /// </summary>
        public string Status { get; set; } = string.Empty;
        
        /// <summary>
        /// Current download speed (e.g., "1.2MB/s")
        /// </summary>
        public string? Speed { get; set; }
        
        /// <summary>
        /// Estimated time remaining for completion
        /// </summary>
        public string? ETA { get; set; }
    }

    /// <summary>
    /// Service for downloading YouTube videos and audio using yt-dlp
    /// Provides async/sync methods with progress tracking and cancellation support
    /// </summary>
    public class ytdlpService : IDisposable
    {
        private readonly string _ytdlpBIN;
        private Process? _currentProcess;
        private bool _disposed = false;
        
        // Thread-safe tracking of the most recently downloaded file
        private string? _lastDownloadedFilePath;
        private readonly object _filePathLock = new object();
        
        /// <summary>
        /// Fired when download progress updates (percentage, speed, etc.)
        /// </summary>
        public event EventHandler<DownloadProgress>? ProgressChanged;
        
        /// <summary>
        /// Fired when a download completes successfully
        /// </summary>
        public event EventHandler<string>? DownloadCompleted;
        
        /// <summary>
        /// Fired when a download fails or is cancelled
        /// </summary>
        public event EventHandler<string>? DownloadFailed;

        /// <summary>
        /// Initializes the yt-dlp service with the specified executable path
        /// </summary>
        /// <param name="ytdlpPath">Path to yt-dlp executable. Defaults to "python/yt-dlp.exe"</param>
        /// <exception cref="FileNotFoundException">Thrown when yt-dlp executable is not found</exception>
        public ytdlpService(string ytdlpPath = "python/yt-dlp.exe")
        {
            _ytdlpBIN = ytdlpPath;
            
            if (!File.Exists(_ytdlpBIN))
            {
                throw new FileNotFoundException($"yt-dlp executable not found at: {_ytdlpBIN}");
            }
        }

        /// <summary>
        /// Gets the full path of the most recently downloaded file
        /// </summary>
        /// <returns>File path if available, null otherwise</returns>
        public string? GetDownloadedFilePath()
        {
            lock (_filePathLock)
            {
                return _lastDownloadedFilePath;
            }
        }

        /// <summary>
        /// Clears the stored downloaded file path from memory
        /// </summary>
        public void ClearDownloadedFilePath()
        {
            lock (_filePathLock)
            {
                _lastDownloadedFilePath = null;
            }
        }

        /// <summary>
        /// Downloads a YouTube video asynchronously with cancellation support
        /// </summary>
        /// <param name="link">YouTube video URL</param>
        /// <param name="options">Download configuration options</param>
        /// <param name="cancellationToken">Token to cancel the operation</param>
        /// <returns>Task representing the async operation</returns>
        /// <exception cref="OperationCanceledException">Thrown when download is cancelled</exception>
        public async Task DownloadYoutubeAsync(string link, DownloadOptions? options = null, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            options ??= new DownloadOptions();
            
            // Reset the file path tracker for this new download
            ClearDownloadedFilePath();
            
            try
            {
                await Task.Run(() => DownloadYoutube(link, options, cancellationToken), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                DownloadFailed?.Invoke(this, "Download was cancelled by user");
                throw;
            }
            catch (Exception ex)
            {
                DownloadFailed?.Invoke(this, ex.Message);
            }
        }

        /// <summary>
        /// Downloads a YouTube video synchronously
        /// </summary>
        /// <param name="link">YouTube video URL</param>
        /// <param name="options">Download configuration options</param>
        /// <param name="cancellationToken">Token to cancel the operation</param>
        /// <exception cref="ArgumentException">Thrown when link is empty or whitespace</exception>
        public void DownloadYoutube(string link, DownloadOptions? options = null, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            options ??= new DownloadOptions();
            
            if (string.IsNullOrWhiteSpace(link))
            {
                throw new ArgumentException("YouTube link cannot be empty", nameof(link));
            }

            // Create output directory if it doesn't exist
            Directory.CreateDirectory(options.OutputPath);

            string args = BuildArguments(link, options);
            ExecuteDownload(args, cancellationToken);
        }

        /// <summary>
        /// Constructs command-line arguments for yt-dlp based on download options
        /// </summary>
        /// <param name="link">YouTube video URL</param>
        /// <param name="options">Download configuration</param>
        /// <returns>Complete argument string for yt-dlp</returns>
        private string BuildArguments(string link, DownloadOptions options)
        {
            var args = $"\"{link}\"";
            
            // Set up output filename template
            string outputTemplate = Path.Combine(options.OutputPath, 
                string.IsNullOrWhiteSpace(options.CustomFilename) 
                    ? "%(title)s.%(ext)s"  // Use video title as filename
                    : $"{options.CustomFilename}.%(ext)s");  // Use custom filename
            
            args += $" -o \"{outputTemplate}\"";

            // Add ffmpeg location
            args += " --ffmpeg-location \"./python\"";

            // Configure format-specific download parameters
            switch (options.Format)
            {
                case DownloadFormat.AudioM4A:
                    args += " -f \"bestaudio[ext=m4a]/bestaudio\"";
                    args += " --extract-audio --audio-format m4a";
                    args += $" --audio-quality {(int)options.AudioBitrate}";
                    break;
                    
                case DownloadFormat.VideoMP4:
                    string videoFormat = GetVideoFormatString(options.VideoResolution);
                    string audioFormat = $"bestaudio[abr<={(int)options.AudioBitrate}]";
                    args += $" -f \"{videoFormat}+{audioFormat}/best[ext=mp4]/best\"";
                    args += " --merge-output-format mp4";
                    break;
            }

            // Add standard options for better user experience
            args += " --no-playlist";        // Download single video, not entire playlist
            args += " --embed-thumbnail";     // Include thumbnail in file metadata
            args += " --add-metadata";        // Add video metadata (title, uploader, etc.)
            args += " --progress";            // Show download progress
            args += " --newline";             // Better progress output formatting
            args += " --console-title";       // Update console title with progress
           

            return args;
        }

        /// <summary>
        /// Generates the video format selector string based on resolution preference
        /// </summary>
        /// <param name="resolution">Desired maximum video resolution</param>
        /// <returns>yt-dlp format selector string</returns>
        private string GetVideoFormatString(VideoResolution resolution)
        {
            return resolution switch
            {
                VideoResolution.SD480 => "best[height<=480][ext=mp4]",
                VideoResolution.HD720 => "best[height<=720][ext=mp4]", 
                VideoResolution.HD1080 => "best[height<=1080][ext=mp4]",
                VideoResolution.UHD1440 => "best[height<=1440][ext=mp4]",
                VideoResolution.UHD2160 => "best[height<=2160][ext=mp4]",
                _ => "best[height<=720][ext=mp4]"  // Default fallback
            };
        }

        /// <summary>
        /// Executes the yt-dlp process and handles output parsing
        /// </summary>
        /// <param name="args">Command-line arguments for yt-dlp</param>
        /// <param name="cancellationToken">Token to cancel the operation</param>
        private void ExecuteDownload(string args, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            
            using var process = new Process();
            _currentProcess = process;
            
            // Configure process to capture output and run hidden
            process.StartInfo = new ProcessStartInfo
            {
                FileName = _ytdlpBIN,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            // Set up event handlers for real-time output processing
            process.OutputDataReceived += (sender, e) => ParseOutput(e.Data);
            process.ErrorDataReceived += (sender, e) => ParseError(e.Data);

            try
            {
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                // Monitor process and handle cancellation requests
                while (!process.HasExited)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        try
                        {
                            process.Kill(true);  // Kill process tree
                        }
                        catch { /* Ignore kill errors */ }
                        
                        cancellationToken.ThrowIfCancellationRequested();
                    }
                    
                    Thread.Sleep(100);  // Check every 100ms
                }

                // Handle process completion
                if (process.ExitCode == 0)
                {
                    string downloadedFile = GetDownloadedFilePath() ?? "Unknown file";
                    DownloadCompleted?.Invoke(this, $"Download completed successfully: {Path.GetFileName(downloadedFile)}");
                }
                else if (cancellationToken.IsCancellationRequested)
                {
                    DownloadFailed?.Invoke(this, "Download was cancelled");
                }
                else
                {
                    DownloadFailed?.Invoke(this, $"Download failed with exit code: {process.ExitCode}");
                }
            }
            catch (OperationCanceledException)
            {
                DownloadFailed?.Invoke(this, "Download was cancelled");
                throw;
            }
            catch (Exception ex)
            {
                DownloadFailed?.Invoke(this, $"Error executing yt-dlp: {ex.Message}");
            }
            finally
            {
                _currentProcess = null;
            }
        }

        /// <summary>
        /// Forcefully cancels any running download operation
        /// </summary>
        public void CancelDownload()
        {
            try
            {
                if (_currentProcess != null && !_currentProcess.HasExited)
                {
                    _currentProcess.Kill(true);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error killing process: {ex.Message}");
            }
        }

        /// <summary>
        /// Parses yt-dlp output to extract progress information and downloaded file paths
        /// </summary>
        /// <param name="output">Raw output line from yt-dlp</param>
        private void ParseOutput(string? output)
        {
            if (string.IsNullOrWhiteSpace(output)) return;

            try
            {
                // Attempt to capture the downloaded file path
                // This handles cases where yt-dlp outputs the final file path
                if (!output.Contains("[download]") && !output.Contains("[ffmpeg]") && 
                    !output.Contains("Downloading") && !output.Contains("Extracting"))
                {
                    // Check if this looks like a file path
                    if (File.Exists(output.Trim()))
                    {
                        lock (_filePathLock)
                        {
                            _lastDownloadedFilePath = Path.GetFullPath(output.Trim());
                        }
                        return;
                    }
                }

                // Parse destination file path from download messages
                if (output.Contains("[download] Destination:"))
                {
                    var match = Regex.Match(output, @"\[download\] Destination: (.+)");
                    if (match.Success)
                    {
                        string filePath = match.Groups[1].Value.Trim();
                        lock (_filePathLock)
                        {
                            _lastDownloadedFilePath = Path.GetFullPath(filePath);
                        }
                    }
                }

                // Parse download progress from output
                if (output.Contains("[download]") && output.Contains("%"))
                {
                    var progress = new DownloadProgress();
                    
                    // Extract percentage
                    var percentMatch = Regex.Match(output, @"(\d+\.?\d*)%");
                    if (percentMatch.Success)
                    {
                        if (double.TryParse(percentMatch.Groups[1].Value, out double percentage))
                        {
                            progress.Percentage = Math.Min(100.0, Math.Max(0.0, percentage));
                        }
                    }

                    // Extract download speed
                    var speedMatch = Regex.Match(output, @"(\d+\.?\d*\s*[KMGT]?i?B/s)");
                    if (speedMatch.Success)
                    {
                        progress.Speed = speedMatch.Groups[1].Value.Trim();
                    }

                    // Extract estimated time remaining
                    var etaMatch = Regex.Match(output, @"ETA\s+(\d+:\d+|\d+s)");
                    if (etaMatch.Success)
                    {
                        progress.ETA = etaMatch.Groups[1].Value;
                    }

                    progress.Status = "Downloading...";
                    ProgressChanged?.Invoke(this, progress);
                }
                // Handle different phases of the download process
                else if (output.Contains("[ffmpeg]"))
                {
                    ProgressChanged?.Invoke(this, new DownloadProgress { Status = "Processing video...", Percentage = -1 });
                }
                else if (output.Contains("Downloading webpage"))
                {
                    ProgressChanged?.Invoke(this, new DownloadProgress { Status = "Fetching video info...", Percentage = -1 });
                }
                else if (output.Contains("Extracting URL"))
                {
                    ProgressChanged?.Invoke(this, new DownloadProgress { Status = "Extracting download URL...", Percentage = -1 });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error parsing output: {ex.Message}");
            }
        }

        /// <summary>
        /// Parses error output from yt-dlp and filters out non-critical warnings
        /// </summary>
        /// <param name="error">Error message from yt-dlp</param>
        private void ParseError(string? error)
        {
            if (!string.IsNullOrWhiteSpace(error))
            {
                // Filter out common non-critical warnings
                if (error.Contains("WARNING") || error.Contains("Unable to extract") && error.Contains("thumbnail"))
                {
                    return;  // Ignore thumbnail extraction warnings
                }
                
                DownloadFailed?.Invoke(this, $"yt-dlp error: {error}");
            }
        }

        /// <summary>
        /// Checks if the yt-dlp executable is available at the configured path
        /// </summary>
        /// <returns>True if yt-dlp is available, false otherwise</returns>
        public bool IsYtdlpAvailable()
        {
            ThrowIfDisposed();
            return File.Exists(_ytdlpBIN);
        }

        /// <summary>
        /// Retrieves video metadata asynchronously without downloading
        /// </summary>
        /// <param name="link">YouTube video URL</param>
        /// <returns>JSON metadata string if successful, null otherwise</returns>
        public async Task<string?> GetVideoInfoAsync(string link)
        {
            ThrowIfDisposed();
            return await Task.Run(() => GetVideoInfo(link));
        }

        /// <summary>
        /// Retrieves video metadata synchronously without downloading
        /// </summary>
        /// <param name="link">YouTube video URL</param>
        /// <returns>JSON metadata string if successful, null otherwise</returns>
        public string? GetVideoInfo(string link)
        {
            ThrowIfDisposed();
            string args = $"\"{link}\" --dump-json --no-download";
            
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = _ytdlpBIN,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            try
            {
                process.Start();
                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                return process.ExitCode == 0 ? output : null;
            }
            catch (Exception)
            {
                return null;  // Return null on any error
            }
        }

        /// <summary>
        /// Throws ObjectDisposedException if the service has been disposed
        /// </summary>
        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ytdlpService));
        }

        /// <summary>
        /// Disposes of the service and releases all resources
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Protected dispose method following the dispose pattern
        /// </summary>
        /// <param name="disposing">True if disposing managed resources</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Cancel any running downloads
                    CancelDownload();
                    
                    // Clean up stored file path
                    ClearDownloadedFilePath();
                    
                    // Remove event handlers to prevent memory leaks
                    ProgressChanged = null;
                    DownloadCompleted = null;
                    DownloadFailed = null;
                }

                _disposed = true;
            }
        }

        /// <summary>
        /// Finalizer to ensure resources are cleaned up if Dispose wasn't called
        /// </summary>
        ~ytdlpService()
        {
            Dispose(false);
        }
    }
}