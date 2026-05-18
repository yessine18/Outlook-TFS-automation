import os
import sys
import json
import asyncio
import warnings
from dotenv import load_dotenv

from langchain_groq import ChatGroq
from langchain_core.tools import tool
from langgraph.prebuilt import create_react_agent
from langchain_core.messages import SystemMessage, HumanMessage

from mcp import ClientSession, StdioServerParameters
from mcp.client.stdio import stdio_client

from langchain_neo4j import Neo4jGraph, GraphCypherQAChain

# Suppress warnings
warnings.filterwarnings('ignore')

load_dotenv()

GROQ_API_KEY = os.environ.get("GROQ_API_KEY")
if not GROQ_API_KEY:
    raise ValueError("GROQ_API_KEY environment variable is required")

# Retrieve the sender's domain injected securely from the C# backend
# We enforce lowercase because Neo4j exact string matching is case-sensitive!
TARGET_DOMAIN = os.environ.get("SENDER_DOMAIN", "m365x62207154.onmicrosoft.com").lower()

NEO4J_URI = "bolt://172.26.193.129:7687"
NEO4J_USER = "neo4j"
NEO4J_PASSWORD = "secret123"

# 1. Tool: Internal Graph RAG (Neo4j)
@tool
def search_historical_graph_knowledge(query: str) -> str:
    """
    Search the internal company graph database (Neo4j) for historical tickets,
    past resolutions, and user/asset relationships. 
    Use this to find if similar issues were resolved in the past.
    """
    try:
        # Initialize graph connection
        graph = Neo4jGraph(url=NEO4J_URI, username=NEO4J_USER, password=NEO4J_PASSWORD)
        
        # We ditch GraphCypherQAChain and zero-shot Text2Cypher. 
        # Instead, we use a robust, hardcoded Cypher template that guarantees Row-Level Security!
        cypher_query = """
        MATCH (tic:Ticket)-[:BELONGS_TO]->(t:Tenant {domain: $domain})
        MATCH (tic)-[:RESOLVED_WITH]->(res:Resolution)
        // Fetch only 5 most recent resolutions to save LLM tokens!
        RETURN tic.title AS Issue, tic.category AS Category, res.description AS Solution
        LIMIT 5
        """
        
        # Execute the query securely parameterized with the SENDER_DOMAIN
        records = graph.query(cypher_query, params={"domain": TARGET_DOMAIN})
        
        if not records:
            return "No historical data found for this tenant."
            
        # Format the historical tickets into a readable string for the Agent LLM
        formatted_history = "Here are the most recent resolved tickets for this Tenant:\n"
        for idx, record in enumerate(records):
            formatted_history += f"{idx+1}. Issue: {record['Issue']} (Category: {record['Category']}) | Resolution: {record['Solution']}\n"
            
        return formatted_history
    except Exception as e:
        return f"Error searching graph database: {e}"


async def run_agent(detailed_description: str):
    # Path to the MCP server
    server_script = os.path.join(os.path.dirname(__file__), "..", "mcp_servers", "learn_catalog_mcp.py")
    server_params = StdioServerParameters(command=sys.executable, args=[server_script], env=os.environ.copy())

    # 1. Fetch Sender Identity and Asset Context from Neo4j Graph
    SENDER_EMAIL = os.environ.get("SENDER_EMAIL", "yessine@m365x62207154.onmicrosoft.com").strip().lower()
    user_context = "No graph metadata found for this user."
    try:
        graph = Neo4jGraph(url=NEO4J_URI, username=NEO4J_USER, password=NEO4J_PASSWORD)
        
        # Fetch sender's profile, department, and active corporate asset
        context_query = """
        MATCH (u:User)-[:WORKS_IN]->(d:Department)-[:PART_OF]->(t:Tenant)
        WHERE toLower(u.email) = $email
        OPTIONAL MATCH (u)-[:OWNS]->(a:Asset)
        OPTIONAL MATCH (tic:Ticket)-[:REPORTED_BY]->(u)
        RETURN u.name AS Name, d.name AS Dept, t.name AS TenantName, 
               a.tag AS AssetTag, a.os AS AssetOS, a.type AS AssetType,
               count(tic) AS PastTicketsCount
        LIMIT 1
        """
        records = graph.query(context_query, params={"email": SENDER_EMAIL})
        if records:
            rec = records[0]
            # Fetch global infrastructure for their tenant
            infra_query = """
            MATCH (i:Infrastructure)-[:HOSTED_BY]->(t:Tenant {domain: $domain})
            RETURN i.name AS Name, i.type AS Type
            """
            infra_records = graph.query(infra_query, params={"domain": TARGET_DOMAIN})
            infra_list = [f"{inf['Name']} ({inf['Type']})" for inf in infra_records]
            infra_str = ", ".join(infra_list) if infra_list else "None"
            
            user_context = (
                f"=== AI GRAPH RECONNAISSANCE ===\n"
                f"- Sender Identity: {rec['Name']} ({SENDER_EMAIL})\n"
                f"- Department: {rec['Dept']} | Organization: {rec['TenantName']}\n"
                f"- Device Context: {rec['AssetTag'] or 'N/A'} ({rec['AssetOS'] or 'N/A'} - {rec['AssetType'] or 'N/A'})\n"
                f"- Ticket History Risk: {rec['PastTicketsCount']} historical tickets submitted by this employee.\n"
                f"- Tenant Servers/Gateways: {infra_str}\n"
                f"================================"
            )
    except Exception as e:
        user_context = f"Error fetching graph context: {e}"

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
                    # Slice to max 2000 characters to prevent Groq API rate limits
                    full_text = "\n".join([c.text for c in result.content if hasattr(c, 'text')])
                    return full_text[:2000] + "\n...(Truncated for token limit)"
                except Exception as e:
                    return f"Error connecting to MS Learn Catalog: {e}"
            
            # 3. Tool: Official Microsoft Documentation (Live MCP Server)
            @tool
            async def search_official_microsoft_documentation(query: str) -> str:
                """
                Search the LIVE, official Microsoft Learn Documentation via the official Microsoft MCP Server.
                Use this when you need real-time, up-to-date documentation on Azure, Microsoft 365, or Entra ID.
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
                    # Microsoft Docs are HUGE. We must slice to 3000 characters max to prevent 17k+ token crashes!
                    full_text = "\n\n".join(results)
                    return full_text[:3000] + "\n...(Truncated for token limit)"
                except Exception as e:
                    return f"Error connecting to official Microsoft MCP: {e}"
            
            # Initialize LLM
            llm = ChatGroq(
                temperature=0.1,
                model_name="llama-3.3-70b-versatile",
                groq_api_key=GROQ_API_KEY
            )
            
            # Define tools
            tools = [search_historical_graph_knowledge, search_microsoft_training_catalog, search_official_microsoft_documentation]
            
            system_prompt = f"""You are a strict Agentic Graph RAG Orchestrator. 
CRITICAL RULE: You MUST NOT answer from your own pre-trained knowledge. You MUST call a tool to gather information.

Here is the verified graph context for this sender, fetched directly from Neo4j:
{user_context}

IMPORTANT INSTRUCTION FOR ALL RESOLUTIONS:
You MUST prepend this exact "=== AI GRAPH RECONNAISSANCE ===" block to the beginning of the "proposedSolution" field inside your JSON response! Do NOT modify or exclude the reconnaissance data. This ensures the IT engineer sees the user's laptop tag, OS version, department, ticket history, and tenant gateways on their Azure DevOps ticket!

- For technical troubleshooting using internal historical tickets, use `search_historical_graph_knowledge`.
- For live, real-time official Microsoft Documentation, use `search_official_microsoft_documentation`.
- For training/certification/exam questions, use `search_microsoft_training_catalog`.

If a tool returns an error, or if no results are found, you MUST set "hasSolution" to false and put the exact error message in the "proposedSolution".

STRICT DESTINATION ROUTING RULES:
1. If you used `search_historical_graph_knowledge`:
   - You MUST set "hasSolution": false (Because historical fixes are strictly for our internal IT Engineers).
   - You MUST write the historical resolution in "proposedSolution".
2. If you used `search_official_microsoft_documentation` or `search_microsoft_training_catalog`:
   - You MUST set "hasSolution": true (Because official MS documentation is safe to send to the client).
   - You MUST write polite step-by-step instructions in "proposedSolution".

Your final response MUST be ONLY a raw JSON object with this exact schema:
{{
  "hasSolution": true,
  "confidenceScore": 0.95,
  "proposedSolution": "Clear steps based STRICTLY on the tool's retrieved data.",
  "toolUsed": "Name of the tool you used",
  "referenceUrls": ["https://url1", "https://url2"]
}}"""
            
            agent_executor = create_react_agent(llm, tools)
            
            try:
                result = await agent_executor.ainvoke({
                    "messages": [
                        SystemMessage(content=system_prompt),
                        HumanMessage(content=f"USER PROBLEM:\n{detailed_description}")
                    ]
                })
                output = result["messages"][-1].content
                
                # Clean up JSON
                if output.startswith("```json"):
                    output = output[7:-3].strip()
                elif output.startswith("```"):
                    output = output[3:-3].strip()
                    
                print(output)
            except Exception as e:
                fallback = {
                    "hasSolution": False,
                    "confidenceScore": 0.0,
                    "proposedSolution": f"Agent Execution Error: {e}",
                    "toolUsed": "None",
                    "referenceUrls": []
                }
                print(json.dumps(fallback))

if __name__ == "__main__":
    if "--stdin" in sys.argv:
        description = sys.stdin.read().strip()
    elif len(sys.argv) > 1:
        description = sys.argv[1]
    else:
        description = "I need to configure Azure Active Directory for our new app."
        
    asyncio.run(run_agent(description))
