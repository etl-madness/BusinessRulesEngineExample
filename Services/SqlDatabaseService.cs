using EtlAnalytics.RulesEngine.Models;
using EtlAnalytics.RulesEngine.Interfaces;
using BusinessRulesEngineExample.Models;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace BusinessRulesEngineExample.Services;

public class NoEncryptionService : IEncryptionService
{
    public string Encrypt(string plainText) => plainText;
    public string Decrypt(string cipherText) => cipherText;
}

public class SqlDatabaseService : IBusinessRuleStore
{
    static SqlDatabaseService()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;
    }

    private readonly string _connectionString;
    private readonly IEncryptionService _encryptionService;

    public SqlDatabaseService(IConfiguration configuration, IEncryptionService encryptionService)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection") 
            ?? Environment.GetEnvironmentVariable("DB_CONNECTION_STRING")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
        _encryptionService = encryptionService;
    }

    public async Task CreateBusinessRuleTablesIfNotExistsAsync()
    {
        const string sql = @"
            IF OBJECT_ID('dbo.DbConnections', 'U') IS NULL
            BEGIN
                CREATE TABLE dbo.DbConnections (
                    Id INT IDENTITY(1,1) PRIMARY KEY,
                    Name NVARCHAR(255) NOT NULL,
                    ConnectionString NVARCHAR(MAX) NOT NULL,
                    ProviderType NVARCHAR(100) NOT NULL DEFAULT 'SqlServer',
                    CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
                );
            END;

            IF OBJECT_ID('dbo.BusinessRules', 'U') IS NULL
            BEGIN
                CREATE TABLE dbo.BusinessRules (
                    Id INT IDENTITY(1,1) PRIMARY KEY,
                    Name NVARCHAR(255) NOT NULL,
                    Description NVARCHAR(MAX) NULL,
                    RuleType NVARCHAR(50) NOT NULL,
                    Code NVARCHAR(MAX) NOT NULL,
                    Version INT NOT NULL DEFAULT 1,
                    ConnectionId INT NULL,
                    IsActive BIT NOT NULL DEFAULT 1,
                    CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
                    UpdatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
                    CONSTRAINT FK_BusinessRules_Connection FOREIGN KEY (ConnectionId) REFERENCES dbo.DbConnections(Id)
                );
            END;

            IF OBJECT_ID('dbo.BusinessRuleHistory', 'U') IS NULL
            BEGIN
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
            END;

            IF OBJECT_ID('dbo.BusinessRuleBundles', 'U') IS NULL
            BEGIN
                CREATE TABLE dbo.BusinessRuleBundles (
                    Id INT IDENTITY(1,1) PRIMARY KEY,
                    Name NVARCHAR(255) NOT NULL,
                    Description NVARCHAR(MAX) NULL,
                    IsActive BIT NOT NULL DEFAULT 1,
                    CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
                    UpdatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
                );
            END;

            IF OBJECT_ID('dbo.BusinessRuleBundleItems', 'U') IS NULL
            BEGIN
                CREATE TABLE dbo.BusinessRuleBundleItems (
                    Id INT IDENTITY(1,1) PRIMARY KEY,
                    BundleId INT NOT NULL,
                    RuleId INT NOT NULL,
                    SequenceOrder INT NOT NULL,
                    CONSTRAINT FK_BundleItems_Bundle FOREIGN KEY (BundleId) REFERENCES dbo.BusinessRuleBundles(Id) ON DELETE CASCADE,
                    CONSTRAINT FK_BundleItems_Rule FOREIGN KEY (RuleId) REFERENCES dbo.BusinessRules(Id)
                );
            END;
        ";
        await using var connection = new SqlConnection(_connectionString);
        await connection.ExecuteAsync(sql);

        // Fix invalid RuleType names from previous versions/manual inserts
        const string fixSql = @"
            UPDATE dbo.BusinessRules SET RuleType = 'CSharp' WHERE RuleType = 'CSharpScript';
            UPDATE dbo.BusinessRuleHistory SET RuleType = 'CSharp' WHERE RuleType = 'CSharpScript';
        ";
        await connection.ExecuteAsync(fixSql);
    }

    public async Task<IReadOnlyList<BusinessRule>> GetBusinessRulesAsync()
    {
        const string sql = "SELECT * FROM dbo.BusinessRules WHERE IsActive = 1 ORDER BY Name;";
        await using var connection = new SqlConnection(_connectionString);
        var rows = await connection.QueryAsync<BusinessRule>(sql);
        return rows.ToList();
    }

    public async Task<BusinessRule?> GetBusinessRuleByIdAsync(int id)
    {
        const string sql = "SELECT * FROM dbo.BusinessRules WHERE Id = @Id;";
        await using var connection = new SqlConnection(_connectionString);
        return await connection.QueryFirstOrDefaultAsync<BusinessRule>(sql, new { Id = id });
    }

    public async Task<int> InsertBusinessRuleAsync(BusinessRule rule)
    {
        const string sql = @"
            INSERT INTO dbo.BusinessRules (Name, Description, RuleType, Code, Version, ConnectionId, IsActive, CreatedAt, UpdatedAt)
            VALUES (@Name, @Description, @RuleType, @Code, 1, @ConnectionId, 1, SYSUTCDATETIME(), SYSUTCDATETIME());
            SELECT CAST(SCOPE_IDENTITY() AS INT);
        ";
        await using var connection = new SqlConnection(_connectionString);
        return await connection.ExecuteScalarAsync<int>(sql, rule);
    }

    public async Task UpdateBusinessRuleAsync(BusinessRule rule)
    {
        const string archiveSql = @"
            INSERT INTO dbo.BusinessRuleHistory (RuleId, Name, Description, RuleType, Code, Version, ArchivedAt)
            SELECT Id, Name, Description, RuleType, Code, Version, SYSUTCDATETIME()
            FROM dbo.BusinessRules
            WHERE Id = @Id;
        ";

        const string updateSql = @"
            UPDATE dbo.BusinessRules
            SET Name = @Name,
                Description = @Description,
                RuleType = @RuleType,
                Code = @Code,
                ConnectionId = @ConnectionId,
                Version = Version + 1,
                UpdatedAt = SYSUTCDATETIME()
            WHERE Id = @Id;
        ";

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        using var transaction = connection.BeginTransaction();
        try
        {
            await connection.ExecuteAsync(archiveSql, new { Id = rule.Id }, transaction);
            await connection.ExecuteAsync(updateSql, rule, transaction);
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task DeleteBusinessRuleAsync(int id)
    {
        const string sql = "UPDATE dbo.BusinessRules SET IsActive = 0 WHERE Id = @Id;";
        await using var connection = new SqlConnection(_connectionString);
        await connection.ExecuteAsync(sql, new { Id = id });
    }

    public async Task<IReadOnlyList<BusinessRuleHistory>> GetBusinessRuleHistoryAsync(int ruleId)
    {
        const string sql = "SELECT * FROM dbo.BusinessRuleHistory WHERE RuleId = @RuleId ORDER BY Version DESC;";
        await using var connection = new SqlConnection(_connectionString);
        var rows = await connection.QueryAsync<BusinessRuleHistory>(sql, new { RuleId = ruleId });
        return rows.ToList();
    }

    public async Task<IReadOnlyList<BusinessRuleBundle>> GetBusinessRuleBundlesAsync()
    {
        const string sql = "SELECT * FROM dbo.BusinessRuleBundles WHERE IsActive = 1 ORDER BY Name;";
        await using var connection = new SqlConnection(_connectionString);
        var rows = await connection.QueryAsync<BusinessRuleBundle>(sql);
        return rows.ToList();
    }

    public async Task<BusinessRuleBundle?> GetBusinessRuleBundleByIdAsync(int id)
    {
        const string sql = "SELECT * FROM dbo.BusinessRuleBundles WHERE Id = @Id;";
        const string itemsSql = @"
            SELECT i.*, r.Name as RuleName, r.RuleType 
            FROM dbo.BusinessRuleBundleItems i
            JOIN dbo.BusinessRules r ON i.RuleId = r.Id
            WHERE i.BundleId = @BundleId
            ORDER BY i.SequenceOrder;
        ";
        await using var connection = new SqlConnection(_connectionString);
        var bundle = await connection.QueryFirstOrDefaultAsync<BusinessRuleBundle>(sql, new { Id = id });
        if (bundle != null)
        {
            var items = await connection.QueryAsync<BusinessRuleBundleItem>(itemsSql, new { BundleId = id });
            bundle.Items = items.ToList();
        }
        return bundle;
    }

    public async Task<int> InsertBusinessRuleBundleAsync(BusinessRuleBundle bundle)
    {
        const string sql = @"
            INSERT INTO dbo.BusinessRuleBundles (Name, Description, IsActive, CreatedAt, UpdatedAt)
            VALUES (@Name, @Description, 1, SYSUTCDATETIME(), SYSUTCDATETIME());
            SELECT CAST(SCOPE_IDENTITY() AS INT);
        ";
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        using var transaction = connection.BeginTransaction();
        try
        {
            var id = await connection.ExecuteScalarAsync<int>(sql, bundle, transaction);
            foreach (var item in bundle.Items)
            {
                item.BundleId = id;
                await connection.ExecuteAsync(@"
                    INSERT INTO dbo.BusinessRuleBundleItems (BundleId, RuleId, SequenceOrder)
                    VALUES (@BundleId, @RuleId, @SequenceOrder);
                ", item, transaction);
            }
            await transaction.CommitAsync();
            return id;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task UpdateBusinessRuleBundleAsync(BusinessRuleBundle bundle)
    {
        const string sql = @"
            UPDATE dbo.BusinessRuleBundles
            SET Name = @Name, Description = @Description, UpdatedAt = SYSUTCDATETIME()
            WHERE Id = @Id;
        ";
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        using var transaction = connection.BeginTransaction();
        try
        {
            await connection.ExecuteAsync(sql, bundle, transaction);
            await connection.ExecuteAsync("DELETE FROM dbo.BusinessRuleBundleItems WHERE BundleId = @Id;", new { Id = bundle.Id }, transaction);
            foreach (var item in bundle.Items)
            {
                item.BundleId = bundle.Id;
                await connection.ExecuteAsync(@"
                    INSERT INTO dbo.BusinessRuleBundleItems (BundleId, RuleId, SequenceOrder)
                    VALUES (@BundleId, @RuleId, @SequenceOrder);
                ", item, transaction);
            }
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task DeleteBusinessRuleBundleAsync(int id)
    {
        const string sql = "UPDATE dbo.BusinessRuleBundles SET IsActive = 0 WHERE Id = @Id;";
        await using var connection = new SqlConnection(_connectionString);
        await connection.ExecuteAsync(sql, new { Id = id });
    }

    public async Task<BusinessRuleBundle?> GetBusinessRuleBundleByNameAsync(string name)
    {
        const string sql = "SELECT * FROM dbo.BusinessRuleBundles WHERE Name = @Name AND IsActive = 1;";
        const string itemsSql = @"
            SELECT i.*, r.Name as RuleName, r.RuleType 
            FROM dbo.BusinessRuleBundleItems i
            JOIN dbo.BusinessRules r ON i.RuleId = r.Id
            WHERE i.BundleId = @BundleId
            ORDER BY i.SequenceOrder;
        ";
        await using var connection = new SqlConnection(_connectionString);
        var bundle = await connection.QueryFirstOrDefaultAsync<BusinessRuleBundle>(sql, new { Name = name });
        if (bundle != null)
        {
            var items = await connection.QueryAsync<BusinessRuleBundleItem>(itemsSql, new { BundleId = bundle.Id });
            bundle.Items = items.ToList();
        }
        return bundle;
    }

    public async Task<IEnumerable<DbConnectionDefinition>> GetAllDbConnectionsAsync()
    {
        const string sql = "SELECT * FROM dbo.DbConnections ORDER BY Name;";
        await using var connection = new SqlConnection(_connectionString);
        var conns = (await connection.QueryAsync<DbConnectionDefinition>(sql)).ToList();
        foreach (var conn in conns)
        {
            conn.ConnectionString = _encryptionService.Decrypt(conn.ConnectionString);
        }
        return conns;
    }

    public async Task<DbConnectionDefinition?> GetDbConnectionByIdAsync(int id)
    {
        const string sql = "SELECT * FROM dbo.DbConnections WHERE Id = @Id;";
        await using var connection = new SqlConnection(_connectionString);
        var dbConn = await connection.QueryFirstOrDefaultAsync<DbConnectionDefinition>(sql, new { Id = id });
        if (dbConn != null)
        {
            dbConn.ConnectionString = _encryptionService.Decrypt(dbConn.ConnectionString);
        }
        return dbConn;
    }

    public async Task<int> InsertDbConnectionAsync(DbConnectionDefinition conn)
    {
        var encryptedConn = new DbConnectionDefinition
        {
            Name = conn.Name,
            ProviderType = conn.ProviderType,
            ConnectionString = _encryptionService.Encrypt(conn.ConnectionString)
        };

        const string sql = @"
            INSERT INTO dbo.DbConnections (Name, ConnectionString, ProviderType, CreatedAt)
            VALUES (@Name, @ConnectionString, @ProviderType, SYSUTCDATETIME());
            SELECT CAST(SCOPE_IDENTITY() AS INT);
        ";
        await using var connection = new SqlConnection(_connectionString);
        return await connection.ExecuteScalarAsync<int>(sql, encryptedConn);
    }

    public async Task UpdateDbConnectionAsync(DbConnectionDefinition conn)
    {
        var encryptedConn = new DbConnectionDefinition
        {
            Id = conn.Id,
            Name = conn.Name,
            ProviderType = conn.ProviderType,
            ConnectionString = _encryptionService.Encrypt(conn.ConnectionString)
        };

        const string sql = @"
            UPDATE dbo.DbConnections
            SET Name = @Name,
                ConnectionString = @ConnectionString,
                ProviderType = @ProviderType
            WHERE Id = @Id;
        ";
        await using var connection = new SqlConnection(_connectionString);
        await connection.ExecuteAsync(sql, encryptedConn);
    }

    public async Task DeleteDbConnectionAsync(int id)
    {
        const string checkSql = "SELECT COUNT(*) FROM dbo.BusinessRules WHERE ConnectionId = @Id AND IsActive = 1;";
        await using var connection = new SqlConnection(_connectionString);
        var count = await connection.ExecuteScalarAsync<int>(checkSql, new { Id = id });
        if (count > 0)
        {
            throw new InvalidOperationException($"Cannot delete connection. It is being used by {count} business rules.");
        }

        const string sql = "DELETE FROM dbo.DbConnections WHERE Id = @Id;";
        await connection.ExecuteAsync(sql, new { Id = id });
    }
}
