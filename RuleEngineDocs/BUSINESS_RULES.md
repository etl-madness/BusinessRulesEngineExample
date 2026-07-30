# Business Rules - EtlAnalytics.RulesEngine

This document outlines the core business rules and logic implemented within the `EtlAnalytics.RulesEngine` project.

## 1. Rule Execution Framework

### 1.1 Supported Rule Types
- **T-SQL**: SQL scripts executed against a database.
- **C#**: Dynamic C# scripts executed within a restricted runtime environment.
- **Javascript**: Logic scripts executed via the Jint engine (requires `EtlAnalytics.RulesEngine.Javascript` extension).

### 1.2 Execution Lifecycle
- Rules can be executed individually or as part of a **Bundle**.
- **Result Piping**: In a bundle, the output of each rule is passed as the `PreviousResult` to the subsequent rule.
- **Parallel Execution**: Rules sharing the same `SequenceOrder` within a bundle are executed concurrently. The engine waits for all rules in the group to complete before proceeding.
- **Result Aggregation**: Results from parallel rules are aggregated into a `List<object?>`.
- **State Management**: The execution context maintains a history of all step results within a bundle, indexed by their sequence order. For parallel groups, the value stored is the aggregated list of results.
- **Bundle Abort Policy**: If any step (sequential or parallel) within a bundle fails (throws an exception), the entire bundle execution is terminated immediately.

## 2. SQL Rule Constraints & Security

### 2.1 Forbidden Keywords
To prevent unauthorized database modifications, SQL rules are scanned for forbidden keywords. Any rule containing these keywords is blocked from execution:
- `DROP`, `TRUNCATE`, `DELETE`, `UPDATE`, `INSERT`
- `GRANT`, `REVOKE`, `ALTER`, `CREATE`
- `xp_cmdshell`
- `sys.`, `information_schema`

### 2.2 Connection Management
- Rules can specify a target connection via `ConnectionId`.
- If no `ConnectionId` is provided, the engine falls back to the default system connection string.
- Connection strings retrieved from the database or configuration are expected to be encrypted.

### 2.3 SQL Parameters
Every SQL rule is automatically provided with the following JSON parameters:
- `PreviousResultJson`: The JSON-serialized result of the previous rule in the bundle.
- `StepResultsJson`: A JSON object containing results from all previous steps in the bundle.

### 2.4 SQL Timeouts
- The default timeout for SQL execution is **30 seconds**.

## 3. C# Scripting Constraints & Security

### 3.1 Restricted Sandbox
C# scripts are executed with a limited set of allowed assemblies and namespaces to ensure system stability:
- **Allowed Assemblies**: `mscorlib`, `System.Linq`, `System.Collections.Generic`, and the `EtlAnalytics.RulesEngine` core assembly.
- **Allowed Imports**: `System`, `System.Collections.Generic`, `System.Linq`, `System.Text`, `System.Threading.Tasks`, and `EtlAnalytics.RulesEngine.Models`.

### 3.2 C# Timeouts
- The default timeout for C# script execution is **10 seconds**.

## 4. Security & Encryption

### 4.1 Sensitive Data Protection
- All connection strings and potentially sensitive configuration values must be encrypted at rest.
- The engine uses **AES-256** encryption.
- Encryption keys are derived using **PBKDF2** with 100,000 iterations and a SHA256 hash.
- A fixed salt (`EtlAnalytics.Salt.RulesEngine`) is used for key derivation consistency across the library.

### 4.2 Key Management
- The encryption key is prioritized from the `DB_ENCRYPTION_KEY` environment variable, falling back to the `Security:EncryptionKey` app configuration setting.

## 5. Versioning and Metadata
- Each `BusinessRule` tracks its own version number (defaulting to 1).
- Rules track `CreatedAt` and `UpdatedAt` timestamps for auditability.
- Rules include an `IsActive` flag to allow for soft-disabling without deletion.

## 6. Usage Examples

### 6.1 Accessing Previous Step Data (C#)
In a C# rule, you can access the result of the immediately preceding rule using `PreviousResult`, or any specific step using the `StepResults` dictionary.

```csharp
// Example: Validate that the previous step returned at least 5 rows
var previousRows = (List<dynamic>)PreviousResult;
if (previousRows.Count < 5) {
    return "Failure: Insufficient data from previous step.";
}

// Example: Access data from the first step in the bundle (SequenceOrder 1)
var step1Data = StepResults[1];
return $"Processed {previousRows.Count} rows using configuration from step 1.";
```

### 6.2 Accessing Previous Step Data (T-SQL)
The syntax for accessing the `@PreviousResultJson` parameter varies depending on the target database provider.

| Database | Parameter Prefix | JSON Extraction Example |
| :--- | :---: | :--- |
| **SQL Server** | `@` | `CROSS APPLY OPENJSON(@PreviousResultJson) WITH (Status INT '$.Status')` |
| **PostgreSQL** | `:` | `SELECT * FROM table WHERE data ->> 'Status' = :PreviousResultJson` |
| **MySQL** | `?` | `SELECT * FROM table WHERE JSON_EXTRACT(?PreviousResultJson, '$.Status') = 1` |

#### **SQL Server Example**
```sql
-- Using OPENJSON to parse the previous result
SELECT TOP 1 * FROM Discounts 
CROSS APPLY OPENJSON(@PreviousResultJson) WITH (CustomerType NVARCHAR(50)) p
WHERE p.CustomerType = 'VIP';

-- Accessing specific historical steps from StepResultsJson
DECLARE @Step1Results NVARCHAR(MAX) = JSON_QUERY(@StepResultsJson, '$."1"');
SELECT * FROM OPENJSON(@Step1Results) WITH (ConfigValue INT '$.Value');
```

### 6.7 Parallel Execution & Result Aggregation (C#)
This example shows how to perform multiple independent tasks in parallel and then process their combined results in a final step.

#### **Step 1: SQL Rule (Sequence 1 - Fetch Products)**
```sql
SELECT ProductId, Name FROM Products WHERE Category = 'Electronics';
```

#### **Step 2: SQL Rule (Sequence 1 - Fetch Stock)**
```sql
SELECT ProductId, Quantity FROM Inventory WHERE WarehouseId = 10;
```

#### **Step 3: C# Rule (Sequence 2 - Join & Process)**
```csharp
// Name: ProcessParallelResults
// PreviousResult contains a List<object> with results from Step 1 and Step 2
var parallelResults = (List<object>)PreviousResult;
var products = (List<dynamic>)parallelResults[0];
var stock = (List<dynamic>)parallelResults[1];

Log($"Processing {products.Count} products with corresponding stock data.");

foreach(var p in products) {
    var s = stock.FirstOrDefault(x => x.ProductId == p.ProductId);
    if (s != null && s.Quantity < 5) {
        await AlertService.TriggerLowStockAsync(p.Name, s.Quantity);
    }
}
return "Parallel processing complete.";
```

### 6.8 Referencing Multiple Parallel Stages
In workflows containing multiple sequential parallel stages (e.g. Stage 1 Parallel $\rightarrow$ Stage 2 Parallel $\rightarrow$ Stage 3 Final Aggregator), downstream rules can reference outputs from any preceding parallel stage using the `StepResults` dictionary or `@StepResultsJson`.

#### **Workflow Architecture**:
- **Stage 1 (Parallel, `SequenceOrder = 1`)**:
  - `Rule 1A`: Fetch Regional Sales (North)
  - `Rule 1B`: Fetch Regional Sales (South)
- **Stage 2 (Parallel, `SequenceOrder = 2`)**:
  - `Rule 2A`: Fetch Regional Expenses (North)
  - `Rule 2B`: Fetch Regional Expenses (South)
- **Stage 3 (Sequential, `SequenceOrder = 3`)**:
  - `Rule 3`: Final Financial Consolidation (references both Stage 1 and Stage 2 parallel outputs)

#### **Step 3: C# Rule (Sequence 3 - Cross-Stage Parallel Consolidation)**
```csharp
// Name: ConsolidateMultipleParallelStages
// StepResults[1] -> List<object?> containing Stage 1 parallel results
// StepResults[2] -> List<object?> containing Stage 2 parallel results

var stage1Parallel = (IList<object?>)StepResults[1];
var northSales = (IEnumerable<dynamic>)stage1Parallel[0];
var southSales = (IEnumerable<dynamic>)stage1Parallel[1];

var stage2Parallel = (IList<object?>)StepResults[2];
var northExpenses = (IEnumerable<dynamic>)stage2Parallel[0];
var southExpenses = (IEnumerable<dynamic>)stage2Parallel[1];

decimal totalNorthRevenue = northSales.Sum(x => (decimal)x.Amount);
decimal totalSouthRevenue = southSales.Sum(x => (decimal)x.Amount);

decimal totalNorthExpense = northExpenses.Sum(x => (decimal)x.Amount);
decimal totalSouthExpense = southExpenses.Sum(x => (decimal)x.Amount);

decimal northNet = totalNorthRevenue - totalNorthExpense;
decimal southNet = totalSouthRevenue - totalSouthExpense;

Log($"Stage 1 Revenue: North={totalNorthRevenue:C}, South={totalSouthRevenue:C}");
Log($"Stage 2 Expenses: North={totalNorthExpense:C}, South={totalSouthExpense:C}");

return new {
    NorthRegion = new { Revenue = totalNorthRevenue, Expense = totalNorthExpense, NetProfit = northNet },
    SouthRegion = new { Revenue = totalSouthRevenue, Expense = totalSouthExpense, NetProfit = southNet },
    TotalCompanyNetProfit = northNet + southNet
};
```

#### **Step 3: T-SQL Rule (Sequence 3 - Cross-Stage Parallel SQL Aggregation)**
```sql
-- Name: ConsolidateMultipleParallelStagesSql
-- @StepResultsJson structure:
-- {
--   "1": [ [NorthSales...], [SouthSales...] ],
--   "2": [ [NorthExpenses...], [SouthExpenses...] ]
-- }

DECLARE @Stage1_NorthSales NVARCHAR(MAX) = JSON_QUERY(@StepResultsJson, '$."1"[0]');
DECLARE @Stage1_SouthSales NVARCHAR(MAX) = JSON_QUERY(@StepResultsJson, '$."1"[1]');

DECLARE @Stage2_NorthExpenses NVARCHAR(MAX) = JSON_QUERY(@StepResultsJson, '$."2"[0]');
DECLARE @Stage2_SouthExpenses NVARCHAR(MAX) = JSON_QUERY(@StepResultsJson, '$."2"[1]');

SELECT 
    'North' AS Region,
    (SELECT ISNULL(SUM(Amount),0) FROM OPENJSON(@Stage1_NorthSales) WITH (Amount DECIMAL(18,2) '$.Amount')) AS Revenue,
    (SELECT ISNULL(SUM(Amount),0) FROM OPENJSON(@Stage2_NorthExpenses) WITH (Amount DECIMAL(18,2) '$.Amount')) AS Expense
UNION ALL
SELECT 
    'South' AS Region,
    (SELECT ISNULL(SUM(Amount),0) FROM OPENJSON(@Stage1_SouthSales) WITH (Amount DECIMAL(18,2) '$.Amount')) AS Revenue,
    (SELECT ISNULL(SUM(Amount),0) FROM OPENJSON(@Stage2_SouthExpenses) WITH (Amount DECIMAL(18,2) '$.Amount')) AS Expense;
```
