# Mail Listener Worker

A .NET Worker Service that monitors an email inbox using Microsoft Graph API and automatically sends professional auto-reply emails to new incoming messages.

## Features

- Polls mailbox for unread emails every minute
- Sends professionally styled HTML auto-reply with ticket number
- Marks processed emails as read
- Uses Azure AD authentication with Microsoft Graph

## Project Structure

```
PFE/
├── MailListenerWorker/
│   ├── Templates/
│   │   └── AutoReplyTemplate.html    # Email template
│   ├── MailPollingService.cs         # Main service logic
│   ├── Program.cs                    # Entry point
│   ├── appsettings.json              # Configuration
│   └── MailListenerWorker.csproj
└── PFE.sln
```

## Prerequisites

- .NET 10 SDK
- Azure AD App Registration with:
  - `Mail.Read` permission (Application)
  - `Mail.Send` permission (Application)
  - Admin consent granted

## Configuration

Configure Azure AD credentials using User Secrets (recommended for development):

```bash
cd MailListenerWorker
dotnet user-secrets set "AzureAd:TenantId" "your-tenant-id"
dotnet user-secrets set "AzureAd:ClientId" "your-client-id"
dotnet user-secrets set "AzureAd:ClientSecret" "your-client-secret"
dotnet user-secrets set "AzureAd:MailboxUser" "support@yourdomain.com"
```

Or update `appsettings.json` for production (not recommended for secrets).

## Running

```bash
dotnet run --project MailListenerWorker
```

## Future Plans

- Integration with Python AI Agent for email classification
- SQL database for ticket storage and tracking
- Dashboard for support team

## License

Private - All rights reserved.
