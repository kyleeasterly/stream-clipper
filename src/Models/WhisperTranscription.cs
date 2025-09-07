namespace StreamClipper.Models;

public class WhisperTranscription
{
    public string Text { get; set; } = string.Empty;
    public List<TranscriptionSegment> Segments { get; set; } = new();
    public string Language { get; set; } = string.Empty;
}