using System.Text.Json;
using StreamClipper.Models;

namespace StreamClipper.Services;

public class TranscriptionService
{
    private readonly ILogger<TranscriptionService> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public TranscriptionService(ILogger<TranscriptionService> logger)
    {
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    public async Task<WhisperTranscription?> LoadTranscriptionAsync(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                _logger.LogError("Transcription file not found: {FilePath}", filePath);
                return null;
            }

            var json = await File.ReadAllTextAsync(filePath);
            var transcription = JsonSerializer.Deserialize<WhisperTranscription>(json, _jsonOptions);

            if (transcription != null && ValidateTranscription(transcription))
            {
                _logger.LogInformation("Successfully loaded transcription from: {FilePath}", filePath);
                return transcription;
            }

            _logger.LogWarning("Invalid transcription data in file: {FilePath}", filePath);
            return null;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse JSON from file: {FilePath}", filePath);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading transcription from file: {FilePath}", filePath);
            return null;
        }
    }

    public bool ValidateTranscription(WhisperTranscription transcription)
    {
        if (transcription == null)
            return false;

        if (string.IsNullOrWhiteSpace(transcription.Text))
        {
            _logger.LogWarning("Transcription text is empty");
            return false;
        }

        if (transcription.Segments == null || !transcription.Segments.Any())
        {
            _logger.LogWarning("Transcription has no segments");
            return false;
        }

        return true;
    }

    public string FormatTimestamp(double seconds)
    {
        var timeSpan = TimeSpan.FromSeconds(seconds);
        return timeSpan.ToString(@"hh\:mm\:ss\.fff");
    }

    public string GetTranscriptionSummary(WhisperTranscription transcription)
    {
        if (transcription == null)
            return "No transcription loaded";

        var duration = transcription.Segments.LastOrDefault()?.End ?? 0;
        var wordCount = transcription.Segments
            .Where(s => s.Words != null)
            .SelectMany(s => s.Words!)
            .Count();

        return $"Duration: {FormatTimestamp(duration)} | Segments: {transcription.Segments.Count} | Words: {wordCount} | Language: {transcription.Language}";
    }
}