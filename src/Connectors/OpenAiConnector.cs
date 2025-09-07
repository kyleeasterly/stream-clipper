using OpenAI.Chat;
using OpenAI.Embeddings;
using System.ClientModel;

namespace StreamClipper.Connectors;

public class OpenAiConnector
{
    private readonly ChatClient _chatClient;
    private readonly EmbeddingClient _embeddingClient;
    private readonly string _model;
    private readonly string _embeddingModel;

    public OpenAiConnector(string model = "gpt-4o-mini", string embeddingModel = "text-embedding-3-small")
    {
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY") 
            ?? throw new InvalidOperationException("OPENAI_API_KEY environment variable is not set");
        
        _model = model;
        _embeddingModel = embeddingModel;
        _chatClient = new ChatClient(model, apiKey);
        _embeddingClient = new EmbeddingClient(embeddingModel, apiKey);
    }

    public async Task<string> GenerateCompletionAsync(
        string systemPrompt, 
        string userMessage, 
        float temperature = 0.7f)
    {
        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(systemPrompt),
            new UserChatMessage(userMessage)
        };

        var options = new ChatCompletionOptions
        {
            Temperature = temperature
        };

        var completion = await _chatClient.CompleteChatAsync(messages, options);
        
        return completion.Value.Content[0].Text;
    }

    public async Task<float[]> GenerateEmbeddingAsync(string text)
    {
        var embedding = await _embeddingClient.GenerateEmbeddingAsync(text);
        return embedding.Value.ToFloats().ToArray();
    }

    public async Task<List<float[]>> GenerateEmbeddingsAsync(List<string> texts, int maxParallelism = 5)
    {
        var semaphore = new SemaphoreSlim(maxParallelism);
        var tasks = texts.Select(async text =>
        {
            await semaphore.WaitAsync();
            try
            {
                return await GenerateEmbeddingAsync(text);
            }
            finally
            {
                semaphore.Release();
            }
        });

        var results = await Task.WhenAll(tasks);
        return results.ToList();
    }

    public async Task<List<BulkCompletionResult>> GenerateBulkCompletionsAsync(
        string systemPromptTemplate,
        string userMessageTemplate,
        Dictionary<string, List<string>> templateFieldValues,
        float temperature = 0.7f,
        int maxParallelism = 5,
        int multiplier = 1,
        IProgress<BulkCompletionProgress>? progress = null)
    {
        var combinations = GenerateAllCombinations(templateFieldValues);
        var totalTasks = combinations.Count * multiplier;
        var results = new List<BulkCompletionResult>();
        var completed = 0;
        var semaphore = new SemaphoreSlim(maxParallelism);

        var tasks = new List<Task<BulkCompletionResult>>();

        for (int m = 0; m < multiplier; m++)
        {
            foreach (var combination in combinations)
            {
                tasks.Add(ProcessSingleCompletionAsync(
                    systemPromptTemplate, 
                    userMessageTemplate, 
                    combination, 
                    temperature, 
                    semaphore,
                    () =>
                    {
                        var currentCompleted = Interlocked.Increment(ref completed);
                        progress?.Report(new BulkCompletionProgress
                        {
                            Completed = currentCompleted,
                            Total = totalTasks,
                            PercentComplete = (currentCompleted * 100.0) / totalTasks
                        });
                    }));
            }
        }

        var completedTasks = await Task.WhenAll(tasks);
        results.AddRange(completedTasks);

        return results;
    }

    private async Task<BulkCompletionResult> ProcessSingleCompletionAsync(
        string systemPromptTemplate,
        string userMessageTemplate,
        Dictionary<string, string> fieldValues,
        float temperature,
        SemaphoreSlim semaphore,
        Action onCompleted)
    {
        await semaphore.WaitAsync();
        try
        {
            var systemPrompt = ReplaceTemplateFields(systemPromptTemplate, fieldValues);
            var userMessage = ReplaceTemplateFields(userMessageTemplate, fieldValues);

            var generatedContent = await GenerateCompletionAsync(systemPrompt, userMessage, temperature);

            onCompleted();

            return new BulkCompletionResult
            {
                SystemPrompt = systemPrompt,
                UserMessage = userMessage,
                GeneratedContent = generatedContent,
                TemplateValues = new Dictionary<string, string>(fieldValues),
                Timestamp = DateTime.UtcNow
            };
        }
        finally
        {
            semaphore.Release();
        }
    }

    private string ReplaceTemplateFields(string template, Dictionary<string, string> fieldValues)
    {
        var result = template;
        foreach (var kvp in fieldValues)
        {
            result = result.Replace($"{{{kvp.Key}}}", kvp.Value);
        }
        return result;
    }

    private List<Dictionary<string, string>> GenerateAllCombinations(Dictionary<string, List<string>> fieldValues)
    {
        if (fieldValues.Count == 0)
            return new List<Dictionary<string, string>> { new Dictionary<string, string>() };

        var fields = fieldValues.Keys.ToList();
        var combinations = new List<Dictionary<string, string>>();
        GenerateCombinationsRecursive(fieldValues, fields, 0, new Dictionary<string, string>(), combinations);
        
        return combinations;
    }

    private void GenerateCombinationsRecursive(
        Dictionary<string, List<string>> fieldValues,
        List<string> fields,
        int fieldIndex,
        Dictionary<string, string> current,
        List<Dictionary<string, string>> results)
    {
        if (fieldIndex >= fields.Count)
        {
            results.Add(new Dictionary<string, string>(current));
            return;
        }

        var field = fields[fieldIndex];
        foreach (var value in fieldValues[field])
        {
            current[field] = value;
            GenerateCombinationsRecursive(fieldValues, fields, fieldIndex + 1, current, results);
        }
    }

    public static List<string> ExtractTemplateFields(string template)
    {
        var regex = new System.Text.RegularExpressions.Regex(@"\{(\w+)\}");
        var matches = regex.Matches(template);
        var fields = new HashSet<string>();
        
        foreach (System.Text.RegularExpressions.Match match in matches)
        {
            fields.Add(match.Groups[1].Value);
        }
        
        return fields.ToList();
    }

    public static float CalculateCosineSimilarity(float[] vector1, float[] vector2)
    {
        if (vector1.Length != vector2.Length)
            throw new ArgumentException("Vectors must have the same length");

        float dotProduct = 0;
        float magnitude1 = 0;
        float magnitude2 = 0;

        for (int i = 0; i < vector1.Length; i++)
        {
            dotProduct += vector1[i] * vector2[i];
            magnitude1 += vector1[i] * vector1[i];
            magnitude2 += vector2[i] * vector2[i];
        }

        magnitude1 = (float)Math.Sqrt(magnitude1);
        magnitude2 = (float)Math.Sqrt(magnitude2);

        if (magnitude1 == 0 || magnitude2 == 0)
            return 0;

        return dotProduct / (magnitude1 * magnitude2);
    }
}

public class BulkCompletionResult
{
    public string SystemPrompt { get; set; } = "";
    public string UserMessage { get; set; } = "";
    public string GeneratedContent { get; set; } = "";
    public Dictionary<string, string> TemplateValues { get; set; } = new();
    public DateTime Timestamp { get; set; }
    public float[]? Embedding { get; set; }
}

public class BulkCompletionProgress
{
    public int Completed { get; set; }
    public int Total { get; set; }
    public double PercentComplete { get; set; }
}