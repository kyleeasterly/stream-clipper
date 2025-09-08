namespace StreamClipper.Models;

public class TopicSegment
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public int StartSegmentId { get; set; }
    public int EndSegmentId { get; set; }
    public string Topic { get; set; } = string.Empty;
    public string? Description { get; set; }
    public List<TopicSegment>? Children { get; set; }
    public int Level { get; set; } = 0;
    public string? ParentId { get; set; }
    
    public bool HasChildren => Children?.Any() == true;
    public bool CanRegenerate => !HasChildren;
    public int SegmentCount => EndSegmentId - StartSegmentId + 1;
    public bool IsSingleSegment => StartSegmentId == EndSegmentId;
}