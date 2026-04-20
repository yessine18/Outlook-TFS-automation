using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Diagnostics;
using Npgsql;
using MailListenerWorker.Models;

namespace MailListenerWorker.Services;

public class GroqLlmService
{
    private readonly ILogger<GroqLlmService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _apiKey;
    private readonly string _apiUrl;
    private readonly string _model;
    private readonly IConfiguration _configuration;

    public GroqLlmService(ILogger<GroqLlmService> logger, IConfiguration configuration, IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _apiKey = configuration["Groq:ApiKey"] ?? throw new InvalidOperationException("Missing Groq:ApiKey configuration");
        _apiUrl = configuration["Groq:ApiUrl"] ?? "https://api.groq.com/openai/v1/chat/completions";
        _model = configuration["Groq:Model"] ?? "llama-3.3-70b-versatile";
    }

    public async Task<ExtractedEmailData> AnalyzeEmailAsync(string emailSubject, string emailBody, IEnumerable<string> supportedFields, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Analyzing email with LLM: {Subject}", emailSubject);

            var fieldsList = string.Join(", ", supportedFields.Select(f => $"\"{f}\""));
            // Limit email body to prevent token overflow (llama-3.3-70b has 128K context)
            var truncatedBody = emailBody.Length > 6000 ? emailBody[..6000] : emailBody;

            var prompt = $$"""
CRITICAL: You MUST respond with ONLY a single JSON object. No markdown. No code blocks. No explanation.

Analyze this support email and extract ALL information. Do NOT summarize or generalize.
Preserve every specific piece of data: server names, IP addresses, error codes, user counts,
timestamps, version numbers, file paths, account names, and any other concrete details.

Extract the following fields:
- coreProblem: A concise but specific title for a support ticket (max 150 chars). Include key identifiers (server names, error codes) in the title.
- description: A brief 2-3 sentence summary of the issue.
- detailedDescription: A THOROUGH description that preserves ALL specific facts, numbers, names, dates, error messages, and technical details from the email. Do NOT omit any data. This should be comprehensive enough that someone reading it has ALL the information from the original email without needing to see it.
- affectedSystems: Provide a SINGLE COMMA-SEPARATED STRING listing ALL specific systems, servers, applications, databases, or infrastructure mentioned (e.g., "DB-PROD-03, Oracle 19c"). Do NOT use JSON arrays. Use "N/A" if none.
- errorCodes: Provide a SINGLE COMMA-SEPARATED STRING listing ALL error codes or exception messages (e.g., "ORA-12541, HTTP 503"). Do NOT use JSON arrays. Use "N/A" if none.
- stepsToReproduce: If the sender describes steps, a sequence of events, or a timeline, capture them in order as a single string. Use "N/A" if not described.
- impactScope: Who or what is affected — user count, department names, environment (production/staging/dev), geographic scope (e.g., "200 users, production"). Use "N/A" if not specified.
- requestedAction: What the sender is explicitly asking for or expecting as resolution as a single string. Use "N/A" if no specific action requested.
- estimatedHours: How many hours to resolve (integer 1-40)
- severity: One of: Critical, High, Medium, Low
- jobField: MANDATORY: You must select the MOST RELEVANT job field ONLY from this exact comma-separated list: [{{fieldsList}}]. Do NOT invent new fields.
- linksCount: Number of URLs found in the email (integer)
- attachmentCount: Likely attachment count (0 or 1)
- confidence: Your confidence in the extraction accuracy 0.0-1.0

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
                temperature = 0.2,
                max_tokens = 1500
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
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                NumberHandling = JsonNumberHandling.AllowReadingFromString
            };
            var extractedData = JsonSerializer.Deserialize<ExtractedEmailData>(cleanResponse, options);

            if (extractedData == null)
            {
                _logger.LogWarning("Failed to parse LLM response as JSON: {Response}", messageContent);
                return CreateDefaultExtractedData(emailSubject);
            }

            _logger.LogInformation(
                "Email analyzed - Problem: {Problem}, Severity: {Severity}, JobField: {JobField}, EstimatedHours: {Hours}, AffectedSystems: {Systems}, ErrorCodes: {Errors}, Impact: {Impact}",
                extractedData.CoreProblem,
                extractedData.Severity,
                extractedData.JobField,
                extractedData.EstimatedHours,
                extractedData.AffectedSystems,
                extractedData.ErrorCodes,
                extractedData.ImpactScope);

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
            CoreProblem = emailSubject.Length > 150 ? emailSubject[..150] : emailSubject,
            Description = "Issue requires manual review",
            DetailedDescription = "LLM extraction failed — original email should be reviewed manually.",
            AffectedSystems = "N/A",
            ErrorCodes = "N/A",
            StepsToReproduce = "N/A",
            ImpactScope = "N/A",
            RequestedAction = "N/A",
            EstimatedHours = 4,
            Severity = "Medium",
            JobField = string.Empty,
            LinksCount = 0,
            AttachmentCount = 0,
            Confidence = 0.0
        };
    }

    public async Task<RagVerdict> EvaluateRagSolutionAsync(string detailedDescription, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Starting RAG Evaluation for description...");
            
            // 1. Run Python to get the vector array string
            var pythonPath = @"c:\Users\fakhf\OneDrive\Desktop\PFE\inetum-ms-kb\.venv\Scripts\python.exe";
            var scriptPath = @"c:\Users\fakhf\OneDrive\Desktop\PFE\inetum-ms-kb\src\embed\query_vector.py";
            
            var processStartInfo = new ProcessStartInfo
            {
                FileName = pythonPath,
                Arguments = $"\"{scriptPath}\" \"{detailedDescription.Replace("\"", "\\\"").Replace("\n", " ")}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(processStartInfo);
            if (process == null) throw new Exception("Failed to start python process");
            
            var vectorJsonStr = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            
            if (process.ExitCode != 0)
            {
                var error = await process.StandardError.ReadToEndAsync(cancellationToken);
                _logger.LogWarning("Python vector script failed: {Error}", error);
                return CreateDefaultRagVerdict();
            }

            // 2. Search PostgreSQL database for the top 3 chunks
            var connectionString = _configuration.GetConnectionString("DefaultConnection") 
                ?? "Host=localhost;Port=5432;Database=helpdesk_pipeline;Username=postgres;Password=secret";
                
            var docsList = new List<object>();
            
            using (var conn = new NpgsqlConnection(connectionString))
            {
                await conn.OpenAsync(cancellationToken);
                using var cmd = new NpgsqlCommand(@"
                    SELECT document_url, document_title, chunk_text, 1 - (embedding <=> @emb::vector) AS similarity 
                    FROM microsoft_docs 
                    ORDER BY embedding <=> @emb::vector 
                    LIMIT 3;", conn);
                    
                cmd.Parameters.AddWithValue("emb", vectorJsonStr.Trim());
                
                using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    docsList.Add(new {
                        Url = reader.GetString(0),
                        Title = reader.GetString(1),
                        Chunk = reader.GetString(2),
                        Similarity = reader.GetDouble(3)
                    });
                }
            }

            // 3. Send final payload to Groq to make the Auto-Resolve decision!
            var docsJsonString = JsonSerializer.Serialize(docsList);
            
            var prompt = $$"""
            You are a Level-3 AI Helpdesk Agent.

            USER'S PROBLEM DESCRIPTION:
            {{detailedDescription}}

            OFFICIAL MICROSOFT KNOWLEDGE BASE:
            {{docsJsonString}}

            Analyze the Problem against the Knowledge Base chunks.
            Does the Knowledge base provide a verified, explicit solution to the problem?

            Respond in STRICT JSON FORMAT:
            {
              "hasSolution": true/false,
              "confidenceScore": float <0.0-1.0>,
              "proposedSolution": "Clear steps or explanation to solve the user's issue based strictly on the docs. N/A if none.",
              "referenceUrls": ["url1", "url2"]
            }
            """;

            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");

            var requestBody = new
            {
                model = _model,
                messages = new[] { new { role = "user", content = prompt } },
                temperature = 0.1,
                response_format = new { type = "json_object" }
            };

            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
            var response = await client.PostAsync(_apiUrl, content, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Groq API error for RAG Verdict");
                return CreateDefaultRagVerdict();
            }

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var jsonDoc = JsonDocument.Parse(responseContent);
            var messageContent = jsonDoc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
            
            if (string.IsNullOrEmpty(messageContent)) return CreateDefaultRagVerdict();
            
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var verdict = JsonSerializer.Deserialize<RagVerdict>(messageContent, options);
            
            _logger.LogInformation("RAG Complete -> HasSolution: {HasSolution}, Confidence: {Confidence}", verdict?.HasSolution, verdict?.ConfidenceScore);
            return verdict ?? CreateDefaultRagVerdict();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing RAG Pipeline Verdict");
            return CreateDefaultRagVerdict();
        }
    }

    private static RagVerdict CreateDefaultRagVerdict() => new() { HasSolution = false, ConfidenceScore = 0.0, ProposedSolution = "Error in RAG Pipeline" };
}
