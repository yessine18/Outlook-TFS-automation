import os
import re
import yaml
import json
import gzip
import io
import requests

def get_xml(url):
    """Fetches XML content, handling unzipping if necessary."""
    headers = {
        'User-Agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36'
    }
    response = requests.get(url, headers=headers, timeout=30)
    response.raise_for_status()
    
    # The 'requests' library transparently decompresses 'Content-Encoding: gzip'.
    # So if the server correctly flags it, response.text is already unzipped!
    try:
        # We manually try decompression just in case the server sent a raw .gz file 
        # without headers (e.g. application/octet-stream)
        with gzip.open(io.BytesIO(response.content), 'rt', encoding='utf-8') as f:
            return f.read()
    except OSError:
        # Not a gzipped file (meaning it's plain text or Requests already unzipped it for us)
        return response.text

def extract_locs(xml_content):
    """Extracts all <loc> text from an XML string."""
    return re.findall(r'<loc>\s*(.*?)\s*</loc>', xml_content, re.IGNORECASE)

def is_matching(url, includes, excludes):
    """Filters URLs based on include/exclude substring patterns."""
    if includes and not any(inc in url for inc in includes):
        return False
    if excludes and any(exc in url for exc in excludes):
        return False
    return True

def main():
    # Base dir is .venv based on the described folder structure
    # (__file__ = src/ingest/fetch_sitemaps.py)
    base_dir = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
    config_path = os.path.join(base_dir, 'config', 'sources.yaml')
    data_dir = os.path.join(base_dir, 'data')
    
    os.makedirs(data_dir, exist_ok=True)
    txt_path = os.path.join(data_dir, 'url_list.txt')
    jsonl_path = os.path.join(data_dir, 'url_list.jsonl')
    
    try:
        with open(config_path, 'r', encoding='utf-8') as f:
            config = yaml.safe_load(f)
    except Exception as e:
        print(f"Error reading config: {e}")
        return
        
    all_urls = set()
    results = []
    
    for source in config.get('sources', []):
        source_name = source.get('name', 'unknown')
        index_urls = source.get('sitemap_index_urls', [])
        includes = source.get('include_patterns', [])
        excludes = source.get('exclude_patterns', [])
        max_urls = source.get('max_urls', float('inf'))
        
        print(f"Processing source: {source_name}")
        source_count = 0
        
        for index_url in index_urls:
            print(f"  Fetching sitemap index: {index_url}")
            try:
                index_xml = get_xml(index_url)
            except Exception as e:
                print(f"  [ERROR] Failed to fetch index {index_url}: {e}")
                continue
                
            child_sitemaps = extract_locs(index_xml)
            print(f"  Found {len(child_sitemaps)} child sitemaps.")
            
            for child_sitemap in child_sitemaps:
                if source_count >= max_urls:
                    print(f"  [INFO] Reached max_urls limit ({max_urls}) for source {source_name}")
                    break
                    
                print(f"    Fetching sitemap: {child_sitemap}")
                try:
                    sitemap_xml = get_xml(child_sitemap)
                except Exception as e:
                    print(f"    [ERROR] Failed to fetch sitemap {child_sitemap}: {e}")
                    continue
                    
                page_urls = extract_locs(sitemap_xml)
                
                for page_url in page_urls:
                    if source_count >= max_urls:
                        break
                        
                    # Deduplicate global URLs
                    if page_url in all_urls:
                        continue
                        
                    # Filter URL
                    if is_matching(page_url, includes, excludes):
                        all_urls.add(page_url)
                        results.append({
                            "url": page_url,
                            "source": source_name
                        })
                        source_count += 1
                        
        print(f"Finished {source_name}. Total URLs gathered from this source: {source_count}\n")
        
    print(f"Saving {len(results)} deduplicated URLs to data folder...")
    with open(txt_path, 'w', encoding='utf-8') as f_txt:
        for r in results:
            f_txt.write(r['url'] + '\n')
            
    with open(jsonl_path, 'w', encoding='utf-8') as f_jsonl:
        for r in results:
            f_jsonl.write(json.dumps(r) + '\n')
            
    print("Done!")

if __name__ == '__main__':
    main()
