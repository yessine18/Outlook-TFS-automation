using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Azure.Identity;

namespace MailListenerWorker.Services;

/// <summary>
/// Sends Teams notifications to channels using Microsoft Graph API with app credentials
/// </summary>
public class TeamsChatService
{
    private readonly ILogger<TeamsChatService> _logger;
    private readonly GraphServiceClient _graphClient;

    public TeamsChatService(
        ILogger<TeamsChatService> logger,
        IConfiguration configuration)
    {
        _logger = logger;

        var tenantId = configuration["AzureAd:TenantId"];
        var clientId = configuration["AzureAd:ClientId"];
        var clientSecret = configuration["AzureAd:ClientSecret"];

        if (string.IsNullOrWhiteSpace(tenantId) ||
            string.IsNullOrWhiteSpace(clientId) ||
            string.IsNullOrWhiteSpace(clientSecret))
        {
            throw new InvalidOperationException("Missing required AzureAd settings (TenantId, ClientId, ClientSecret).");
        }

        // Use app credentials for posting to channels
        var credential = new ClientSecretCredential(tenantId, clientId, clientSecret);
        _graphClient = new GraphServiceClient(credential, ["https://graph.microsoft.com/.default"]);
    }

    /// <summary>
    /// Sends a message to a Teams channel about a new issue
    /// </summary>
    public async Task SendIssueNotificationAsync(
        string teamId,
        string channelId,
        string ticketId,
        string ticketTitle,
        string severity,
        int estimatedHours,
        string department,
        string senderName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrEmpty(teamId) || string.IsNullOrEmpty(channelId))
            {
                _logger.LogInformation("No team/channel ID provided. Skipping Teams notification for ticket {TicketId}", ticketId);
                return;
            }

            _logger.LogInformation(
                "Sending Teams channel message for ticket {TicketId} to team {TeamId} channel {ChannelId}",
                ticketId,
                teamId,
                channelId);

            var messageBody = BuildMessageContent(ticketId, ticketTitle, severity, estimatedHours, department, senderName);

            var chatMessage = new ChatMessage
            {
                Body = new ItemBody
                {
                    ContentType = BodyType.Html,
                    Content = messageBody
                }
            };

            await SendChannelMessageAsync(teamId, channelId, chatMessage, ticketId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending Teams notification for ticket {TicketId}", ticketId);
        }
    }

    private async Task SendChannelMessageAsync(
        string teamId,
        string channelId,
        ChatMessage message,
        string ticketId,
        CancellationToken cancellationToken)
    {
        try
        {
            // POST to /teams/{teamId}/channels/{channelId}/messages
            await _graphClient.Teams[teamId].Channels[channelId].Messages
                .PostAsync(message, cancellationToken: cancellationToken);

            _logger.LogInformation("Teams channel message sent for ticket {TicketId}", ticketId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending Teams channel message for ticket {TicketId}", ticketId);
        }
    }

    private static string BuildMessageContent(
        string ticketId,
        string title,
        string severity,
        int estimatedHours,
        string department,
        string senderName)
    {
        var severityColor = severity switch
        {
            "Critical" => "#A80000",
            "High" => "#D83B01",
            "Medium" => "#0078D4",
            _ => "#107C10"
        };

        return $"""
<div style="border-left: 4px solid {severityColor}; padding: 12px; font-family: Segoe UI, Arial, sans-serif;">
    <h2 style="margin: 0 0 10px 0; color: #333; font-size: 16px;">🎫 New Issue Assigned</h2>

    <table style="width: 100%; border-collapse: collapse; font-size: 13px;">
        <tr>
            <td style="padding: 6px 0; font-weight: 600; color: #666; width: 30%;">Ticket #</td>
            <td style="padding: 6px 0; color: #333;"><strong>{ticketId}</strong></td>
        </tr>
        <tr>
            <td style="padding: 6px 0; font-weight: 600; color: #666;">Title</td>
            <td style="padding: 6px 0; color: #333;">{title}</td>
        </tr>
        <tr>
            <td style="padding: 6px 0; font-weight: 600; color: #666;">Severity</td>
            <td style="padding: 6px 0; color: #333;"><span style="color: {severityColor}; font-weight: bold;">{severity}</span></td>
        </tr>
        <tr>
            <td style="padding: 6px 0; font-weight: 600; color: #666;">Est. Time</td>
            <td style="padding: 6px 0; color: #333;">{estimatedHours} hours</td>
        </tr>
        <tr>
            <td style="padding: 6px 0; font-weight: 600; color: #666;">Department</td>
            <td style="padding: 6px 0; color: #333;">{department}</td>
        </tr>
        <tr>
            <td style="padding: 6px 0; font-weight: 600; color: #666;">Reporter</td>
            <td style="padding: 6px 0; color: #333;">{senderName}</td>
        </tr>
    </table>
</div>
""";
    }
}
