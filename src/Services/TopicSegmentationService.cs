using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using StreamClipper.Connectors;
using StreamClipper.Models;

namespace StreamClipper.Services;

public class TopicSegmentationService
{
    private readonly ILogger<TopicSegmentationService> _logger;
    private readonly IConfiguration _configuration;
    private readonly OpenAiConnector _openAiConnector;
    private readonly string _dataFolder;

    public TopicSegmentationService(ILogger<TopicSegmentationService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
        
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

    public async Task<List<TopicSegment>> SegmentTranscriptAsync(WhisperTranscription transcription)
    {
        if (transcription?.Segments == null || !transcription.Segments.Any())
        {
            _logger.LogWarning("No segments found in transcription");
            return new List<TopicSegment>();
        }

        try
        {
            // Prepare the user message with segment IDs and text
            var userMessage = PrepareSegmentMessage(transcription.Segments);
            
            // System prompt for GPT-5
            var systemPrompt = @"You are analyzing a video transcript that has been divided into segments. Your task is to identify major topic transitions and create a topic outline.

Instructions:
1. Read through all the segments and identify where topics change
2. Group consecutive segments that discuss the same topic
3. Return ONLY the starting segment ID and topic name for each topic section
4. Use the exact format (curly braces identify variables, not literal part of output): {ID} {Topic Name}
5. Be concise with topic names (2-6 words typically)
6. Only mark significant topic changes, not minor tangents
7. The first topic should always start at segment 0

Example output:
0 Introduction and setup
5 Technical discussion
12 Problem solving
18 Conclusion and next steps

Important: Do not include every segment ID, only the starting ID of each new topic. Do not wrap the segment ID in anything i.e. square brackets or parenthesis, follow the example output format exactly.";

            // Call GPT-5-mini
            var response = await _openAiConnector.GenerateCompletionAsync(
                systemPrompt,
                userMessage
            );

            // Parse the response into topic segments
            var topics = ParseTopicResponse(response, transcription.Segments.Count);
            
            _logger.LogInformation($"Successfully segmented transcript into {topics.Count} topics");
            return topics;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to segment transcript");
            throw;
        }
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

    private List<TopicSegment> ParseTopicResponse(string response, int totalSegments)
    {
        var topics = new List<TopicSegment>();
        var lines = response.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        
        // Regex to match lines like "5 Discussion of game state"
        var regex = new Regex(@"^(\d+)\s+(.+)$");
        
        TopicSegment? previousTopic = null;
        
        foreach (var line in lines)
        {
            var match = regex.Match(line.Trim());
            if (match.Success)
            {
                var segmentId = int.Parse(match.Groups[1].Value);
                var topicName = match.Groups[2].Value.Trim();
                
                // Set the end segment ID for the previous topic
                if (previousTopic != null)
                {
                    previousTopic.EndSegmentId = segmentId - 1;
                }
                
                var topic = new TopicSegment
                {
                    StartSegmentId = segmentId,
                    EndSegmentId = totalSegments - 1, // Will be updated by next topic or remain as last
                    Topic = topicName
                };
                
                topics.Add(topic);
                previousTopic = topic;
            }
        }
        
        return topics;
    }

    public async Task<StreamClipperProject> CreateProjectAsync(
        WhisperTranscription transcription,
        string? videoPath = null,
        string? whisperJsonPath = null)
    {
        var project = new StreamClipperProject
        {
            Name = Path.GetFileNameWithoutExtension(videoPath ?? whisperJsonPath ?? "Untitled"),
            OriginalVideoPath = videoPath,
            WhisperJsonPath = whisperJsonPath,
            Transcription = transcription,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        // Segment the transcript into topics
        project.Topics = await SegmentTranscriptAsync(transcription);
        
        // Save the project
        await SaveProjectAsync(project);
        
        return project;
    }

    public async Task SaveProjectAsync(StreamClipperProject project)
    {
        try
        {
            project.UpdatedAt = DateTime.UtcNow;
            
            var projectFileName = $"{project.Name}_{project.Id}.scproject.json";
            var projectPath = Path.Combine(_dataFolder, projectFileName);
            
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
            
            var json = JsonSerializer.Serialize(project, options);
            await File.WriteAllTextAsync(projectPath, json);
            
            _logger.LogInformation($"Project saved to: {projectPath}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save project");
            throw;
        }
    }

    public async Task<StreamClipperProject?> LoadProjectAsync(string projectPath)
    {
        try
        {
            if (!File.Exists(projectPath))
            {
                _logger.LogError($"Project file not found: {projectPath}");
                return null;
            }

            var json = await File.ReadAllTextAsync(projectPath);
            
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
            
            var project = JsonSerializer.Deserialize<StreamClipperProject>(json, options);
            
            _logger.LogInformation($"Project loaded from: {projectPath}");
            return project;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to load project from: {projectPath}");
            return null;
        }
    }

    public List<string> GetRecentProjects()
    {
        try
        {
            if (!Directory.Exists(_dataFolder))
                return new List<string>();

            return Directory.GetFiles(_dataFolder, "*.scproject.json")
                .OrderByDescending(File.GetLastWriteTime)
                .Take(10)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get recent projects");
            return new List<string>();
        }
    }

    public string GetTopicForSegment(List<TopicSegment> topics, int segmentId)
    {
        var topic = topics.FirstOrDefault(t => 
            segmentId >= t.StartSegmentId && segmentId <= t.EndSegmentId);
        
        return topic?.Topic ?? "Unknown Topic";
    }

    public async Task<List<TopicSegment>> SegmentSubtopicAsync(
        TopicSegment parentTopic,
        List<TranscriptionSegment> allSegments)
    {
        _logger.LogInformation($"=== Starting sub-topic segmentation for: '{parentTopic.Topic}' ===");
        _logger.LogInformation($"Parent topic range: Segments {parentTopic.StartSegmentId} to {parentTopic.EndSegmentId} ({parentTopic.SegmentCount} segments)");
        
        if (parentTopic.IsSingleSegment)
        {
            _logger.LogWarning($"Cannot segment a single segment. Topic '{parentTopic.Topic}' only contains segment {parentTopic.StartSegmentId}");
            return new List<TopicSegment>();
        }

        try
        {
            _logger.LogDebug($"Total segments available: {allSegments.Count}");
            
            // Get only the segments in the parent topic's range
            var relevantSegments = allSegments
                .Where(s => s.Id >= parentTopic.StartSegmentId && s.Id <= parentTopic.EndSegmentId)
                .ToList();

            _logger.LogInformation($"Filtered to {relevantSegments.Count} relevant segments for sub-topic analysis");

            if (!relevantSegments.Any())
            {
                _logger.LogWarning($"No segments found in the specified range [{parentTopic.StartSegmentId}-{parentTopic.EndSegmentId}]");
                return new List<TopicSegment>();
            }

            // Prepare the message with the subset of segments
            _logger.LogDebug("Preparing segment message for GPT...");
            var userMessage = PrepareSegmentMessage(relevantSegments);
            _logger.LogDebug($"User message prepared: {userMessage.Length} characters");
            
            // Modified system prompt for sub-segmentation
            var systemPrompt = $@"You are analyzing a portion of a video transcript that covers the topic: '{parentTopic.Topic}'.
This section spans segments {parentTopic.StartSegmentId} to {parentTopic.EndSegmentId}.

Your task is to identify sub-topics within this specific section and create a more detailed outline.

Instructions:
1. Read through the provided segments and identify where sub-topics change
2. Group consecutive segments that discuss the same sub-topic
3. Return ONLY the starting segment ID and sub-topic name for each section
4. Use the exact format: [ID] [Sub-topic Name]
5. Be concise with sub-topic names (2-6 words typically)
6. Only mark significant sub-topic changes within this section
7. The first sub-topic should start at segment {parentTopic.StartSegmentId}
8. All segment IDs must be within the range {parentTopic.StartSegmentId} to {parentTopic.EndSegmentId}

Example output:
{parentTopic.StartSegmentId} Initial setup steps
{parentTopic.StartSegmentId + 3} Configuration details
{parentTopic.StartSegmentId + 7} Testing the setup

Important: Only return segment IDs that are within the parent topic's range.";

            _logger.LogInformation($"Calling GPT-5-mini for sub-topic analysis...");
            _logger.LogDebug($"System prompt length: {systemPrompt.Length} characters");
            
            // Call GPT-5-mini
            var response = await _openAiConnector.GenerateCompletionAsync(
                systemPrompt,
                userMessage
            );

            _logger.LogInformation($"Received response from GPT-5-mini: {response.Length} characters");
            _logger.LogDebug($"GPT Response preview: {response.Substring(0, Math.Min(200, response.Length))}...");

            // Parse the response into sub-topics
            _logger.LogDebug("Parsing GPT response into topic segments...");
            var subTopics = ParseTopicResponse(response, parentTopic.EndSegmentId + 1);
            
            _logger.LogInformation($"Parsed {subTopics.Count} sub-topics from GPT response");
            
            // Set the parent-child relationships
            foreach (var subTopic in subTopics)
            {
                subTopic.ParentId = parentTopic.Id;
                subTopic.Level = parentTopic.Level + 1;
                _logger.LogDebug($"Sub-topic: [{subTopic.StartSegmentId}-{subTopic.EndSegmentId}] {subTopic.Topic}");
            }
            
            // Ensure the last sub-topic ends at the parent's end segment
            if (subTopics.Any())
            {
                var lastTopic = subTopics.Last();
                if (lastTopic.EndSegmentId != parentTopic.EndSegmentId)
                {
                    _logger.LogDebug($"Adjusting last sub-topic end from {lastTopic.EndSegmentId} to {parentTopic.EndSegmentId}");
                    lastTopic.EndSegmentId = parentTopic.EndSegmentId;
                }
            }
            
            _logger.LogInformation($"✅ Successfully segmented '{parentTopic.Topic}' into {subTopics.Count} sub-topics");
            return subTopics;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"❌ Failed to segment sub-topic: {parentTopic.Topic}");
            _logger.LogError($"Exception details: {ex.Message}");
            _logger.LogError($"Stack trace: {ex.StackTrace}");
            throw;
        }
    }

    public async Task<List<TopicSegment>> RegenerateTopicAsync(
        TopicSegment topicToRegenerate,
        List<TranscriptionSegment> allSegments,
        TopicSegment? parentTopic = null)
    {
        if (!topicToRegenerate.CanRegenerate)
        {
            throw new InvalidOperationException("Cannot regenerate a topic that has children");
        }

        _logger.LogInformation($"Regenerating topic: {topicToRegenerate.Topic}");
        
        if (parentTopic != null)
        {
            // Regenerating a sub-topic
            return await SegmentSubtopicAsync(parentTopic, allSegments);
        }
        else
        {
            // Regenerating top-level topics
            var transcription = new WhisperTranscription { Segments = allSegments };
            return await SegmentTranscriptAsync(transcription);
        }
    }

    public async Task SaveProjectManuallyAsync(StreamClipperProject project)
    {
        try
        {
            project.LastSavedAt = DateTime.UtcNow;
            project.UpdatedAt = DateTime.UtcNow;
            project.HasUnsavedChanges = false;
            
            await SaveProjectAsync(project);
            
            _logger.LogInformation($"Project manually saved at: {project.LastSavedAt}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to manually save project");
            throw;
        }
    }
}