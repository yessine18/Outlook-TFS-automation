import os
import sys
import json
import asyncio
import warnings
from dotenv import load_dotenv

import psycopg2
from sentence_transformers import SentenceTransformer

from langchain_groq import ChatGroq
from langchain_core.tools import tool
from langgraph.prebuilt import create_react_agent
from langchain_core.messages import SystemMessage, HumanMessage

from mcp import ClientSession, StdioServerParameters
from mcp.client.stdio import stdio_client

# Suppress sentence-transformers warnings
warnings.filterwarnings('ignore')
os.environ["TOKENIZERS_PARALLELISM"] = "false"

load_dotenv()

GROQ_API_KEY = os.environ.get("GROQ_API_KEY")
if not GROQ_API_KEY:
    raise ValueError("GROQ_API_KEY environment variable is required")

DB_HOST = os.environ.get("DB_HOST", "localhost")
DB_PORT = os.environ.get("DB_PORT", "5432")
DB_NAME = os.environ.get("DB_NAME", "helpdesk_pipeline")
DB_USER = os.environ.get("DB_USER", "postgres")
DB_PASS = os.environ.get("DB_PASS", "secret")

# 1. Tool: Internal KB Search (PGVector)
@tool
def search_internal_knowledge_base(query: str) -> str:
    """
    Search the internal company knowledge base (PostgreSQL) for internal policies,
    past tickets, and specific company documentation. Use this to find solutions to technical problems.
    """
    try:
        model = SentenceTransformer('all-MiniLM-L6-v2')
        embedding = model.encode(query)
        vector_str = json.dumps([float(x) for x in embedding])
        
        conn = psycopg2.connect(
            host=DB_HOST, port=DB_PORT, database=DB_NAME, user=DB_USER, password=DB_PASS
        )
        cursor = conn.cursor()
        
        sql = """
            SELECT document_url, document_title, chunk_text
            FROM microsoft_docs 
            ORDER BY embedding <=> %s::vector 
            LIMIT 3;
        """
        cursor.execute(sql, (vector_str,))
        rows = cursor.fetchall()
        
        if not rows:
            return "No internal documentation found for this query."
            
        results = []
        for row in rows:
            results.append(f"Title: {row[1]}\nURL: {row[0]}\nContent: {row[2]}")
            
        return "\n\n".join(results)
    except Exception as e:
        return f"Error searching internal database: {e}"


async def run_agent(detailed_description: str):
    # Path to the MCP server
    server_script = os.path.join(os.path.dirname(__file__), "..", "mcp_servers", "learn_catalog_mcp.py")
    server_params = StdioServerParameters(command=sys.executable, args=[server_script], env=os.environ.copy())

    # Establish stdio connection to the Catalog MCP Server
    async with stdio_client(server_params) as (read, write):
        async with ClientSession(read, write) as session:
            await session.initialize()
            
            # 2. Tool: Microsoft Learn Catalog (MCP)
            @tool
            async def search_microsoft_training_catalog(query: str, content_type: str = "modules", limit: int = 5) -> str:
                """
                Search the official Microsoft Learn Catalog for training modules, learning paths, certifications, and exams.
                Use this when the user explicitly asks about official Microsoft training, learning, or exams.
                """
                try:
                    result = await session.call_tool("search_microsoft_training_catalog", arguments={
                        "query": query, "content_type": content_type, "limit": limit
                    })
                    # Combine all text outputs from the MCP tool
                    return "\n".join([c.text for c in result.content if hasattr(c, 'text')])
                except Exception as e:
                    return f"Error connecting to MS Learn Catalog: {e}"
            
            # 3. Tool: Official Microsoft Documentation (Live MCP Server)
            @tool
            async def search_official_microsoft_documentation(query: str) -> str:
                """
                Search the LIVE, official Microsoft Learn Documentation via the official Microsoft MCP Server.
                Use this when you need real-time, up-to-date documentation on Azure, Microsoft 365, or Entra ID,
                and the internal knowledge base does not contain the answer.
                """
                url = "https://learn.microsoft.com/api/mcp"
                headers = {"Content-Type": "application/json"}
                payload = {
                    "jsonrpc": "2.0",
                    "id": 1,
                    "method": "tools/call",
                    "params": {
                        "name": "microsoft_docs_search",
                        "arguments": { "query": query }
                    }
                }
                
                try:
                    import httpx
                    import json
                    results = []
                    async with httpx.AsyncClient() as client:
                        async with client.stream('POST', url, json=payload, headers=headers) as response:
                            async for line in response.aiter_lines():
                                if line.startswith("data: "):
                                    data_str = line[6:]
                                    try:
                                        data_json = json.loads(data_str)
                                        if "result" in data_json and "content" in data_json["result"]:
                                            content = data_json["result"]["content"]
                                            for c in content:
                                                if 'text' in c:
                                                    results.append(c['text'])
                                    except json.JSONDecodeError:
                                        pass
                    if not results:
                        return "No live documentation found on Microsoft Learn."
                    return "\n\n".join(results)
                except Exception as e:
                    return f"Error connecting to official Microsoft MCP: {e}"
            
            # Initialize LLM
            llm = ChatGroq(
                temperature=0.1,
                model_name="llama-3.3-70b-versatile",
                groq_api_key=GROQ_API_KEY
            )
            
            # Define tools
            tools = [search_internal_knowledge_base, search_microsoft_training_catalog, search_official_microsoft_documentation]
            
            system_prompt = """You are a strict Agentic RAG Orchestrator. 
CRITICAL RULE: You MUST NOT answer from your own pre-trained knowledge. You MUST call a tool to gather information.
- For technical troubleshooting using internal offline data, use `search_internal_knowledge_base`.
- For live, real-time official Microsoft Documentation, use `search_official_microsoft_documentation`.
- For training/certification/exam questions, use `search_microsoft_training_catalog`.

If a tool returns an error (e.g., "Error connecting...", "Missing credentials"), or if no results are found, you MUST set "hasSolution" to false and put the exact error message in the "proposedSolution". Do NOT invent or hallucinate an answer.

Once you have gathered the necessary information from the tools, your final response MUST be ONLY a raw JSON object with this exact schema (no markdown formatting or backticks):
{
  "hasSolution": true,
  "confidenceScore": 0.95,
  "proposedSolution": "Clear steps based STRICTLY on the tool's retrieved data. If tools failed or returned nothing, explain why.",
  "toolUsed": "Name of the tool you used",
  "referenceUrls": ["https://url1", "https://url2"]
}"""
            
            agent_executor = create_react_agent(llm, tools)
            
            try:
                result = await agent_executor.ainvoke({
                    "messages": [
                        SystemMessage(content=system_prompt),
                        HumanMessage(content=f"USER PROBLEM:\n{detailed_description}")
                    ]
                })
                output = result["messages"][-1].content
                
                # Clean up JSON if LLM included markdown
                if output.startswith("```json"):
                    output = output[7:-3].strip()
                elif output.startswith("```"):
                    output = output[3:-3].strip()
                    
                print(output)
            except Exception as e:
                # Fallback JSON for the C# backend
                fallback = {
                    "hasSolution": False,
                    "confidenceScore": 0.0,
                    "proposedSolution": f"Agent Execution Error: {e}",
                    "toolUsed": "None",
                    "referenceUrls": []
                }
                print(json.dumps(fallback))

if __name__ == "__main__":
    if len(sys.argv) > 1:
        description = sys.argv[1]
    else:
        description = "I need to configure Azure Active Directory for our new app, and are there any official Microsoft exams for it?"
        
    asyncio.run(run_agent(description))
