using System.Diagnostics;

namespace StreamClipper.Connectors;

public class WhisperConnector
{
    private readonly ILogger<WhisperConnector> _logger;

    public WhisperConnector(ILogger<WhisperConnector> logger)
    {
        _logger = logger;
    }

    public async Task<string> RunWhisperAsync(string videoPath)
    {
        if (!File.Exists(videoPath))
        {
            throw new FileNotFoundException($"Video file not found: {videoPath}");
        }

        var outputDir = Path.GetDirectoryName(videoPath) ?? ".";
        var outputJsonPath = Path.Combine(outputDir, Path.GetFileNameWithoutExtension(videoPath) + ".json");

        try
        {
            _logger.LogInformation("Starting Whisper transcription for: {VideoPath}", videoPath);

            var arguments = $"\"{videoPath}\" --model base --output_format json --output_dir \"{outputDir}\" --word_timestamps True";
            
            Console.WriteLine("========================================");
            Console.WriteLine("Executing Whisper command:");
            Console.WriteLine($"whisper {arguments}");
            Console.WriteLine("========================================");
            
            _logger.LogInformation("Whisper command: whisper {Arguments}", arguments);

            var processStartInfo = new ProcessStartInfo
            {
                FileName = "whisper",
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = processStartInfo };
            
            process.OutputDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    _logger.LogInformation("Whisper: {Output}", e.Data);
                }
            };

            process.ErrorDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    _logger.LogInformation("Whisper: {Error}", e.Data);
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                throw new Exception($"Whisper failed with exit code {process.ExitCode}");
            }

            _logger.LogInformation("Whisper completed. Output JSON: {JsonPath}", outputJsonPath);
            return outputJsonPath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error running Whisper");
            throw;
        }
    }
}