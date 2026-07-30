# Tutorial: Learning `EtlAnalytics.RulesEngine` with this Blazor Example

This tutorial uses the **BusinessRulesEngineExample** app to teach the core ideas behind `EtlAnalytics.RulesEngine` and `EtlAnalytics.RulesEngine.Dapper`.

## What this app demonstrates

The app is a Blazor Server UI over a rules engine. It lets you:

- Create and edit rules (`/rules`)
- Compose ordered bundles (`/bundles`)
- Manage database connections used by SQL rules (`/connections`)
- Execute rules and bundles and inspect logs
- Execute bundles through API endpoints

The rule engine supports two rule types:

- **TSQL** rules (query data)
- **C#** rules (process/transform previous results)

---

## 1) Concepts to understand first

### `BusinessRuleEngine<TContext>`
This is the orchestrator. It loads rules from your store, executes each step, and passes results through `PreviousResult` / `StepResults`.

### `RuleExecutionContext`
Your app-specific context extends this base class. In this project:

- `BusinessRuleContext : RuleExecutionContext`
- Adds `KveReports` to hold vulnerability report data

### `IBusinessRuleStore`
The engine needs a data source for rules, bundles, and DB connections. This app uses `SqlDatabaseService` as that implementation.

### `ISqlRuleExecutor` (from RulesEngine.Dapper integration)
This is the SQL execution abstraction used by the engine for TSQL rules. In this app it is wired to `DapperSqlRuleExecutor`.

---

## 2) Program startup explained (`Program.cs`)

Below are the most important lines and why they matter.

## `builder.Services.AddRazorPages();`
Enables Razor Pages hosting plumbing for Blazor Server.

## `builder.Services.AddServerSideBlazor();`
Registers Blazor Server services and SignalR circuit support.

## `builder.Services.AddControllers();`
Enables API controllers (used by `RulesController`).

## `builder.Services.AddEndpointsApiExplorer();`
Required for endpoint discovery in Swagger.

## `builder.Services.AddSwaggerGen();`
Generates OpenAPI/Swagger docs in Development.

## `builder.Services.AddHttpClient();`
Allows pages/components to call API endpoints via `IHttpClientFactory`.

## Radzen service registrations

```csharp
builder.Services.AddScoped<DialogService>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddScoped<TooltipService>();
builder.Services.AddScoped<ContextMenuService>();
```
These support the Radzen UI components used throughout the app.

## Rules engine registrations (critical)

```csharp
builder.Services.AddSingleton<IEncryptionService, NoEncryptionService>();
builder.Services.AddScoped<SqlDatabaseService>();
builder.Services.AddScoped<IBusinessRuleStore>(sp => sp.GetRequiredService<SqlDatabaseService>());
builder.Services.AddScoped<IRuleDbProvider, SqlServerRuleDbProvider>();
builder.Services.AddScoped<ISqlRuleExecutor, DapperSqlRuleExecutor>();
builder.Services.AddScoped<BusinessRuleEngine<BusinessRuleContext>>();
```

How these fit together:

- `SqlDatabaseService` stores and retrieves rules/bundles/connections from SQL Server.
- `IBusinessRuleStore` resolves to `SqlDatabaseService`, so the engine can query rule metadata.
- `IRuleDbProvider` resolves provider-specific DB connections (`SqlServerRuleDbProvider`).
- `ISqlRuleExecutor` resolves to `DapperSqlRuleExecutor` from **EtlAnalytics.RulesEngine.Dapper**.
- `BusinessRuleEngine<BusinessRuleContext>` uses all of the above to run TSQL and C# rules.

> In production, replace `NoEncryptionService` with `AesEncryptionService` and configure `Security:EncryptionKey` or `DB_ENCRYPTION_KEY`.

## Pipeline and endpoint mapping

```csharp
app.UseStaticFiles();
app.UseRouting();
app.MapBlazorHub();
app.MapControllers();
app.MapFallbackToPage("/_Host");
```

- `MapBlazorHub` handles Blazor Server real-time UI.
- `MapControllers` exposes API routes (for example: `/api/rules/...`).

## Database bootstrapping

At startup the app creates required tables if they do not exist:

```csharp
await dbService.CreateBusinessRuleTablesIfNotExistsAsync();
```

This keeps first-run setup simple for local learning.

---

## 3) Run the app and create your first workflow

1. Configure connection string in `appsettings.json` or environment variable `DB_CONNECTION_STRING`.
2. Run the app.
3. Open `/connections` and save a SQL Server connection.
4. Open `/rules` and create:
   - Rule A (TSQL): fetch vulnerability rows
   - Rule B (C#): count or classify results from `globals.PreviousResult`
5. Open `/bundles` and create a bundle with Rule A first, Rule B second.
6. Execute the bundle and inspect logs/final output.

---

## 4) How data flows in a bundle (Parallel Sequence Stages)

When bundle steps run:

1. **Sequence Grouping (Stages)**: Rules sharing the same `SequenceOrder` (Stage) run concurrently in parallel via `Task.WhenAll`.
2. **Sequential Stages**: Stages execute in ascending `SequenceOrder` (Stage 1 $\rightarrow$ Stage 2 $\rightarrow$ Stage 3).
3. **Parallel Result Aggregation**:
   - For a single-rule stage, its output is stored in `PreviousResult` and `StepResults[stage]`.
   - For a multi-rule (parallel) stage, all rule outputs are aggregated into a `List<object?>` and assigned to `PreviousResult` and `StepResults[stage]`.
4. **Consuming Parallel Results**:
   - Downstream C# rules receive `globals.PreviousResult` as a `List<object?>` where each element index `[i]` corresponds to the return value of rule `i` in that parallel stage.
   - For TSQL steps, the engine injects `@PreviousResultJson` representing the parallel stage JSON array (`[ [rule1_results...], [rule2_results...] ]`).

#### Example: Referencing Parallel Rule Outputs in a Downstream C# Script Rule

```csharp
// Stage 2 C# Script Rule consuming outputs from Stage 1 Parallel Rules
using System;
using System.Linq;
using System.Collections.Generic;

// globals.PreviousResult contains List<object?> from Stage 1:
// [0] -> Output from Rule A (e.g. High Severity records)
// [1] -> Output from Rule B (e.g. Critical Severity records)

var ruleAResults = (IEnumerable<dynamic>)globals.PreviousResult[0];
var ruleBResults = (IEnumerable<dynamic>)globals.PreviousResult[1];

int countA = ruleAResults.Count();
int countB = ruleBResults.Count();

globals.Log($"Parallel Rule A returned {countA} items.");
globals.Log($"Parallel Rule B returned {countB} items.");

return new {
    CombinedCount = countA + countB,
    RuleA_Count = countA,
    RuleB_Count = countB
};
```

#### Example: Referencing Parallel Rule Outputs in a Downstream T-SQL Query

```sql
-- Stage 2 TSQL Query parsing output from Parallel Rule A (Index 0) using OPENJSON
SELECT * 
FROM OPENJSON(@PreviousResultJson, '$[0]')
WITH (
    Id INT '$.Id',
    RuleName NVARCHAR(100) '$.RuleName',
    Status NVARCHAR(50) '$.Status'
);
```

This pattern lets you build parallel SQL + C# pipelines with native execution grouping.

---

## 5) API usage in this sample

`RulesController` demonstrates server-side execution endpoints, including bundle execution by name. This is useful for automation and dashboard scenarios.

Typical flow:

- Resolve bundle from `IBusinessRuleStore`
- Create `BusinessRuleContext`
- Call `ExecuteBundleAsync(...)`
- Return result + logs to caller

---

## 6) Practical learning checklist

- Create one SQL rule and execute it.
- Add one C# rule that consumes SQL output.
- Place both in a bundle and validate sequence behavior.
- Add logging in C# rule and watch UI/API logs.
- Switch from `NoEncryptionService` to `AesEncryptionService` when ready.

---

## 7) Common troubleshooting

- **Rules not running**: ensure `ISqlRuleExecutor` is registered (`DapperSqlRuleExecutor`).
- **No DB data**: verify connection string and selected `ConnectionId` on SQL rules.
- **Bundle not found**: check exact bundle name and active status.
- **Swagger missing**: only enabled in Development in this app.

---

This project is a practical baseline for building rule-driven processing with Blazor, SQL Server, and `EtlAnalytics.RulesEngine`.