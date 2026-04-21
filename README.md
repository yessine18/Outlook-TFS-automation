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
    
    %% Client Validation
    H -.->|Validation Links| K["✅/❌ Client Validation Webhook API"]
    K -->|Resolve/Re-open| G

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

## 🗺️ Roadmap

- [x] **RAG Integration** — Retrieval-Augmented Generation using a local `pgvector` database to instantly auto-answer client questions with 100% safe guardrails.
- [x] **Client Validation Architecture** — Auto-inserted Webhook logic letting end-users safely validate RAG answers.
- [x] **Microsoft Teams Notifications** — Per-department Adaptive Card alerts via Power Automate Webhooks, color-coded by outcome.
- [ ] **CI/CD Pipeline** — Azure DevOps pipeline for automated build, test, and deployment
- [x] **Enhanced Error Routing** — Fallback mechanism for pipeline failures alerting the TMA Support Team.
- [ ] **Advanced Email Handling** — Support for email threads, inline images, and large attachments.