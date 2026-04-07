using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;
using MailListenerWorker.Models;

namespace MailListenerWorker.Services;

public class GroqLlmService
{
    private readonly ILogger<GroqLlmService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _apiKey;
    private readonly string _apiUrl;
    private readonly string _model;

    public GroqLlmService(ILogger<GroqLlmService> logger, IConfiguration configuration, IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _apiKey = configuration["Groq:ApiKey"] ?? throw new InvalidOperationException("Missing Groq:ApiKey configuration");
        _apiUrl = configuration["Groq:ApiUrl"] ?? "https://api.groq.com/openai/v1/chat/completions";
        _model = configuration["Groq:Model"] ?? "mixtral-8x7b-32768";
    }

    public async Task<ExtractedEmailData> AnalyzeEmailAsync(string emailSubject, string emailBody, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Analyzing email with LLM: {Subject}", emailSubject);

            // Limit email body to prevent token overflow
            var truncatedBody = emailBody.Length > 2000 ? emailBody[..2000] : emailBody;

            var prompt = $$"""
CRITICAL: You MUST respond with ONLY a single JSON object. No markdown. No code blocks. No explanation.

Analyze this email and extract:
- coreProblem: A concise title suitable for a support ticket (1-2 sentences, max 100 chars)
- description: A 2-3 sentence summary of the issue (max 300 chars)
- estimatedHours: How many hours to resolve (integer 1-40)
- severity: One of: Critical, High, Medium, Low
- jobField: The responsible team/role (e.g., "Administrator", "Database – Oracle")
- linksCount: Number of URLs (integer)
- attachmentCount: Likely count (0 or 1)
- confidence: Your confidence 0.0-1.0

Email Subject: {{emailSubject}}

Email Body:
{{truncatedBody}}

Respond with ONLY valid JSON (no markdown, no backticks, no text before or after). Start with { and end with }.
""";

            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");

            var requestBody = new
            {
                model = _model,
                messages = new[]
                {
                    new
                    {
                        role = "user",
                        content = prompt
                    }
                },
                temperature = 0.3,
                max_tokens = 300
            };

            var content = new StringContent(
                JsonSerializer.Serialize(requestBody),
                Encoding.UTF8,
                "application/json");

            _logger.LogDebug("Sending LLM request to {ApiUrl}", _apiUrl);
            var response = await client.PostAsync(_apiUrl, content, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("Groq API error ({StatusCode}): {Error}", response.StatusCode, errorContent);
                return CreateDefaultExtractedData(emailSubject);
            }

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var jsonDoc = JsonDocument.Parse(responseContent);
            var messageContent = jsonDoc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            if (string.IsNullOrEmpty(messageContent))
            {
                _logger.LogWarning("Empty response from Groq API");
                return CreateDefaultExtractedData(emailSubject);
            }

            _logger.LogDebug("LLM Raw Response: {Response}", messageContent);

            // Clean response: remove markdown code blocks if present
            var cleanResponse = messageContent;
            if (cleanResponse.StartsWith("```json"))
                cleanResponse = cleanResponse.Substring(7);
            if (cleanResponse.StartsWith("```"))
                cleanResponse = cleanResponse.Substring(3);
            if (cleanResponse.EndsWith("```"))
                cleanResponse = cleanResponse.Substring(0, cleanResponse.Length - 3);
            cleanResponse = cleanResponse.Trim();

            _logger.LogDebug("LLM Cleaned Response: {Response}", cleanResponse);

            // Parse the JSON response from LLM (handle camelCase JSON)
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
            var extractedData = JsonSerializer.Deserialize<ExtractedEmailData>(cleanResponse, options);

            if (extractedData == null)
            {
                _logger.LogWarning("Failed to parse LLM response as JSON: {Response}", messageContent);
                return CreateDefaultExtractedData(emailSubject);
            }

            _logger.LogInformation(
                "Email analyzed - Problem: {Problem}, Severity: {Severity}, JobField: {JobField}, EstimatedHours: {Hours}",
                extractedData.CoreProblem,
                extractedData.Severity,
                extractedData.JobField,
                extractedData.EstimatedHours);

            return extractedData;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing email with Groq LLM");
            return CreateDefaultExtractedData(emailSubject);
        }
    }

    private static ExtractedEmailData CreateDefaultExtractedData(string emailSubject)
    {
        return new ExtractedEmailData
        {
            CoreProblem = emailSubject.Length > 100 ? emailSubject[..100] : emailSubject,
            Description = "Issue requires manual review",
            EstimatedHours = 4,
            Severity = "Medium",
            JobField = string.Empty,
            LinksCount = 0,
            AttachmentCount = 0,
            Confidence = 0.0
        };
    }
}
