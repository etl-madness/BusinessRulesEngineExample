using Microsoft.AspNetCore.Mvc;
using BusinessRulesEngineExample.Services;
using BusinessRulesEngineExample.Models;
using EtlAnalytics.RulesEngine.Models;
using EtlAnalytics.RulesEngine.Services;
using System.Text.Json;

namespace BusinessRulesEngineExample.Controllers;

[ApiController]
[Route("api/[controller]")]
public class RulesController : ControllerBase
{
    private readonly SqlDatabaseService _dbService;
    private readonly BusinessRuleEngine<BusinessRuleContext> _engine;

    public RulesController(SqlDatabaseService dbService, BusinessRuleEngine<BusinessRuleContext> engine)
    {
        _dbService = dbService;
        _engine = engine;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<BusinessRule>>> GetRules()
    {
        try
        {
            var rules = await _dbService.GetBusinessRulesAsync();
            return Ok(rules);
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Internal server error: {ex.Message}");
        }
    }

    [HttpPost("bundle/execute")]
    public async Task<IActionResult> ExecuteBundle([FromQuery] string name)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return BadRequest("Bundle name is required.");
            }

            var bundle = await _dbService.GetBusinessRuleBundleByNameAsync(name);
            if (bundle == null)
            {
                return NotFound($"Bundle '{name}' not found.");
            }

            var logs = new List<string>();
            var context = new BusinessRuleContext
            {
                Log = line => logs.Add($"[SCRIPT] {line}")
            };

            var result = await _engine.ExecuteBundleAsync(bundle, context, log =>
            {
                logs.Add(log);
            });

            var vulnerabilityCount = TryReadInt(result, "CriticalCount")
                ?? context.KveReports.Sum(r => r.Cves?.Count ?? 0);

            return Ok(new
            {
                BundleName = bundle.Name,
                VulnerabilityCount = vulnerabilityCount,
                Result = result,
                Logs = logs
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Bundle execution failed: {ex.Message}");
        }
    }

    private static int? TryReadInt(object? source, string propertyName)
    {
        if (source == null)
        {
            return null;
        }

        if (source is IDictionary<string, object> dictionary && dictionary.TryGetValue(propertyName, out var dictionaryValue))
        {
            return ConvertToInt(dictionaryValue);
        }

        var property = source.GetType().GetProperty(propertyName);
        if (property == null)
        {
            return null;
        }

        return ConvertToInt(property.GetValue(source));
    }

    private static int? ConvertToInt(object? value)
    {
        return value switch
        {
            null => null,
            int intValue => intValue,
            long longValue => (int)longValue,
            short shortValue => shortValue,
            byte byteValue => byteValue,
            JsonElement jsonElement when jsonElement.ValueKind == JsonValueKind.Number && jsonElement.TryGetInt32(out var parsedValue) => parsedValue,
            _ when int.TryParse(value.ToString(), out var parsed) => parsed,
            _ => null
        };
    }

    [HttpGet("bundle")]
    public async Task<ActionResult<IEnumerable<BusinessRuleBundle>>> GetBundledRules()
    {
        try
        {
            var rules = await _dbService.GetBusinessRuleBundlesAsync();
            return Ok(rules);
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Internal server error: {ex.Message}");
        }
    }

    [HttpPost("{id}/execute")]
    public async Task<IActionResult> ExecuteRule(int id)
    {
        try
        {
            var rule = await _dbService.GetBusinessRuleByIdAsync(id);
            if (rule == null)
            {
                return NotFound($"Rule with ID {id} not found.");
            }

            var logs = new List<string>();
            var context = new BusinessRuleContext
            {
                Log = line => logs.Add($"[SCRIPT] {line}")
            };

            var result = await _engine.ExecuteRuleAsync(rule, context, log =>
            {
                logs.Add(log);
            });

            return Ok(new
            {
                RuleName = rule.Name,
                Result = result,
                Logs = logs
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Execution failed: {ex.Message}");
        }
    }
}
