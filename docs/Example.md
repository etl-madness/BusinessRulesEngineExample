# Integration Example: EtlAnalytics.RulesEngine

This example demonstrates how to integrate the core engine and the Dapper SQL provider into a modern .NET 8 or .NET 10 console application.

## 1. The Data Context
Define your custom data container by inheriting from `RuleExecutionContext`.

```csharp
// PizzaAppContext.cs
using EtlAnalytics.RulesEngine.Models;

public class PizzaAppContext : RuleExecutionContext
{
    public string CustomerName { get; set; } = string.Empty;
    public double OrderTotal { get; set; }
    public List<string> Toppings { get; set; } = new();
}
```

## 2. The Rule Store
Implement `IBusinessRuleStore` to tell the engine how to find your rules. This example uses Dapper.

```csharp
// SqlRuleStore.cs
using Dapper;
using EtlAnalytics.RulesEngine.Interfaces;
using EtlAnalytics.RulesEngine.Models;

public class SqlRuleStore : IBusinessRuleStore
{
    private readonly string _connectionString;
    private readonly IRuleDbProvider _dbProvider;

    public SqlRuleStore(string connectionString, IRuleDbProvider dbProvider)
    {
        _connectionString = connectionString;
        _dbProvider = dbProvider;
    }

    public async Task<BusinessRule?> GetBusinessRuleByIdAsync(int id)
    {
        using var db = _dbProvider.CreateConnection(_connectionString);
        return await db.QueryFirstOrDefaultAsync<BusinessRule>(
            "SELECT * FROM BusinessRules WHERE Id = @Id", new { Id = id });
    }

    public async Task<BusinessRuleBundle?> GetBusinessRuleBundleByNameAsync(string name)
    {
        using var db = _dbProvider.CreateConnection(_connectionString);
        var bundle = await db.QueryFirstOrDefaultAsync<BusinessRuleBundle>(
            "SELECT * FROM BusinessRuleBundles WHERE Name = @Name", new { Name = name });

        if (bundle != null)
        {
            var items = await db.QueryAsync<BusinessRuleBundleItem>(
                "SELECT * FROM BusinessRuleBundleItems WHERE BundleId = @Id ORDER BY SequenceOrder", 
                new { Id = bundle.Id });
            bundle.Items = items.ToList();
        }
        return bundle;
    }

    public async Task<DbConnectionDefinition?> GetDbConnectionByIdAsync(int id)
    {
        using var db = _dbProvider.CreateConnection(_connectionString);
        return await db.QueryFirstOrDefaultAsync<DbConnectionDefinition>(
            "SELECT * FROM DbConnections WHERE Id = @Id", new { Id = id });
    }

    public async Task<IEnumerable<DbConnectionDefinition>> GetAllDbConnectionsAsync()
    {
        using var db = _dbProvider.CreateConnection(_connectionString);
        return await db.QueryAsync<DbConnectionDefinition>("SELECT * FROM DbConnections");
    }
}
```

## 3. The Program Setup (DI)
Register your services and execute a rule.

```csharp
// Program.cs
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using EtlAnalytics.RulesEngine.Services;
using EtlAnalytics.RulesEngine.Interfaces;
using EtlAnalytics.RulesEngine.Providers; // From .Dapper package

var services = new ServiceCollection();

// 1. Setup Configuration
var config = new ConfigurationBuilder()
    .AddInMemoryCollection(new Dictionary<string, string?> {
        { "ConnectionStrings:RulesDb", "Server=.;Database=RulesEngine;Trusted_Connection=True;" },
        { "Security:EncryptionKey", "YourSuperSecretEncryptionKey123!" }
    })
    .Build();

services.AddSingleton<IConfiguration>(config);

// 2. Register Core Services
services.AddSingleton<IEncryptionService, AesEncryptionService>();
services.AddScoped<BusinessRuleEngine<PizzaAppContext>>();

// 3. Register the Dapper SQL Executor and Providers
services.AddScoped<ISqlRuleExecutor, DapperSqlRuleExecutor>();
services.AddScoped<IRuleDbProvider, SqlServerRuleDbProvider>();

// 4. Register your Custom Store
services.AddScoped<IBusinessRuleStore>(sp => 
    new SqlRuleStore(
        config.GetConnectionString("RulesDb")!, 
        sp.GetRequiredService<IRuleDbProvider>()
    ));

var serviceProvider = services.BuildServiceProvider();

// --- EXECUTION EXAMPLE ---

var engine = serviceProvider.GetRequiredService<BusinessRuleEngine<PizzaAppContext>>();
var store = serviceProvider.GetRequiredService<IBusinessRuleStore>();

// Create a sample context
var context = new PizzaAppContext 
{ 
    CustomerName = "Alice", 
    OrderTotal = 120.50,
    Toppings = new List<string> { "Pepperoni", "Mushrooms" }
};

// Execute a Rule Bundle by name
Console.WriteLine("Executing 'DiscountWorkflow'...");
var finalResult = await engine.ExecuteBundleAsync("DiscountWorkflow", context, log => 
{
    Console.WriteLine($"[LOG]: {log}");
});

Console.WriteLine($"Final Calculation Result: {finalResult}");
```

## 4. Sample Rule Definitions (In Database)

### C# Rule: "Check VIP Status"
```csharp
if (globals.OrderTotal > 100) {
    globals.Log("Applying VIP discount eligibility");
    return true;
}
return false;
```

### T-SQL Rule: "Fetch Coupon Code"
```sql
SELECT TOP 1 CouponCode 
FROM dbo.Coupons 
WHERE IsActive = 1 
  AND IsVipOnly = CAST(@PreviousResultJson AS BIT)
```
