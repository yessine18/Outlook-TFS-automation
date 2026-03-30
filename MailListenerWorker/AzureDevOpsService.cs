using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;
using Microsoft.VisualStudio.Services.WebApi.Patch;
using Microsoft.VisualStudio.Services.WebApi.Patch.Json;

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
        CancellationToken cancellationToken)
    {
        try
        {
            var patchDocument = new JsonPatchDocument
            {
                new JsonPatchOperation
                {
                    Operation = Operation.Add,
                    Path = "/fields/System.Title",
                    Value = $"[EMAIL] {emailSubject}"
                },
                new JsonPatchOperation
                {
                    Operation = Operation.Add,
                    Path = "/fields/System.Description",
                    Value = $"<b>From:</b> {senderName} ({senderEmail})<br/>" +
                           $"<b>Received:</b> {(receivedDateTime.HasValue ? receivedDateTime.Value.ToString("yyyy-MM-dd HH:mm:ss") : "Unknown")}<br/>" +
                           $"<hr/><b>Content:</b><br/>{emailBody}" +
                           $"<br/><div data-sender-email=\"{senderEmail}\" data-sender-name=\"{senderName}\" style=\"display:none;\"></div>"
                },
                new JsonPatchOperation
                {
                    Operation = Operation.Add,
                    Path = "/fields/System.Tags",
                    Value = "Email; AutoCreated"
                }
            };

            var workItemType = "Issue"; // Default basic type, can be Bug or Task based on your project
            var createdWorkItem = await _witClient.CreateWorkItemAsync(
                patchDocument,
                _projectName,
                workItemType,
                validateOnly: false,
                cancellationToken: cancellationToken);

            // Dynamically get the initial state and add the sent tag so we don't send a duplicate update immediately
            if (createdWorkItem.Fields.TryGetValue("System.State", out var stateObj))
            {
                var initialState = stateObj.ToString().Replace(" ", "");
                var updatedTags = $"Email; AutoCreated; EmailSent_{initialState}";
                
                var tagUpdateDoc = new JsonPatchDocument
                {
                    new JsonPatchOperation { Operation = Operation.Add, Path = "/fields/System.Tags", Value = updatedTags }
                };
                
                createdWorkItem = await _witClient.UpdateWorkItemAsync(tagUpdateDoc, createdWorkItem.Id ?? 0, cancellationToken: cancellationToken);
            }

            _logger.LogInformation(
                "Work item #{WorkItemId} created from email: {Subject}",
                createdWorkItem.Id,
                emailSubject);

            return createdWorkItem;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating work item for email: {Subject}", emailSubject);
            throw;
        }
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
