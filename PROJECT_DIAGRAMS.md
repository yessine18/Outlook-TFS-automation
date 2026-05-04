# 📊 Project Diagrams — PFE Presentation

> All diagrams are in **Mermaid** format. You can render them in your PFE slides, in markdown viewers, or at [mermaid.live](https://mermaid.live).

---

## 📋 Recommended Presentation Order

| Order | Diagram | Purpose | When to Present |
|---|---|---|---|
| **1st** | 1. System Architecture | Show the big picture first | "Here's what the system looks like" |
| **2nd** | 7. Class Diagram | Explain the code structure | "Here are the main services" |
| **3rd** | 6. Database ER Diagram | Show the data model | "Here's how data is stored" |
| **4th** | 2.1 Pipeline Part 1 | Walk through email → AI analysis | "Step 1: An email arrives..." |
| **5th** | 2.2 Pipeline Part 2 | Walk through DB → ADO creation | "Step 2: We save and create the ticket..." |
| **6th** | 2.3 Pipeline Part 3 | Walk through notifications | "Step 3: We notify everyone..." |
| **7th** | 3. RAG Pipeline | Deep dive into the AI engine | "Now let me zoom into the RAG system..." |
| **8th** | 5. State Machine | Show all possible ticket states | "Here are all the states a ticket goes through" |
| **9th** | 4. ADO State Sync | Explain the sync loop | "How we keep everything in sync" |
| **10th** | 9. Client Validation | Show the accept/reject flow | "How the client interacts with AI solutions" |
| **11th** | 8.1 Sequence Part 1 | Show component interactions (creation) | "Here's the full interaction timeline" |
| **12th** | 8.2 Sequence Part 2 | Show component interactions (feedback) | "And here's what happens after..." |
| **13th** | 11. Error Handling | Show resilience design | "What happens when things go wrong" |
| **14th** | 10.1 + 10.2 Deployment | Dev vs Prod architecture | "And here's how we deploy it" |

---

## 1. System Architecture (Global Overview)

This is the big picture — all external services and how they connect.
Render at [plantuml.com](https://www.plantuml.com/plantuml/uml) or any PlantUML viewer.

```plantuml
@startuml
skinparam backgroundColor white
skinparam componentStyle rectangle
skinparam defaultFontSize 12
skinparam ArrowThickness 1.5
skinparam RoundCorner 8
skinparam PackageBorderColor #888888
skinparam PackageBackgroundColor #F8F9FA
skinparam DatabaseBackgroundColor #E8F5E9
skinparam ActorBackgroundColor #FFF3E0

title Helpdesk Automation Pipeline - System Architecture

package "External Cloud Services" <<Cloud>> #E3F2FD {
    [Outlook Mailbox\n(Microsoft Graph API)] as OUTLOOK
    [Groq Cloud LLM\n(LLaMA 3.3 70B)] as GROQ
    [Azure DevOps\n(REST API + SDK)] as ADO
    [Microsoft Teams\n(Power Automate)] as TEAMS
}

package ".NET Worker Service" {
    [MailPollingService\n(Core Orchestrator)\nPolls every 60s] as MPS
    [GroqLlmService\n(AI Engine)\nAnalysis + RAG] as LLM
    [AzureDevOpsService\n(ADO Integration)] as ADOSVC
    [JobFieldMappingService\n(CSV Router)] as JFS
    [Minimal API - Kestrel\n(Validation + Feedback)] as API
}

package "Data Layer" {
    database "PostgreSQL + pgvector\n\nTickets | StateLogs\nmicrosoft_docs (14K vectors)" as PG
    file "departements.csv\n\nJob Field > Assignee\nDepartment > Webhook" as CSV
}

package "Python Bridge (.venv)" {
    [query_vector.py\nsentence-transformers\nall-MiniLM-L6-v2\nText > 384D vector] as PYTHON
}

actor "Client\n(Email sender)" as CLIENT

' === PIPELINE FLOW (numbered) ===

OUTLOOK --> MPS : 1. Poll unread emails
MPS --> LLM : 2. Send email body
LLM --> GROQ : 3. Extract metadata (POST)
MPS --> JFS : 4. Resolve assignee
JFS --> CSV : 4. Read mapping
LLM --> PYTHON : 5. Vectorize (spawn process)
PYTHON --> LLM : 5. Return 384D array
LLM --> PG : 6. Cosine similarity search
LLM --> GROQ : 7. RAG verdict (POST)
MPS --> PG : 8. Save ticket (DB first)
MPS --> OUTLOOK : 9. Mark as read (PATCH)
MPS --> ADOSVC : 10. Create work item
ADOSVC --> ADO : 10. JsonPatchDocument
MPS --> OUTLOOK : 11. Send auto-reply email
MPS --> TEAMS : 12. Send Adaptive Card

' === CLIENT INTERACTION ===

CLIENT --> API : Accept/Reject (GET)\nFeedback (POST)
API --> PG : Update ticket
API --> ADOSVC : Update ADO state

@enduml
```

---

## 2. Email Processing Pipeline

The main pipeline is divided into **3 diagrams** for clarity.

### 2.1 Part 1 — Email Ingestion & AI Analysis (Steps 1–1.5)

```mermaid
flowchart TD
    A["📨 Unread Email Arrives<br/>in Outlook Inbox"] --> B["🔄 PollMailboxAsync()<br/>Graph API: GET top 10 unread"]
    B --> C{"Sender domain<br/>in AllowedDomains?"}
    C -->|"❌ No"| D["🚫 Mark as Read<br/>& Skip (spam)"]
    C -->|"✅ Yes"| E["📄 Fetch Full Email Body<br/>Graph API: GET body HTML"]
    
    E --> F["🤖 STEP 1: LLM Analysis<br/>GroqLlmService.AnalyzeEmailAsync()"]
    F --> G["📤 POST to Groq API<br/>llama-3.3-70b-versatile<br/>temperature: 0.2"]
    G --> H["📦 Extract Metadata<br/>CoreProblem, Severity,<br/>JobField, DetailedDescription,<br/>AffectedSystems, ErrorCodes"]
    H --> I["📂 Resolve Assignee<br/>JobFieldMappingService<br/>CSV lookup by JobField"]
    
    I --> J["🧠 STEP 1.5: RAG Evaluation<br/>EvaluateRagSolutionAsync()"]
    J --> K["🐍 Python Bridge<br/>Encode problem → 384D vector"]
    K --> L["🗄️ pgvector SQL Query<br/>Top 3 similar docs (cosine)"]
    L --> M["🤖 LLM Verdict<br/>temperature: 0.1<br/>Has solution? Confidence?"]
    
    M --> N{"Confidence<br/>> 70%?"}
    N -->|"✅ Yes"| O["🟢 Prepend AI Solution<br/>to DetailedDescription"]
    N -->|"❌ No"| P["🟠 Continue normally<br/>(human assignment)"]
    
    O --> Q["➡️ Continue to Part 2"]
    P --> Q

    style A fill:#e0f2fe
    style D fill:#fee2e2
    style O fill:#dcfce7
    style P fill:#fff7ed
```

### 2.2 Part 2 — Database & Azure DevOps (Steps 2–4)

```mermaid
flowchart TD
    A["➡️ From Part 1<br/>(LLM + RAG complete)"] --> B["🗄️ STEP 2: Database Persist<br/>(DATABASE FIRST)"]
    
    B --> C["Create Ticket Entity<br/>TicketId = new GUID<br/>Status = LlmSuccess"]
    C --> D["💾 db.SaveChangesAsync()<br/>PostgreSQL INSERT"]
    D --> E{"DB Save<br/>succeeded?"}
    E -->|"❌ No"| F["🚨 Alert TMA Team<br/>Mark as Read & ABORT"]
    E -->|"✅ Yes"| G["✉️ STEP 3: Mark Email as Read<br/>Graph API PATCH isRead=true"]
    
    G --> H["📋 STEP 4: Create ADO Work Item<br/>AzureDevOpsService"]
    H --> I["Build JsonPatchDocument<br/>Title, Description, Priority,<br/>Tags, AssignedTo"]
    I --> J["📤 CreateWorkItemAsync()<br/>Type: Issue"]
    
    J --> K{"Assignee<br/>identity valid?"}
    K -->|"❌ VssException"| L["🔄 Retry WITHOUT assignee<br/>(remove AssignedTo field)"]
    K -->|"✅ Yes"| M["✅ Work Item Created<br/>ID returned"]
    L --> M
    
    M --> N["🗄️ Back-fill DB Ticket<br/>AdoWorkItemId, AdoUrl,<br/>AdoItemState"]
    
    N --> O{"RAG resolved<br/>with confidence > 70%?"}
    O -->|"✅ Yes"| P["Status = PendingClientValidation"]
    O -->|"❌ No"| Q["Status = AdoCreated"]
    
    P --> R["➡️ Continue to Part 3"]
    Q --> R

    style A fill:#e0f2fe
    style F fill:#fee2e2
    style M fill:#dcfce7
```

### 2.3 Part 3 — Notifications (Step 5)

```mermaid
flowchart TD
    A["➡️ From Part 2<br/>(DB + ADO complete)"] --> B["📬 STEP 5: Send Notifications"]
    
    B --> C["✉️ Auto-Reply Email<br/>to Original Sender"]
    C --> D["Load AutoReplyTemplate.html<br/>(embedded resource)"]
    D --> E["Generate QR Code<br/>(base64 data URI)"]
    E --> F{"RAG resolved?"}
    F -->|"✅ Yes"| G["Inject Green AI Solution Box<br/>+ Accept/Reject Buttons"]
    F -->|"❌ No"| H["Standard confirmation<br/>template only"]
    G --> I["📤 SendMail via Graph API"]
    H --> I
    
    B --> J["👤 Assignee Notification<br/>to IT Engineer"]
    J --> K["Load AssigneeNotificationTemplate.html"]
    K --> L["📤 SendMail via Graph API"]
    
    B --> M["💬 Teams Notification<br/>to Department Channel"]
    M --> N["Build Adaptive Card v1.4<br/>JSON payload"]
    N --> O{"RAG resolved?"}
    O -->|"✅ Yes"| P["🟢 Green Card"]
    O -->|"❌ No"| Q["🟠 Orange Card"]
    P --> R["📤 POST to Power Automate<br/>Webhook URL"]
    Q --> R
    
    I --> S["✅ Pipeline Complete!"]
    L --> S
    R --> S

    style A fill:#e0f2fe
    style G fill:#dcfce7
    style Q fill:#fff7ed
    style S fill:#dcfce7
```

---

## 3. RAG Auto-Resolution Pipeline (Detailed)

Zoomed-in view of the 3-phase RAG system.

```mermaid
flowchart LR
    subgraph "Phase 1: Vectorization"
        A["📩 Client Problem<br/>(DetailedDescription)"] --> B["🐍 Python Child Process<br/>query_vector.py"]
        B --> C["🤗 HuggingFace Model<br/>all-MiniLM-L6-v2"]
        C --> D["📊 384D Float Array<br/>[0.023, -0.156, ...]"]
    end

    subgraph "Phase 2: Similarity Search"
        D --> E["🗄️ PostgreSQL pgvector"]
        E --> F["SQL: cosine distance<br/>embedding <=> @vector"]
        F --> G["📄 Top 3 Docs<br/>(title, URL, text chunk)"]
    end

    subgraph "Phase 3: LLM Verdict"
        G --> H["🤖 Groq LLM<br/>temperature: 0.1"]
        H --> I{"Has explicit<br/>solution?"}
        I -->|"✅ Yes + conf > 70%"| J["🟢 RagVerdict<br/>proposedSolution<br/>referenceUrls<br/>confidenceScore"]
        I -->|"❌ No"| K["🟠 No auto-resolve<br/>→ human assignment"]
    end

    style A fill:#e0f2fe
    style J fill:#dcfce7
    style K fill:#fff7ed
```

---

## 4. ADO State Synchronization Flow

How the worker keeps PostgreSQL in sync with Azure DevOps board changes.

```mermaid
flowchart TD
    A["⏰ Every 60 seconds<br/>PollWorkItemUpdatesAsync()"] --> B["📋 WIQL Query<br/>Tags CONTAINS 'AutoCreated'<br/>ChangedDate > @today - 30"]
    B --> C["📦 Get all matching<br/>Work Items with fields"]
    
    C --> D["🔁 For each Work Item"]
    D --> E{"ADO State ≠<br/>DB State?"}
    E -->|"No change"| D
    E -->|"Changed!"| F["🗄️ Update PostgreSQL<br/>ticket.AdoItemState = newState<br/>+ Add TicketStateLog"]
    
    F --> G{"Tag 'EmailSent_State'<br/>exists?"}
    G -->|"✅ Already sent"| D
    G -->|"❌ First time"| H["📧 Send State-Change Email<br/>to original sender"]
    H --> I{"State = Done<br/>or Closed?"}
    I -->|"✅ Yes"| J["💌 Inject Adaptive Card<br/>(feedback form)"]
    I -->|"❌ No"| K["Standard state<br/>update email"]
    J --> L["🏷️ Add Tag<br/>EmailSent_Done"]
    K --> L
    L --> D

    style A fill:#e0f2fe
    style F fill:#fef3c7
    style J fill:#dcfce7
```

---

## 5. Pipeline State Machine

All possible states and transitions for a ticket.

```mermaid
stateDiagram-v2
    [*] --> EmailReceived: Email polled from Outlook
    
    EmailReceived --> LlmProcessing: Send to Groq
    LlmProcessing --> LlmSuccess: Extraction OK
    LlmProcessing --> LlmFailed: API error / parse error
    
    LlmSuccess --> AdoCreating: Build JsonPatchDocument
    AdoCreating --> AdoCreated: Work Item created
    AdoCreating --> AdoFailed: ADO API error
    
    AdoCreated --> MailSendingFailed: Email send error
    
    AdoCreated --> PendingClientValidation: RAG found solution (conf > 70%)
    
    PendingClientValidation --> ClientAcceptedResolution: Client clicks Accept
    PendingClientValidation --> ClientRejectedResolution: Client clicks Reject
    
    LlmFailed --> [*]: TMA Alert sent
    AdoFailed --> [*]: TMA Alert sent
    MailSendingFailed --> [*]: TMA Alert sent
    ClientAcceptedResolution --> [*]: Ticket closed (Done)
    ClientRejectedResolution --> [*]: Re-assigned to human
```

---

## 6. Database Entity-Relationship Diagram

```mermaid
erDiagram
    TICKETS {
        uuid TicketId PK
        varchar MessageId UK "MS Graph message ID"
        varchar SenderEmail
        varchar Subject
        varchar BodyExcerpt "max 4000 chars"
        timestamp ReceivedAt
        varchar ExtractedDepartment "LLM job field"
        varchar ExtractedIntent "LLM core problem"
        double LlmConfidenceScore "0.0 - 1.0"
        int AdoWorkItemId "nullable"
        varchar AdoAssignee "nullable"
        varchar AdoUrl "nullable"
        varchar AdoItemState "nullable"
        int ClientRating "1-5 stars"
        text ClientFeedback "nullable"
        varchar CurrentPipelineStatus "enum as string"
        timestamp LastUpdatedAt
    }

    TICKET_STATE_LOGS {
        uuid LogId PK
        uuid TicketId FK
        varchar PipelineStatus "enum as string"
        varchar ErrorMessage "nullable"
        timestamp CreatedAt
    }

    MICROSOFT_DOCS {
        serial id PK
        text document_url
        text document_title
        text chunk_text "max ~500 words"
        vector embedding "384 dimensions"
    }

    TICKETS ||--o{ TICKET_STATE_LOGS : "has many"
```

---

## 7. Class Diagram (Key Services)

```mermaid
classDiagram
    class MailPollingService {
        -GraphServiceClient _graphClient
        -AzureDevOpsService _adoService
        -GroqLlmService _llmService
        -JobFieldMappingService _jobFieldService
        -IServiceScopeFactory _scopeFactory
        +ExecuteAsync(CancellationToken)
        -PollMailboxAsync(CancellationToken)
        -PollWorkItemUpdatesAsync(CancellationToken)
        -ProcessEmailAsync(Message, CancellationToken)
        -SendAutoReplyAsync(...)
        -SendAssigneeNotificationAsync(...)
        -SendTeamsNotificationAsync(...)
        -SendTmaAlertAsync(...)
    }

    class AzureDevOpsService {
        -WorkItemTrackingHttpClient _witClient
        +CreateEmailWorkItemAsync(...)
        +UpdateWorkItemStateAsync(int, string)
        +GetUpdatedWorkItemsAsync(string)
        +AddWorkItemTagAsync(int, string, string)
        +AddWorkItemCommentAsync(int, string)
        -BuildHiddenMetadata(...)
    }

    class GroqLlmService {
        -HttpClient _httpClient
        -string _apiKey
        +AnalyzeEmailAsync(string, string, List, CT)
        +EvaluateRagSolutionAsync(string, CT)
        -CreateDefaultExtractedData()
    }

    class JobFieldMappingService {
        -Dictionary _mappings
        +ResolveEmail(string, string) string
        +GetAllJobFields() List
        +GetMapping(string) JobFieldMapping
    }

    class AppDbContext {
        +DbSet~Ticket~ Tickets
        +DbSet~TicketStateLog~ TicketStateLogs
        +OnModelCreating(ModelBuilder)
    }

    MailPollingService --> AzureDevOpsService : uses
    MailPollingService --> GroqLlmService : uses
    MailPollingService --> JobFieldMappingService : uses
    MailPollingService --> AppDbContext : creates via scope
    AzureDevOpsService --> AppDbContext : indirect
```

---

## 8. Sequence Diagram — Full Ticket Lifecycle

### 8.1 Part 1: Ticket Creation

```mermaid
sequenceDiagram
    actor Client
    participant Outlook as 📨 Outlook
    participant Worker as 🔄 Worker Service
    participant Groq as 🤖 Groq LLM
    participant Python as 🐍 Python
    participant PG as 🗄️ PostgreSQL
    participant ADO as 📋 Azure DevOps
    participant Teams as 💬 Teams

    Client->>Outlook: Send support email
    
    loop Every 60 seconds
        Worker->>Outlook: GET unread messages (Graph API)
    end
    
    Outlook-->>Worker: New email found
    Worker->>Worker: Check domain whitelist
    Worker->>Outlook: GET full email body
    
    Worker->>Groq: POST /chat/completions (analyze email)
    Groq-->>Worker: ExtractedEmailData JSON
    
    Worker->>Python: Spawn query_vector.py
    Python-->>Worker: 384D vector (stdout JSON)
    Worker->>PG: SQL: cosine similarity search
    PG-->>Worker: Top 3 matching docs
    Worker->>Groq: POST /chat/completions (RAG verdict)
    Groq-->>Worker: RagVerdict JSON
    
    Worker->>PG: INSERT Ticket (database-first)
    Worker->>Outlook: PATCH mark as read
    Worker->>ADO: CreateWorkItemAsync (Issue)
    ADO-->>Worker: WorkItem ID returned
    Worker->>PG: UPDATE Ticket with ADO data
    
    Worker->>Outlook: Send auto-reply email
    Worker->>Outlook: Send assignee notification
    Worker->>Teams: POST Adaptive Card (webhook)
```

### 8.2 Part 2: State Sync & Client Feedback

```mermaid
sequenceDiagram
    actor Assignee
    actor Client
    participant ADO as 📋 Azure DevOps
    participant Worker as 🔄 Worker Service
    participant PG as 🗄️ PostgreSQL
    participant Outlook as 📨 Outlook

    Assignee->>ADO: Move ticket: To Do → Doing
    
    loop Every 60 seconds
        Worker->>ADO: WIQL query (changed items)
    end
    
    ADO-->>Worker: State = "Doing"
    Worker->>PG: UPDATE AdoItemState
    Worker->>Outlook: Send state-change email to Client
    Worker->>ADO: Add tag "EmailSent_Doing"
    
    Assignee->>ADO: Move ticket: Doing → Done
    ADO-->>Worker: State = "Done"
    Worker->>PG: UPDATE AdoItemState
    Worker->>Outlook: Send closure email + Adaptive Card
    Worker->>ADO: Add tag "EmailSent_Done"
    
    Client->>Outlook: Open email, see Adaptive Card
    Client->>Worker: POST /api/ticket/{id}/feedback (rating + comment)
    Worker->>PG: UPDATE ClientRating, ClientFeedback
    Worker->>ADO: AddWorkItemCommentAsync (HTML feedback)
```

---

## 9. Client Validation Flow (RAG Accept/Reject)

```mermaid
flowchart TD
    A["📩 Client receives<br/>auto-reply with AI solution"] --> B{"Client's decision"}
    
    B -->|"Clicks ✅ Accept"| C["GET /api/ticket/{id}/validate?accepted=true"]
    B -->|"Clicks ❌ Reject"| D["GET /api/ticket/{id}/validate?accepted=false"]
    
    C --> E["ADO: Set state → Done"]
    E --> F["DB: Status = ClientAcceptedResolution"]
    F --> G["🟢 HTML: 'Validation Successful'"]
    
    D --> H["ADO: Set state → To Do"]
    H --> I["DB: Status = ClientRejectedResolution"]
    I --> J["🟡 HTML: 'Support Requested'"]
    
    G --> K["✅ Ticket Closed<br/>Problem solved by AI"]
    J --> L["👤 Human agent<br/>takes over"]

    style C fill:#dcfce7
    style D fill:#fef3c7
    style K fill:#dcfce7
    style L fill:#fff7ed
```

---

## 10. Deployment Architecture

### 10.1 Development (Current)

```mermaid
graph LR
    subgraph "Your Laptop"
        APP["🔄 dotnet run<br/>localhost:5000"]
        PG["🗄️ PostgreSQL<br/>(Docker container)"]
        PY["🐍 Python .venv"]
    end

    DT["🌐 Dev Tunnel<br/>*.devtunnels.ms"]
    
    APP <--> PG
    APP <--> PY
    APP <--> DT
    DT <-->|"Public URL"| OL["📨 Outlook"]
    APP <-->|"Graph API"| OL
    APP <-->|"REST API"| ADO["📋 Azure DevOps"]
    APP <-->|"HTTP POST"| GROQ["🤖 Groq Cloud"]
    APP <-->|"Webhook"| TEAMS["💬 Teams"]
```

### 10.2 Production (Azure)

```mermaid
graph LR
    subgraph "Azure Resource Group"
        ACI["🐳 Azure Container Instance<br/>.NET + Python (Docker)"]
        PG["🗄️ PostgreSQL Flexible Server<br/>+ pgvector extension"]
        KV["🔐 Azure Key Vault<br/>(secrets)"]
    end

    ACI <--> PG
    ACI --> KV
    ACI <-->|"Graph API"| OL["📨 Outlook"]
    ACI <-->|"REST API"| ADO["📋 Azure DevOps"]
    ACI <-->|"HTTP POST"| GROQ["🤖 Groq Cloud"]
    ACI <-->|"Webhook"| TEAMS["💬 Teams"]
    
    CLIENT["👤 Client"] -->|"Validation links<br/>Feedback POST"| ACI
```

---

## 11. Error Handling Flow

```mermaid
flowchart TD
    A["Pipeline Step Fails"] --> B{"Which step?"}
    
    B -->|"LLM Analysis"| C["❌ ABORT pipeline<br/>Mark as read"]
    B -->|"Database Persist"| D["❌ ABORT pipeline<br/>Mark as read"]
    B -->|"ADO Creation"| E["Update DB:<br/>Status = AdoFailed"]
    B -->|"Email Sending"| F["Update DB:<br/>Status = MailSendingFailed"]
    B -->|"RAG Evaluation"| G["⚠️ Continue without RAG<br/>(graceful degradation)"]
    
    C --> H["📧 SendTmaAlertAsync()<br/>→ Application Support Team"]
    D --> H
    E --> H
    F --> H
    
    G --> I["Pipeline continues<br/>normally (human route)"]
    
    H --> J["HTML Error Report Email<br/>• Failed step name<br/>• Email subject<br/>• Sender email<br/>• Error type + message<br/>• UTC timestamp"]

    style C fill:#fee2e2
    style D fill:#fee2e2
    style E fill:#fef3c7
    style F fill:#fef3c7
    style G fill:#fff7ed
    style I fill:#dcfce7
```
