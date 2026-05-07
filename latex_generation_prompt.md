# LaTeX Report Generation Prompt

Send this prompt along with the `PFE_Report_Outline_Updated.md` file inside the project directory. The LLM will have access to the full codebase.

---

## PROMPT START

You are an academic technical writer tasked with generating the **full LaTeX content** of a PFE (Projet de Fin d'Études) engineering thesis report. I will provide you with a detailed outline containing every chapter, section, subsection, and bullet point. Your job is to **expand each item into professionally written, publication-ready LaTeX content**.

### CRITICAL FIRST STEP — EXPLORE THE CODEBASE

**Before writing a single line of LaTeX, you MUST thoroughly explore the project directory** to understand the real codebase. This is mandatory because:
- You must reference **real file names** (e.g., `\texttt{MailPollingService.cs}`, `\texttt{orchestrator.py}`), not guessed ones.
- You must reference **real method names** (e.g., `\texttt{ProcessEmailAsync}`, `\texttt{SummarizeFollowUpAsync}`), not invented ones.
- You must reference **real class names, models, and services** as they exist in the code.
- Code snippets MUST come from the **actual source files**, not fabricated examples.
- Architecture descriptions must match the **real data flow** in the code.

**Exploration checklist — do this first:**
1. List the root directory structure to understand the project layout.
2. Read `README.md` for a high-level overview of features and architecture.
3. Read `MailListenerWorker/Program.cs` for the DI setup and Minimal API endpoints.
4. Read `MailListenerWorker/MailPollingService.cs` for the core pipeline logic (email polling, thread detection, ADO sync, notifications).
5. Read `MailListenerWorker/Services/GroqLlmService.cs` for the LLM integration and RAG orchestration.
6. Read `MailListenerWorker/AzureDevOpsService.cs` for ADO work item creation and attachment uploads.
7. Read `MailListenerWorker/Models/Ticket.cs` for the database entity and state machine.
8. Read `MailListenerWorker/Data/AppDbContext.cs` for the EF Core schema.
9. Read `MailListenerWorker/Services/JobFieldMappingService.cs` for CSV-based routing.
10. Read `MailListenerWorker/departements.csv` for the job field → assignee mapping table.
11. Explore `inetum-ms-kb/src/agent/` for the Python LangGraph orchestrator and MCP servers.
12. Explore `MailListenerWorker/Templates/` for the HTML email template.
13. Explore `MailListenerWorker/wwwroot/` for the dashboard SPA.
14. Read `MailListenerWorker/Migrations/` to understand the database schema evolution.

**Only after completing this exploration should you begin writing the LaTeX content.** Every code snippet, every file reference, every method name in your output must match the real codebase.

---

### THE PROJECT

This project is an **AI-Driven IT Service Management (ITSM) Automation Pipeline** built for Inetum (a global IT consulting company). It is a .NET Worker Service that monitors a shared Outlook mailbox, uses a Groq LLM (LLaMA 3.3 70B) to extract structured metadata from support emails, routes tickets to the correct Azure DevOps assignee via CSV-based job field mapping, and employs a **LangGraph Agentic RAG Orchestrator** with three tools (Internal pgvector KB, Microsoft Learn Catalog MCP, Official Microsoft MCP) to autonomously resolve Level-1 issues. The system features conversation-aware thread tracking (ConversationId), email attachment handling, Outlook Adaptive Card feedback, Microsoft Teams notifications, and a live dashboard.

---

### STRICT WRITING RULES — YOU MUST FOLLOW ALL OF THESE

#### 1. Structure and Flow
- **Every `\section{}` MUST begin with a short introductory paragraph** (2–4 sentences) that previews what will be covered in that section. You CANNOT write a section title and immediately jump to a `\subsection{}` without an intro paragraph first.
- **Every `\section{}` MUST end with a short concluding paragraph** (2–3 sentences) that summarizes the key takeaways and transitions to the next section.
- **Every `\subsection{}` MUST also begin with a short introductory sentence or paragraph** before diving into `\subsubsection{}` items. You CANNOT write a subsection title and immediately jump to a subsubsection without an intro.
- The writing must flow like a narrative — not like a list of disconnected paragraphs.

#### 2. Title Capitalization
- All titles (`\chapter{}`, `\section{}`, `\subsection{}`, `\subsubsection{}`) must use **sentence case**: only the **first word** has an uppercase first letter. The rest are lowercase unless they are proper nouns (e.g., "Azure DevOps", "Microsoft", "PostgreSQL").
- ✅ Correct: `\section{Relational state management and fault tolerance}`
- ✅ Correct: `\subsection{The LangGraph orchestrator and tool binding}`
- ❌ Wrong: `\section{Relational State Management And Fault Tolerance}`
- ❌ Wrong: `\subsection{The LangGraph Orchestrator And Tool Binding}`

#### 3. Source Citations and Anti-Plagiarism
- **Every piece of information** that comes from an external source (article, website, documentation, book, specification) **MUST include an inline citation** using `\cite{key}` or `\footnote{\url{...}}`.
- If you define a concept (e.g., "ITIL is a framework for..."), you MUST cite the official source.
- If you mention a technology's capabilities (e.g., "LangGraph supports cyclic reasoning..."), you MUST cite the official documentation.
- If you compare technologies (e.g., "PostgreSQL vs MongoDB"), you MUST cite benchmarks or official docs.
- Use `\cite{}` for formal references and provide a corresponding `\bibitem{}` entry. If no formal reference exists, use `\footnote{\url{https://...}}` for web sources.
- **DO NOT write any factual claim without a source. This is non-negotiable.**
- At the end of the document, generate a complete `\begin{thebibliography}{}` section with all referenced sources.

#### 4. Figures
- Every figure MUST be referenced in the text **before** it appears, using `Figure~\ref{fig:label}`.
- Every figure MUST have a short paragraph description in the text body that explains what the figure shows and why it matters.
- Every figure MUST have a `\caption{}` (title) underneath it.
- Use this format for standard figures:
```latex
As illustrated in Figure~\ref{fig:iteration1_arch}, the initial architecture consists of...

\begin{figure}[H]
    \centering
    \includegraphics[width=0.95\textwidth]{images yass/iteration1_architecture.png}
    \caption{Architecture of Iteration 1: basic API integration between Outlook and Azure DevOps}
    \label{fig:iteration1_arch}
\end{figure}
```
- For **large/wide diagrams** (architecture diagrams, flowcharts, sequence diagrams), use sideways figures for landscape viewing:
```latex
For a comprehensive view of the complete data flow, Figure~\ref{fig:final_arch_full} presents the architecture in landscape format.

\begin{sidewaysfigure}
    \centering
    \includegraphics[width=\textheight, keepaspectratio]{images yass/final_architecture.png}
    
    \vspace{0.5cm}
    \caption{Final architecture: bi-directional communication and thread intelligence (full-page landscape view)}
    \label{fig:final_arch_full}
\end{sidewaysfigure}
\newpage
```
- Use descriptive label names: `fig:iteration3_state_machine`, `fig:package_diagram`, `fig:rag_flow`, etc.
- Leave placeholder image paths using the format `images yass/descriptive_name.png` — I will replace them with actual screenshots later.

#### 5. Tables
- Every table MUST be referenced in the text **before** it appears, using `Table~\ref{tab:label}`.
- Every table MUST have a short paragraph description explaining its content.
- Every table MUST have a `\caption{}` (title) **ABOVE** the table (not below like figures).
- Use this format:
```latex
Table~\ref{tab:tech_comparison} presents a comparative analysis of the candidate frameworks...

\begin{table}[H]
    \centering
    \caption{Comparison of orchestration frameworks for agentic AI systems}
    \label{tab:tech_comparison}
    \begin{tabular}{|l|c|c|c|}
        \hline
        \textbf{Criteria} & \textbf{LangGraph} & \textbf{AutoGen} & \textbf{CrewAI} \\
        \hline
        Cyclic reasoning & Yes & Limited & No \\
        \hline
    \end{tabular}
\end{table}
```

#### 6. Code Snippets
- Use `\begin{lstlisting}` for code snippets. Keep them SHORT (5–15 lines max). Only show the most critical logic, not boilerplate.
- Every code snippet must be preceded by a paragraph explaining what the code does and why it's important.
- Use syntax highlighting if available via the `listings` package.

#### 7. Technology Comparisons
- When introducing a technology in Chapter 3 (evolutionary iterations), include a **comparison table** with 2–3 alternatives, justifying the choice.
- Each comparison must cite official sources.
- The comparison should be followed by a paragraph summarizing WHY the chosen technology was selected.

#### 8. Evolutionary Iterations (Chapter 3)
- Each iteration in Chapter 3 follows this strict pattern:
  1. **Introductory paragraph**: What problem this iteration solves (reference the limitation from the previous iteration).
  2. **Technologies introduced**: List with citations.
  3. **Architecture and implementation**: Detailed explanation with code snippets for core logic.
  4. **Architecture diagram**: A `\begin{figure}` showing the system's state at this iteration.
  5. **Observed limitation**: A paragraph explaining what's still missing, which motivates the next iteration.
- The last iteration (Iteration 6) does NOT have an "Observed Limitation" section — it is the final system.

#### 9. General Writing Style
- Write in **third person** and **past tense** for implementation descriptions (e.g., "The system was designed to...", "The service was configured to...").
- Write in **present tense** for describing how the system currently works (e.g., "The agent evaluates the context and selects...").
- Use **formal academic English**. No contractions (don't → do not), no slang, no first person ("I" or "we").
- Paragraphs should be 4–8 sentences. Avoid single-sentence paragraphs.
- Use `\textbf{}` for first mentions of key technical terms.
- Use `\textit{}` for emphasis.
- Use `\texttt{}` for inline code, file names, and technical identifiers (e.g., `\texttt{ConversationId}`, `\texttt{MailPollingService.cs}`).

#### 10. LaTeX Packages You Can Assume Are Available
```latex
\usepackage{graphicx}       % Images
\usepackage{float}           % [H] positioning
\usepackage{rotating}        % sidewaysfigure
\usepackage{hyperref}        % URLs and cross-references
\usepackage{listings}        % Code snippets
\usepackage{booktabs}        % Professional tables
\usepackage{array}           % Table formatting
\usepackage{caption}         % Caption customization
\usepackage{cite}            % Citations
```

---

### GENERATION INSTRUCTIONS

1. **Read the attached outline carefully.** Every bullet point, every subsection, every item must be expanded into full LaTeX content.
2. **Generate the complete LaTeX body** — from `\chapter{General introduction}` through `\chapter{General conclusion and perspectives}`, including the bibliography.
3. **Do NOT generate the preamble** (`\documentclass`, `\usepackage`, `\begin{document}`). Only generate the chapter/section content.
4. **For each chapter**, generate complete `\chapter{}`, `\section{}`, `\subsection{}`, `\subsubsection{}` with all content.
5. **Include placeholder figure references** everywhere an architecture diagram, screenshot, flowchart, or UML diagram would appear. Use descriptive file paths like `images yass/iteration4_agentic_rag.png`.
6. **Include comparison tables** in Chapter 3 for every major technology choice.
7. **Generate a complete bibliography** at the end with all cited sources.

### OUTLINE IS ATTACHED BELOW — EXPAND EVERY ITEM INTO FULL LATEX CONTENT.

## PROMPT END
