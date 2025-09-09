using System.Text;
using System.Text.RegularExpressions;
using StreamClipper.Connectors;
using StreamClipper.Models;

namespace StreamClipper.Services;

public class VideoClippingService
{
    private readonly ILogger<VideoClippingService> _logger;
    private readonly IConfiguration _configuration;
    private readonly OpenAiConnector _openAiConnector;
    private readonly FfmpegConnector _ffmpegConnector;
    private readonly string _dataFolder;

    public VideoClippingService(
        ILogger<VideoClippingService> logger, 
        IConfiguration configuration,
        FfmpegConnector ffmpegConnector)
    {
        _logger = logger;
        _configuration = configuration;
        _ffmpegConnector = ffmpegConnector;
        
        // Create a logger factory for OpenAiConnector
        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Debug);
        });
        var openAiLogger = loggerFactory.CreateLogger<OpenAiConnector>();
        
        _openAiConnector = new OpenAiConnector("gpt-5-mini", openAiLogger);
        
        // Get data folder from configuration
        _dataFolder = _configuration["DataFolder"] ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "StreamClipper"
        );
        
        // Ensure data folder exists
        Directory.CreateDirectory(_dataFolder);
    }

    public async Task<(string outputPath, List<int> selectedSegmentIds)> CreateAiDirectedClipAsync(
        WhisperTranscription transcription,
        StreamClipperProject project,
        string directorPrompt)
    {
        if (transcription?.Segments == null || !transcription.Segments.Any())
        {
            throw new ArgumentException("No segments found in transcription");
        }

        if (string.IsNullOrWhiteSpace(project.OriginalVideoPath))
        {
            throw new ArgumentException("Project does not have an associated video file");
        }

        if (!File.Exists(project.OriginalVideoPath))
        {
            throw new FileNotFoundException($"Original video file not found: {project.OriginalVideoPath}");
        }

        try
        {
            _logger.LogInformation("Starting AI-directed clip creation");
            _logger.LogInformation($"Director prompt: {directorPrompt}");
            
            // Step 1: Get segment selection from GPT-5
            var selectedSegmentIds = await GetAiSelectedSegmentsAsync(transcription.Segments, directorPrompt);
            
            if (!selectedSegmentIds.Any())
            {
                throw new Exception("AI director did not select any segments for the clip");
            }
            
            _logger.LogInformation($"AI director selected {selectedSegmentIds.Count} segments: {string.Join(", ", selectedSegmentIds)}");
            
            // Step 2: Convert segment IDs to time ranges
            var timeRanges = GetTimeRangesFromSegments(transcription.Segments, selectedSegmentIds);
            
            // Step 3: Generate output filename
            var outputFileName = GenerateOutputFileName(project.Name, directorPrompt);
            var outputPath = Path.Combine(_dataFolder, outputFileName);
            
            // Step 4: Create the video clip using FFmpeg
            await _ffmpegConnector.CreateClipFromSegmentsAsync(
                project.OriginalVideoPath,
                timeRanges,
                outputPath
            );
            
            _logger.LogInformation($"AI-directed clip created successfully: {outputPath}");
            return (outputPath, selectedSegmentIds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create AI-directed clip");
            throw;
        }
    }

    private async Task<List<int>> GetAiSelectedSegmentsAsync(List<TranscriptionSegment> segments, string directorPrompt)
    {
        // Prepare the segments message
        var userMessage = PrepareSegmentMessage(segments);
        
        // System prompt for AI director
        var systemPrompt = @"You are an AI video editor. The user will provide instructions about what story they want to tell from a video transcript.

Your task:
1. Review all the transcript segments carefully
2. Select ONLY the segment IDs that are essential to telling the requested story
3. Choose segments that create a coherent narrative flow
4. Include segments that provide necessary context and progression
5. Return ONLY the segment IDs, one per line, in chronological order
6. Do not include any other text, explanations, or formatting

Important:
- Be selective - only include segments that directly contribute to the story
- Maintain narrative coherence - the selected segments should flow logically
- Consider pacing - include enough content to tell the story well, but avoid redundancy
- Return segment IDs in their original chronological order (ascending)";

        // Add the director prompt to the user message
        var fullUserMessage = $"Director Instructions: {directorPrompt}\n\nTranscript Segments:\n{userMessage}";
        
        // Call GPT-5
        var response = await _openAiConnector.GenerateCompletionAsync(systemPrompt, fullUserMessage);
        
        // Parse the response to extract segment IDs
        return ParseSegmentIds(response);
    }

    private string PrepareSegmentMessage(List<TranscriptionSegment> segments)
    {
        var sb = new StringBuilder();
        foreach (var segment in segments)
        {
            sb.AppendLine($"{segment.Id} {segment.Text.Trim()}");
        }
        return sb.ToString();
    }

    private List<int> ParseSegmentIds(string response)
    {
        var segmentIds = new List<int>();
        var lines = response.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        
        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();
            
            // Try to parse as a simple integer
            if (int.TryParse(trimmedLine, out int segmentId))
            {
                segmentIds.Add(segmentId);
            }
            else
            {
                // Try to extract a number from the line (in case GPT added extra formatting)
                var match = Regex.Match(trimmedLine, @"^\d+");
                if (match.Success && int.TryParse(match.Value, out int extractedId))
                {
                    segmentIds.Add(extractedId);
                }
            }
        }
        
        // Remove duplicates and sort
        segmentIds = segmentIds.Distinct().OrderBy(id => id).ToList();
        
        _logger.LogInformation($"Parsed {segmentIds.Count} segment IDs from AI response");
        return segmentIds;
    }

    private List<(double start, double end)> GetTimeRangesFromSegments(
        List<TranscriptionSegment> allSegments, 
        List<int> selectedIds)
    {
        var timeRanges = new List<(double start, double end)>();
        
        foreach (var segmentId in selectedIds)
        {
            var segment = allSegments.FirstOrDefault(s => s.Id == segmentId);
            if (segment != null)
            {
                timeRanges.Add((segment.Start, segment.End));
                _logger.LogDebug($"Segment {segmentId}: {segment.Start:F2}s - {segment.End:F2}s");
            }
            else
            {
                _logger.LogWarning($"Segment ID {segmentId} not found in transcription");
            }
        }
        
        return timeRanges;
    }

    private string GenerateOutputFileName(string projectName, string directorPrompt)
    {
        // Clean the project name
        var cleanProjectName = SanitizeFileName(projectName);
        
        // Get first few words from the prompt for the filename
        var promptWords = directorPrompt.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Take(5)
            .Select(w => SanitizeFileName(w.ToLower()));
        var promptSuffix = string.Join("_", promptWords);
        
        // Add timestamp
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        
        // Combine into filename
        var fileName = $"{cleanProjectName}_{timestamp}_clip_{promptSuffix}.mp4";
        
        // Ensure the filename isn't too long
        if (fileName.Length > 100)
        {
            fileName = $"{cleanProjectName}_{timestamp}_clip.mp4";
        }
        
        return fileName;
    }

    private string SanitizeFileName(string fileName)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = string.Join("", fileName.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));
        return sanitized.Length > 0 ? sanitized : "untitled";
    }
}