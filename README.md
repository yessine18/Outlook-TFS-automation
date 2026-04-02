# Mail Listener Worker — Outlook ↔ Azure DevOps Automation

**.NET Worker Service** that automatically creates Azure DevOps tickets from emails using AI-powered analysis.

## What It Does
- Monitors Outlook inbox every minute
- Analyzes emails using **Groq LLM** (Mixtral 8x7b) to extract:
  - Core problem → ticket title
  - Severity level → priority (1-4)
  - Estimated resolution time
  - Responsible job field → assignee
- Creates **Azure DevOps Work Items** with auto-extracted data
- Sends professional **auto-reply** to sender with ticket reference
- Sends **Teams notifications** to assigned team member
- Sends **status update emails** when tickets change state

## Prerequisites
1. **.NET 10.0** SDK
2. **Azure AD App Registration** with `Mail.Read`, `Mail.Send` permissions
   - Credentials: `TenantId`, `ClientId`, `ClientSecret`
   - Mailbox to monitor (e.g., `support@domain.com`)
3. **Azure DevOps** Organization, Project, and PAT token
4. **Groq API Key** (free tier available at [console.groq.com](https://console.groq.com))
5. **Microsoft Teams Webhook URL** (optional, for notifications)

## Configuration

### Set Credentials (choose one):

**Option A — User Secrets (recommended):**
```bash
cd MailListenerWorker

# Azure AD
dotnet user-secrets set "AzureAd:TenantId" "YOUR_TENANT_ID"
dotnet user-secrets set "AzureAd:ClientId" "YOUR_CLIENT_ID"
dotnet user-secrets set "AzureAd:ClientSecret" "YOUR_CLIENT_SECRET"
dotnet user-secrets set "AzureAd:MailboxUser" "support@domain.com"

# Azure DevOps
dotnet user-secrets set "AzureDevOps:OrganizationUrl" "https://dev.azure.com/<org>"
dotnet user-secrets set "AzureDevOps:ProjectName" "<project>"
dotnet user-secrets set "AzureDevOps:PatToken" "YOUR_PAT"

# Groq LLM
dotnet user-secrets set "Groq:ApiKey" "YOUR_GROQ_API_KEY"

# Teams (optional)
dotnet user-secrets set "Teams:WebhookUrl" "YOUR_WEBHOOK"

# Job Field CSV
dotnet user-secrets set "JobFieldCsv:Path" "departements.csv"
dotnet user-secrets set "JobFieldCsv:DefaultAssignee" "support@domain.com"
```

**Option B — appsettings.json:**
See `MailListenerWorker/appsettings.json` template.

## Run
```bash
dotnet run --project MailListenerWorker
```

## Key Features
- ✅ AI-powered email problem extraction
- ✅ Intelligent job-based ticket routing
- ✅ Automatic priority assignment
- ✅ Professional HTML email templates
- ✅ Teams adaptive card notifications
- ✅ Status update tracking with tags
- ✅ Graceful fallback if LLM fails

## Security
- **Do NOT commit secrets** to appsettings.json
- **Do NOT send sensitive PII** to Groq API
- Use **User Secrets** locally or **Azure Key Vault** in production
- **Teams webhooks & tokens** must be kept secret