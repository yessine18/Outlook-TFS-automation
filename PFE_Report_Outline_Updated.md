# PFE Report Outline: Intelligent Ticketing Pipeline
### Revised Structure — Balanced 4 Chapters (Equal Page Count)

---

## General Introduction

**Project Context and Problematic**: Set the stage by discussing the digital transformation of IT Service Management (ITSM). Highlight the specific operational bottleneck: the inefficiency and high latency of manual email triage and L1 support routing.

**Proposed Solution Overview**: Briefly introduce the end-to-end, "zero-touch" AI orchestrator built to resolve this issue. Emphasize the shift from static automation to an **Agentic AI architecture**, highlighting the integration of Large Language Models (LLMs), dynamic tool routing, and the Model Context Protocol (MCP) within existing enterprise workflows.

**Report Structure**: Provide a concise roadmap of the 4 chapters contained within the thesis, guiding the reader through the document's logical progression.

---

## Chapter 1: Preliminary Study and Project Context

*Writing Tip: This chapter grounds the reader in the "Why" of the project. Start with the state of the art to justify WHY AI and Agentic orchestration are needed, then introduce the concepts. The jury should understand the gap before learning the theory.*

**1.1 Introduction**
Introduce the chapter's focus: presenting the host organization, evaluating the current state of ITSM automation, identifying research gaps, introducing the theoretical foundations of the proposed solution, and defining system requirements.

### 1.2 Host Organization Presentation

**1.2.1 General Overview**
  - 1.2.1.1 Inetum Group: History, global mission, and international footprint.
  - 1.2.1.2 Inetum Tunisie: The local branch hosting the project and its strategic importance.
  - 1.2.1.3 Microsoft Business Line (Africa): The specific operational division executing this Microsoft-centric project.

**1.2.2 Business Areas and Expertise**
  - 1.2.2.1 Inetum Group: Global services (IT consulting, software delivery).
  - 1.2.2.2 Inetum Tunisie: Local expertise (infrastructure management, digital transformation).
  - 1.2.2.3 Microsoft Business Line: Specialization in Microsoft Cloud architecture, modern workplace solutions, and enterprise IT support.

### 1.3 State of the Art and Related Work

> [!IMPORTANT]
> This section comes FIRST — before the theoretical background — so the jury understands the current landscape and gaps that justify the introduction of AI, RAG, and MCP concepts in the next section.

**1.3.1 Foundations of IT Service Management (ITSM)**
  - 1.3.1.1 Core Principles of ITSM: Define ITIL frameworks, SLAs, and professional IT service delivery goals.
  - 1.3.1.2 The Incident and Request Lifecycle: Explain how a support ticket moves from creation, triage, and assignment to resolution and closure.

**1.3.2 Traditional Rule-Based Middleware**
  - Explain the limitations, fragility, and maintenance overhead of standard, keyword-based email routing filters. Demonstrate why static rules cannot scale.

**1.3.3 Commercial AI-Enhanced ITSM Platforms**
  - Review out-of-the-box, premium tools (ServiceNow AI, Zendesk AI, Freshdesk) and their drawbacks: cost, vendor lock-in, limited customization, and lack of MCP-based extensibility.

**1.3.4 Applied NLP and Agentic AI in Support Systems**
  - Review the industry shift from linear, static RAG pipelines to autonomous, multi-tool Agentic AI frameworks. Cite recent research on LLM-driven ticket resolution.

**1.3.5 Research Gaps and Limitations**
  - Conclude why existing solutions are either too costly, too generic, or too rigid for Inetum's specific operational needs. This establishes the **scientific justification** for building a custom Agentic AI system.

### 1.4 Proposed Solution: Theoretical Foundations and Project Framework

> [!NOTE]
> This section merges the former "Theoretical Background" and "Project Framework" into a single, coherent narrative. Each concept is introduced as a direct response to the gaps identified in the state of the art.

**1.4.1 Problem Statement and Project Vision**
  - Clearly define the pain point: human agents wasting valuable engineering hours reading, classifying, and manually routing support emails.
  - Introduce the custom, serverless **Agentic LLM orchestrator** as the definitive, tailored fix.

**1.4.2 Process Automation and Event-Driven Architecture**
  - Explain the paradigm of background daemons, autonomous polling loops, and the role of REST APIs and Webhooks in inter-system communication. *(Justification for the .NET Worker Service approach.)*

**1.4.3 Large Language Models (LLMs) for Intelligent Extraction**
  - Cover the basics of generative AI, transformer architectures, and prompt-based text comprehension. *(Justification for using Groq/LLaMA for metadata extraction.)*

**1.4.4 Vector Databases and Retrieval-Augmented Generation (RAG)**
  - Explain the conversion of semantic text into high-dimensional vector embeddings for similarity searches. Define RAG as the architecture that mitigates AI hallucinations by grounding generative responses in verified knowledge bases. *(Justification for the pgvector + knowledge base approach.)*

**1.4.5 Agentic AI and The Model Context Protocol (MCP)**
  - Define autonomous AI agents (ReAct framework) capable of multi-step reasoning and dynamic tool selection. Introduce MCP as the modern, secure standard for connecting AIs to external APIs without exposing credentials. *(Justification for the LangGraph orchestrator with 3 tools.)*

**1.4.6 Key Performance Objectives**
  - List target metrics: sub-60-second end-to-end processing latency, autonomous L1 ticket deflection rate, high classification accuracy, zero-duplicate thread handling.

### 1.5 System Requirements

**1.5.1 Functional Requirements (IEEE 29148 Standard)**
  - List specific system capabilities: ingest unread emails, extract structured JSON metadata, autonomously select data retrieval tools, generate ADO issues, dispatch Teams adaptive cards, detect email thread replies, handle inline images and attachments, expose client validation endpoints.

**1.5.2 Non-Functional Requirements (ISO/IEC 25010 Standard)**
  - Detail operational constraints: performance efficiency (sub-60s), reliability & fault tolerance, security & privacy (Zero Data Retention), auditability & traceability, maintainability & extensibility.

### 1.6 Project Methodology

**1.6.1 The CRISP-DM Framework for AI Lifecycle**
  - Explain how this methodology was utilized to manage the AI modeling, prompt engineering, and Agentic tuning phases.

**1.6.2 Agile Kanban and Evolutionary Prototyping**
  - Explain how an Agile Kanban board (To Do → Doing → Done) was combined with evolutionary prototyping to iteratively build and scale the system's software architecture. Highlight the continuous flow model, WIP limits, and how the ADO board itself served as both the product's feature AND the project management tool.

**1.6.3 Project Execution Roadmap**
  - Insert the **7-phase execution table** (Infrastructure & Auth → Email Ingestion → Data Persistence → AI Classification → Agentic Orchestration → Enterprise Integrations → Client Feedback & Monitoring).

**1.7 Conclusion**
Summarize the contextual foundations, the identified gaps, and the proposed solution. Transition into the system modeling chapter.

---

## Chapter 2: System Specification and UML Modeling

*Writing Tip: This chapter is strictly about formal system design using standard UML notation. Start with actors, then decompose the system into functional packages, detail each package's use cases, and model the dynamic behavior with activity and sequence diagrams. Technology-agnostic.*

**2.1 Introduction**
Outline the goal of translating the business needs (from Chapter 1) into formal system specifications and defining the boundaries of the application using industry-standard UML modeling.

### 2.2 Identification of System Actors

**2.2.1 Primary Actors (Human)**
  - Define the roles of the End-User (initiating requests via email) and the IT Support Engineer (handling escalated, complex tickets on the ADO board).

**2.2.2 Secondary Actors (System)**
  - Identify external systems the application must interact with: Microsoft 365 (Graph API), Azure DevOps (ADO API), the Vector Database, the LLM Inference Engine, the MCP Servers (Training API + Official Documentation), and Microsoft Teams (Webhook API).

### 2.3 High-Level System Decomposition (Package Diagram)

  - Present a **UML Package Diagram** that decomposes the system into its core functional modules and illustrates the dependencies between them. This diagram serves as the structural map that organizes all subsequent use case diagrams into a coherent, modular architecture:
    - **Email Ingestion and Thread Management** — Mailbox polling, thread detection, attachment extraction
    - **AI Analysis and Agentic Orchestration** — LLM extraction, multi-tool agent routing, knowledge retrieval
    - **ITSM Work Item Management** — ADO work item creation, state synchronization, audit logging
    - **Notification and Communication** — Threaded auto-replies, assignee alerts, Teams adaptive cards
    - **Client Interaction and Feedback** — AI resolution validation, Adaptive Card feedback, satisfaction tracking

### 2.4 Detailed Use Case Modeling

> [!NOTE]
> Each subsection below corresponds to one package from the diagram above. For every package, a dedicated use case diagram is presented alongside formal textual descriptions of its contained use cases.

**2.4.1 Package: Email Ingestion and Thread Management**
  - *Use Case Diagram* — Show actor interactions for email reception, deduplication, ConversationId matching, and attachment handling.
  - *Use Case Description: Process Incoming Support Request* — A formal, step-by-step textual breakdown of the primary email-to-ticket pipeline.
  - *Use Case Description: Detect and Process Email Thread Replies* — A formal description of ConversationId matching, LLM follow-up summarization, and ADO comment appending.

**2.4.2 Package: AI Analysis and Agentic Orchestration**
  - *Use Case Diagram* — Show actor interactions for LLM extraction and autonomous tool routing.
  - *Use Case Description: Extract Structured Metadata via LLM* — A formal description of the Groq LLM call that classifies and extracts email metadata.
  - *Use Case Description: Execute Agentic Tool Routing* — A formal description of the orchestrator autonomously evaluating the email context and routing the query to the internal database, the Learn Catalog MCP, or the Official Microsoft MCP Server.

**2.4.3 Package: ITSM Work Item Management**
  - *Use Case Diagram* — Show actor interactions for work item creation, state tracking, and synchronization.
  - *Use Case Description: Create and Route Work Item* — A formal description of ADO Issue creation with priority, assignee, and metadata.
  - *Use Case Description: Synchronize Pipeline State* — A formal description of the background WIQL polling process that detects ADO state transitions.

**2.4.4 Package: Notification and Communication**
  - *Use Case Diagram* — Show actor interactions for email replies, assignee alerts, and Teams notifications.
  - *Use Case Description: Dispatch Threaded Notifications* — A formal description of the `.Reply`-based auto-reply system and state-change notifications.

**2.4.5 Package: Client Interaction and Feedback**
  - *Use Case Diagram* — Show actor interactions for resolution validation and feedback collection.
  - *Use Case Description: Validate AI Resolution* — A formal description of the Accept/Reject validation flow via email buttons.
  - *Use Case Description: Collect Client Feedback* — A formal breakdown of the Outlook Adaptive Card star rating and comment submission.

### 2.5 Dynamic Behavior Modeling

**2.5.1 Algorithmic Decision Flow (Activity Diagram)**
  - A detailed flowchart demonstrating the conditional logic: email ingestion → thread detection → LLM extraction → Agentic routing → ADO creation → notification dispatch → feedback collection.

**2.5.2 Chronological API Orchestration (Sequence Diagram)**
  - A timeline diagram mapping the synchronous LLM tool calls, the `.Reply` email thread management, and the asynchronous Webhook/Client feedback interactions.

**2.6 Conclusion**
Summarize the formalized system design and transition into the cloud infrastructure and AI orchestration chapter.

---

## Chapter 3: Cloud Infrastructure & AI Orchestration

*Writing Tip: This chapter is the FIRST half of your technical realization. It covers everything that happens BEFORE the .NET backend logic: the Azure Portal configurations, the security setup, and the entire Python-based Agentic AI pipeline. This ensures your massive Azure Entra ID work gets the dedicated space it deserves.*

**3.1 Introduction**
Introduce the core realization phase. Explain that the system was built iteratively through evolutionary prototyping. This chapter focuses on the cloud security foundation and the AI intelligence layer.

### 3.2 Development Environment and Tooling
  - 3.2.1 IDEs: Visual Studio Code, Visual Studio
  - 3.2.2 Runtime Environments: .NET 10.0 SDK, Python 3 (Virtual Environment)
  - 3.2.3 Containerization: Docker (PostgreSQL with pgvector)
  - 3.2.4 Security: .NET User Secrets for credential injection (decoupled from Python sub-processes)

### 3.3 Microsoft Entra ID Integration & Cloud Security

> [!IMPORTANT]
> This section is critical. Without the Azure Portal configurations, no code in the project can authenticate or access any Microsoft API. This was the foundational step of the entire project.

**3.3.1 Azure App Registration & Service Principal Creation**
  - Walkthrough of registering the application in the Azure Portal under the Inetum tenant.
  - Explain the concept of a Service Principal: an identity for the application itself, enabling daemon-based (no human login) access.
  - Show screenshot of the App Registration overview (blur secrets).
  - Explain how `TenantId`, `ClientId`, and `ClientSecret` are generated.

**3.3.2 Configuring OAuth 2.0 Client Credentials Flow**
  - Explain why the Client Credentials flow was chosen over Authorization Code flow (background worker with no UI).
  - Detail the `ClientSecretCredential` implementation in .NET using `Azure.Identity`.
  - Explain how the token is automatically refreshed by the SDK.

**3.3.3 Microsoft Graph API Permissions & Admin Consent**
  - Explain the difference between *Delegated* permissions (user context) and *Application* permissions (daemon context).
  - Detail the exact permissions granted: `Mail.Read`, `Mail.Send`, `Mail.ReadWrite`, `User.Read.All`.
  - Show the Azure Portal screenshot of the "API Permissions" blade with Admin Consent granted.
  - Discuss the **Principle of Least Privilege**: granting only the minimum required permissions.

**3.3.4 Exposing APIs & URI Configuration**
  - Detail any Redirect URIs configured for the Actionable Messages validation endpoints.
  - Explain the Dev Tunnel / Ngrok setup used during development to expose local REST endpoints to external callbacks.
  - Discuss the production URI strategy for the client validation webhooks (`/api/ticket/{id}/validate`).

**3.3.5 Secure Credential Injection Architecture**
  - Explain the `.NET User Secrets` mechanism for local development.
  - Detail how credentials are injected into the Python sub-process environment (`ProcessStartInfo.EnvironmentVariables`) without using `.env` files.
  - Discuss the production path: Azure Key Vault integration.

### 3.4 The Agentic Knowledge Base (pgvector)

**3.4.1 Document Ingestion Pipeline**
  - Explain the 4-stage Python pipeline: `fetch/` (XML sitemap download) → `scrape/` (HTTP page retrieval) → `parse/` (HTML to Markdown DOM cleaning) → `embed/` (batch vectorization).
  - Detail the scale: 14,000+ Microsoft Documentation files processed.

**3.4.2 Generating Vector Embeddings**
  - Explain the `sentence-transformers` library and the `all-MiniLM-L6-v2` model.
  - Detail the 384-dimensional vector space and why this lightweight model was chosen over larger alternatives (speed vs. accuracy tradeoff for enterprise deployment).
  - Explain the batch embedding process using `vectorize_docs.py`.

**3.4.3 Storing and Querying Vectors in PostgreSQL**
  - Explain the `pgvector` extension and the `vector(384)` column type.
  - Detail the cosine similarity search using the `<=>` operator.
  - Show the raw SQL query used for Top-K retrieval.
  - Explain the Python–C# bridge: child process spawning (`python.exe query_vector.py`) with stdout JSON capture.

### 3.5 Python LangGraph Agentic Orchestrator

**3.5.1 From Linear RAG to Agentic AI**
  - Explain the architectural upgrade from a simple "query → retrieve → respond" pipeline to a **LangGraph ReAct agent** capable of autonomous multi-step reasoning.
  - Detail the `create_react_agent` setup with LangChain tool bindings.

**3.5.2 Tool 1 — Internal Knowledge Base (`search_internal_knowledge_base`)**
  - pgvector cosine similarity search against the 14,000 embedded documents.
  - Returns the Top-3 most relevant documentation chunks with similarity scores.

**3.5.3 Tool 2 — Learn Catalog MCP (`search_microsoft_training_catalog`)**
  - Custom `learn_catalog_mcp.py` FastMCP server.
  - Authenticates via Entra ID App-Only credentials (Client Credentials flow) to the Microsoft Learn Catalog API.
  - Returns official training modules, learning paths, and certifications relevant to the user's problem.

**3.5.4 Tool 3 — Official Microsoft MCP (`search_official_microsoft_documentation`)**
  - Custom HTTP POST wrapper using `httpx`.
  - Engineered to bypass the `405 Method Not Allowed` error on Microsoft's official MCP endpoint.
  - Parses raw SSE (Server-Sent Events) streams into clean documentation text.

**3.5.5 The RagVerdict Decision Contract**
  - The Agent autonomously decides which tool(s) to invoke based on the problem context.
  - Synthesizes the results and outputs a strict JSON `RagVerdict` contract (`hasSolution`, `solution`, `confidence`, `sourceUrls`) compatible with the C# backend.

**3.6 Conclusion**
Wrap up the cloud infrastructure and AI orchestration chapter. Transition into the backend automation and system integration chapter.

---

## Chapter 4: Backend Automation & System Integration

*Writing Tip: This is the second half of the technical realization. It covers the .NET Worker Service logic, the Azure DevOps integration, the email communication system, the client feedback loop, and the live dashboard. This chapter follows the evolutionary prototyping approach, showing how each iteration solved a specific limitation.*

**4.1 Introduction**
Introduce the backend automation layer. Explain that this chapter details the .NET Worker Service that orchestrates the entire pipeline: from email ingestion through ticket creation to client feedback collection.

### 4.2 Iteration 1: Basic API Integration (Email → Azure DevOps)

**4.2.1 Architecture and Implementation**
  - **Technologies introduced**: .NET 10 Worker Service, Microsoft Graph SDK v5, Azure DevOps REST API (`Microsoft.TeamFoundationServer.Client`), Azure AD `ClientSecretCredential`
  - Implement a `BackgroundService` daemon that polls a shared Outlook mailbox every 60 seconds via Microsoft Graph API. For each unread email, create an Azure DevOps Issue work item with the raw subject and body, then mark the email as read.
  - *Present the architectural diagram at this stage.*

**4.2.2 Observed Limitation: The "Garbage In" Problem**
  - Raw email HTML dumped directly into ADO descriptions. No metadata extraction, no severity classification, no intelligent routing. Every ticket lands on the default assignee.

### 4.3 Iteration 2: LLM-Powered Contextual Extraction and Intelligent Routing

**4.3.1 Architecture and Implementation**
  - **Technologies introduced**: Groq Cloud API (LLaMA 3.3 70B Versatile), CsvHelper, JSON prompt engineering
  - Integrate a Groq LLM call that extracts structured JSON metadata from each email: `coreProblem`, `severity`, `estimatedHours`, `jobField`, `detailedDescription`, `affectedSystems`, `errorCodes`, etc.
  - Implement CSV-based job field → assignee routing (`departements.csv`), domain whitelisting for spam filtering, and professional HTML auto-reply emails with QR codes.
  - *Present the updated architectural diagram.*

**4.3.2 Observed Limitation: The "Statelessness and Fragility" Problem**
  - No persistent storage. If the pipeline crashes after LLM extraction but before ADO creation, the data is lost. No audit trail.

### 4.4 Iteration 3: Relational State Management and Fault Tolerance

**4.4.1 Architecture and Implementation**
  - **Technologies introduced**: PostgreSQL (ACID-compliant), Entity Framework Core, `PipelineStatus` state machine, `TicketStateLogs` audit table
  - Implement "Database-First" persistence: save the ticket to PostgreSQL BEFORE creating the ADO Work Item. Add a 10-value pipeline state machine (`EmailReceived → LlmSuccess → AdoCreated → AdoFailed → MailSendingFailed → ...`). Implement bidirectional ADO state sync via WIQL polling with tag-based email deduplication (`EmailSent_{State}`).
  - Add step-level error isolation (each pipeline step wrapped in independent try/catch) and TMA alert emails for catastrophic failures.
  - *Present the updated architectural diagram.*

**4.4.2 Observed Limitation: The "Volume" Problem**
  - Every ticket is escalated to a human. Even common, well-documented issues that could be resolved instantly by citing official documentation still generate full work items, wasting L1 engineering hours.

### 4.5 Iteration 4: Agentic AI Integration and Auto-Resolution

**4.5.1 Architecture and Implementation**
  - **Technologies introduced**: Python–C# bridge (child process spawning), LangGraph orchestrator integration (from Chapter 3), `RagVerdict` JSON deserialization
  - The .NET Worker passes the extracted `detailedProblem` to the Python LangGraph orchestrator (detailed in Chapter 3). The orchestrator returns a `RagVerdict` JSON.
  - If auto-resolved: inject a green-highlighted solution box into the auto-reply email with Accept/Reject validation buttons and Microsoft reference URLs.
  - An ADO Work Item is **always** created regardless of the RAG outcome. If auto-resolved, the solution and source URLs are appended to the Work Item logs.
  - *Present the updated architectural diagram.*

**4.5.2 Observed Limitation: The "Open-Loop" Problem**
  - The system delivers AI solutions but has no way to know if the client found them useful. No feedback loop exists to measure AI accuracy or client satisfaction.

### 4.6 Iteration 5: Client Validation and Interactive Feedback

**4.6.1 Architecture and Implementation**
  - **Technologies introduced**: Minimal API validation webhooks (`GET /api/ticket/{id}/validate`), Outlook Actionable Messages (Adaptive Cards v1.4), `Action.Http POST`, client rating persistence
  - **AI Resolution Validation**: When the Agentic AI finds a solution, the auto-reply email includes dynamic Accept/Reject buttons. If the client clicks **Accept** → the ADO Work Item is automatically moved to "Done" and the ticket is closed. If the client clicks **Reject** → the ticket remains open for human escalation.
  - **Satisfaction Feedback**: When a ticket reaches "Done/Closed" (whether by AI or human), the worker sends a closure email containing an **Outlook Adaptive Card** with a star rating (1–5) and an optional comment form. The feedback is submitted via `Action.Http POST`, persisted to PostgreSQL (`ClientRating`, `ClientFeedback` columns), and appended as a styled HTML summary to the ADO Work Item discussion.
  - **Duplicate Validation Protection**: Check `CurrentPipelineStatus` to prevent clients from validating the same ticket multiple times.
  - *Present the updated architectural diagram.*

**4.6.2 Observed Limitation: The "Broken Conversations" Problem**
  - All bot replies are sent as isolated, standalone emails using `SendMail`. When a client replies to the bot's email, the system treats it as a brand-new issue and creates a **duplicate ADO Work Item**. No thread tracking exists. Inline images and file attachments in emails are ignored.

### 4.7 Iteration 6: Bi-Directional Communication and Thread Intelligence

**4.7.1 Architecture and Implementation**
  - **Technologies introduced**: Microsoft Graph `.Reply` endpoint, `ConversationId` tracking (with database index), LLM follow-up summarization (`SummarizeFollowUpAsync`), Graph API `Attachments` endpoint, ADO `CreateAttachmentAsync`, Power Automate Teams Webhooks, Kestrel static file serving
  - **Thread-Aware Communication**: Switch all auto-replies from `SendMail` to `Messages[id].Reply`, ensuring every bot communication stays in the client's original Outlook conversation thread. Store `ConversationId` on each ticket with a database index.
  - **Thread Reply Detection**: When a follow-up email arrives with a matching `ConversationId`, the LLM summarizes the reply and appends it as a styled HTML comment to the existing ADO Work Item — **no duplicate ticket created**.
  - **Attachment Handling**: Inline images embedded as base64 HTML comments in ADO. File attachments uploaded to ADO storage (4MB cap).
  - **Microsoft Teams Alerts**: Color-coded Adaptive Cards (🟢 RAG-resolved / 🟠 human-assigned) fired to the correct Teams channel via per-department Power Automate Webhooks.
  - **Live Dashboard**: Vanilla HTML/CSS/JS SPA via Kestrel static files with REST endpoints (`/api/tickets`, `/api/stats`).
  - *Present the final architectural diagram.*

### 4.8 Conclusion of the Evolutionary Prototyping Phase
Summarize how each iteration solved a specific limitation, and how the final system represents a fully autonomous, multi-tool, thread-aware Agentic AI orchestrator.

**4.9 Conclusion**
Wrap up the backend automation chapter. Transition into the General Conclusion.

---

## General Conclusion and Perspectives

**Summary of Achievements**: Provide a definitive wrap-up confirming that the project successfully transitioned the L1 helpdesk from a manual bottleneck to a highly autonomous, **multi-tool Agentic AI orchestrator** with conversation-aware thread intelligence.

**Value Delivered**: Deliver a final, strong statement on the operational efficiency, cost savings, and modernization brought to Inetum's Microsoft Business Line.

**Future Perspectives**: Propose logical next steps:
  - Expanding the MCP ecosystem (adding Jira/ServiceNow tools)
  - Adding vision capabilities for image-based issue analysis
  - Establishing fully automated CI/CD pipelines with Azure Container Registry
  - Multi-language support for international helpdesk operations
  - Advanced analytics and predictive ticket volume forecasting

---

## Webography

Provide a meticulously structured list of links to official documentation utilized during the project:
- LangGraph / LangChain
- Model Context Protocol (MCP) Specification
- Microsoft Graph SDK
- Azure DevOps REST API
- Groq Cloud / LLaMA
- PostgreSQL / pgvector
- HuggingFace / sentence-transformers
- Microsoft Learn Catalog API
- Outlook Actionable Messages
- Power Automate Webhooks
- Microsoft Entra ID / Azure Identity
- ISO/IEC 25010 / IEEE 29148
