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

            // Merge nearby segments to reduce the number of cuts
            // Use a 2-second threshold since Whisper segments are often consecutive
            var mergedRanges = MergeNearbySegments(timeRanges, gapThreshold: 2.0);
            _logger.LogInformation($"Merged {timeRanges.Count} segments into {mergedRanges.Count} continuous ranges");

            var tempDir = Path.GetTempPath();
            var segmentListPath = Path.Combine(tempDir, $"segments_{Guid.NewGuid()}.txt");
            var tempFiles = new List<string>();

            try
            {
                // Process segments in parallel for better performance
                var tasks = new List<Task<(int index, string path)>>();
                var semaphore = new SemaphoreSlim(4); // Limit parallel FFmpeg processes

                for (int i = 0; i < mergedRanges.Count; i++)
                {
                    var index = i;
                    var (start, end) = mergedRanges[i];
                    tasks.Add(ExtractSegmentAsync(inputVideoPath, start, end, index, tempDir, semaphore));
                }

                var results = await Task.WhenAll(tasks);
                tempFiles.AddRange(results.OrderBy(r => r.index).Select(r => r.path));

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

    private async Task<(int index, string path)> ExtractSegmentAsync(
        string inputVideoPath, 
        double start, 
        double end, 
        int index,
        string tempDir,
        SemaphoreSlim semaphore)
    {
        await semaphore.WaitAsync();
        try
        {
            var duration = end - start;
            var tempSegmentPath = Path.Combine(tempDir, $"segment_{Guid.NewGuid()}_{index:D3}.mp4");

            _logger.LogInformation($"Extracting segment {index + 1}: {start:F2}s - {end:F2}s (duration: {duration:F2}s)");

            // Use stream copy for much faster extraction (no re-encoding)
            // Use fast seek with -ss before -i for better performance
            var extractArgs = $"-ss {start:F3} -i \"{inputVideoPath}\" -t {duration:F3} -c copy -avoid_negative_ts make_zero \"{tempSegmentPath}\"";
            
            await RunFfmpegCommandAsync(extractArgs);
            
            return (index, tempSegmentPath);
        }
        finally
        {
            semaphore.Release();
        }
    }

    private List<(double start, double end)> MergeNearbySegments(
        List<(double start, double end)> segments, 
        double gapThreshold = 2.0)
    {
        if (!segments.Any())
            return segments;

        var sorted = segments.OrderBy(s => s.start).ToList();
        var merged = new List<(double start, double end)>();
        
        var currentStart = sorted[0].start;
        var currentEnd = sorted[0].end;

        for (int i = 1; i < sorted.Count; i++)
        {
            var segment = sorted[i];
            
            // If the gap between segments is small, merge them
            if (segment.start - currentEnd <= gapThreshold)
            {
                currentEnd = Math.Max(currentEnd, segment.end);
            }
            else
            {
                // Gap is too large, save current merged segment and start a new one
                merged.Add((currentStart, currentEnd));
                currentStart = segment.start;
                currentEnd = segment.end;
            }
        }
        
        // Add the last segment
        merged.Add((currentStart, currentEnd));
        
        return merged;
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