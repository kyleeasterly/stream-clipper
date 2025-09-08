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
        _openAiConnector = new OpenAiConnector("gpt-5-mini");
        
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
4. Use the exact format: [ID] [Topic Name]
5. Be concise with topic names (2-6 words typically)
6. Only mark significant topic changes, not minor tangents
7. The first topic should always start at segment 0

Example output:
0 Introduction and setup
5 Technical discussion
12 Problem solving
18 Conclusion and next steps

Important: Do not include every segment ID, only the starting ID of each new topic.";

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
}