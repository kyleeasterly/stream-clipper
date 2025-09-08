namespace StreamClipper.Models;

public class TopicSegment
{
    public int StartSegmentId { get; set; }
    public int EndSegmentId { get; set; }
    public string Topic { get; set; } = string.Empty;
    public string? Description { get; set; }
}