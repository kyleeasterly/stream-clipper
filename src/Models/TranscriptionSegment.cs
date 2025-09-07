using System.Text.Json.Serialization;

namespace StreamClipper.Models;

public class TranscriptionSegment
{
    public int Id { get; set; }
    public int Seek { get; set; }
    public double Start { get; set; }
    public double End { get; set; }
    public string Text { get; set; } = string.Empty;
    public List<int>? Tokens { get; set; }
    public double Temperature { get; set; }
    
    [JsonPropertyName("avg_logprob")]
    public double AvgLogprob { get; set; }
    
    [JsonPropertyName("compression_ratio")]
    public double CompressionRatio { get; set; }
    
    [JsonPropertyName("no_speech_prob")]
    public double NoSpeechProb { get; set; }
    
    public List<TranscriptionWord>? Words { get; set; }
}