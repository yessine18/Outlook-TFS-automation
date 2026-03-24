using Azure.Identity;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MailListenerWorker
{
    public class MailPollingService : BackgroundService
    {
        private readonly ILogger<MailPollingService> _logger;
        private readonly GraphServiceClient _graphClient;
        private readonly string _mailboxUser;

        public MailPollingService(ILogger<MailPollingService> logger)
        {
            _logger = logger;

            var tenantId = "YOUR_TENANT_ID";
            var clientId = "YOUR_CLIENT_ID";
            var clientSecret = "YOUR_CLIENT_SECRET";
            _mailboxUser = "Support@M365x62207154.OnMicrosoft.com";

            var credential = new ClientSecretCredential(tenantId, clientId, clientSecret);
            _graphClient = new GraphServiceClient(credential, new[] { "https://graph.microsoft.com/.default" });
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("MailPollingService started.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var messages = await _graphClient
                        .Users[_mailboxUser]
                        .MailFolders["Inbox"]
                        .Messages
                        .GetAsync(config =>
                        {
                            config.QueryParameters.Filter = "isRead eq false";
                            config.QueryParameters.Top = 10;
                            config.QueryParameters.Select =
                                new[] { "id", "subject", "from", "receivedDateTime", "bodyPreview", "isRead" };
                        }, stoppingToken);

                    if (messages?.Value != null)
                    {
                        foreach (var msg in messages.Value)
                        {
                            if (msg.IsRead == true) continue;

                            // TODO: later call Python Agent A (via HTTP) and SQL storage
                            _logger.LogInformation(
                                "\n========== NEW EMAIL ==========\n" +
                                "Subject: {Subject}\n" +
                                "From: {From}\n" +
                                "Received: {ReceivedAt}\n" +
                                "Preview:\n{Body}\n" +
                                "================================",
                                msg.Subject,
                                msg.From?.EmailAddress?.Address,
                                msg.ReceivedDateTime?.ToString("yyyy-MM-dd HH:mm:ss"),
                                msg.BodyPreview);

                            // Send auto-reply
                            var senderEmail = msg.From?.EmailAddress?.Address;
                            var senderName = msg.From?.EmailAddress?.Name ?? senderEmail;

                            if (!string.IsNullOrEmpty(senderEmail))
                            {
                                await SendAutoReplyAsync(senderEmail, senderName, msg.Subject, stoppingToken);
                                _logger.LogInformation("Auto-reply sent to: {Email}", senderEmail);
                            }

                            // Mark as read
                            await _graphClient
                                .Users[_mailboxUser]
                                .Messages[msg.Id]
                                .PatchAsync(new Message { IsRead = true }, cancellationToken: stoppingToken);

                            _logger.LogInformation("Marked as read: {Id}", msg.Id);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error while polling mailbox");
                }

                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }

            _logger.LogInformation("MailPollingService stopping.");
        }

        private async Task SendAutoReplyAsync(string recipientEmail, string recipientName, string? originalSubject, CancellationToken cancellationToken)
        {
            var ticketNumber = $"TKT-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString()[..6].ToUpper()}";

            var htmlTemplate = $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='UTF-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
</head>
<body style='margin: 0; padding: 0; font-family: Segoe UI, Tahoma, Geneva, Verdana, sans-serif; background-color: #f4f7fa;'>
    <table role='presentation' width='100%' cellspacing='0' cellpadding='0' style='background-color: #f4f7fa;'>
        <tr>
            <td align='center' style='padding: 40px 20px;'>
                <table role='presentation' width='600' cellspacing='0' cellpadding='0' style='background-color: #ffffff; border-radius: 12px; box-shadow: 0 4px 20px rgba(0, 0, 0, 0.1); overflow: hidden;'>

                    <!-- Header with Logo -->
                    <tr>
                        <td style='background: linear-gradient(135deg, #1e3a5f 0%, #2d5a87 100%); padding: 40px 50px; text-align: center;'>
                            <!--[if mso]>
                            <v:rect xmlns:v='urn:schemas-microsoft-com:vml' fill='true' stroke='false' style='width:600px;height:120px;'>
                            <v:fill type='gradient' color='#2d5a87' color2='#1e3a5f' angle='135'/>
                            <v:textbox inset='0,0,0,0'>
                            <![endif]-->
                            <div>
                                <!-- Company Logo -->
                                <img src='https://i.imgur.com/mwLOpeI.png' alt='Company Logo' style='max-width: 180px; height: auto; margin-bottom: 15px;'/>
                                <h1 style='color: #ffffff; margin: 0; font-size: 24px; font-weight: 600; letter-spacing: 1px;'>Support Team</h1>
                            </div>
                            <!--[if mso]></v:textbox></v:rect><![endif]-->
                        </td>
                    </tr>

                    <!-- Confirmation Badge -->
                    <tr>
                        <td align='center' style='padding: 30px 50px 0;'>
                            <table role='presentation' cellspacing='0' cellpadding='0'>
                                <tr>
                                    <td style='background-color: #e8f5e9; border-radius: 50px; padding: 12px 30px;'>
                                        <span style='color: #2e7d32; font-size: 14px; font-weight: 600;'>✓ REQUEST RECEIVED</span>
                                    </td>
                                </tr>
                            </table>
                        </td>
                    </tr>

                    <!-- Main Content -->
                    <tr>
                        <td style='padding: 30px 50px;'>
                            <h2 style='color: #1e3a5f; margin: 0 0 20px; font-size: 22px; font-weight: 600;'>Dear {recipientName},</h2>

                            <p style='color: #4a5568; font-size: 15px; line-height: 1.8; margin: 0 0 20px;'>
                                Thank you for contacting us. We have successfully received your inquiry and want to assure you that it is now in the hands of our dedicated support team.
                            </p>

                            <!-- Ticket Info Box -->
                            <table role='presentation' width='100%' cellspacing='0' cellpadding='0' style='margin: 25px 0;'>
                                <tr>
                                    <td style='background: linear-gradient(135deg, #f8fafc 0%, #eef2f7 100%); border-left: 4px solid #1e3a5f; border-radius: 8px; padding: 25px;'>
                                        <table role='presentation' width='100%' cellspacing='0' cellpadding='0'>
                                            <tr>
                                                <td>
                                                    <p style='color: #64748b; font-size: 12px; text-transform: uppercase; letter-spacing: 1px; margin: 0 0 8px;'>Reference Number</p>
                                                    <p style='color: #1e3a5f; font-size: 20px; font-weight: 700; margin: 0 0 15px; font-family: Consolas, monospace;'>{ticketNumber}</p>
                                                    <p style='color: #64748b; font-size: 12px; text-transform: uppercase; letter-spacing: 1px; margin: 0 0 8px;'>Subject</p>
                                                    <p style='color: #334155; font-size: 14px; margin: 0;'>{originalSubject ?? "Your Request"}</p>
                                                </td>
                                            </tr>
                                        </table>
                                    </td>
                                </tr>
                            </table>

                            <h3 style='color: #1e3a5f; font-size: 16px; margin: 25px 0 15px; font-weight: 600;'>What happens next?</h3>

                            <!-- Timeline -->
                            <table role='presentation' width='100%' cellspacing='0' cellpadding='0'>
                                <tr>
                                    <td style='padding: 10px 0;'>
                                        <table role='presentation' cellspacing='0' cellpadding='0'>
                                            <tr>
                                                <td style='width: 40px; vertical-align: top;'>
                                                    <div style='width: 28px; height: 28px; background-color: #1e3a5f; border-radius: 50%; text-align: center; line-height: 28px; color: white; font-size: 12px; font-weight: bold;'>1</div>
                                                </td>
                                                <td style='padding-left: 15px;'>
                                                    <p style='color: #334155; font-size: 14px; margin: 0; font-weight: 600;'>Review</p>
                                                    <p style='color: #64748b; font-size: 13px; margin: 5px 0 0;'>Our team is analyzing your request</p>
                                                </td>
                                            </tr>
                                        </table>
                                    </td>
                                </tr>
                                <tr>
                                    <td style='padding: 10px 0;'>
                                        <table role='presentation' cellspacing='0' cellpadding='0'>
                                            <tr>
                                                <td style='width: 40px; vertical-align: top;'>
                                                    <div style='width: 28px; height: 28px; background-color: #64748b; border-radius: 50%; text-align: center; line-height: 28px; color: white; font-size: 12px; font-weight: bold;'>2</div>
                                                </td>
                                                <td style='padding-left: 15px;'>
                                                    <p style='color: #334155; font-size: 14px; margin: 0; font-weight: 600;'>Assignment</p>
                                                    <p style='color: #64748b; font-size: 13px; margin: 5px 0 0;'>A specialist will be assigned to your case</p>
                                                </td>
                                            </tr>
                                        </table>
                                    </td>
                                </tr>
                                <tr>
                                    <td style='padding: 10px 0;'>
                                        <table role='presentation' cellspacing='0' cellpadding='0'>
                                            <tr>
                                                <td style='width: 40px; vertical-align: top;'>
                                                    <div style='width: 28px; height: 28px; background-color: #64748b; border-radius: 50%; text-align: center; line-height: 28px; color: white; font-size: 12px; font-weight: bold;'>3</div>
                                                </td>
                                                <td style='padding-left: 15px;'>
                                                    <p style='color: #334155; font-size: 14px; margin: 0; font-weight: 600;'>Resolution</p>
                                                    <p style='color: #64748b; font-size: 13px; margin: 5px 0 0;'>We'll respond within 24-48 business hours</p>
                                                </td>
                                            </tr>
                                        </table>
                                    </td>
                                </tr>
                            </table>

                            <p style='color: #4a5568; font-size: 14px; line-height: 1.7; margin: 25px 0 0; padding: 20px; background-color: #fff8e1; border-radius: 8px; border-left: 4px solid #ffc107;'>
                                <strong style='color: #f57c00;'>💡 Pro Tip:</strong> Please keep your reference number handy for any future correspondence regarding this request.
                            </p>
                        </td>
                    </tr>

                    <!-- Divider -->
                    <tr>
                        <td style='padding: 0 50px;'>
                            <hr style='border: none; height: 1px; background-color: #e2e8f0; margin: 0;'/>
                        </td>
                    </tr>

                    <!-- Footer -->
                    <tr>
                        <td style='padding: 30px 50px; background-color: #f8fafc;'>
                            <table role='presentation' width='100%' cellspacing='0' cellpadding='0'>
                                <tr>
                                    <td align='center'>
                                        <p style='color: #1e3a5f; font-size: 15px; font-weight: 600; margin: 0 0 10px;'>Need immediate assistance?</p>
                                        <p style='color: #64748b; font-size: 13px; margin: 0 0 20px;'>Our team is here to help you</p>

                                        <table role='presentation' cellspacing='0' cellpadding='0'>
                                            <tr>
                                                <td style='padding: 0 10px;'>
                                                    <a href='mailto:support@company.com' style='color: #1e3a5f; text-decoration: none; font-size: 13px;'>📧 support@company.com</a>
                                                </td>
                                                <td style='padding: 0 10px;'>
                                                    <span style='color: #1e3a5f; font-size: 13px;'>📞 +1 (555) 123-4567</span>
                                                </td>
                                            </tr>
                                        </table>
                                    </td>
                                </tr>
                            </table>
                        </td>
                    </tr>

                    <!-- Bottom Bar -->
                    <tr>
                        <td style='background-color: #1e3a5f; padding: 20px 50px; text-align: center;'>
                            <p style='color: #94a3b8; font-size: 12px; margin: 0;'>
                                © {DateTime.UtcNow.Year} Your Company Name. All rights reserved.
                            </p>
                            <p style='color: #64748b; font-size: 11px; margin: 10px 0 0;'>
                                This is an automated message. Please do not reply directly to this email.
                            </p>
                        </td>
                    </tr>

                </table>
            </td>
        </tr>
    </table>
</body>
</html>";

            var replyMessage = new Message
            {
                Subject = $"Re: {originalSubject ?? "Your Request"} [{ticketNumber}]",
                Body = new ItemBody
                {
                    ContentType = BodyType.Html,
                    Content = htmlTemplate
                },
                ToRecipients = new List<Recipient>
                {
                    new Recipient
                    {
                        EmailAddress = new EmailAddress
                        {
                            Address = recipientEmail,
                            Name = recipientName
                        }
                    }
                }
            };

            await _graphClient
                .Users[_mailboxUser]
                .SendMail
                .PostAsync(new Microsoft.Graph.Users.Item.SendMail.SendMailPostRequestBody
                {
                    Message = replyMessage,
                    SaveToSentItems = true
                }, cancellationToken: cancellationToken);
        }
    }
}