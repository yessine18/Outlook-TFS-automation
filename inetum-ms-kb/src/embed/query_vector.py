import sys
import json
import warnings
import os

# Suppress all HuggingFace and SentenceTransformer warnings 
# so they don't pollute the standard output which C# needs to parse.
warnings.filterwarnings('ignore')
os.environ["TOKENIZERS_PARALLELISM"] = "false"

try:
    # Import after suppressing warnings
    import logging
    logging.getLogger("sentence_transformers").setLevel(logging.ERROR)
    from sentence_transformers import SentenceTransformer
    
    if len(sys.argv) < 2:
        print(json.dumps({"error": "No input text provided"}))
        sys.exit(1)
        
    text = sys.argv[1]
    
    # This automatically loads from cache if previously downloaded
    model = SentenceTransformer('all-MiniLM-L6-v2')
    embedding = model.encode(text)
    
    # Print strictly valid JSON containing the array of 384 floats
    print(json.dumps([float(x) for x in embedding]))
    sys.exit(0)
except Exception as e:
    # If something fails, return a JSON error so C# can catch it gracefully
    print(json.dumps({"error": str(e)}))
    sys.exit(1)
