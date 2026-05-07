# PFE Report Outline: AI-Driven ITSM Automation
### Revised Structure — Supervisor Approved Format (Max 4 Chapters)

---

## General Introduction

**Project Context and Problematic**: Set the stage by discussing the digital transformation of IT Service Management (ITSM). Highlight the specific operational bottleneck: the inefficiency and high latency of manual email triage and L1 support routing.

**Proposed Solution Overview**: Briefly introduce the end-to-end, "zero-touch" AI orchestrator built to resolve this issue. Emphasize the shift from static automation to an **Agentic RAG architecture**, highlighting the integration of Large Language Models (LLMs), dynamic tool routing, and the Model Context Protocol (MCP) within existing enterprise workflows.

**Report Structure**: Provide a concise roadmap of the chapters contained within the thesis (3 mandatory + 1 optional deployment chapter), guiding the reader through the document's logical progression.

---

## Chapter 1: Preliminary Study and Project Context

*Writing Tip: This chapter grounds the reader in the "Why" of the project. Start with the state of the art to justify WHY AI and RAG are needed, then introduce the concepts. The jury should understand the gap before learning the theory.*

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

**1.3.4 Applied NLP and Agentic RAG in Support Systems**
  - Review the industry shift from linear, static RAG pipelines to autonomous, multi-tool Agentic AI frameworks. Cite recent research on LLM-driven ticket resolution.

**1.3.5 Research Gaps and Limitations**
  - Conclude why existing solutions are either too costly, too generic, or too rigid for Inetum's specific operational needs. This establishes the **scientific justification** for building a custom Agentic RAG system.

### 1.4 Proposed Solution: Theoretical Foundations and Project Framework

> [!NOTE]
> This section merges the former "Theoretical Background" and "Project Framework" into a single, coherent narrative. Each concept is introduced as a direct response to the gaps identified in the state of the art.

**1.4.1 Problem Statement and Project Vision**
  - Clearly define the pain point: human agents wasting valuable engineering hours reading, classifying, and manually routing support emails.
  - Introduce the custom, serverless **Agentic LLM-RAG orchestrator** as the definitive, tailored fix.

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

**1.5.1 Functional Requirements**
  - List specific system capabilities: ingest unread emails, extract structured JSON metadata, autonomously select data retrieval tools, generate ADO issues, dispatch Teams adaptive cards, detect email thread replies, handle inline images and attachments.

**1.5.2 Non-Functional Requirements**
  - Detail operational constraints: high availability, fast execution latency, ACID-compliant persistence, secure credential injection, XSS sanitization, graceful degradation on AI failure.

### 1.6 Project Methodology

**1.6.1 The CRISP-DM Framework for AI Lifecycle**
  - Explain how this methodology was utilized to manage the AI modeling, prompt engineering, and Agentic tuning phases.

**1.6.2 Agile Kanban and Evolutionary Prototyping**
  - Explain how an Agile Kanban board (To Do → Doing → Done) was combined with evolutionary prototyping to iteratively build and scale the system's software architecture. Highlight the continuous flow model, WIP limits, and how the ADO board itself served as both the product's feature AND the project management tool.

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
    - **AI Analysis and Agentic RAG Orchestration** — LLM extraction, multi-tool agent routing, knowledge retrieval
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

**2.4.2 Package: AI Analysis and Agentic RAG Orchestration**
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
  - A detailed flowchart demonstrating the conditional logic: email ingestion → thread detection → LLM extraction → Agentic RAG routing → ADO creation → notification dispatch → feedback collection.

**2.5.2 Chronological API Orchestration (Sequence Diagram)**
  - A timeline diagram mapping the synchronous LLM tool calls, the `.Reply` email thread management, and the asynchronous Webhook/Client feedback interactions.

**2.6 Conclusion**
Summarize the formalized system design and transition into the technical realization chapter.

---

## Chapter 3: Technical Realization and Evolutionary Architecture

*Writing Tip: This is the heart of your thesis. It merges technological choices with implementation by following the evolutionary prototyping approach. Each iteration introduces new technologies, presents the architecture diagram at that stage, and identifies the limitation that motivated the next iteration. Avoid pasting long blocks of simple code; use snippets to highlight core logic.*

**3.1 Introduction**
Introduce the core realization phase, where the system was built iteratively through evolutionary prototyping. Each iteration adds a new capability layer, is justified by a specific technical limitation of the previous version, and introduces the technologies required to solve it.

### 3.2 Development Environment and Tooling
  - 3.2.1 IDEs: Visual Studio Code, Visual Studio
  - 3.2.2 Runtime Environments: .NET 10.0 SDK, Python 3 (Virtual Environment)
  - 3.2.3 Containerization: Docker
  - 3.2.4 Security: .NET User Secrets for credential injection (decoupled from Python sub-processes)

### 3.3 Evolutionary Prototyping: From Basic Automation to Agentic Intelligence

---

#### 3.3.1 Iteration 1: Basic API Integration (Email → Azure DevOps)

**3.3.1.1 Architecture and Implementation**
  - **Technologies introduced**: .NET 10 Worker Service, Microsoft Graph SDK v5, Azure DevOps REST API (`Microsoft.TeamFoundationServer.Client`), Azure AD `ClientSecretCredential`
  - Implement a `BackgroundService` daemon that polls a shared Outlook mailbox every 60 seconds via Microsoft Graph API. For each unread email, create an Azure DevOps Issue work item with the raw subject and body, then mark the email as read.
  - *Present the architectural diagram at this stage.*

**3.3.1.2 Observed Limitation: The "Garbage In" Problem**
  - Raw email HTML dumped directly into ADO descriptions. No metadata extraction, no severity classification, no intelligent routing. Every ticket lands on the default assignee. Engineers waste time re-reading and re-classifying.

---

#### 3.3.2 Iteration 2: LLM-Powered Contextual Extraction and Intelligent Routing

**3.3.2.1 Architecture and Implementation**
  - **Technologies introduced**: Groq Cloud API (LLaMA 3.3 70B Versatile), CsvHelper, JSON prompt engineering
  - Integrate a Groq LLM call that extracts structured JSON metadata from each email: `coreProblem`, `severity`, `estimatedHours`, `jobField`, `detailedDescription`, `affectedSystems`, `errorCodes`, etc.
  - Implement CSV-based job field → assignee routing (`departements.csv`), domain whitelisting for spam filtering, and professional HTML auto-reply emails with QR codes.
  - *Present the updated architectural diagram.*

**3.3.2.2 Observed Limitation: The "Statelessness and Fragility" Problem**
  - No persistent storage. If the pipeline crashes after LLM extraction but before ADO creation, the data is lost. No audit trail. No way to track ticket lifecycle or recover from partial failures.

---

#### 3.3.3 Iteration 3: Relational State Management and Fault Tolerance

**3.3.3.1 Architecture and Implementation**
  - **Technologies introduced**: PostgreSQL (ACID-compliant), Entity Framework Core, `pgvector` extension, `PipelineStatus` state machine, `TicketStateLogs` audit table
  - Implement "Database-First" persistence: save the ticket to PostgreSQL BEFORE creating the ADO Work Item. Add a 10-value pipeline state machine (`EmailReceived → LlmSuccess → AdoCreated → AdoFailed → MailSendingFailed → ...`). Implement bidirectional ADO state sync via WIQL polling with tag-based email deduplication (`EmailSent_{State}`).
  - Add step-level error isolation (each pipeline step wrapped in independent try/catch) and TMA alert emails for catastrophic failures.
  - *Present the updated architectural diagram.*

**3.3.3.2 Observed Limitation: The "Volume" Problem**
  - Every ticket is escalated to a human. Even common, well-documented issues (password resets, VPN configs, known Azure errors) that could be resolved instantly by citing official documentation still generate full work items, wasting L1 engineering hours.

---

#### 3.3.4 Iteration 4: Agentic RAG — Intelligent Knowledge Retrieval and Auto-Resolution

**3.3.4.1 Architecture and Implementation**
  - **Technologies introduced**: `sentence-transformers` (`all-MiniLM-L6-v2`), 384-dimensional vector embeddings, pgvector cosine similarity (`<=>` operator), Python–C# bridge (child process spawning), LangGraph (`create_react_agent`), LangChain tool bindings, Model Context Protocol (MCP), FastMCP Python server, Entra ID App-Only authentication, `httpx` HTTP wrapper
  - Build a Microsoft Docs knowledge base: scrape 14,000+ documentation files → parse HTML to Markdown → batch-embed into pgvector.
  - Implement a **LangGraph ReAct agent** capable of autonomous multi-step reasoning, bound to three distinct tools:
    - **Tool 1 — Internal KB** (`search_internal_knowledge_base`): pgvector cosine similarity search against 14,000 embedded documents.
    - **Tool 2 — Learn Catalog MCP** (`search_microsoft_training_catalog`): Custom `learn_catalog_mcp.py` FastMCP server that authenticates via Entra ID and queries the Microsoft Learn Catalog API for training modules, learning paths, and certifications.
    - **Tool 3 — Official Microsoft MCP** (`search_official_microsoft_documentation`): Custom HTTP POST wrapper engineered to bypass the `405 Method Not Allowed` error on Microsoft's official MCP endpoint, parsing raw SSE streams into clean documentation text.
  - The Agent autonomously decides which tool(s) to invoke based on the problem context, synthesizes the results, and outputs a strict JSON `RagVerdict` contract compatible with the C# backend.
  - If auto-resolved: inject a green-highlighted solution box into the auto-reply email with Accept/Reject validation buttons and Microsoft reference URLs.
  - *Present the updated architectural diagram.*

**3.3.4.2 Observed Limitation: The "Open-Loop" Problem**
  - The system delivers AI solutions but has no way to know if the client found them useful. There is no mechanism for the client to accept or reject an AI-generated resolution. If the solution is wrong, the ticket sits unresolved. No feedback loop exists to measure AI accuracy or client satisfaction.

---

#### 3.3.5 Iteration 5: Client Validation and Interactive Feedback

**3.3.5.1 Architecture and Implementation**
  - **Technologies introduced**: Minimal API validation webhooks (`GET /api/ticket/{id}/validate`), Outlook Actionable Messages (Adaptive Cards v1.4), `Action.Http POST`, client rating persistence
  - **AI Resolution Validation**: When the Agentic RAG finds a solution, the auto-reply email includes dynamic Accept/Reject buttons. If the client clicks **Accept** → the ADO Work Item is automatically moved to "Done" and the ticket is closed. If the client clicks **Reject** → the ticket remains open for human escalation, ensuring no issue is left unresolved.
  - **Satisfaction Feedback**: When a ticket reaches "Done/Closed" (whether by AI or human), the worker sends a closure email containing an **Outlook Adaptive Card** with a star rating (1–5) and an optional comment form. The feedback is submitted via `Action.Http POST`, persisted to PostgreSQL (`ClientRating`, `ClientFeedback` columns), and appended as a styled HTML summary to the ADO Work Item discussion.
  - **Duplicate Validation Protection**: Check `CurrentPipelineStatus` to prevent clients from validating the same ticket multiple times.
  - *Present the updated architectural diagram.*

**3.3.5.2 Observed Limitation: The "Broken Conversations" Problem**
  - All bot replies are sent as isolated, standalone emails using `SendMail`. When a client replies to the bot's email, the system treats it as a brand-new issue and creates a **duplicate ADO Work Item**. No thread tracking exists. Inline images and file attachments in emails are ignored. State-change notifications (e.g., "your ticket is Done") arrive as disconnected emails, confusing the client.

---

#### 3.3.6 Iteration 6: Bi-Directional Communication and Thread Intelligence

**3.3.6.1 Architecture and Implementation**
  - **Technologies introduced**: Microsoft Graph `.Reply` endpoint, `ConversationId` tracking (with database index), LLM follow-up summarization (`SummarizeFollowUpAsync`), Graph API `Attachments` endpoint, ADO `CreateAttachmentAsync`, Power Automate Teams Webhooks, Kestrel static file serving
  - **Thread-Aware Communication**: Switch all auto-replies from `SendMail` (isolated emails) to `Messages[id].Reply`, ensuring every bot communication stays in the client's original Outlook conversation thread. Store `ConversationId` on each ticket with a database index. State-change notifications also reply in the same thread by looking up the original `MessageId` from PostgreSQL.
  - **Thread Reply Detection**: When a follow-up email arrives with a matching `ConversationId`, the LLM summarizes the reply (e.g., *"The client reported that the issue persists after rebooting..."*) and appends it as a styled HTML comment to the existing ADO Work Item — **no duplicate ticket created**. An acknowledgment reply is sent to the client within the same thread.
  - **Attachment Handling**: Fetch email attachments via Graph API. Inline images (screenshots) are embedded as base64 HTML comments in the ADO Work Item. File attachments (PDFs, logs) are uploaded to ADO attachment storage and linked to the Work Item (4MB cap).
  - **Microsoft Teams Alerts**: Fire color-coded Adaptive Cards (🟢 RAG-resolved / 🟠 human-assigned) to the correct Teams channel via per-department Power Automate Webhooks.
  - **Live Dashboard**: Serve a vanilla HTML/CSS/JS SPA via Kestrel static files with REST endpoints (`/api/tickets`, `/api/stats`).
  - *Present the final architectural diagram.*

#### 3.3.7 Conclusion of the Evolutionary Prototyping Phase
Summarize how each iteration solved a specific limitation, and how the final system represents a fully autonomous, multi-tool, thread-aware Agentic AI orchestrator. Emphasize that the evolutionary approach allowed for incremental validation and risk mitigation.

**3.4 Conclusion**
Wrap up the technical realization chapter. If a deployment chapter follows, transition into it. Otherwise, transition directly into the General Conclusion.

---

## Chapter 4 *(Optional — Only if CI/CD and Production Deployment are completed)*: Deployment, Validation, and Outcomes

*Writing Tip: Prove that your system works in production. Use screenshots, test cases, and concrete metrics.*

**4.1 Introduction**
Outline the final phase: packaging the source code, deploying it, and validating its capabilities in a production-like environment.

### 4.2 DevOps and Containerization

**4.2.1 Multi-Stage Docker Builds**
  - Explain the complexities of the Dockerfile setup, combining the compiled .NET application, the Python runtime, and the MCP environments into a cohesive image.

**4.2.2 CI/CD Pipeline**
  - Detail the Azure DevOps pipeline for automated build, test, and deployment.

**4.2.3 Cloud Deployment Architecture**
  - Detail the target hosting architecture (Azure Container Registry, App Services, PostgreSQL Flexible Server, Key Vault).

### 4.3 Performance and Security Validation

**4.3.1 End-to-End Scenario Testing**
  - Walk through comprehensive test cases with visual evidence (screenshots). Cover:
    - The "Happy Path" (email → LLM → ADO → auto-reply)
    - Dynamic Tool Routing (verifying the Agent correctly chooses pgvector vs. Learn Catalog MCP vs. Official MS MCP)
    - Thread Detection (reply to bot → no duplicate ticket → LLM summary appended)
    - Attachment Handling (inline images + file uploads visible on ADO)
    - Feedback Loop (Adaptive Card star rating → persisted to DB + ADO comment)

**4.3.2 Interactive Dashboard Monitoring**
  - Demonstrate the live SPA analytics dashboard (`/api/tickets`, `/api/stats`).

### 4.4 Project Outcomes and Key Metrics

**4.4.1 Technical KPIs**
  - Present final system metrics: average end-to-end latency, tool routing accuracy, Agentic RAG deflection rate, thread detection accuracy.

**4.4.2 Business Impact and ROI**
  - Summarize the tangible return on investment for Inetum. Quantify engineering hours saved and improved Mean Time To Resolution (MTTR).

**4.5 Conclusion**
Conclude the deployment and validation chapter.

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
