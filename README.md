<p align="center">
  <img src="https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white" alt=".NET 10">
  <img src="https://img.shields.io/badge/Azure_DevOps-0078D7?logo=azuredevops&logoColor=white" alt="Azure DevOps">
  <img src="https://img.shields.io/badge/Microsoft_Graph-00A4EF?logo=microsoft&logoColor=white" alt="Microsoft Graph">
  <img src="https://img.shields.io/badge/Microsoft_Teams-6264A7?logo=microsoftteams&logoColor=white" alt="Microsoft Teams">
  <img src="https://img.shields.io/badge/Groq_LLM-F55036?logo=meta&logoColor=white" alt="Groq LLM">
  <img src="https://img.shields.io/badge/PostgreSQL-4169E1?logo=postgresql&logoColor=white" alt="PostgreSQL">
  <img src="https://img.shields.io/badge/Python-3776AB?logo=python&logoColor=white" alt="Python">
  <img src="https://img.shields.io/badge/HuggingFace-FFD21E?logo=huggingface&logoColor=black" alt="HuggingFace">
</p>

# 📧 Outlook → Azure DevOps Automation (with RAG Auto-Resolve)

A **.NET Worker Service** that transforms incoming support emails into fully tracked Azure DevOps work items — powered by AI-driven analysis, intelligent routing, real-time state synchronization, and a custom **Retrieval-Augmented Generation (RAG)** engine for instantaneous ticket resolution.

---

## ⚡ How It Works

```mermaid
flowchart LR
    A["📨 Outlook Inbox"] -->|MS Graph API| B["🔄 Mail Polling Service"]
    B -->|Email Body| C["🤖 Groq LLM Extraction"]
    
    subgraph AI Auto-Resolve Pipeline
        C -->|Detailed Problem| D["🐍 Python Vectorizer (Local)"]
        D -->|384D Math Array| E["🗄️ PostgreSQL (pgvector)"]
        E -->|Top 3 Docs| F["🧠 Groq LLM AI Verdict"]
    end
    
    %% RAG Outcomes
    F -->|Verdict: Has Solution| H["🟢 Fast Auto-Reply (with Solution)"]
    F -->|Verdict: No Solution| G["📋 Azure DevOps Work Item"]

    %% Human Routing & Infrastructure
    G -->|State Sync| E
    B -->|Persist Metadata| E
    
    G -->|Assign| I["👤 Assignee Notification"]
    G -->|Standard Confirmation| H
    E -->|REST API| J["📊 Live Dashboard"]
    
    %% Client Validation & Feedback
    H -.->|Validation Links| K["✅/❌ Client Validation Webhook API"]
    K -->|Resolve/Re-open| G
    H -.->|Actionable Message| M["⭐ Outlook Adaptive Card Feedback"]
    M -->|Update Discussion| G
    M -->|Persist Rating| E

    %% Teams Alerts
    G -->|Power Automate Webhook| L["💬 Microsoft Teams Adaptive Card"]
```

1. **Poll** — The worker service natively monitors a shared support Outlook mailbox every 60 seconds via the Microsoft Graph API.
2. **Analyze** — Each new email is sent to **Groq LLM** (LLaMA 3.3 70B) which safely extracts the abstract metadata: core problem, severity, estimated resolution time, and the relevant IT Job Field.
3. **RAG Vector Search** — A local Python bridge (using HuggingFace's `all-MiniLM-L6-v2`) converts the problem into a mathematical 384D vector array. C# queries PostgreSQL (`pgvector`) to find the **Top 3 matching Microsoft Documentation chunks** from an offline dataset of 14,000+ files.
4. **Auto-Resolve Evaluation** — Llama-3 evaluates the problem natively strictly against these 3 official Microsoft Docs. If it finds an explicit, step-by-step solution, it dynamically generates an Instant Fix email.
5. **Route & Create** — An Azure DevOps **Issue** work item is created. The extracted job field is mapped against a CSV directory to identify the correct Azure DevOps assignee. (If the RAG AI resolved it, the step-by-step solution is also appended to this ticket's Description logs!)
6. **Notify** — A professional HTML auto-reply (with QR code) is sent out to the user. If auto-resolved, this email contains a massive green highlighted box with the exact solution and Microsoft URLs. Concurrently, a separate Custom Notification email is dispatched to the IT Assignee to alert them of the ticket.
7. **Teams Alert** — An **Adaptive Card** is immediately fired to the correct Microsoft Teams channel (mapped per job field via Power Automate Webhooks), displaying ticket details, priority, status, and a direct ADO link — color-coded 🟢 green for RAG-resolved or 🟠 orange for human-assigned tickets.
8. **Track & Sync** — Every ticket and state transition is comprehensively persisted to PostgreSQL. A standalone vanilla web dashboard queries this DB periodically to display real-time pipeline stats and track Azure DevOps board state changes over time.
9. **Interactive Feedback** — When a ticket is marked as Done/Closed in ADO, the worker syncs the state and sends a closure email containing an **Outlook Actionable Message (Adaptive Card)**. Clients can rate the support directly inside Outlook, instantly logging their feedback to the PostgreSQL database and appending a beautiful HTML summary to the Azure DevOps Work Item discussion.

---

## ✅ Features

| Feature | Description |
|---|---|
| 🤖 **AI Email Analysis** | Groq LLM extracts problem, severity, department, and resolution estimate |
| 🧠 **Local RAG Vector Engine** | Offline, quota-free vector similarity search using PostgreSQL `pgvector` |
| ⚡ **AI Auto-Resolution** | Instantly solves level-1 issues by citing official knowledge base documents |
| 📋 **Auto Work Item Creation** | Creates Azure DevOps Issues with priority, assignee, and metadata |
| 🔀 **Intelligent Routing** | CSV-based job field → assignee mapping with fallback defaults |
| 🔗 **Client Validation Workflow** | Generates dynamic REST callbacks allowing clients to accept or reject AI resolutions |
| 💬 **Microsoft Teams Alerts** | Fires Adaptive Cards to the correct Teams channel via Power Automate Webhooks, color-coded by RAG outcome |
| 📬 **Auto-Reply Emails** | Professional HTML emails with ticket reference, QR codes, interactive buttons, and AI solutions |
| 👤 **Assignee Notifications** | Dedicated email notifications to the assigned team member |
| 🔄 **State Sync** | Polls ADO board and sends status update emails on human state changes |
| 🗄️ **Full Audit Trail** | PostgreSQL database tracks every ticket and state transition |
| ⭐ **Outlook Actionable Messages** | In-email Adaptive Card feedback forms synced directly to Azure DevOps history |
| 🛡️ **Graceful Fallback** | If the AI is unsure, the ticket safely generates as normal for a human agent |

---

## 🛠️ Tech Stack

- **Runtime**: .NET 10.0 (Web SDK) + Python 3 (Virtual Environment)
- **Vector Search Engine**: `sentence-transformers` (`all-MiniLM-L6-v2`) + Python `requests`
- **Email Integration**: Microsoft Graph SDK v5
- **Teams Integration**: Power Automate Webhooks (Adaptive Cards v1.4 via `HttpClient`)
- **Work Items**: Azure DevOps REST API (`Microsoft.TeamFoundationServer.Client`)
- **AI/LLM**: Groq API (LLaMA 3.3 70B Versatile)
- **Database**: PostgreSQL with `pgvector` Plugin + Entity Framework Core 8
- **Auth**: Azure AD Client Credentials (`Azure.Identity`)
- **Frontend**: Vanilla HTML/CSS/JS dashboard (served via Kestrel static files)

---

## 📋 Prerequisites

| Requirement | Details |
|---|---|
| **.NET 10.0 SDK** | [Download](https://dotnet.microsoft.com/download) |
| **Python 3+** | Create `.venv` and install `sentence-transformers`, `torch`, `psycopg2` | 
| **Azure AD App Registration** | With `Mail.Read`, `Mail.Send` application permissions (admin-consented) |
| **Azure DevOps** | Organization + Project + PAT token with Work Items read/write |
| **Groq API Key** | Free tier at [console.groq.com](https://console.groq.com) |
| **PostgreSQL + pgvector** | `docker run --name my-postgres -e POSTGRES_PASSWORD=secret -d -p 5432:5432 postgres:16` and manually compile `pgvector` inside. |
| **Microsoft Teams** | Create a Power Automate Workflow ("Post alert to Teams channel via webhook") per department channel and paste each generated URL in `departements.csv` under the `WebhookUrl` column. |

---

## ⚙️ Configuration (User Secrets)

```bash
cd MailListenerWorker

# Azure AD
dotnet user-secrets set "AzureAd:TenantId"     "YOUR_TENANT_ID"
dotnet user-secrets set "AzureAd:ClientId"      "YOUR_CLIENT_ID"
dotnet user-secrets set "AzureAd:ClientSecret"   "YOUR_CLIENT_SECRET"
dotnet user-secrets set "AzureAd:MailboxUser"     "support@yourdomain.com"

# Azure DevOps
dotnet user-secrets set "AzureDevOps:OrganizationUrl"  "https://dev.azure.com/YOUR_ORG"
dotnet user-secrets set "AzureDevOps:ProjectName"       "YOUR_PROJECT"
dotnet user-secrets set "AzureDevOps:PatToken"           "YOUR_PAT"

# Groq LLM
dotnet user-secrets set "Groq:ApiKey"  "YOUR_GROQ_API_KEY"

# Application Settings
dotnet user-secrets set "Email:BaseAppUrl" "http://localhost:5000"

# PostgreSQL (Must have pgvector installed!)
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=helpdesk_pipeline;Username=postgres;Password=YOUR_PASSWORD"
```

---

## 🚀 Run

```bash
# 1. Apply database migrations
cd MailListenerWorker
dotnet ef database update

# 2. Run the background automation service!
dotnet run --project MailListenerWorker
```

---

## 📁 Project Structure

```text
PFE/
├── MailListenerWorker/
│   ├── Program.cs                     # DI setup, app bootstrap + Minimal API (Client Validation)
│   ├── MailPollingService.cs           # Background worker: Email polling, ADO sync, RAG, Teams Alerts
│   ├── AzureDevOpsService.cs           # Azure DevOps ADO item generation and mutation
│   ├── departements.csv               # Job Field → Email, Department, WebhookUrl routing table
│   ├── Data/
│   │   └── AppDbContext.cs             # EF Core DbContext 
│   ├── Models/
│   │   ├── JobFieldMapping.cs          # Job field entity (Email, TeamId, ChannelId, WebhookUrl)
│   │   └── Enums/PipelineStatus.cs     # Full pipeline state machine
│   ├── Services/
│   │   ├── GroqLlmService.cs           # Groq LLM integration + RAG Vectorization Pipeline
│   │   └── JobFieldMappingService.cs   # CSV-based department routing + Webhook URL resolution
│   ├── Templates/
│   │   └── AutoReplyTemplate.html      # RAG-Injection-enabled HTML design
│   └── wwwroot/                        # Live dashboard (static SPA)
│
└── inetum-ms-kb/                       # AI Knowledge Base Scraper Engine
    ├── .venv/                          # Python environments
    ├── src/
    │   ├── scrape/                     # Microsoft Docs XML sitemap scrapers
    │   ├── parse/                      # HTML to Markdown DOM cleaners
    │   └── embed/                      
    │       ├── vectorize_docs.py       # Batch PGVector Embedding compiler for 14,000 files
    │       ├── query_vector.py         # Sub-process Python bridge for C# Worker
    │       └── test_rag.py             # Mock test-suite for NLP Verdict engine
    └── config/                         # Data paths
```

---

## 🗺️ Roadmap (Chronological Build Order)

### Phase 1 — Foundation & Core Infrastructure
- [x] **Project Scaffolding** — .NET Worker Service project creation with `Microsoft.NET.Sdk.Web`, dependency injection, and configuration management
- [x] **Azure AD Authentication** — App Registration with `ClientSecretCredential` (OAuth2 Client Credentials flow) for daemon-based access to Microsoft Graph
- [x] **User Secrets Management** — Secure local credential storage via `dotnet user-secrets` for TenantId, ClientId, ClientSecret, and PAT tokens
- [x] **Microsoft Graph SDK Integration** — `GraphServiceClient` setup for reading, sending, and patching Outlook emails programmatically

### Phase 2 — Email Polling & Processing
- [x] **Background Worker Service** — `BackgroundService` with infinite loop polling the shared Outlook mailbox every 60 seconds via `MailFolders["Inbox"].Messages.GetAsync()`
- [x] **Email Ingestion Pipeline** — Fetch top 10 unread messages, extract sender, subject, body, and timestamp metadata
- [x] **Mark-as-Read Deduplication** — Automatically mark processed emails as read via `PatchAsync()` to prevent re-processing

### Phase 3 — AI-Powered Analysis
- [x] **Groq LLM Integration** — HTTP client to Groq Cloud API (`llama-3.3-70b-versatile`) for structured email metadata extraction (coreProblem, severity, jobField, detailedDescription, etc.)
- [x] **Constrained Job Field Extraction** — LLM prompt engineering to force classification strictly against valid CSV-defined job fields, preventing hallucinated departments
- [x] **Graceful LLM Fallback** — Safe `CreateDefaultExtractedData()` fallback when LLM fails, ensuring the pipeline never crashes on AI errors

### Phase 4 — Azure DevOps Automation
- [x] **Azure DevOps Work Item Creation** — `JsonPatchDocument`-based Issue creation with title, description, priority, tags, and assignee via `WorkItemTrackingHttpClient`
- [x] **CSV-Based Intelligent Routing** — `CsvHelper`-powered `departements.csv` parser mapping Job Fields to assignee emails, departments, and webhook URLs
- [x] **Assignee Identity Resilience** — Catch `VssServiceException` for unknown ADO identities and retry without assignee, preventing pipeline failures
- [x] **Hidden Metadata Embedding** — Store sender email/name as invisible `data-*` HTML attributes inside the ADO Work Item Description for later retrieval during state sync

### Phase 5 — Email Notifications
- [x] **Professional HTML Auto-Reply** — Embedded resource HTML template (`AutoReplyTemplate.html`) with ticket number, severity badge, estimated resolution time, and dynamic variable replacement
- [x] **QR Code Generation** — Base64-encoded QR code data URI pointing to the ADO Work Item URL, embedded directly in the auto-reply email
- [x] **Assignee Notification Emails** — Dedicated HTML notification email dispatched to the assigned IT engineer with ticket details and priority
- [x] **Personalized Closure Emails** — Dynamic hero text that changes based on ticket state (open vs. Done/Closed), addressing the client by name

### Phase 6 — Database & Audit Trail
- [x] **PostgreSQL + Entity Framework Core** — `AppDbContext` with `Tickets` and `TicketStateLogs` tables, Fluent API configuration, and `UseNpgsql()` provider
- [x] **Database-First Persistence** — Ticket saved to PostgreSQL BEFORE creating the ADO Work Item, ensuring data safety even if ADO fails
- [x] **Pipeline State Machine** — 10-value `PipelineStatus` enum (EmailReceived → LlmProcessing → AdoCreated → etc.) stored as string column for human readability
- [x] **Immutable Audit Log** — `TicketStateLogs` table recording every state transition with timestamp and optional error message
- [x] **Scoped DbContext Pattern** — `IServiceScopeFactory` to create fresh DbContext instances inside the Singleton background service, avoiding lifetime conflicts

### Phase 7 — ADO State Synchronization
- [x] **WIQL Polling** — Periodic Azure DevOps WIQL query fetching all `AutoCreated`-tagged work items changed in the last 30 days
- [x] **Bidirectional State Sync** — Compare ADO board state with PostgreSQL state and update DB on mismatch
- [x] **State-Change Email Notifications** — Automatic email to the original sender when their ticket state changes (To Do → Doing → Done)
- [x] **Tag-Based Email Deduplication** — `EmailSent_{State}` tags appended to ADO Work Items to prevent duplicate notification emails across polling cycles

### Phase 8 — Spam Filtering & Security
- [x] **Domain Whitelist Filter** — `Email:AllowedDomains` configuration array to process only emails from authorized company domains, silently skipping spam
- [x] **Input Sanitization** — `HtmlEncode()` on all user-supplied content (feedback, comments) to prevent XSS (cross site scripting) injection in ADO discussions for malscious users & scripts (JS)!!!!

### Phase 9 — RAG Auto-Resolution Engine
- [x] **Microsoft Docs Knowledge Base** — Python scraper pipeline (`fetch/` → `scrape/` → `parse/` → `embed/`) processing 14,000+ Microsoft Documentation files
- [x] **Vector Embedding Pipeline** — `sentence-transformers` (`all-MiniLM-L6-v2`) batch-encoding documents into 384-dimensional vectors stored in PostgreSQL via `pgvector`
- [x] **Python–C# Bridge** — Child process spawning (`python.exe query_vector.py`) with stdout JSON capture for real-time vector generation from C#
- [x] **Cosine Similarity Search** — Raw `NpgsqlConnection` SQL query using the `<=>` pgvector operator to find Top 3 matching documentation chunks
- [x] **LLM Verdict Engine** — Second Groq call with `temperature: 0.1` evaluating whether retrieved documents contain an explicit, step-by-step solution (structured `RagVerdict` JSON output)
- [x] **AI Solution Email Injection** — Green-highlighted solution box with Accept/Reject validation buttons injected into auto-reply emails for RAG-resolved tickets

### Phase 10 — Client Validation & Feedback
- [x] **REST Validation Endpoints** — Minimal API `GET /api/ticket/{id}/validate?accepted=true|false` for client-side Accept/Reject via email button clicks
- [x] **ADO State Mutation** — Accepted = set to "Done", Rejected = keep at "To Do" or "Doing", with corresponding `PipelineStatus` updates (ClientAcceptedResolution / ClientRejectedResolution)
- [x] **Duplicate Validation Protection** — Check `CurrentPipelineStatus` to prevent clients from validating the same ticket multiple times
- [x] **Outlook Actionable Messages** — `<script type="application/adaptivecard+json">` embedded in closure emails with star rating (1–5) and comment form, submitted via `Action.Http` POST
- [x] **Feedback Persistence** — `POST /api/ticket/{id}/feedback` endpoint saving `ClientRating` and `ClientFeedback` to PostgreSQL and appending styled HTML comment to ADO Work Item Discussion

### Phase 11 — Microsoft Teams Notifications
- [x] **Power Automate Webhook Integration** — Per-department webhook URLs stored in `departements.csv`, resolved dynamically per ticket
- [x] **Adaptive Card v1.4 Alerts** — Rich Teams cards with ticket details, priority, assignee, status, and direct ADO link
- [x] **Color-Coded Outcome Cards** — Green (🟢) for RAG-resolved tickets, Orange (🟠) for human-assigned tickets

### Phase 12 — Error Handling & Reliability
- [x] **TMA Alert System** — Formatted HTML error report emails sent to the Application Support team on any pipeline failure, including failed step, error type, and timestamp
- [x] **Step-Level Error Isolation** — Each pipeline step (LLM → DB → ADO → Email) wrapped in independent try/catch blocks so downstream steps can survive upstream failures
- [x] **Graceful RAG Degradation** — If RAG fails, pipeline continues normally without auto-resolution (no crash)
- [x] **Database Error Status Tracking** — Failed steps update the ticket's `PipelineStatus` to `AdoFailed` or `MailSendingFailed` with error messages logged in `TicketStateLogs`

### Phase 13 — Dashboard & Monitoring
- [x] **Live Dashboard SPA** — Vanilla HTML/CSS/JS static frontend served via Kestrel (`UseStaticFiles()` + `MapFallbackToFile("index.html")`)
- [x] **Dashboard REST API** — `GET /api/tickets` (latest 50) and `GET /api/stats` (total/processed/failed counts) endpoints with CORS support

### 🔜 Upcoming
- [ ] **Advanced Email Handling** — Email thread support (ConversationId tracking), inline images, and large attachments
- [ ] **CI/CD Pipeline** — Azure DevOps pipeline for automated build, test, and deployment
- [ ] **Production Deployment** — Docker containerization and Azure cloud deployment (App Service / Container Instance + PostgreSQL Flexible Server + Key Vault)