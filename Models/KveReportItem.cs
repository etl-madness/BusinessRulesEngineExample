using System.Collections.Generic;

namespace BusinessRulesEngineExample.Models;

public class KveReportItem
{
    public string KveCampaign { get; set; } = string.Empty;
    public List<string> Cves { get; set; } = new();
    public string Vendor { get; set; } = string.Empty;
    public string Product { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}
