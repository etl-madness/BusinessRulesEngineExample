using Microsoft.AspNetCore.Mvc;
using BusinessRulesEngineExample.Services;
using BusinessRulesEngineExample.Models;
using EtlAnalytics.RulesEngine.Models;
using EtlAnalytics.RulesEngine.Services;

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
