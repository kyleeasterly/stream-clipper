namespace StreamClipper.Models;

public class TopicSegment
{
    public int StartSegmentId { get; set; }
    public int EndSegmentId { get; set; }
    public string Topic { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public class StreamClipperProject
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string? OriginalVideoPath { get; set; }
    public string? WhisperJsonPath { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public WhisperTranscription? Transcription { get; set; }
    public List<TopicSegment> Topics { get; set; } = new();
    public Dictionary<string, object> Metadata { get; set; } = new();
}