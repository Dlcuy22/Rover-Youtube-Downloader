using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Rover.Service
{
    public enum DownloadFormat
    {
        AudioM4A,
        VideoMP4
    }

    public enum AudioBitrate
    {
        Low = 128,
        Medium = 192,
        High = 256,
        VeryHigh = 320
    }

    public enum VideoResolution
    {
        SD480 = 480,
        HD720 = 720,
        HD1080 = 1080,
        UHD1440 = 1440,
        UHD2160 = 2160 // Sampe 4k Jier
    }

    public class DownloadOptions
    {
        public DownloadFormat Format { get; set; } = DownloadFormat.VideoMP4;
        public AudioBitrate AudioBitrate { get; set; } = AudioBitrate.Medium;
        public VideoResolution VideoResolution { get; set; } = VideoResolution.HD720;
        public string OutputPath { get; set; } = "%USERPROFILE%/Downloads";
        public string? CustomFilename { get; set; } = null;
    }

    public class DownloadProgress
    {
        public double Percentage { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? Speed { get; set; }
        public string? ETA { get; set; }
    }

    public class ytdlpService : IDisposable
    {
        private readonly string _ytdlpBIN;
        private Process? _currentProcess; // FIXED: Track current process for cancellation
        private bool _disposed = false;
        
        public event EventHandler<DownloadProgress>? ProgressChanged;
        public event EventHandler<string>? DownloadCompleted;
        public event EventHandler<string>? DownloadFailed;

        public ytdlpService(string ytdlpPath = "python/yt-dlp.exe")
        {
            _ytdlpBIN = ytdlpPath;
            
            if (!File.Exists(_ytdlpBIN))
            {
                throw new FileNotFoundException($"yt-dlp executable not found at: {_ytdlpBIN}");
            }
        }

        // FIXED: Added cancellation token support
        public async Task DownloadYoutubeAsync(string link, DownloadOptions? options = null, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            options ??= new DownloadOptions();
            
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

        // FIXED: Added cancellation token support
        public void DownloadYoutube(string link, DownloadOptions? options = null, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            options ??= new DownloadOptions();
            
            if (string.IsNullOrWhiteSpace(link))
            {
                throw new ArgumentException("YouTube link cannot be empty", nameof(link));
            }

            // Ensure output directory exists
            Directory.CreateDirectory(options.OutputPath);

            string args = BuildArguments(link, options);
            
            ExecuteDownload(args, cancellationToken);
        }

        // FIXED: Improved argument building with proper quality settings
        private string BuildArguments(string link, DownloadOptions options)
        {
            var args = $"\"{link}\"";
            
            // Output path and filename template
            string outputTemplate = Path.Combine(options.OutputPath, 
                string.IsNullOrWhiteSpace(options.CustomFilename) 
                    ? "%(title)s.%(ext)s" 
                    : $"{options.CustomFilename}.%(ext)s");
            
            args += $" -o \"{outputTemplate}\"";

            // Format-specific arguments - FIXED
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

            // Additional options
            args += " --no-playlist";
            args += " --embed-thumbnail";
            args += " --add-metadata";
            args += " --progress";
            args += " --newline";
            args += " --console-title"; // This helps with progress tracking

            return args;
        }

        // FIXED: Improved video format selection
        private string GetVideoFormatString(VideoResolution resolution)
        {
            return resolution switch
            {
                VideoResolution.SD480 => "best[height<=480][ext=mp4]",
                VideoResolution.HD720 => "best[height<=720][ext=mp4]", 
                VideoResolution.HD1080 => "best[height<=1080][ext=mp4]",
                VideoResolution.UHD1440 => "best[height<=1440][ext=mp4]",
                VideoResolution.UHD2160 => "best[height<=2160][ext=mp4]",
                _ => "best[height<=720][ext=mp4]"
            };
        }

        // FIXED: Added cancellation support to execution
        private void ExecuteDownload(string args, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            
            using var process = new Process();
            _currentProcess = process; // Store reference for cancellation :3
            
            process.StartInfo = new ProcessStartInfo
            {
                FileName = _ytdlpBIN,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            process.OutputDataReceived += (sender, e) => ParseOutput(e.Data);
            process.ErrorDataReceived += (sender, e) => ParseError(e.Data);

            try
            {
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                // FIXED: Wait with cancellation support
                while (!process.HasExited)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        try
                        {
                            process.Kill(true); // Kill process tree
                        }
                        catch { }
                        
                        cancellationToken.ThrowIfCancellationRequested();
                    }
                    
                    Thread.Sleep(100); // Check every 100ms
                }

                if (process.ExitCode == 0)
                {
                    DownloadCompleted?.Invoke(this, "Download completed successfully");
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

        // FIXED: Added method to cancel download
        public void CancelDownload()
        {
            try
            {
                if (_currentProcess != null && !_currentProcess.HasExited)
                {
                    _currentProcess.Kill(true); // Kill the entire process tree
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error killing process: {ex.Message}");
            }
        }

        // FIXED: Improved progress parsing to handle percentage correctly
        private void ParseOutput(string? output)
        {
            if (string.IsNullOrWhiteSpace(output)) return;

            try
            {
                // Parse yt-dlp progress output
                if (output.Contains("[download]") && output.Contains("%"))
                {
                    var progress = new DownloadProgress();
                    
                    // FIXED: Better percentage extraction and validation
                    var percentMatch = System.Text.RegularExpressions.Regex.Match(output, @"(\d+\.?\d*)%");
                    if (percentMatch.Success)
                    {
                        if (double.TryParse(percentMatch.Groups[1].Value, out double percentage))
                        {
                            // FIXED: Ensure percentage is between 0-100
                            progress.Percentage = Math.Min(100.0, Math.Max(0.0, percentage));
                        }
                    }

                    // Extract speed - FIXED: Better regex pattern
                    var speedMatch = System.Text.RegularExpressions.Regex.Match(output, @"(\d+\.?\d*\s*[KMGT]?i?B/s)");
                    if (speedMatch.Success)
                    {
                        progress.Speed = speedMatch.Groups[1].Value.Trim();
                    }

                    // Extract ETA - FIXED: Better regex pattern
                    var etaMatch = System.Text.RegularExpressions.Regex.Match(output, @"ETA\s+(\d+:\d+|\d+s)");
                    if (etaMatch.Success)
                    {
                        progress.ETA = etaMatch.Groups[1].Value;
                    }

                    progress.Status = "Downloading...";
                    ProgressChanged?.Invoke(this, progress);
                }
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
                // Log parsing errors if needed
                System.Diagnostics.Debug.WriteLine($"Error parsing output: {ex.Message}");
            }
        }

        private void ParseError(string? error)
        {
            if (!string.IsNullOrWhiteSpace(error))
            {
                // Don't report certain non-critical errors
                if (error.Contains("WARNING") || error.Contains("Unable to extract") && error.Contains("thumbnail"))
                {
                    return;
                }
                
                DownloadFailed?.Invoke(this, $"yt-dlp error: {error}");
            }
        }

        public bool IsYtdlpAvailable()
        {
            ThrowIfDisposed();
            return File.Exists(_ytdlpBIN);
        }

        public async Task<string?> GetVideoInfoAsync(string link)
        {
            ThrowIfDisposed();
            return await Task.Run(() => GetVideoInfo(link));
        }

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
                return null;
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ytdlpService));
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Cancel any running download
                    CancelDownload();
                    
                    // Clean up events
                    ProgressChanged = null;
                    DownloadCompleted = null;
                    DownloadFailed = null;
                }

                _disposed = true;
            }
        }

        ~ytdlpService()
        {
            Dispose(false);
        }
    }
}