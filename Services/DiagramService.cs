namespace BusinessRulesEngineExample.Services;

using BusinessRulesEngineExample.Models;
using EtlAnalytics.RulesEngine.Models;
using System.Text;
using System.Web;

public class DiagramService
{
    public string GenerateRuleMermaid(BusinessRule rule, DbConnectionDefinition? connection = null)
    {
        if (rule == null) return "graph TD\n    Empty[\"No rule selected\"]";

        var sb = new StringBuilder();
        sb.AppendLine("graph TD");
        sb.AppendLine("    classDef inputStyle fill:#eff6ff,stroke:#3b82f6,stroke-width:2px;");
        sb.AppendLine("    classDef execStyle fill:#fef3c7,stroke:#f59e0b,stroke-width:2px;");
        sb.AppendLine("    classDef dbStyle fill:#e0e7ff,stroke:#6366f1,stroke-width:2px;");
        sb.AppendLine("    classDef outputStyle fill:#f0fdf4,stroke:#22c55e,stroke-width:2px;");

        // Input Context
        sb.AppendLine("    subgraph Input[\"📥 Execution Context Inputs\"]");
        sb.AppendLine("        CtxPrev[\"PreviousResult / @PreviousResultJson\"]");
        sb.AppendLine("        CtxSteps[\"StepResults / @StepResultsJson\"]");
        sb.AppendLine("    end");
        sb.AppendLine("    class Input inputStyle;");

        string connName = connection?.Name ?? (rule.ConnectionId.HasValue ? $"Connection #{rule.ConnectionId}" : "Default Database Connection");
        string dbType = connection?.ProviderType.ToString() ?? "SqlServer";

        sb.AppendLine($"    DB[(\"🗄️ Target Database<br/><b>{HttpUtility.HtmlEncode(connName)}</b> ({dbType})\")]");
        sb.AppendLine("    class DB dbStyle;");

        string ruleTypeLabel = rule.RuleType.ToString();
        string cleanCode = HttpUtility.HtmlEncode(rule.Code ?? "").Replace("\n", " ").Replace("\r", "");
        string truncatedCode = cleanCode.Length > 80 ? cleanCode.Substring(0, 80) + "..." : cleanCode;

        sb.AppendLine($"    Exec[\"⚙️ Rule: <b>{HttpUtility.HtmlEncode(rule.Name)}</b><br/>Type: <i>{ruleTypeLabel}</i><br/>Code Snippet: <code>{truncatedCode}</code>\"]");
        sb.AppendLine("    class Exec execStyle;");

        sb.AppendLine("    Out[\"📤 Execution Output / Step Result\"]");
        sb.AppendLine("    class Out outputStyle;");

        // Connectors
        sb.AppendLine("    CtxPrev --> Exec");
        sb.AppendLine("    CtxSteps --> Exec");
        if (rule.RuleType.ToString() == "TSQL")
        {
            sb.AppendLine("    Exec <-->|Executes Query| DB");
        }
        sb.AppendLine("    Exec --> Out");

        return sb.ToString();
    }

    public string GenerateBundleMermaid(BusinessRuleBundle bundle, List<BusinessRule> allRules)
    {
        if (bundle?.Items == null || !bundle.Items.Any())
            return "graph TD\n    Empty[\"No rules configured in this bundle\"]";

        var sb = new StringBuilder();
        sb.AppendLine("graph TD");
        sb.AppendLine("    classDef stageStyle fill:#f8fafc,stroke:#94a3b8,stroke-width:2px;");
        sb.AppendLine("    classDef parallelStyle fill:#fffbeb,stroke:#d97706,stroke-width:2px;");
        sb.AppendLine("    classDef ruleStyle fill:#ffffff,stroke:#3b82f6,stroke-width:1px;");

        var stageGroups = bundle.Items
            .GroupBy(i => i.SequenceOrder)
            .OrderBy(g => g.Key)
            .ToList();

        string? previousStageId = null;

        foreach (var group in stageGroups)
        {
            int stageOrder = group.Key;
            var items = group.ToList();
            bool isParallel = items.Count > 1;
            string stageId = $"Stage_{stageOrder}";

            string stageTitle = isParallel 
                ? $"⚡ Stage {stageOrder} (Parallel Group - {items.Count} Concurrent Rules)" 
                : $"Stage {stageOrder} (Sequential)";

            sb.AppendLine($"    subgraph {stageId}[\"{stageTitle}\"]");

            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                var matchedRule = allRules.FirstOrDefault(r => r.Id == item.RuleId);
                string ruleNodeId = $"R_{stageOrder}_{item.RuleId}_{i}";
                string ruleName = matchedRule?.Name ?? item.RuleName ?? $"Rule #{item.RuleId}";
                string ruleType = matchedRule?.RuleType.ToString() ?? item.RuleType?.ToString() ?? "Script";

                sb.AppendLine($"        {ruleNodeId}[\"📄 <b>{HttpUtility.HtmlEncode(ruleName)}</b><br/><i>Type: {ruleType}</i>\"]");
                sb.AppendLine($"        class {ruleNodeId} ruleStyle;");
            }

            sb.AppendLine("    end");
            sb.AppendLine($"    class {stageId} {(isParallel ? "parallelStyle" : "stageStyle")};");

            if (previousStageId != null)
            {
                sb.AppendLine($"    {previousStageId} -->|Aggregates Stage Outputs| {stageId}");
            }

            previousStageId = stageId;
        }

        return sb.ToString();
    }
}
