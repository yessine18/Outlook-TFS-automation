from typing import Any
import httpx
import os
import msal
from mcp.server.fastmcp import FastMCP
from dotenv import load_dotenv

load_dotenv()

# Entra ID credentials
TENANT_ID = os.environ.get("ENTRA_TENANT_ID")
CLIENT_ID = os.environ.get("ENTRA_CLIENT_ID")
CLIENT_SECRET = os.environ.get("ENTRA_CLIENT_SECRET")

# Microsoft Identity platform authority
AUTHORITY = f"https://login.microsoftonline.com/{TENANT_ID}"
# The specific scope required for Microsoft Learn Platform API App-Only access
SCOPE = ["https://learn.microsoft.com/.default"]

# Initialize FastMCP server
mcp = FastMCP("Microsoft Learn Catalog MCP Server")

async def get_access_token() -> str:
    """Authenticates with Entra ID to fetch an App-Only access token."""
    if not all([TENANT_ID, CLIENT_ID, CLIENT_SECRET]):
        raise ValueError("Missing Entra ID environment variables (ENTRA_TENANT_ID, ENTRA_CLIENT_ID, ENTRA_CLIENT_SECRET)")

    app = msal.ConfidentialClientApplication(
        CLIENT_ID, authority=AUTHORITY, client_credential=CLIENT_SECRET
    )
    
    # Try silent cache first
    result = app.acquire_token_silent(SCOPE, account=None)
    if not result:
        # Perform actual HTTP call to Entra ID
        result = app.acquire_token_for_client(scopes=SCOPE)
    
    if "access_token" in result:
        return result["access_token"]
    else:
        raise Exception(f"Could not get access token: {result.get('error_description', result.get('error'))}")

@mcp.tool()
async def search_microsoft_training_catalog(query: str = "", content_type: str = "modules", limit: int = 5) -> dict[str, Any]:
    """
    Search the official Microsoft Learn Catalog for training modules, learning paths, and certifications/exams.
    
    Args:
        query: Optional search term to filter results (e.g., 'Azure Active Directory', 'C#').
        content_type: The type of content to search for. Valid options: 'modules', 'learningPaths', 'certifications', 'exams'.
        limit: Maximum number of results to return (max 20) to keep the context window small.
    """
    try:
        # Ensure limit is reasonable
        limit = min(max(limit, 1), 20)
        
        token = await get_access_token()
        headers = {
            "Authorization": f"Bearer {token}",
            "Accept": "application/json"
        }
        
        # The Catalog API base URL with the type parameter
        api_url = f"https://learn.microsoft.com/api/catalog/?type={content_type}"
        
        async with httpx.AsyncClient() as client:
            response = await client.get(api_url, headers=headers, timeout=30.0)
            response.raise_for_status()
            data = response.json()
            
        items = data.get(content_type, [])
        
        # The Microsoft Learn API returns the full catalog of that type.
        # We perform in-memory filtering based on the LLM's query.
        # We perform in-memory filtering based on the LLM's query using a keyword scoring system.
        if query:
            query_words = [w.lower() for w in query.split() if len(w) > 3]
            scored_items = []
            for item in items:
                title = item.get('title', '').lower()
                summary = item.get('summary', '').lower()
                text_to_search = title + " " + summary
                
                # Count how many keywords appear in the title/summary
                score = sum(1 for word in query_words if word in text_to_search)
                if score > 0:
                    scored_items.append((score, item))
            
            # Sort by highest score first
            scored_items.sort(key=lambda x: x[0], reverse=True)
            items = [item for score, item in scored_items]
            
        # Format the top N results to send back to the LLM
        results = []
        for item in items[:limit]:
            results.append({
                "title": item.get('title'),
                "url": item.get('url'),
                "summary": item.get('summary'),
                "levels": item.get('levels', []),
                "roles": item.get('roles', []),
                "products": item.get('products', [])
            })
            
        return {
            "status": "success",
            "content_type": content_type,
            "query": query,
            "total_matches_found": len(items),
            "results_returned": len(results),
            "results": results
        }
        
    except Exception as e:
        return {"status": "error", "message": str(e)}

if __name__ == "__main__":
    # Start the MCP server using standard input/output (stdio) which is the default for FastMCP
    mcp.run()
