using System.Collections.Generic;
using EtlAnalytics.RulesEngine.Models;

namespace BusinessRulesEngineExample.Models;

public class BusinessRuleContext : RuleExecutionContext
{
    public List<KveReportItem> KveReports { get; set; } = new();
}
