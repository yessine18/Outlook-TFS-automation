import os
from langchain_neo4j import Neo4jGraph
from dotenv import load_dotenv

load_dotenv()

NEO4J_URI = "bolt://172.26.193.129:7687"
NEO4J_USER = "neo4j"
NEO4J_PASSWORD = "secret123"

try:
    print("Testing Neo4jGraph with enhanced_schema=False")
    graph = Neo4jGraph(url=NEO4J_URI, username=NEO4J_USER, password=NEO4J_PASSWORD, enhanced_schema=False)
    print("Schema:", graph.get_schema)
    print("Success!")
except Exception as e:
    print("Error:", e)
