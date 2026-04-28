# 🔬 Technical Deep-Dive — Complete Code Understanding Guide

> **Purpose**: This document provides a detailed, file-by-file technical breakdown of the entire Helpdesk Automation codebase. It is designed to prepare you for a technical discussion with your PFE supervisor by explaining every function, library, API call, and design decision.

---

## Table of Contents
1. [Technology Stack & Libraries](#1-technology-stack--libraries)
2. [Environment & Configuration](#2-environment--configuration)
3. [File-by-File Code Breakdown](#3-file-by-file-code-breakdown)
4. [Pipeline Execution Flow (Step-by-Step)](#4-pipeline-execution-flow)
5. [Key APIs Used](#5-key-apis-used)
6. [Database Schema](#6-database-schema)
7. [Design Patterns & Decisions](#7-design-patterns--decisions)

---

## 1. Technology Stack & Libraries

### .NET Libraries (NuGet Packages)

| Package | Purpose | Where Used |
|---|---|---|
| `Microsoft.Graph` (v5) | SDK to interact with Outlook mailboxes (read, send, patch emails) via the Microsoft Graph REST API | `MailPollingService.cs` |
| `Azure.Identity` | Provides `ClientSecretCredential` for OAuth2 daemon authentication (no user login needed) | `MailPollingService.cs` constructor |
| `Microsoft.TeamFoundation.WorkItemTracking.WebApi` | Azure DevOps SDK for creating/updating Work Items via `JsonPatchDocument` | `AzureDevOpsService.cs` |
| `Microsoft.VisualStudio.Services.WebApi` | Provides `VssConnection` and `VssBasicCredential` for PAT-based ADO authentication | `AzureDevOpsService.cs` constructor |
| `Npgsql.EntityFrameworkCore.PostgreSQL` | EF Core provider for PostgreSQL — translates LINQ queries to SQL | `Program.cs`, `AppDbContext.cs` |
| `Npgsql` (raw ADO.NET) | Direct SQL queries for pgvector similarity search (EF Core doesn't support `<=>` operator) | `GroqLlmService.cs` (RAG) |
| `CsvHelper` | Parses the `departements.csv` routing table into strongly-typed C# objects | `JobFieldMappingService.cs` |
| `System.Text.Json` | Serializes/deserializes JSON for the Groq API requests and LLM response parsing | `GroqLlmService.cs` |

### Python Libraries (pip in `.venv`)

| Package | Purpose |
|---|---|
| `sentence-transformers` | Loads the `all-MiniLM-L6-v2` HuggingFace model to convert text into 384-dimensional vector arrays |
| `torch` | PyTorch backend required by `sentence-transformers` for tensor computation |
| `psycopg2-binary` | PostgreSQL adapter for Python (used in `vectorize_docs.py` to batch-insert embeddings) |

### External APIs

| API | Auth Method | Endpoint |
|---|---|---|
| **Microsoft Graph API** | OAuth2 Client Credentials (`ClientSecretCredential`) | `https://graph.microsoft.com/v1.0/users/{mailbox}/...` |
| **Azure DevOps REST API** | Personal Access Token (PAT) via `VssBasicCredential` | `https://dev.azure.com/{org}/{project}/_apis/wit/...` |
| **Groq Cloud API** | Bearer Token (API Key) | `https://api.groq.com/openai/v1/chat/completions` |
| **Power Automate Webhooks** | No auth (URL is the secret) | Per-department webhook URLs stored in `departements.csv` |

---

## 2. Environment & Configuration

### `appsettings.json` — Non-Secret Configuration

| Key | Purpose |
|---|---|
| `ConnectionStrings:DefaultConnection` | PostgreSQL connection string (used by EF Core and raw Npgsql) |
| `AzureDevOps:OrganizationUrl` | Base URL of the Azure DevOps organization |
| `AzureDevOps:ProjectName` | Target ADO project name for work item creation |
| `Groq:ApiUrl` | Groq's OpenAI-compatible chat completions endpoint |
| `Groq:Model` | LLM model identifier (`llama-3.3-70b-versatile`) |
| `Email:AllowedDomains` | Whitelist of email domains that the system will process (spam filter) |
| `Email:LogoUrl` / `FooterLogoUrl` | URLs to company logos embedded in HTML email templates |
| `Email:BaseAppUrl` | Public URL (Dev Tunnel) for webhook callbacks and Adaptive Card actions |
| `JobFieldCsv:Path` | Path to the CSV file containing Job Field → Assignee → Webhook mappings |

### .NET User Secrets — Sensitive Credentials (never committed to Git)

| Secret Key | Purpose |
|---|---|
| `AzureAd:TenantId` | Microsoft Entra ID tenant identifier |
| `AzureAd:ClientId` | App Registration client ID |
| `AzureAd:ClientSecret` | App Registration client secret |
| `AzureAd:MailboxUser` | Email address of the shared support mailbox |
| `AzureDevOps:PatToken` | Personal Access Token for ADO API |
| `Groq:ApiKey` | API key for Groq Cloud |

---

## 3. File-by-File Code Breakdown

---

### 📄 `Program.cs` — Application Bootstrap & Minimal API (171 lines)

**What it does**: This is the entry point. It configures Dependency Injection (DI), registers all services, and defines 4 HTTP endpoints (Minimal API).

**Key Sections**:

#### DI Registration (Lines 6–36)
```
WebApplication.CreateBuilder(args)
```
- Registers `AppDbContext` with PostgreSQL via `UseNpgsql()`
- Registers `AzureDevOpsService`, `GroqLlmService` as **Singletons** (one instance for the app lifetime)
- Registers `JobFieldMappingService` with a factory lambda that reads the CSV path from config
- Registers `MailPollingService` as a **Hosted Background Service** via `AddHostedService<>()`
- Adds CORS policy `"AllowAll"` for the dashboard frontend

#### Minimal API Endpoints (Lines 44–167)

| Endpoint | Method | Purpose |
|---|---|---|
| `/api/tickets` | GET | Returns the latest 50 tickets from PostgreSQL (for the dashboard) |
| `/api/stats` | GET | Returns total/processed/failed ticket counts (for the dashboard) |
| `/api/ticket/{id}/validate` | GET | Client clicks "Yes/No" validation links in the RAG auto-reply email. Updates ADO state and DB status. Returns styled HTML pages. |
| `/api/ticket/{id}/feedback` | POST | Receives Adaptive Card feedback (rating + comment) from Outlook. Persists to DB and appends an HTML comment to ADO Work Item history via `AddWorkItemCommentAsync()`. |

**Key function — Feedback endpoint (Line 127)**:
- Receives `FeedbackPayload` (rating, comment) from Outlook's `Action.Http`
- Finds the ticket by `AdoWorkItemId`
- Saves `ClientRating` and `ClientFeedback` to PostgreSQL
- Builds a styled HTML comment and calls `adoService.AddWorkItemCommentAsync()`
- Uses `System.Web.HttpUtility.HtmlEncode()` to sanitize user input against XSS

---

### 📄 `MailPollingService.cs` — Core Orchestrator (1067 lines)

**What it does**: This is the **heart of the entire project**. It is a `BackgroundService` that runs an infinite loop every 60 seconds, performing two jobs: polling the mailbox for new emails and syncing Azure DevOps state changes.

#### Constructor (Lines 36–75)
- Creates a `ClientSecretCredential` from Azure AD config (OAuth2 Client Credentials flow)
- Instantiates a `GraphServiceClient` — the Microsoft Graph SDK entry point
- Loads two HTML email templates from embedded resources via `Assembly.GetManifestResourceStream()`

#### `ExecuteAsync()` — The Main Loop (Lines 77–102)
```csharp
while (!stoppingToken.IsCancellationRequested)
{
    await PollMailboxAsync(stoppingToken);      // Check for new emails
    await PollWorkItemUpdatesAsync(stoppingToken); // Sync ADO state changes
    await Task.Delay(TimeSpan.FromMinutes(1));
}
```
On any unhandled exception, it calls `SendTmaAlertAsync()` to email the Application Support team.

#### `PollMailboxAsync()` — Email Ingestion (Lines 104–133)
- Calls `_graphClient.Users[mailbox].MailFolders["Inbox"].Messages.GetAsync()` with filter `isRead eq false`
- Fetches top 10 unread messages with specific fields (`id`, `subject`, `from`, `receivedDateTime`, `bodyPreview`)
- Iterates and calls `ProcessEmailAsync()` for each

#### `ProcessEmailAsync()` — The 5-Step Pipeline (Lines 247–577)

This is the most important function. It orchestrates the entire pipeline for a single email:

**STEP 1 — LLM Analysis (Lines 287–310)**
- Calls `_llmService.AnalyzeEmailAsync()` with the email body and the list of valid job fields from the CSV
- Receives back an `ExtractedEmailData` object containing: CoreProblem, Severity, JobField, DetailedDescription, AffectedSystems, ErrorCodes, etc.
- Resolves the assignee email via `_jobFieldService.ResolveEmail(extractedData.JobField)`

**STEP 1.5 — RAG Auto-Resolve (Lines 312–338)**
- Calls `_llmService.EvaluateRagSolutionAsync()` with the detailed description
- If the RAG finds a solution with confidence > 70%, it prepends the AI solution HTML to the detailed description (so it appears in the ADO work item)

**STEP 2 — Database Persist (Lines 340–389)**
- Creates a new `Ticket` entity with a fresh `Guid`
- Sets `CurrentPipelineStatus = PipelineStatus.LlmSuccess`
- Saves to PostgreSQL via `db.SaveChangesAsync()`
- **Design decision**: Database is saved BEFORE creating the ADO work item ("database-first"). If ADO fails, we still have the ticket recorded.

**STEP 3 — Mark Email as Read (Line 394)**
- Calls `_graphClient.Users[mailbox].Messages[id].PatchAsync(new Message { IsRead = true })`

**STEP 4 — Azure DevOps Work Item Creation (Lines 396–491)**
- Calls `_adoService.CreateEmailWorkItemAsync()` with all extracted data
- Back-fills the ticket record with `AdoWorkItemId`, `AdoUrl`, `AdoItemState`
- If RAG resolved it: sets status to `PendingClientValidation`
- If not: sets status to `AdoCreated`

**STEP 5 — Send Notifications (Lines 493–574)**
- **Auto-reply email** to the original sender via `SendAutoReplyAsync()`
- **Assignee notification** to the IT engineer via `SendAssigneeNotificationAsync()`
- **Teams Adaptive Card** to the department channel via `SendTeamsNotificationAsync()`

#### `PollWorkItemUpdatesAsync()` — ADO State Sync (Lines 135–231)
- Calls `_adoService.GetUpdatedWorkItemsAsync("AutoCreated")` to fetch all tracked work items
- For each item: compares the ADO state with the PostgreSQL state
- If different: updates the DB and sends a state-change email to the original sender
- Uses a **tag-based deduplication system** (`EmailSent_Done`, `EmailSent_Doing`) to prevent duplicate notification emails

#### `ExtractHtmlAttribute()` — Regex Metadata Parser (Lines 234–245)
- Uses `Regex.Match()` to extract `data-sender-email` and `data-sender-name` from the hidden HTML metadata embedded in the ADO work item description
- Handles three attribute formats: double-quoted, single-quoted, and unquoted values
- **Why regex?** Azure DevOps sanitizes HTML when storing it, which breaks simple string index-based parsing

#### `SendAutoReplyAsync()` — HTML Email Builder (Lines 589–796)
- Loads the `AutoReplyTemplate.html` embedded resource
- Generates a QR code (base64 data URI) pointing to the ADO work item URL
- If RAG resolved: injects the green "AI Solution" box with Accept/Reject validation links
- If ticket is Done/Closed: injects the Adaptive Card JSON (`<script type="application/adaptivecard+json">`) with the originator ID for Outlook feedback
- Performs template variable replacement (`{{TicketNumber}}`, `{{Severity}}`, etc.)
- Sends via `_graphClient.Users[mailbox].SendMail.PostAsync()`

#### `SendTeamsNotificationAsync()` — Power Automate Webhook (Lines 952–1053)
- Builds an Adaptive Card v1.4 JSON payload using C# raw string literals (`$$"""..."""`)
- Wraps it in the `{ "type": "message", "attachments": [...] }` format required by Power Automate
- Sends via `HttpClient.PostAsync()` to the webhook URL
- Color-codes the card: green for RAG-resolved, orange for human-assigned

#### `SendTmaAlertAsync()` — Error Alert System (Lines 867–949)
- Sends a formatted HTML error report email to the Application Support team
- Includes: failed step, email subject, sender, error type, error message, and UTC timestamp
- **Critical behavior**: After sending the alert, calls `Environment.Exit(1)` to halt the process immediately and prevent cascading failures

#### `LoadEmailTemplate()` — Embedded Resource Loader (Lines 1055–1065)
- Uses `Assembly.GetExecutingAssembly().GetManifestResourceStream()` to load HTML templates
- Templates are compiled into the DLL as embedded resources (configured in `.csproj`)

---

### 📄 `AzureDevOpsService.cs` — ADO Integration (400 lines)

**What it does**: Encapsulates all Azure DevOps REST API interactions.

#### Constructor (Lines 20–34)
- Creates a `VssBasicCredential` from the PAT token (username is empty for PAT auth)
- Opens a `VssConnection` to the ADO organization
- Gets a `WorkItemTrackingHttpClient` — the typed HTTP client for the Work Items API

#### `CreateEmailWorkItemAsync()` — Work Item Creation (Lines 36–230)
- Builds a `JsonPatchDocument` — a list of `JsonPatchOperation` objects that describe field changes
- Sets: `System.Title`, `System.Description`, `System.Tags`, `Microsoft.VSTS.Common.Priority`, `System.AssignedTo`
- Calls `_witClient.CreateWorkItemAsync()` with type `"Issue"`
- **Resilience**: If the assignee identity is unknown in ADO (throws `VssServiceException`), it catches the error, removes the `AssignedTo` field, and retries the creation without an assignee
- After creation, immediately adds the `EmailSent_{state}` tag to prevent duplicate notification emails

#### `BuildHiddenMetadata()` — Metadata Embedding (Lines 263–289)
- Generates a `<div style="display:none">` block containing `<span>` elements with `data-*` attributes
- Stores sender email, sender name, severity, job field, etc. inside the work item description
- Uses `HtmlEncode()` to escape special characters (preventing HTML injection and Azure DevOps sanitization issues)
- **Why?** When the worker later syncs state changes, it needs to know WHO to send the notification email to — this metadata is the only way to retrieve that information from the ADO work item

#### `GetUpdatedWorkItemsAsync()` — WIQL Query (Lines 309–343)
- Executes a WIQL (Work Item Query Language) query: `SELECT ... FROM WorkItems WHERE [System.Tags] Contains 'AutoCreated' AND [System.ChangedDate] > @today - 30`
- Returns all work items changed in the last 30 days with expanded field data

#### `UpdateWorkItemStateAsync()` — State Mutation (Lines 235–261)
- Creates a `JsonPatchDocument` with a single operation: set `System.State` to the new value
- Used by the validation endpoint to set tickets to "Done" (accepted) or "To Do" (rejected)

#### `AddWorkItemCommentAsync()` — Discussion History (Lines 372–398)
- Writes to `System.History` field via `JsonPatchDocument`
- This appends a new entry to the Work Item's Discussion tab (supports full HTML)

#### `AddWorkItemTagAsync()` — Tag Management (Lines 345–371)
- Appends a new tag to the existing semicolon-separated tag string
- Used for the deduplication system (`EmailSent_Done`, `EmailSent_Doing`)

---

### 📄 `Services/GroqLlmService.cs` — AI Engine (315 lines)

**What it does**: Handles all LLM interactions (metadata extraction + RAG evaluation).

#### `AnalyzeEmailAsync()` — Email Metadata Extraction (Lines 31–165)
- Constructs a detailed prompt instructing the LLM to extract: `coreProblem`, `description`, `detailedDescription`, `affectedSystems`, `errorCodes`, `stepsToReproduce`, `impactScope`, `requestedAction`, `severity`, `jobField`, etc.
- **Critical constraint**: The prompt includes the exact list of valid job fields from the CSV (`supportedFields`), forcing the LLM to pick only from real departments
- Sends a POST request to `https://api.groq.com/openai/v1/chat/completions` with `temperature: 0.2` (low creativity, high accuracy) and `max_tokens: 1500`
- Parses the JSON response, cleans markdown code blocks (`\`\`\`json`), and deserializes into `ExtractedEmailData`
- On any failure: returns a safe `CreateDefaultExtractedData()` fallback (never crashes the pipeline)

#### `EvaluateRagSolutionAsync()` — RAG Pipeline (Lines 188–311)

This is the most complex function in the project. It chains three systems together:

**Phase 1: Python Vector Generation (Lines 194–219)**
- Spawns a child process: `python.exe query_vector.py "problem description"`
- The Python script loads `all-MiniLM-L6-v2`, encodes the text, and prints a 384-dimensional vector array as JSON to stdout
- C# captures the output via `Process.StandardOutput.ReadToEndAsync()`

**Phase 2: PostgreSQL pgvector Similarity Search (Lines 221–248)**
- Opens a raw `NpgsqlConnection` (not EF Core — because EF doesn't support the `<=>` cosine distance operator)
- Executes SQL: `SELECT document_url, document_title, chunk_text, 1 - (embedding <=> @emb::vector) AS similarity FROM microsoft_docs ORDER BY embedding <=> @emb::vector LIMIT 3`
- The `<=>` operator computes cosine distance between the input vector and every stored embedding
- Returns the top 3 most similar Microsoft Documentation chunks

**Phase 3: LLM Verdict (Lines 250–304)**
- Sends the user's problem + the 3 retrieved documents to Groq with a strict prompt: "Does the Knowledge Base provide a verified, explicit solution?"
- Uses `response_format: { type: "json_object" }` to force structured JSON output
- Uses `temperature: 0.1` (extremely conservative — almost no creativity)
- Deserializes into `RagVerdict` (hasSolution, confidenceScore, proposedSolution, referenceUrls)

---

### 📄 `Services/JobFieldMappingService.cs` — CSV Routing (149 lines)

**What it does**: Parses `departements.csv` at startup and provides lookup functions.

- Uses `CsvHelper` with `[Name("Job field")]` attribute mapping to handle CSV column headers
- Stores all mappings in a `Dictionary<string, JobFieldMapping>` with `StringComparer.OrdinalIgnoreCase`
- `ResolveEmail(jobField)`: Returns the assignee email for a given job field, or the default
- `GetAllJobFields()`: Returns all valid job field names (fed to the LLM prompt to constrain its output)
- `GetMapping(jobField)`: Returns the full mapping including `WebhookUrl` (for Teams notifications)

---

### 📄 `Data/AppDbContext.cs` — Database Configuration (144 lines)

**What it does**: Entity Framework Core `DbContext` using Fluent API for PostgreSQL schema configuration.

- **Two tables**: `Tickets` and `TicketStateLogs`
- `PipelineStatus` enum is stored as a **string** column via `.HasConversion<string>()` (not an integer — for human readability in the database)
- `MessageId` has a unique index (`IX_Tickets_MessageId`) to prevent duplicate ticket processing
- `CurrentPipelineStatus` and `AdoItemState` have indexes for fast dashboard queries
- One-to-Many relationship: `Ticket` → `TicketStateLogs` with cascade delete

---

### 📄 Data Models (in `Models/`)

| File | Purpose |
|---|---|
| `Ticket.cs` | Core entity: email metadata + LLM results + ADO mapping + client feedback (rating/comment) |
| `TicketStateLog.cs` | Immutable audit log entry: captures every pipeline state transition with timestamp and optional error |
| `ExtractedEmailData.cs` | DTO for LLM output: 13 fields including `GetPriority()` (severity→priority mapping) and `GetExpectedResponseHours()` |
| `RagVerdict.cs` | DTO for RAG output: `HasSolution`, `ConfidenceScore`, `ProposedSolution`, `ReferenceUrls` |
| `FeedbackPayload.cs` | DTO for the Adaptive Card HTTP POST: `rating` (string) and `comment` (string) |
| `JobFieldMapping.cs` | DTO for CSV rows: `JobField`, `Email`, `Department`, `TeamId`, `ChannelId`, `WebhookUrl` |
| `Enums/PipelineStatus.cs` | 10-value enum representing the full ticket lifecycle state machine |

---

## 4. Pipeline Execution Flow

```
Email arrives in Outlook
    │
    ▼
PollMailboxAsync() — Graph API: GET unread messages
    │
    ▼
ProcessEmailAsync()
    ├── STEP 1: AnalyzeEmailAsync() — Groq LLM extracts metadata
    ├── STEP 1.5: EvaluateRagSolutionAsync() — Python→pgvector→LLM
    ├── STEP 2: Save Ticket to PostgreSQL (database-first)
    ├── STEP 3: Mark email as read (Graph API PATCH)
    ├── STEP 4: CreateEmailWorkItemAsync() — ADO REST API
    └── STEP 5: Send notifications
         ├── Auto-reply email (Graph API)
         ├── Assignee notification email (Graph API)
         └── Teams Adaptive Card (Power Automate webhook)

    ═══════════════════════════════════════
    60 seconds later...
    ═══════════════════════════════════════

PollWorkItemUpdatesAsync() — ADO WIQL query
    │
    ▼
For each work item with changed state:
    ├── Sync state to PostgreSQL
    ├── Send state-change email to client
    ├── If Done/Closed: inject Adaptive Card feedback form
    └── Add EmailSent_{State} tag to prevent duplicates
```

---

## 5. Key APIs Used

### Microsoft Graph API Calls

| Operation | SDK Method | Purpose |
|---|---|---|
| Read inbox | `_graphClient.Users[mail].MailFolders["Inbox"].Messages.GetAsync()` | Poll for unread emails |
| Get full body | `_graphClient.Users[mail].Messages[id].GetAsync()` | Fetch HTML body content |
| Mark as read | `_graphClient.Users[mail].Messages[id].PatchAsync()` | Prevent re-processing |
| Send email | `_graphClient.Users[mail].SendMail.PostAsync()` | Auto-replies, notifications, alerts |

### Azure DevOps API Calls

| Operation | SDK Method | Purpose |
|---|---|---|
| Create work item | `_witClient.CreateWorkItemAsync(patchDoc, project, "Issue")` | Generate Issue from email |
| Update state | `_witClient.UpdateWorkItemAsync(patchDoc, id)` | Change to Done/To Do |
| Query items | `_witClient.QueryByWiqlAsync(wiql)` | Find recently changed items |
| Get fields | `_witClient.GetWorkItemsAsync(ids, expand: Fields)` | Retrieve full work item data |

### Groq API Calls

| Operation | Endpoint | Purpose |
|---|---|---|
| Email analysis | `POST /v1/chat/completions` | Extract metadata from email body |
| RAG verdict | `POST /v1/chat/completions` (with `response_format: json_object`) | Evaluate auto-resolution |

---

## 6. Database Schema

### `Tickets` Table

| Column | Type | Nullable | Purpose |
|---|---|---|---|
| `TicketId` | UUID (PK) | No | Primary key |
| `MessageId` | VARCHAR(512) | No | MS Graph message ID (unique index) |
| `SenderEmail` | VARCHAR(320) | No | Original sender |
| `Subject` | VARCHAR(1000) | No | Email subject |
| `BodyExcerpt` | VARCHAR(4000) | No | Truncated body for display |
| `ReceivedAt` | TIMESTAMP | No | When email was received |
| `ExtractedDepartment` | VARCHAR(200) | Yes | LLM-extracted job field |
| `ExtractedIntent` | VARCHAR(500) | Yes | LLM-extracted core problem |
| `LlmConfidenceScore` | DOUBLE | Yes | LLM confidence (0.0–1.0) |
| `AdoWorkItemId` | INT | Yes | Azure DevOps work item ID |
| `AdoAssignee` | VARCHAR(320) | Yes | Assigned engineer email |
| `AdoUrl` | VARCHAR(2048) | Yes | Direct URL to ADO work item |
| `AdoItemState` | VARCHAR(100) | Yes | Current ADO board state |
| `ClientRating` | INT | Yes | Star rating (1-5) from feedback |
| `ClientFeedback` | TEXT | Yes | Written comment from feedback |
| `CurrentPipelineStatus` | VARCHAR(50) | No | Pipeline state (stored as string) |
| `LastUpdatedAt` | TIMESTAMP | No | Last modification time |

### `TicketStateLogs` Table

| Column | Type | Purpose |
|---|---|---|
| `LogId` | UUID (PK) | Primary key |
| `TicketId` | UUID (FK) | References `Tickets.TicketId` |
| `PipelineStatus` | VARCHAR(50) | The status entered |
| `ErrorMessage` | VARCHAR(4000) | Error details (nullable) |
| `CreatedAt` | TIMESTAMP | When this transition occurred |

### `microsoft_docs` Table (pgvector — managed by Python)

| Column | Type | Purpose |
|---|---|---|
| `id` | SERIAL (PK) | Auto-increment ID |
| `document_url` | TEXT | Source Microsoft Docs URL |
| `document_title` | TEXT | Document title |
| `chunk_text` | TEXT | Text chunk (max ~500 words) |
| `embedding` | VECTOR(384) | 384-dimensional vector from all-MiniLM-L6-v2 |

---

## 7. Design Patterns & Decisions

### Database-First Persistence
The ticket is saved to PostgreSQL **before** creating the ADO work item. If ADO fails, the ticket still exists in the database and can be retried.

### Tag-Based Email Deduplication
When the worker sends a state-change email, it adds a tag like `EmailSent_Done` to the ADO work item. On the next polling cycle, it checks for this tag and skips sending if it already exists.

### Graceful Degradation
- If the LLM fails → returns default extracted data, pipeline continues
- If RAG fails → pipeline continues without auto-resolution
- If ADO creation fails → DB is updated to `AdoFailed`, TMA is alerted
- If email sending fails → DB is updated to `MailSendingFailed`, but ticket and ADO item are safe

### Scoped DbContext Pattern
The `MailPollingService` is a Singleton (it lives forever), but `AppDbContext` is Scoped (created per-request). To avoid lifetime conflicts, the service uses `IServiceScopeFactory` to create fresh DB contexts inside each operation: `using var scope = _scopeFactory.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();`

### Embedded Resources for Templates
HTML email templates are compiled into the DLL binary as embedded resources (not read from disk at runtime). This ensures templates are always available even if the working directory changes.

### Hidden Metadata in ADO Descriptions
Since ADO work items don't support custom fields in the Basic process template, sender metadata is stored as invisible HTML `<span>` elements with `data-*` attributes inside the Description field. This is parsed back using Regex when the worker needs to send state-change emails.
