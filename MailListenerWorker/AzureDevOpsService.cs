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

            // Build a rich, structured description with ALL extracted details
            var descBuilder = new System.Text.StringBuilder();

            // ── Header: Sender & Metadata ──
            descBuilder.Append($"<b>From:</b> {senderName} ({senderEmail})<br/>");
            descBuilder.Append($"<b>Received:</b> {(receivedDateTime.HasValue ? receivedDateTime.Value.ToString("yyyy-MM-dd HH:mm:ss") : "Unknown")}<br/>");

            if (extractedData != null)
            {
                descBuilder.Append($"<b>Severity:</b> {extractedData.Severity}<br/>");
                descBuilder.Append($"<b>Estimated Time:</b> {extractedData.EstimatedHours} hours<br/>");
                descBuilder.Append($"<b>Job Field:</b> {extractedData.JobField}<br/>");
                descBuilder.Append($"<b>Confidence:</b> {extractedData.Confidence:P0}<br/>");
                descBuilder.Append("<hr/>");

                // ── Summary ──
                if (!string.IsNullOrEmpty(extractedData.Description))
                {
                    descBuilder.Append($"<b>Summary:</b><br/>{extractedData.Description}<br/><br/>");
                }

                // ── Detailed Description (the key improvement) ──
                if (!string.IsNullOrEmpty(extractedData.DetailedDescription) && extractedData.DetailedDescription != "N/A")
                {
                    descBuilder.Append($"<b>📋 Detailed Description:</b><br/>{extractedData.DetailedDescription}<br/><br/>");
                }

                // ── Affected Systems ──
                if (!string.IsNullOrEmpty(extractedData.AffectedSystems) && extractedData.AffectedSystems != "N/A")
                {
                    descBuilder.Append($"<b>🖥️ Affected Systems:</b><br/>{extractedData.AffectedSystems}<br/><br/>");
                }

                // ── Error Codes ──
                if (!string.IsNullOrEmpty(extractedData.ErrorCodes) && extractedData.ErrorCodes != "N/A")
                {
                    descBuilder.Append($"<b>⚠️ Error Codes:</b><br/>{extractedData.ErrorCodes}<br/><br/>");
                }

                // ── Impact Scope ──
                if (!string.IsNullOrEmpty(extractedData.ImpactScope) && extractedData.ImpactScope != "N/A")
                {
                    descBuilder.Append($"<b>💥 Impact:</b><br/>{extractedData.ImpactScope}<br/><br/>");
                }

                // ── Steps to Reproduce ──
                if (!string.IsNullOrEmpty(extractedData.StepsToReproduce) && extractedData.StepsToReproduce != "N/A")
                {
                    descBuilder.Append($"<b>🔄 Steps / Timeline:</b><br/>{extractedData.StepsToReproduce}<br/><br/>");
                }

                // ── Requested Action ──
                if (!string.IsNullOrEmpty(extractedData.RequestedAction) && extractedData.RequestedAction != "N/A")
                {
                    descBuilder.Append($"<b>✅ Requested Action:</b><br/>{extractedData.RequestedAction}<br/><br/>");
                }
            }

            // ── Hidden metadata for auto-reply system ──
            descBuilder.Append(BuildHiddenMetadata(senderEmail, senderName, extractedData, assigneeEmail));

            var fullDescription = descBuilder.ToString();

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
                    Value = "Email, AutoCreated"
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
            WorkItem? createdWorkItem;
            try
            {
                createdWorkItem = await _witClient.CreateWorkItemAsync(
                    patchDocument,
                    _projectName,
                    workItemType,
                    validateOnly: false,
                    cancellationToken: cancellationToken);
            }
            catch (Microsoft.VisualStudio.Services.Common.VssServiceException ex) when (ex.Message.Contains("identity", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("The identity '{Assignee}' for field 'Assigned To' is unknown in Azure DevOps. Retrying creation without assignee.", assigneeEmail);
                
                // Remove the AssignedTo operation and retry
                var retryPatch = new JsonPatchDocument();
                foreach (var op in patchDocument)
                {
                    if (op.Path != "/fields/System.AssignedTo")
                        retryPatch.Add(op);
                }

                createdWorkItem = await _witClient.CreateWorkItemAsync(
                    retryPatch,
                    _projectName,
                    workItemType,
                    validateOnly: false,
                    cancellationToken: cancellationToken);
            }

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
                    var updatedTags = $"Email, AutoCreated, EmailSent_{initialState}";

                    var tagUpdateDoc = new JsonPatchDocument
                    {
                        new JsonPatchOperation { Operation = Operation.Add, Path = "/fields/System.Tags", Value = updatedTags }
                    };

                    var updatedItem = await _witClient.UpdateWorkItemAsync(tagUpdateDoc, createdWorkItem.Id ?? 0, cancellationToken: cancellationToken);
                    
                    _logger.LogInformation("Added default tags to WorkItem #{Id}", createdWorkItem.Id);
                    return updatedItem ?? createdWorkItem;
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

    /// <summary>
    /// Updates the state of an existing Azure DevOps work item via HTTP PATCH.
    /// </summary>
    public async Task UpdateWorkItemStateAsync(int workItemId, string newState, CancellationToken cancellationToken = default)
    {
        try
        {
            var patchDocument = new JsonPatchDocument
            {
                new JsonPatchOperation
                {
                    Operation = Operation.Add,
                    Path = "/fields/System.State",
                    Value = newState
                }
            };

            await _witClient.UpdateWorkItemAsync(
                patchDocument,
                workItemId,
                cancellationToken: cancellationToken);

            _logger.LogInformation("✅ Successfully updated WorkItem #{WorkItemId} state to '{NewState}'", workItemId, newState);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to update state to '{NewState}' for WorkItem #{WorkItemId}", newState, workItemId);
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
                      $"<span data-sender-email=\"{HtmlEncode(senderEmail)}\"></span>" +
                      $"<span data-sender-name=\"{HtmlEncode(senderName)}\"></span>";

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

    public async Task<WorkItem?> GetWorkItemByIdAsync(int id, CancellationToken cancellationToken)
    {
        try
        {
            return await _witClient.GetWorkItemAsync(id, expand: WorkItemExpand.Fields, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch WorkItem #{Id} directly.", id);
            return null;
        }
    }

    public async Task<List<WorkItem>> GetUpdatedWorkItemsAsync(string tagFilter, CancellationToken cancellationToken)
    {
        try
        {
            // Query for Work Items that have 'AutoCreated' tag but we need to check their states
            // 1. FETCH ALL RECENT ITEMS IN THE PROJECT ─────────────────
            var wiql = new Wiql
            {
                Query = $"Select [System.Id], [System.State], [System.Tags], [System.Description], [System.Title] " +
                        $"From WorkItems " +
                        $"Where [System.TeamProject] = '{_projectName}' " +
                        $"And ([System.Tags] Contains '{tagFilter}' OR [System.WorkItemType] = 'Issue') " +
                        $"And [System.ChangedDate] > @today - 30"
            };

            var result = await _witClient.QueryByWiqlAsync(wiql, cancellationToken: cancellationToken);
            _logger.LogInformation("WIQL Query found {Count} recent issues to check.", result.WorkItems?.Count() ?? 0);

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
    public async Task AddWorkItemCommentAsync(int workItemId, string commentText, CancellationToken cancellationToken = default)
    {
        try
        {
            var patchDocument = new JsonPatchDocument
            {
                new JsonPatchOperation
                {
                    Operation = Operation.Add,
                    Path = "/fields/System.History",
                    Value = commentText
                }
            };

            await _witClient.UpdateWorkItemAsync(
                patchDocument,
                workItemId,
                validateOnly: false,
                cancellationToken: cancellationToken);
                
            _logger.LogInformation("Added feedback comment to WorkItem #{WorkItemId}", workItemId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding comment to work item #{WorkItemId}", workItemId);
        }
    }
}
