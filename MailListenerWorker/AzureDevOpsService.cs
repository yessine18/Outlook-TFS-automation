using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;
using Microsoft.VisualStudio.Services.WebApi.Patch;
using Microsoft.VisualStudio.Services.WebApi.Patch.Json;
using MailListenerWorker.Models;

namespace MailListenerWorker;

public class AzureDevOpsService
{
    private readonly ILogger<AzureDevOpsService> _logger;
    private readonly VssConnection _connection;
    private readonly WorkItemTrackingHttpClient _witClient;
    private readonly string _projectName;

    public AzureDevOpsService(IConfiguration configuration, ILogger<AzureDevOpsService> logger)
    {
        _logger = logger;

        var organizationUrl = configuration["AzureDevOps:OrganizationUrl"]
            ?? throw new InvalidOperationException("Missing AzureDevOps:OrganizationUrl");
        var patToken = configuration["AzureDevOps:PatToken"]
            ?? throw new InvalidOperationException("Missing AzureDevOps:PatToken");
        _projectName = configuration["AzureDevOps:ProjectName"]
            ?? throw new InvalidOperationException("Missing AzureDevOps:ProjectName");

        var credentials = new VssBasicCredential("", patToken);
        _connection = new VssConnection(new Uri(organizationUrl), credentials);
        _witClient = _connection.GetClient<WorkItemTrackingHttpClient>();
    }

    public async Task<WorkItem> CreateEmailWorkItemAsync(
        string emailSubject,
        string emailBody,
        string senderEmail,
        string senderName,
        DateTimeOffset? receivedDateTime,
        ExtractedEmailData? extractedData = null,
        string? assigneeEmail = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Use LLM-extracted core problem as title, or fall back to subject
            var titleText = !string.IsNullOrEmpty(extractedData?.CoreProblem)
                ? extractedData.CoreProblem
                : emailSubject;

            // Use LLM-extracted description if available, otherwise use full email body preview
            var descriptionText = !string.IsNullOrEmpty(extractedData?.Description)
                ? $"{extractedData.Description}<br/><hr/>"
                : string.Empty;

            // Build description with metadata
            var fullDescription = $"<b>From:</b> {senderName} ({senderEmail})<br/>" +
                                 $"<b>Received:</b> {(receivedDateTime.HasValue ? receivedDateTime.Value.ToString("yyyy-MM-dd HH:mm:ss") : "Unknown")}<br/>" +
                                 (extractedData != null
                                     ? $"<b>Estimated Time:</b> {extractedData.EstimatedHours} hours<br/>" +
                                       $"<b>Severity:</b> {extractedData.Severity}<br/>" +
                                       $"<b>Job Field:</b> {extractedData.JobField}<br/><hr/>"
                                     : "") +
                                 $"<b>Summary:</b><br/>{descriptionText}" +
                                 BuildHiddenMetadata(senderEmail, senderName, extractedData, assigneeEmail);

            var patchDocument = new JsonPatchDocument
            {
                new JsonPatchOperation
                {
                    Operation = Operation.Add,
                    Path = "/fields/System.Title",
                    Value = titleText
                },
                new JsonPatchOperation
                {
                    Operation = Operation.Add,
                    Path = "/fields/System.Description",
                    Value = fullDescription
                },
                new JsonPatchOperation
                {
                    Operation = Operation.Add,
                    Path = "/fields/System.Tags",
                    Value = "Email; AutoCreated"
                }
            };

            // Add priority if severity is extracted
            if (extractedData != null)
            {
                patchDocument.Add(new JsonPatchOperation
                {
                    Operation = Operation.Add,
                    Path = "/fields/Microsoft.VSTS.Common.Priority",
                    Value = extractedData.GetPriority()
                });
            }

            // Add assignee if provided
            if (!string.IsNullOrEmpty(assigneeEmail))
            {
                patchDocument.Add(new JsonPatchOperation
                {
                    Operation = Operation.Add,
                    Path = "/fields/System.AssignedTo",
                    Value = assigneeEmail
                });
            }

            var workItemType = "Issue";
            var createdWorkItem = await _witClient.CreateWorkItemAsync(
                patchDocument,
                _projectName,
                workItemType,
                validateOnly: false,
                cancellationToken: cancellationToken);

            if (createdWorkItem == null)
            {
                throw new InvalidOperationException("Failed to create work item: received null response from Azure DevOps");
            }

            // Dynamically get the initial state and add the sent tag so we don't send a duplicate update immediately
            if (createdWorkItem.Fields != null)
            {
                if (createdWorkItem.Fields.TryGetValue("System.State", out var stateObj))
                {
                    var initialState = stateObj?.ToString()?.Replace(" ", "") ?? "To Do";
                    var updatedTags = $"Email; AutoCreated; EmailSent_{initialState}";

                    var tagUpdateDoc = new JsonPatchDocument
                    {
                        new JsonPatchOperation { Operation = Operation.Add, Path = "/fields/System.Tags", Value = updatedTags }
                    };

                    var updatedItem = await _witClient.UpdateWorkItemAsync(tagUpdateDoc, createdWorkItem.Id ?? 0, cancellationToken: cancellationToken);
                    if (updatedItem != null)
                    {
                        createdWorkItem = updatedItem;
                    }
                }
            }

            _logger.LogInformation(
                "Work item #{WorkItemId} created from email: {Subject} (Priority: {Priority}, Severity: {Severity})",
                createdWorkItem.Id,
                titleText,
                extractedData?.GetPriority() ?? 3,
                extractedData?.Severity ?? "Unknown");

            return createdWorkItem;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating work item for email: {Subject}", emailSubject);
            throw;
        }
    }

    private static string BuildHiddenMetadata(
        string senderEmail,
        string senderName,
        ExtractedEmailData? extractedData,
        string? assigneeEmail)
    {
        var metadata = $"<br/><div style=\"display:none;\">" +
                      $"<span data-sender-email=\"{senderEmail}\"></span>" +
                      $"<span data-sender-name=\"{senderName}\"></span>";

        if (extractedData != null)
        {
            metadata += $"<span data-severity=\"{extractedData.Severity}\"></span>" +
                       $"<span data-estimated-hours=\"{extractedData.EstimatedHours}\"></span>" +
                       $"<span data-job-field=\"{extractedData.JobField}\"></span>" +
                       $"<span data-links-count=\"{extractedData.LinksCount}\"></span>" +
                       $"<span data-extracted-description=\"{HtmlEncode(extractedData.Description)}\"></span>";
        }

        if (!string.IsNullOrEmpty(assigneeEmail))
        {
            metadata += $"<span data-assigned-to=\"{assigneeEmail}\"></span>";
        }

        metadata += "</div>";
        return metadata;
    }

    private static string HtmlEncode(string text)
    {
        return System.Web.HttpUtility.HtmlEncode(text).Replace("\"", "&quot;");
    }

    public async Task<List<WorkItem>> GetUpdatedWorkItemsAsync(string tagFilter, CancellationToken cancellationToken)
    {
        try
        {
            // Query for Work Items that have 'AutoCreated' tag but we need to check their states
            var wiql = new Wiql
            {
                Query = $"Select [System.Id], [System.State], [System.Tags], [System.Description], [System.Title] " +
                        $"From WorkItems " +
                        $"Where [System.TeamProject] = '{_projectName}' " +
                        $"And [System.Tags] Contains '{tagFilter}' " +
                        $"And [System.ChangedDate] > @today - 7"
            };

            var result = await _witClient.QueryByWiqlAsync(wiql, cancellationToken: cancellationToken);
            if (result.WorkItems == null || !result.WorkItems.Any())
                return new List<WorkItem>();

            var ids = result.WorkItems.Select(wi => wi.Id).ToArray();

            // Getting fields involves a separate query by ID
            return await _witClient.GetWorkItemsAsync(
                ids,
                expand: WorkItemExpand.Fields,
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching updated work items");
            return new List<WorkItem>();
        }
    }

    public async Task AddWorkItemTagAsync(int workItemId, string currentTags, string newTag, CancellationToken cancellationToken)
    {
        try
        {
            var updatedTags = string.IsNullOrEmpty(currentTags) ? newTag : $"{currentTags}; {newTag}";

            var patchDocument = new JsonPatchDocument
            {
                new JsonPatchOperation
                {
                    Operation = Operation.Add,
                    Path = "/fields/System.Tags",
                    Value = updatedTags
                }
            };

            await _witClient.UpdateWorkItemAsync(
                patchDocument,
                workItemId,
                validateOnly: false,
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding tag to work item #{WorkItemId}", workItemId);
        }
    }
}
