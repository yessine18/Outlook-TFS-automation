# Outlook ↔ Azure DevOps (TFS) Automation — Mail Listener Worker

A **.NET Worker Service** that:
- monitors an Outlook mailbox using **Microsoft Graph**
- automatically creates **Azure DevOps (TFS) Work Items** from new emails
- sends a professional **HTML auto-reply** with a ticket/work-item reference
- optionally sends **status update emails** when the Azure DevOps work item state changes

---

## What this project does (Flow)

1. **Poll Outlook Inbox** every minute for unread messages.
2. For each unread email:
   - fetch the full email body via Microsoft Graph
   - create a new Azure DevOps Work Item (type: `Issue`) with:
     - title: `[EMAIL] <subject>`
     - description containing sender + received date + email content
     - hidden HTML attributes used later to re-contact the sender
     - tags: `Email; AutoCreated; ...`
   - send an **auto-reply email** (HTML template) including:
     - ticket/work item number
     - current status/state
   - mark the email as **read**
3. **Poll Azure DevOps** for recently changed Work Items (last 7 days) that have the `AutoCreated` tag.
4. If a work item changed state and an update hasn’t been emailed yet:
   - extract sender name/email from the hidden attributes in the work item description
   - send a status update email
   - add a tag like `EmailSent_<State>` to avoid duplicates (example: `EmailSent_ToDo`)

---

## Repository structure

```text
.
├── MailListenerWorker/
│   ├── AzureDevOpsService.cs              # Azure DevOps Work Item creation/query/tagging (PAT auth)
│   ├── MailPollingService.cs              # Mail polling + work item automation + reply/update sending
│   ├── Program.cs                         # Worker host + dependency injection wiring
│   ├── appsettings.json                   # Local config template (DO NOT store real secrets)
│   ├── MailListenerWorker.csproj          # net10.0 worker project + package refs + embedded resources
│   └── Templates/
│       └── AutoReplyTemplate.html         # HTML email template (embedded resource)
├── PFE.sln
└── README.md
```

---

## Tech stack

- **.NET Worker Service** (`Microsoft.NET.Sdk.Worker`)
- **Microsoft Graph SDK** (mail reading + sending)
- **Azure.Identity** (`ClientSecretCredential` for Graph)
- **Azure DevOps SDK** (`Microsoft.TeamFoundationServer.Client`) using **PAT**

---

## Prerequisites

### 1) .NET SDK
- .NET **10.0** SDK (project targets `net10.0`)

### 2) Azure AD App Registration (for Microsoft Graph)
Create an app registration and grant **Application** permissions:
- `Mail.Read`
- `Mail.Send`

Then **grant admin consent**.

You’ll need:
- TenantId
- ClientId
- ClientSecret
- Mailbox user (the mailbox to monitor, e.g. `support@domain.com`)

### 3) Azure DevOps (TFS) access
You’ll need:
- Azure DevOps Organization URL (example in repo config)
- Project name
- A **Personal Access Token (PAT)** with Work Item permissions (create/read/update)

---

## Configuration

> Recommended: use **User Secrets** for local development.  
> Do **NOT** commit real secrets into `appsettings.json`.

### Option A — User Secrets (recommended)

From the repo root:

```bash
cd MailListenerWorker

# Microsoft Graph / Azure AD
dotnet user-secrets set "AzureAd:TenantId" "YOUR_TENANT_ID"
dotnet user-secrets set "AzureAd:ClientId" "YOUR_CLIENT_ID"
dotnet user-secrets set "AzureAd:ClientSecret" "YOUR_CLIENT_SECRET"
dotnet user-secrets set "AzureAd:MailboxUser" "support@yourdomain.com"

# Azure DevOps
dotnet user-secrets set "AzureDevOps:OrganizationUrl" "https://dev.azure.com/<org>"
dotnet user-secrets set "AzureDevOps:ProjectName" "<project-name>"
dotnet user-secrets set "AzureDevOps:PatToken" "YOUR_PAT"
```

### Option B — appsettings.json (not recommended for secrets)

File: `MailListenerWorker/appsettings.json`

```json
{
  "AzureAd": {
    "TenantId": "",
    "ClientId": "",
    "ClientSecret": "",
    "MailboxUser": ""
  },
  "AzureDevOps": {
    "OrganizationUrl": "https://dev.azure.com/<org>",
    "ProjectName": "<project-name>",
    "PatToken": ""
  }
}
```

---

## Running the worker

From the repo root:

```bash
dotnet run --project MailListenerWorker
```

The worker runs continuously and polls every **1 minute**.

---

## Email template

Template path:
- `MailListenerWorker/Templates/AutoReplyTemplate.html`

It is included as an **embedded resource** and loaded at runtime using:
- `MailListenerWorker.Templates.AutoReplyTemplate.html`

Template placeholders used by the code:
- `{{RecipientName}}`
- `{{TicketNumber}}`
- `{{TicketStatus}}`
- `{{OriginalSubject}}`
- `{{Year}}`
- `{{SupportEmail}}`
- `{{SupportPhone}}`

---

## Azure DevOps Work Item logic

When an email arrives, a Work Item is created with:
- **Type**: `Issue`
- **Title**: `[EMAIL] <subject>`
- **Tags**: `Email; AutoCreated` + an initial “already emailed” state tag:
  - `EmailSent_<InitialStateWithoutSpaces>`

A hidden HTML block is inserted into the work item description so the service can later email the sender when the item state changes:
- `data-sender-email="..."`
- `data-sender-name="..."`

---

## Notes / Security

- Never commit real values for:
  - `AzureAd:ClientSecret`
  - `AzureDevOps:PatToken`
- Prefer **User Secrets** (local) or environment variables / Key Vault (production).

---

## Future improvements (ideas)

- Better error handling + retry policies (Graph + ADO)
- Persist processed message IDs to avoid relying only on `isRead`
- Configurable polling interval
- Support multiple folders / mailbox routing rules
- Replace tag-based “email sent” tracking with a database
- Add an API/dashboard for support team visibility

---

## License

Private / internal project (update as needed).