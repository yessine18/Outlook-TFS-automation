using Microsoft.Extensions.Logging;
using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration.Attributes;
using MailListenerWorker.Models;

namespace MailListenerWorker.Services;

/// <summary>
/// Service for loading and managing job field mappings from CSV
/// </summary>
public class JobFieldMappingService
{
    private readonly ILogger<JobFieldMappingService> _logger;
    private readonly Dictionary<string, JobFieldMapping> _jobFieldCache;
    private readonly string _csvPath;

    public JobFieldMappingService(ILogger<JobFieldMappingService> logger, string csvPath)
    {
        _logger = logger;
        _csvPath = csvPath;
        _jobFieldCache = new Dictionary<string, JobFieldMapping>(StringComparer.OrdinalIgnoreCase);
        LoadCsvAsync().GetAwaiter().GetResult();
    }

    /// <summary>
    /// Loads job field mappings from CSV file
    /// Expected CSV format: Display name (Job Field), User principal name (Email), Department
    /// </summary>
    private async Task LoadCsvAsync()
    {
        try
        {
            // Resolve the CSV file path
            string resolvedPath = _csvPath;

            // If path is relative, look in current directory and app base directory
            if (!Path.IsPathRooted(_csvPath))
            {
                string currentDir = Environment.CurrentDirectory;
                string appDir = AppContext.BaseDirectory;

                string pathInCurrent = Path.Combine(currentDir, _csvPath);
                string pathInApp = Path.Combine(appDir, _csvPath);

                if (File.Exists(pathInCurrent))
                    resolvedPath = pathInCurrent;
                else if (File.Exists(pathInApp))
                    resolvedPath = pathInApp;
            }

            if (!File.Exists(resolvedPath))
            {
                _logger.LogWarning("Job field CSV file not found at {Path} (resolved to {ResolvedPath})", _csvPath, resolvedPath);
                return;
            }

            using var reader = new StreamReader(resolvedPath);
            using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);

            var records = await Task.Run(() => csv.GetRecords<JobFieldCsvRecord>().ToList());

            foreach (var record in records)
            {
                if (string.IsNullOrWhiteSpace(record.DisplayName) || string.IsNullOrWhiteSpace(record.Email))
                    continue;

                var mapping = new JobFieldMapping
                {
                    JobField = record.DisplayName.Trim(),
                    Email = record.Email.Trim(),
                    Department = record.Department?.Trim() ?? string.Empty,
                    TeamId = record.TeamId?.Trim() ?? string.Empty,
                    ChannelId = record.ChannelId?.Trim() ?? string.Empty,
                    WebhookUrl = record.WebhookUrl?.Trim() ?? string.Empty
                };

                _jobFieldCache[mapping.JobField] = mapping;
                _logger.LogDebug("Loaded job field: {JobField} → {Email}", mapping.JobField, mapping.Email);
            }

            _logger.LogInformation("Loaded {Count} job field mappings from CSV at {Path}", _jobFieldCache.Count, resolvedPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading job field CSV from {Path}", _csvPath);
        }
    }

    /// <summary>
    /// Resolves a job field to an email address
    /// </summary>
    public string ResolveEmail(string jobField, string defaultEmail = "")
    {
        if (string.IsNullOrEmpty(jobField))
        {
            _logger.LogInformation("No job field specified, using default email");
            return defaultEmail;
        }

        if (_jobFieldCache.TryGetValue(jobField, out var mapping))
        {
            _logger.LogInformation("Resolved job field '{JobField}' to email {Email}", jobField, mapping.Email);
            return mapping.Email;
        }

        _logger.LogWarning("Job field '{JobField}' not found in mapping, using default email", jobField);
        return defaultEmail;
    }

    /// <summary>
    /// Gets all available job fields
    /// </summary>
    public IEnumerable<string> GetAllJobFields() => _jobFieldCache.Keys;

    /// <summary>
    /// Gets the full mapping for a job field
    /// </summary>
    public JobFieldMapping? GetMapping(string jobField)
    {
        _jobFieldCache.TryGetValue(jobField, out var mapping);
        return mapping;
    }

    /// <summary>
    /// Helper class for CSV deserialization
    /// </summary>
    private class JobFieldCsvRecord
    {
        [Name("Job field")]
        public string DisplayName { get; set; } = string.Empty;

        [Name("User principal name")]
        public string Email { get; set; } = string.Empty;

        [Name("Department")]
        public string Department { get; set; } = string.Empty;

        [Name("TeamId")] // The CSV in the downloads folder accidentally has a trailing space on 'TeamId ' 
        public string TeamId { get; set; } = string.Empty;

        [Name("ChannelId")]
        public string ChannelId { get; set; } = string.Empty;

        [Name("WebhookUrl")]
        public string WebhookUrl { get; set; } = string.Empty;
    }
}
