# PFE Automation Pipeline: Helpdesk Email to Azure DevOps

This project is a .NET Worker Service that automates the process of reading support emails, analyzing their content using Large Language Models (LLMs), routing them to the correct department, and automatically creating Work Items in Azure DevOps.

---

## 🏗️ Project Status (Kanban Board Overview)

To keep track of the development lifecycle, here is the current status of tasks, features, and future enhancements.

### ✅ What We've Done Already (Completed)
* **Email Interception:** Implemented a worker service to continuously poll and read incoming emails using Microsoft Graph / Azure AD authentication.
* **LLM Integration:** Integrated Groq LLM (llama-3.3-70b-versatile) to parse email content, extract relevant data, and determine the context.
* **Intelligent Routing:** Configured dynamic routing to match extracted issues to specific departments and assignees based on both keyword matching and a `.csv` mapping file.
* **Azure DevOps Integration:** Automated the creation of Work Items in Azure DevOps containing the formatted issue details and assigning them to the correct user.
* **Email Responses:** Implemented auto-reply and assignee notification templates using HTML.

### 📌 What Are The Next Steps (To Do)
* **RAG Implementation (Retrieval-Augmented Generation):** Develop a system to answer client emails directly using a Vector Database holding company data, and potentially web-scrape Microsoft documentation for technical answers.
* **End-to-End State Tracking Database:** Add a relational database (e.g., PostgreSQL) to track the entire lifecycle of an issue—from the moment the email is received to its completion in Azure DevOps. This will track workflow states (e.g., *Email Read*, *LLM Processing*, *LLM Error*, *ADO Created*) to ensure no issue is lost if a pipeline error occurs.
* **CI/CD Pipeline:** Integrate an automated CI/CD pipeline in Azure DevOps to build, test, and deploy the worker service automatically.

### 🔄 In Progress (Currently Working On)
* **Microsoft Teams Integration:** Automate sending a Microsoft Teams message to the specific person assigned to the newly created Azure DevOps Work Item.
* **Knowledge Base Collection:** Gathering and formatting internal company data to initialize and populate the Vector Database for the upcoming RAG feature.
* **Relational Database Configuration:** Setting up the Entity Framework Core DbContext, models, and migrations for the new SQL/PostgreSQL state-tracking database.

### 🔍 Under Review (Testing / QA)
* **TFS Form Backend Integration:** Testing the "Create TFS Work Item" HTML form to ensure data passes correctly and securely from the front-end to the C# backend.
* **Outlook Task Pane UI:** Reviewing the UI responsiveness of the HTML task pane across different Outlook window sizes and desktop/web variations.

### 📦 Backlog (Future Enhancements)
* **Enhanced Error Routing:** Improve the routing fallback mechanism so that if any unexpected error occurs inside the workflow (e.g., LLM timeout, ADO API failure), the issue is safely forwarded to a general tiered support inbox.
* **Advanced Email Processing:** Improve the handling of complex email threads, inline images, and large attachments.
* **Template Overhaul:** Refine and modernize the design of outgoing HTML email templates (auto-replies and internal notifications).

---

## 🚀 Getting Started

*(You can add instructions here for your team on how to clone, set up `appsettings.json`, and run the worker service locally.)*
