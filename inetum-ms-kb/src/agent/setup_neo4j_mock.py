import os
import random
from faker import Faker
from neo4j import GraphDatabase

# The main testing domain you provided
TARGET_DOMAIN = "m365x62207154.onmicrosoft.com"

# Other fake tenants to prove data security!
OTHER_DOMAINS = ["contoso.com", "fabrikam.com"]
ALL_DOMAINS = [TARGET_DOMAIN] + OTHER_DOMAINS

# Default Neo4j Local Docker Credentials
NEO4J_URI = "bolt://172.26.193.129:7687"
NEO4J_USER = "neo4j"
NEO4J_PASSWORD = "secret123"

fake = Faker()

def seed_database():
    print("Connecting to Neo4j...")
    try:
        driver = GraphDatabase.driver(NEO4J_URI, auth=(NEO4J_USER, NEO4J_PASSWORD))
        driver.verify_connectivity()
    except Exception as e:
        print(f"❌ Failed to connect to Neo4j. Is the Docker container running? Error: {e}")
        return

    print("✅ Connected to Neo4j. Seeding multi-tenant Mock Graph RAG network...")

    with driver.session() as session:
        # 1. Clear existing database
        session.run("MATCH (n) DETACH DELETE n")

        # 2. Create Tenants
        session.run("CREATE (t:Tenant {id: '101', name: 'Target Client', domain: $domain})", domain=TARGET_DOMAIN)
        session.run("CREATE (t:Tenant {id: '102', name: 'Contoso Corp', domain: 'contoso.com'})")
        session.run("CREATE (t:Tenant {id: '103', name: 'Fabrikam Inc', domain: 'fabrikam.com'})")

        # 3. Create Departments & Servers (Infrastructure)
        departments = ['Engineering', 'Human Resources', 'Sales', 'Finance', 'IT Support']
        servers = [
            ("SRV-EXCH-01", "Exchange Server 2019"), ("SRV-DB-PROD", "Oracle 19c"), 
            ("SRV-VPN-GW", "Cisco AnyConnect Gateway"), ("SRV-AD-DC", "Active Directory Domain Controller")
        ]

        # Attach standard infrastructure and departments to ALL tenants
        for domain in ALL_DOMAINS:
            for dept in departments:
                session.run("MATCH (t:Tenant {domain: $domain}) CREATE (d:Department {name: $dept})-[:PART_OF]->(t)", domain=domain, dept=dept)
            for srv, type in servers:
                session.run("MATCH (t:Tenant {domain: $domain}) CREATE (s:Infrastructure {name: $srv, type: $type})-[:HOSTED_BY]->(t)", domain=domain, srv=srv, type=type)

        # 4. Generate 150 Users & Assets distributed across ALL domains
        users = []
        for _ in range(150):
            domain = random.choice(ALL_DOMAINS)
            name = fake.name()
            email = f"{name.replace(' ', '.').lower()}@{domain}"
            dept = random.choice(departments)
            asset_tag = f"LAPTOP-{random.randint(10000, 99999)}"
            os_ver = random.choice(['Windows 10', 'Windows 11', 'macOS Sonoma'])
            
            session.run("""
            MATCH (d:Department {name: $dept})-[:PART_OF]->(t:Tenant {domain: $domain})
            CREATE (u:User {name: $name, email: $email})
            CREATE (u)-[:WORKS_IN]->(d)
            CREATE (a:Asset {tag: $asset_tag, os: $os_ver, type: 'Laptop'})
            CREATE (u)-[:OWNS]->(a)
            """, domain=domain, name=name, email=email, dept=dept, asset_tag=asset_tag, os_ver=os_ver)
            users.append((email, domain))

        # Explicitly seed the exact testing account used by the developer!
        session.run("""
        MATCH (d:Department {name: 'IT Support'})-[:PART_OF]->(t:Tenant {domain: $domain})
        CREATE (u:User {name: 'Developer Test Account', email: $email})
        CREATE (u)-[:WORKS_IN]->(d)
        CREATE (a:Asset {tag: 'LAPTOP-TST01', os: 'Windows 11', type: 'Laptop'})
        CREATE (u)-[:OWNS]->(a)
        """, domain=TARGET_DOMAIN, email=f"test@{TARGET_DOMAIN}")
        users.append((f"test@{TARGET_DOMAIN}", TARGET_DOMAIN))

        # 5. Generate 300 Historical Tickets with dense relationships!
        # WE INCLUDE A FAKE CONTOSO SECRET ISSUE TO PROVE THE LLM CANNOT SEE IT!
        issues = [
            ("VPN Connection Drops", "Updated Cisco AnyConnect to v4.10 and flushed DNS.", "SRV-VPN-GW", "Network"),
            ("Outlook Search Not Working", "Rebuilt the Windows Search Index and disabled Cached Exchange Mode.", "SRV-EXCH-01", "Software"),
            ("Cannot access Payroll DB", "Granted user explicit Read permissions on the DB-PROD Security Group.", "SRV-DB-PROD", "Access"),
            ("Account Locked Out", "Reset password in Active Directory and cleared cached credentials.", "SRV-AD-DC", "Access"),
            ("Blue Screen on Boot (CRITICAL_PROCESS_DIED)", "Rolled back the recent CrowdStrike Falcon update via Safe Mode.", None, "Hardware"),
            ("Teams meeting dropping", "Disabled UDP acceleration in Microsoft Teams admin center.", None, "Software")
        ]

        # Inject a highly specific ticket ONLY into the Contoso Tenant
        contoso_user = next(u[0] for u in users if u[1] == 'contoso.com')
        session.run("""
            MATCH (u:User {email: $email})-[:WORKS_IN]->(d:Department)-[:PART_OF]->(t:Tenant)
            MATCH (u)-[:OWNS]->(a:Asset)
            CREATE (tic:Ticket {title: 'CONFIDENTIAL: Project Apollo Source Code Leak', category: 'Security', status: 'Closed', reported_at: datetime()})
            CREATE (res:Resolution {description: 'Revoked all Contoso developer GitHub PATs and isolated the server.', solved_by: 'SecOps'})
            CREATE (tic)-[:REPORTED_BY]->(u)
            CREATE (tic)-[:AFFECTS]->(a)
            CREATE (tic)-[:RESOLVED_WITH]->(res)
            CREATE (tic)-[:BELONGS_TO]->(t)
        """, email=contoso_user)

        for _ in range(300):
            issue = random.choice(issues)
            user_info = random.choice(users)
            user_email = user_info[0]
            status = random.choice(['Closed', 'Closed', 'Closed', 'Resolved'])
            days_ago = random.randint(1, 365)
            
            query = """
            MATCH (u:User {email: $email})-[:WORKS_IN]->(d:Department)-[:PART_OF]->(t:Tenant)
            MATCH (u)-[:OWNS]->(a:Asset)
            CREATE (tic:Ticket {title: $title, category: $category, status: $status, reported_at: datetime() - duration('P' + $days + 'D')})
            CREATE (res:Resolution {description: $resolution, solved_by: 'IT_Support_Agent'})
            
            CREATE (tic)-[:REPORTED_BY]->(u)
            CREATE (tic)-[:AFFECTS]->(a)
            CREATE (tic)-[:RESOLVED_WITH]->(res)
            CREATE (tic)-[:BELONGS_TO]->(t)
            """
            
            if issue[2]:
                query += f"\nWITH tic MATCH (srv:Infrastructure {{name: '{issue[2]}'}}) CREATE (tic)-[:IMPACTS_SERVER]->(srv)"

            session.run(query, email=user_email, title=issue[0], resolution=issue[1], category=issue[3], status=status, days=days_ago)

    print("🎉 SECURE MULTI-TENANT Mock Graph RAG Database seeded successfully!")
    print(f"Go to http://172.26.193.129:7474 and run: MATCH (n) RETURN n")

if __name__ == "__main__":
    seed_database()
