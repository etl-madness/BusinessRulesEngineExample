# AI Implementation Guide: EtlAnalytics.RulesEngine

This document serves as a structured technical reference for AI agents to integrate, extend, and develop rules using the `EtlAnalytics.RulesEngine` NuGet package.

## 1. Core Architecture

The library is a database-agnostic business rules engine that supports two execution modes: **TSQL** and **C# Scripting**. It follows a standard Dependency Injection (DI) pattern and is decoupled from specific database execution libraries (like Dapper). It targets both **.NET 8** and **.NET 10**.

### Primary Service
- **`BusinessRuleEngine<TContext>`**: The orchestrator. `TContext` must inherit from `RuleExecutionContext`.

### Required Interfaces
Implementing these is mandatory for integration:
- **`IBusinessRuleStore`**: Handles retrieval of `BusinessRule` and `BusinessRuleBundle` objects.
- **`ISqlRuleExecutor`**: Abstracts the actual SQL execution. The engine calls this to fetch data from any source.
- **`IRuleDbProvider`**: Provides `IDbConnection` instances. This is used by implementations of `ISqlRuleExecutor`.
- **`IEncryptionService`**: Handles AES-256 encryption/decryption of connection strings.

## 2. Model Definitions

### `BusinessRule`
- `RuleType`: `TSQL` (0) or `CSharp` (1).
- `Code`: The raw SQL query or C# script.
- `ConnectionId`: Links to a `DbConnectionDefinition`. The engine resolves the correct provider and connection string before calling the `ISqlRuleExecutor`.

### `RuleExecutionContext` (Base Class)
Agents should inherit from this to pass custom data to rules.
- `PreviousResult`: Result from the last rule in a bundle.
- `StepResults`: Dictionary of all previous results in a bundle (`sequenceOrder` -> `result`).
- `CancellationToken`: Used to signal timeouts (10s for C#, 30s for SQL).

## 3. Data Passing in Bundles (Sequence Execution)

The engine supports sequential execution of rules within a `BusinessRuleBundle`. Data is passed between steps automatically via the context.

### C# to C# Data Passing
Rules can read `globals.PreviousResult` or look up specific results in `globals.StepResults`.

### SQL to C# Data Passing
SQL results are returned as `IEnumerable<dynamic>`. C# rules can then process this data.

### C# to SQL Data Passing
The engine injects `PreviousResultJson` and `StepResultsJson` as parameters into every SQL execution.

## 4. Multi-Database Support

The engine passes the `ProviderType` (e.g., "SqlServer", "Postgres") to the `ISqlRuleExecutor`. Implementations of the executor use this to resolve the correct `IRuleDbProvider`.

## 5. Security & Sandboxing (CRITICAL)

AI agents generating rules must adhere to these constraints to avoid execution errors.

### C# Scripting Restrictions
- **Timeout**: Hard-coded to **10 seconds**.
- **Namespace Whitelist**:
  - `System`, `System.Linq`, `System.Collections.Generic`
  - `System.Text`, `System.Threading.Tasks`
- **Forbidden**: `System.IO`, `System.Net`, `System.Diagnostics`, `System.Reflection`.

### T-SQL Sandboxing
- **Timeout**: Hard-coded to **30 seconds**.
- **Blacklisted Keywords**: `DROP`, `TRUNCATE`, `DELETE`, `UPDATE`, `INSERT`, `CREATE`, `ALTER`, `EXEC`, `EXECUTE`, `xp_cmdshell`, `sys.`, `information_schema`.
- **Reference**: For instructions on how to modify this blacklist in the source code, see [forbidden_keywords_modification.md](file:///C:/Users/U00001/source/repos/etl-madness/docs/forbidden_keywords_modification.md).

## 6. Integration Guide

### Dependency Injection Setup
To use the engine, you must register a SQL executor (e.g., one using Dapper).

```csharp
services.AddSingleton<IEncryptionService, AesEncryptionService>();
services.AddScoped<IBusinessRuleStore, MySqlRuleStore>();
services.AddScoped<IRuleDbProvider, SqlServerRuleDbProvider>();

// Register the SQL Executor (can be moved to a separate project)
services.AddScoped<ISqlRuleExecutor, DapperSqlRuleExecutor>();

services.AddScoped<BusinessRuleEngine<MyCustomContext>>();
```

### Execution Flow
1. Retrieve Rule/Bundle from Store.
2. Initialize `TContext`.
3. Call `ExecuteRuleAsync` or `ExecuteBundleAsync`.
4. Engine decrypts connection strings and prepares parameters.
5. Engine calls `ISqlRuleExecutor.ExecuteAsync` for SQL rules.

## 7. C# Rule Development Patterns
(Unchanged from previous versions)

## 8. T-SQL Rule Development Patterns
SQL rules are executed via the registered `ISqlRuleExecutor`. By default, this supports Dapper-style parameter injection (`@PreviousResultJson`).

## 9. Configuration Keys
The `AesEncryptionService` expects:
- Environment Variable: `DB_ENCRYPTION_KEY`
- OR Configuration: `Security:EncryptionKey`
