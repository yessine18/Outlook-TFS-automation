import os
import json
import glob
import psycopg2
from pgvector.psycopg2 import register_vector
from sentence_transformers import SentenceTransformer

# Database configuration
DB_CONN_STR = "host=localhost port=5432 dbname=helpdesk_pipeline user=postgres password=secret"

def chunk_text(text, max_words=500):
    """Splits a long text into chunks of at most `max_words`."""
    words = text.split()
    chunks = []
    current_chunk = []
    current_word_count = 0
    
    for word in words:
        current_chunk.append(word)
        current_word_count += 1
        if current_word_count >= max_words:
            chunks.append(" ".join(current_chunk))
            current_chunk = []
            current_word_count = 0
            
    if current_chunk:
        chunks.append(" ".join(current_chunk))
        
    return chunks

def setup_database(conn):
    """Ensures the pgvector extension and table exist."""
    print("Setting up database...")
    with conn.cursor() as cur:
        cur.execute("CREATE EXTENSION IF NOT EXISTS vector;")
        
        # Reset table to fix dimensions
        cur.execute("DROP TABLE IF EXISTS microsoft_docs;")
        
        # Create table for Microsoft Docs
        # sentence-transformers/all-MiniLM-L6-v2 outputs 384 dimensions
        cur.execute("""
            CREATE TABLE IF NOT EXISTS microsoft_docs (
                id SERIAL PRIMARY KEY,
                document_url TEXT,
                document_title TEXT,
                product TEXT,
                chunk_text TEXT,
                embedding vector(384)
            );
        """)
        conn.commit()

def main():
    print("Loading Local Embedding Model (all-MiniLM-L6-v2)...")
    # This will download the small 80MB model on the first run, and then use your CPU to embed 100% for free.
    model = SentenceTransformer('all-MiniLM-L6-v2')
    
    base_dir = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
    processed_dir = os.path.join(base_dir, 'data', 'processed')
    json_files = glob.glob(os.path.join(processed_dir, "*.json"))
    
    print(f"Found {len(json_files)} processed documents to vectorize.")
    
    try:
        conn = psycopg2.connect(DB_CONN_STR)
        setup_database(conn)
        
        # Register the vector type with psycopg2
        register_vector(conn)
        print("Successfully connected to PostgreSQL and initialized pgvector!")
        
    except Exception as e:
        print(f"Failed to connect to database: {e}")
        return

    processed_count = 0
    total_files = len(json_files)
    
    for fp in json_files:
        with open(fp, 'r', encoding='utf-8') as f:
            doc = json.load(f)
            
        url = doc.get("url", "")
        title = doc.get("title", "")
        text = doc.get("text", "")
        product = doc.get("product", "")
        
        if not text:
            continue
            
        chunks = chunk_text(text, max_words=500)
        
        # Filter chunks that are too small before sending
        valid_chunks = [ch for ch in chunks if len(ch.split()) >= 20]
        if not valid_chunks:
            continue
            
        with conn.cursor() as cur:
            try:
                # Batch embed all valid chunks using our local model
                embeddings = model.encode(valid_chunks)
                
                for i, chunk in enumerate(valid_chunks):
                    emb = [float(val) for val in embeddings[i]]
                    cur.execute(
                        "INSERT INTO microsoft_docs (document_url, document_title, product, chunk_text, embedding) VALUES (%s, %s, %s, %s, %s)",
                        (url, title, product, chunk, emb)
                    )
            except Exception as e:
                print(f"Failed to process document {url}: {e}")
                conn.rollback() # Reset the transaction so future loops can continue
        conn.commit()
        
        processed_count += 1
        if processed_count % 100 == 0:
            print(f"Vectorized {processed_count}/{total_files} files...")
            
    conn.close()
    print("Finished vectorizing all files!")

if __name__ == "__main__":
    main()
