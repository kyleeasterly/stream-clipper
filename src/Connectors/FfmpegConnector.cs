using System.Diagnostics;
using System.Text;

namespace StreamClipper.Connectors;

public class FfmpegConnector
{
    private readonly ILogger<FfmpegConnector> _logger;

    public FfmpegConnector(ILogger<FfmpegConnector> logger)
    {
        _logger = logger;
    }

    public async Task<string> CreateClipFromSegmentsAsync(
        string inputVideoPath, 
        List<(double start, double end)> timeRanges, 
        string outputPath)
    {
        if (!File.Exists(inputVideoPath))
        {
            throw new FileNotFoundException($"Input video file not found: {inputVideoPath}");
        }

        if (!timeRanges.Any())
        {
            throw new ArgumentException("No time ranges provided for clipping");
        }

        try
        {
            _logger.LogInformation($"Creating clip from {timeRanges.Count} segments");
            _logger.LogInformation($"Input: {inputVideoPath}");
            _logger.LogInformation($"Output: {outputPath}");

            // Create a temporary file listing all segments
            var tempDir = Path.GetTempPath();
            var segmentListPath = Path.Combine(tempDir, $"segments_{Guid.NewGuid()}.txt");
            var tempFiles = new List<string>();

            try
            {
                // Extract each segment to a temporary file
                for (int i = 0; i < timeRanges.Count; i++)
                {
                    var (start, end) = timeRanges[i];
                    var duration = end - start;
                    var tempSegmentPath = Path.Combine(tempDir, $"segment_{Guid.NewGuid()}_{i:D3}.mp4");
                    tempFiles.Add(tempSegmentPath);

                    _logger.LogInformation($"Extracting segment {i + 1}/{timeRanges.Count}: {start:F2}s - {end:F2}s");

                    // Extract segment with re-encoding to ensure compatibility
                    var extractArgs = $"-i \"{inputVideoPath}\" -ss {start:F3} -t {duration:F3} -c:v libx264 -c:a aac -avoid_negative_ts make_zero \"{tempSegmentPath}\"";
                    
                    await RunFfmpegCommandAsync(extractArgs);
                }

                // Create concat list file
                var concatList = new StringBuilder();
                foreach (var tempFile in tempFiles)
                {
                    concatList.AppendLine($"file '{tempFile}'");
                }
                await File.WriteAllTextAsync(segmentListPath, concatList.ToString());

                _logger.LogInformation("Concatenating all segments into final output...");

                // Concatenate all segments
                var concatArgs = $"-f concat -safe 0 -i \"{segmentListPath}\" -c copy \"{outputPath}\"";
                await RunFfmpegCommandAsync(concatArgs);

                _logger.LogInformation($"Clip created successfully: {outputPath}");
                return outputPath;
            }
            finally
            {
                // Clean up temporary files
                foreach (var tempFile in tempFiles)
                {
                    if (File.Exists(tempFile))
                    {
                        try { File.Delete(tempFile); } catch { }
                    }
                }
                if (File.Exists(segmentListPath))
                {
                    try { File.Delete(segmentListPath); } catch { }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating clip with FFmpeg");
            throw;
        }
    }

    private async Task RunFfmpegCommandAsync(string arguments)
    {
        var processStartInfo = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = processStartInfo };
        
        var outputBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();

        process.OutputDataReceived += (sender, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                outputBuilder.AppendLine(e.Data);
                _logger.LogDebug("FFmpeg: {Output}", e.Data);
            }
        };

        process.ErrorDataReceived += (sender, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                errorBuilder.AppendLine(e.Data);
                _logger.LogDebug("FFmpeg: {Error}", e.Data);
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            var errorOutput = errorBuilder.ToString();
            _logger.LogError($"FFmpeg failed with exit code {process.ExitCode}. Error: {errorOutput}");
            throw new Exception($"FFmpeg failed with exit code {process.ExitCode}");
        }
    }
}