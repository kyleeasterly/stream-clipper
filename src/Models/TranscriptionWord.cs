namespace StreamClipper.Models;

public class TranscriptionWord
{
    public string Word { get; set; } = string.Empty;
    public double Start { get; set; }
    public double End { get; set; }
    public double? Probability { get; set; }
}