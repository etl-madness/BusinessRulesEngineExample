# BusinessRulesEngineExample

A Blazor Server application demonstrating the usage of `EtlAnalytics.RulesEngine` for managing and executing TSQL and C# business rules, specifically refactored for KVE (Known Vulnerability Exploitation) reporting.

## Database Schema

The following TSQL script sets up the necessary tables for both the Rules Engine infrastructure and the KVE/CVE reporting data.

### 1. Rules Engine Infrastructure
These tables are used by the `EtlAnalytics.RulesEngine` package and the `SqlDatabaseService` to manage rules, versions, and bundles.

```sql
-- 1. Database Connections
CREATE TABLE dbo.DbConnections (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    Name NVARCHAR(255) NOT NULL,
    ConnectionString NVARCHAR(MAX) NOT NULL,
    ProviderType NVARCHAR(100) NOT NULL DEFAULT 'SqlServer',
    CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
);

-- 2. Business Rules
CREATE TABLE dbo.BusinessRules (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    Name NVARCHAR(255) NOT NULL,
    Description NVARCHAR(MAX) NULL,
    RuleType NVARCHAR(50) NOT NULL, -- 'TSQL' or 'CSharp'
    Code NVARCHAR(MAX) NOT NULL,
    Version INT NOT NULL DEFAULT 1,
    ConnectionId INT NULL,
    IsActive BIT NOT NULL DEFAULT 1,
    CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    UpdatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_BusinessRules_Connection FOREIGN KEY (ConnectionId) REFERENCES dbo.DbConnections(Id)
);

-- 3. Business Rule History (Versioning)
CREATE TABLE dbo.BusinessRuleHistory (
    HistoryId INT IDENTITY(1,1) PRIMARY KEY,
    RuleId INT NOT NULL,
    Name NVARCHAR(255) NOT NULL,
    Description NVARCHAR(MAX) NULL,
    RuleType NVARCHAR(50) NOT NULL,
    Code NVARCHAR(MAX) NOT NULL,
    Version INT NOT NULL,
    ArchivedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_BusinessRuleHistory_RuleId FOREIGN KEY (RuleId) REFERENCES dbo.BusinessRules(Id) ON DELETE CASCADE
);

-- 4. Rule Bundles
CREATE TABLE dbo.BusinessRuleBundles (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    Name NVARCHAR(255) NOT NULL,
    Description NVARCHAR(MAX) NULL,
    IsActive BIT NOT NULL DEFAULT 1,
    CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    UpdatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
);

-- 5. Rule Bundle Items (Sequence)
CREATE TABLE dbo.BusinessRuleBundleItems (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    BundleId INT NOT NULL,
    RuleId INT NOT NULL,
    SequenceOrder INT NOT NULL,
    CONSTRAINT FK_BundleItems_Bundle FOREIGN KEY (BundleId) REFERENCES dbo.BusinessRuleBundles(Id) ON DELETE CASCADE,
    CONSTRAINT FK_BundleItems_Rule FOREIGN KEY (RuleId) REFERENCES dbo.BusinessRules(Id)
);
```

### 2. KVE Reporting Data Schema
These tables store the actual vulnerability data processed by the rules.

```sql
-- 6. KVE Campaigns
CREATE TABLE dbo.KveCampaigns (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    CampaignName NVARCHAR(255) NOT NULL,
    Vendor NVARCHAR(255) NOT NULL,
    Product NVARCHAR(255) NOT NULL,
    Status NVARCHAR(100) NOT NULL,
    CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
);

-- 7. KVE CVE Mappings
CREATE TABLE dbo.KveCves (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    CampaignId INT NOT NULL,
    CveId NVARCHAR(50) NOT NULL, -- e.g., 'CVE-2021-34473'
    CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_KveCves_Campaign FOREIGN KEY (CampaignId) REFERENCES dbo.KveCampaigns(Id) ON DELETE CASCADE
);
```

## Example Data (Seed)

Use these statements to populate the database with initial KVE/CVE data for testing.

```sql
-- Seed Campaigns
INSERT INTO dbo.KveCampaigns (CampaignName, Vendor, Product, Status)
VALUES 
('ProxyShell', 'Microsoft', 'Exchange Server', 'Known Exploited'),
('Log4Shell', 'Apache', 'Log4j', 'Critical'),
('Follina', 'Microsoft', 'Windows Diagnostics Tool', 'Known Exploited');

-- Seed CVEs for ProxyShell (Id 1)
INSERT INTO dbo.KveCves (CampaignId, CveId)
VALUES 
(1, 'CVE-2021-34473'),
(1, 'CVE-2021-34523'),
(1, 'CVE-2021-31207');

-- Seed CVEs for Log4Shell (Id 2)
INSERT INTO dbo.KveCves (CampaignId, CveId)
VALUES 
(2, 'CVE-2021-44228'),
(2, 'CVE-2021-45046');

-- Seed CVEs for Follina (Id 3)
INSERT INTO dbo.KveCves (CampaignId, CveId)
VALUES 
(3, 'CVE-2022-30190');

-------------------------------------------------------------------------------
-- 3. EXAMPLE BUSINESS RULES AND BUNDLES
-------------------------------------------------------------------------------

-- Insert a TSQL Rule to fetch exploited campaigns
INSERT INTO dbo.BusinessRules (Name, Description, RuleType, Code, Version, IsActive)
VALUES (
    'Fetch Exploited KVEs', 
    'Retrieves campaigns marked as Known Exploited and their CVEs', 
    'TSQL', 
    'SELECT c.CampaignName as KveCampaign, c.Vendor, c.Product, c.Status, 
       (SELECT STRING_AGG(CveId, '','') FROM dbo.KveCves v WHERE v.CampaignId = c.Id) as Cves
FROM dbo.KveCampaigns c
WHERE c.Status = ''Known Exploited''', 
    1, 1
);

-- Insert a C# Script Rule to process the results
INSERT INTO dbo.BusinessRules (Name, Description, RuleType, Code, Version, IsActive)
VALUES (
    'Analyze Campaign Impact', 
    'Logs the impact of found KVEs', 
    'CSharp', 
    'Log("Starting Impact Analysis...");
// PreviousResult from a TSQL rule is an IEnumerable<dynamic>
var kveReports = PreviousResult as IEnumerable<dynamic>;
if (kveReports == null) {
    Log("No reports found from previous step.");
    return new { CriticalCount = 0, Violations = new List<dynamic>() };
}
return new {
    // Note the () after Count
    CriticalCount = kveReports.Count(), 
    Violations = kveReports  
};', 
    1, 1
);

-- Create a Bundle
INSERT INTO dbo.BusinessRuleBundles (Name, Description, IsActive)
VALUES ('Daily Vulnerability Scan', 'Sequentially fetches and analyzes vulnerability data', 1);

-- Link Rules to Bundle (Assumes Rules 1 & 2, Bundle 1)
-- Note: In a real environment, use SCOPE_IDENTITY() or lookup IDs
INSERT INTO dbo.BusinessRuleBundleItems (BundleId, RuleId, SequenceOrder)
VALUES 
(1, 1, 1), -- Fetch first
(1, 2, 2); -- Analyze second
```

## How to Configure

1.  Create a SQL Server database.
2.  Run the schema script above.
3.  Set your connection string in `Properties/launchSettings.json` or `appsettings.json`.
4.  Run the app and navigate to **Rules** to create your processing logic!
