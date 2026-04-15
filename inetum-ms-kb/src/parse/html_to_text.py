import os
import json
import glob
import trafilatura

def main():
    base_dir = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
    raw_dir = os.path.join(base_dir, 'data', 'raw')
    processed_dir = os.path.join(base_dir, 'data', 'processed')
    
    os.makedirs(processed_dir, exist_ok=True)
    
    meta_files = glob.glob(os.path.join(raw_dir, "*.json"))
    print(f"Found {len(meta_files)} cached raw pages to process.")
    
    processed_count = 0
    
    for meta_path in meta_files:
        # Resolve filenames based on the hash
        hash_val = os.path.basename(meta_path).replace('.json', '')
        html_path = os.path.join(raw_dir, f"{hash_val}.html")
        processed_path = os.path.join(processed_dir, f"{hash_val}.json")
        
        # Check cache skip
        if os.path.exists(processed_path):
            continue
            
        if not os.path.exists(html_path):
            print(f"Missing HTML file for {meta_path}, skipping.")
            continue
            
        # Load metadata
        with open(meta_path, 'r', encoding='utf-8') as f:
            try:
                metadata = json.load(f)
            except json.JSONDecodeError:
                print(f"Invalid JSON metadata: {meta_path}")
                continue
                
        url = metadata.get('url', '')
        source = metadata.get('source', '')
        
        # Load HTML
        with open(html_path, 'r', encoding='utf-8') as f:
            html_content = f.read()
            
        # Extract content using trafilatura
        # bare_extraction returns a dict with title, text, language, etc.
        extracted_data = trafilatura.bare_extraction(
            html_content, 
            url=url,
            include_links=True,
            include_formatting=True
        )
        
        # Trafilatura 2.0+ returns a Document object instead of a dict.
        extracted_dict = extracted_data.as_dict() if hasattr(extracted_data, 'as_dict') else extracted_data
        
        if not extracted_dict or not extracted_dict.get('text'):
            print(f"Failed to extract substantial content from {url}")
            continue
            
        title = extracted_dict.get('title', '') or ''
        text = extracted_dict.get('text', '') or ''
        language = extracted_dict.get('language', '') or ''
        
        # Derive product from URL (e.g., https://learn.microsoft.com/en-us/dynamics365/...)
        product = "unknown"
        url_parts = url.replace("https://", "").replace("http://", "").split('/')
        if len(url_parts) >= 3:
            # url_parts[0] = learn.microsoft.com
            # url_parts[1] = locale (e.g., en-us)
            # url_parts[2] = product (e.g., dynamics365)
            product = url_parts[2]
            
        processed_data = {
            'url': url,
            'title': title,
            'text': text,
            'source': source,
            'language': language,
            'product': product
        }
        
        with open(processed_path, 'w', encoding='utf-8') as f:
            json.dump(processed_data, f, ensure_ascii=False, indent=2)
            
        print(f"Extracted: {product} | {title[:50]}...")
        processed_count += 1
        
    print(f"\nDone! Successfully extracted text from {processed_count} new HTML pages.")
    print(f"Processed files saved in: {processed_dir}")

if __name__ == '__main__':
    main()
