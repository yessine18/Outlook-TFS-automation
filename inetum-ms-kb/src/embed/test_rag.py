import os
import json
import requests
import psycopg2
from sentence_transformers import SentenceTransformer

# Setup constants
GROQ_API_KEY = os.environ.get("GROQ_API_KEY", "YOUR_GROQ_API_KEY_HERE")
DB_CONN_STR = "host=localhost port=5432 dbname=helpdesk_pipeline user=postgres password=secret"

# ---------------------------------------------------------
# Step 0: The Mock Income Email
# ---------------------------------------------------------
email_subject = "Help: How to set up Zone threshold replenishment in D365"
email_body = """
Hi Support,

I am working with Dynamics 365 Supply Chain Management and we want to enable 
Zone threshold replenishment in our warehouse to automate restocking. 

Could you please give me the exact step-by-step instructions on what components 
I need to configure in the system to make this work? Specifically, how do I link 
the replenishment templates to the location directives?

Thanks!
"""

def extract_detailed_description():
    print("--- [STEP 1: LLM Extraction] ---")
    print("Sending raw email to Groq (Llama-3-70b) to extract the Detailed Description...")
    
    prompt = f"""
    Analyze this support email and extract a `detailedDescription` string.
    The description MUST preserve ALL specific facts, numbers, names, dates, error messages, and technical details.
    
    Email Subject: {email_subject}
    Email Body: {email_body}
    
    Respond STRICTLY with a valid JSON object:
    {{"detailedDescription": "..."}}
    """
    
    response = requests.post(
        "https://api.groq.com/openai/v1/chat/completions",
        headers={"Authorization": f"Bearer {GROQ_API_KEY}"},
        json={
            "model": "llama-3.3-70b-versatile",
            "messages": [{"role": "user", "content": prompt}],
            "temperature": 0.2,
            "response_format": {"type": "json_object"}
        }
    )
    
    result = response.json()["choices"][0]["message"]["content"]
    data = json.loads(result)
    print(f"Extracted Description: {data['detailedDescription']}\n")
    return data['detailedDescription']

def vectorize_description(text):
    print("--- [STEP 2: Local Vectorization] ---")
    print("Loading SentenceTransformer (all-MiniLM-L6-v2) to calculate vector...")
    model = SentenceTransformer('all-MiniLM-L6-v2')
    embedding = model.encode(text)
    print(f"Generated Vector Array: [{embedding[0]:.4f}, {embedding[1]:.4f}, ... ] (Length: {len(embedding)})\n")
    return embedding.tolist()

def search_vector_db(embedding):
    print("--- [STEP 3: PostgreSQL pgvector Search] ---")
    print("Searching the `microsoft_docs` database for the top 3 most similar chunks...")
    
    conn = psycopg2.connect(DB_CONN_STR)
    cur = conn.cursor()
    
    # We serialize the python list to a string format PostgreSQL understands as a vector array
    emb_str = f"[{','.join(str(x) for x in embedding)}]"
    
    # Use Cosine Similarity (1 - distance) matching
    query = """
    SELECT document_url, document_title, chunk_text, 1 - (embedding <=> %s::vector) AS similarity 
    FROM microsoft_docs 
    ORDER BY embedding <=> %s::vector 
    LIMIT 3;
    """
    cur.execute(query, (emb_str, emb_str))
    results = cur.fetchall()
    
    formatted_docs = []
    for row in results:
        url, title, chunk, similarity = row
        print(f"Match Similarity: {similarity*100:.2f}% | Title: {title}")
        formatted_docs.append({
            "title": title,
            "url": url,
            "chunk_text": chunk
        })
        
    conn.close()
    print("\n")
    return formatted_docs

def evaluate_rag_verdict(description, docs):
    print("--- [STEP 4: RAG Verdict (Auto-Resolve AI)] ---")
    print("Sending the Problem + Retrieved Docs to Groq for final Verdict...")
    
    docs_json = json.dumps(docs, indent=2)
    
    prompt = f"""
    You are a Level-3 AI Helpdesk Agent.
    
    USER'S PROBLEM DESCRIPTION:
    {description}
    
    OFFICIAL MICROSOFT MICROSOFT KNOWLEDGE BASE:
    {docs_json}
    
    Analyze the Problem against the Knowledge Base chunks.
    Does the Knowledge base provide a verified, explicit solution to the problem?
    
    Respond in STRICT JSON FORMAT:
    {{
      "hasSolution": true/false,
      "confidenceScore": float <0.0-1.0>,
      "proposedSolution": "Clear steps or explanation to solve the user's issue based strictly on the docs",
      "referenceUrls": ["url1", "url2"]
    }}
    """
    
    response = requests.post(
        "https://api.groq.com/openai/v1/chat/completions",
        headers={"Authorization": f"Bearer {GROQ_API_KEY}"},
        json={
            "model": "llama-3.3-70b-versatile",
            "messages": [{"role": "user", "content": prompt}],
            "temperature": 0.1,
            "response_format": {"type": "json_object"}
        }
    )
    
    result = response.json()["choices"][0]["message"]["content"]
    data = json.loads(result)
    
    print("\n================ [ FINAL NLP VERDICT ] ================\n")
    print(json.dumps(data, indent=4))

def main():
    try:
        desc = extract_detailed_description()
        emb = vectorize_description(desc)
        docs = search_vector_db(emb)
        evaluate_rag_verdict(desc, docs)
    except Exception as e:
        print(f"Error occurred during test: {e}")

if __name__ == "__main__":
    main()
