import os
import json
import time
import hashlib
import threading
from urllib.robotparser import RobotFileParser
from urllib.parse import urlparse
import requests
from concurrent.futures import ThreadPoolExecutor, as_completed

# --- Settings ---
MAX_WORKERS = 5
RATE_LIMIT_DELAY = 1.0  # seconds between requests globally (1 req/sec)
TIMEOUT = 30
MAX_RETRIES = 4 # (Total 5 attempts)
USER_AGENT = 'InetumKB-Bot/1.0 (+https://github.com)'

# Global rate limit lock
last_request_time = 0
request_lock = threading.Lock()

def wait_for_rate_limit():
    global last_request_time
    with request_lock:
        now = time.time()
        elapsed = now - last_request_time
        if elapsed < RATE_LIMIT_DELAY:
            time.sleep(RATE_LIMIT_DELAY - elapsed)
        last_request_time = time.time()

def crawl_url(url_obj, raw_dir, rp):
    url = url_obj['url']
    source = url_obj.get('source', 'unknown')
    
    # 1. Hashes and Paths
    url_hash = hashlib.sha1(url.encode('utf-8')).hexdigest()
    html_path = os.path.join(raw_dir, f"{url_hash}.html")
    meta_path = os.path.join(raw_dir, f"{url_hash}.json")
    
    # 2. Check Cache
    if os.path.exists(html_path) and os.path.exists(meta_path):
        return f"Skipped (cached): {url}"
        
    # 3. Check robots.txt
    if not rp.can_fetch(USER_AGENT, url):
        return f"Skipped (robots.txt forbidden): {url}"
        
    headers = {'User-Agent': USER_AGENT}
    
    for attempt in range(MAX_RETRIES + 1):
        wait_for_rate_limit()
        
        try:
            response = requests.get(url, headers=headers, timeout=TIMEOUT)
            
            # Retry on 429 or 5xx
            if response.status_code == 429 or response.status_code >= 500:
                backoff = 2 ** attempt
                print(f"[Status {response.status_code}] Retrying {url} in {backoff}s...")
                time.sleep(backoff)
                continue
                
            response.raise_for_status()
            
            # 4. Save raw HTML
            with open(html_path, 'w', encoding='utf-8') as f:
                f.write(response.text)
                
            # 5. Save metadata next to it
            metadata = {
                'url': url,
                'source': source,
                'status_code': response.status_code,
                'fetched_at': time.time()
            }
            with open(meta_path, 'w', encoding='utf-8') as f:
                json.dump(metadata, f, indent=2)
                
            return f"Success: {url}"
            
        except requests.exceptions.RequestException as e:
            backoff = 2 ** attempt
            print(f"[Error: {e}] Retrying {url} in {backoff}s...")
            time.sleep(backoff)
            
    return f"Failed after {MAX_RETRIES + 1} attempts: {url}"

def main():
    base_dir = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
    data_dir = os.path.join(base_dir, 'data')
    raw_dir = os.path.join(data_dir, 'raw')
    os.makedirs(raw_dir, exist_ok=True)
    
    url_list_path = os.path.join(data_dir, 'url_list.jsonl')
    
    if not os.path.exists(url_list_path):
        print(f"URL list not found at {url_list_path}. Run fetch_sitemaps.py first.")
        return
        
    urls_to_crawl = []
    with open(url_list_path, 'r', encoding='utf-8') as f:
        for line in f:
            if line.strip():
                urls_to_crawl.append(json.loads(line))
                
    print(f"Loaded {len(urls_to_crawl)} URLs to fetch.")
    
    # Initialize robots.txt parser for microsoft
    # Ideally this would be dynamic per hostname, but we know it's for ms-learn
    print("Fetching and parsing robots.txt for learn.microsoft.com...")
    rp = RobotFileParser()
    rp.set_url('https://learn.microsoft.com/robots.txt')
    try:
        rp.read()
    except Exception as e:
        print(f"Warning: Failed to fetch robots.txt ({e}). Proceeding carefully.")
        
    print(f"Starting crawler with {MAX_WORKERS} workers and 1 req/sec rate limit...")
    
    # Run threadpool
    with ThreadPoolExecutor(max_workers=MAX_WORKERS) as executor:
        futures = {executor.submit(crawl_url, url_obj, raw_dir, rp): url_obj for url_obj in urls_to_crawl}
        
        for future in as_completed(futures):
            try:
                result = future.result()
                print(result)
            except Exception as e:
                print(f"Worker crashed: {e}")

if __name__ == '__main__':
    main()
