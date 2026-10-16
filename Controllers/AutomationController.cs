using gestao_chamados.Data;
using gestao_chamados.Models;
using gestao_chamados.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace gestao_chamados.Controllers;

/// <summary>Administração das regras de automação. Só o Admin.</summary>
[Authorize(Roles = RoleNames.Admin)]
public class AutomationController : Controller
{
    private const int RecentLogs = 15;

    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;

    public AutomationController(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
    {
        _context = context;
        _userManager = userManager;
    }

    public async Task<IActionResult> Index()
    {
        var rules = await _context.AutomationRules.AsNoTracking()
            .Include(r => r.ConditionCategory)
            .Include(r => r.ActionAgent)
            .OrderBy(r => r.Id)
            .ToListAsync();

        var stats = await _context.AutomationLogs.AsNoTracking()
            .GroupBy(l => l.RuleId)
            .Select(g => new { RuleId = g.Key, Runs = g.Count(), Last = g.Max(l => l.ExecutedAtUtc) })
            .ToDictionaryAsync(x => x.RuleId);

        var logs = await _context.AutomationLogs.AsNoTracking()
            .OrderByDescending(l => l.ExecutedAtUtc)
            .Take(RecentLogs)
            .Select(l => new AutomationLogRow(l.ExecutedAtUtc, l.RuleId, l.Rule!.Name, l.TicketId, l.Ticket!.Title, l.Description))
            .ToListAsync();

        return View(new AutomationIndexViewModel
        {
            Rules = rules.Select(r => new AutomationRuleRow(
                r.Id,
                r.Name,
                r.IsEnabled,
                AutomationDescriber.Describe(r, r.ConditionCategory?.Name, r.ActionAgent?.FullName),
                stats.TryGetValue(r.Id, out var s) ? s.Runs : 0,
                stats.TryGetValue(r.Id, out var s2) ? s2.Last : null)).ToList(),
            RecentLogs = logs,
            Templates = AutomationTemplates.All
        });
    }

    public async Task<IActionResult> Create(string? template = null)
    {
        var form = AutomationTemplates.Create(template) ?? new AutomationRuleForm();
        await FillListsAsync(form);
        return View("Form", form);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(AutomationRuleForm form)
    {
        var rule = new AutomationRule { CreatedAtUtc = DateTime.UtcNow };
        if (!await TryApplyAsync(form, rule))
        {
            await FillListsAsync(form);
            return View("Form", form);
        }

        _context.AutomationRules.Add(rule);
        await _context.SaveChangesAsync();

        TempData["Notification"] = $"Regra \"{rule.Name}\" criada.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(int id)
    {
        var rule = await _context.AutomationRules.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id);
        if (rule is null)
        {
            return NotFound();
        }

        var form = AutomationRuleForm.FromRule(rule);
        await FillListsAsync(form);
        return View("Form", form);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, AutomationRuleForm form)
    {
        var rule = await _context.AutomationRules.FirstOrDefaultAsync(r => r.Id == id);
        if (rule is null)
        {
            return NotFound();
        }

        if (!await TryApplyAsync(form, rule))
        {
            form.Id = id;
            await FillListsAsync(form);
            return View("Form", form);
        }

        rule.UpdatedAtUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        TempData["Notification"] = $"Regra \"{rule.Name}\" atualizada.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Toggle(int id)
    {
        var rule = await _context.AutomationRules.FirstOrDefaultAsync(r => r.Id == id);
        if (rule is null)
        {
            return NotFound();
        }

        rule.IsEnabled = !rule.IsEnabled;
        rule.UpdatedAtUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        TempData["Notification"] = rule.IsEnabled ? $"Regra \"{rule.Name}\" ativada." : $"Regra \"{rule.Name}\" pausada.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var rule = await _context.AutomationRules.FirstOrDefaultAsync(r => r.Id == id);
        if (rule is null)
        {
            return NotFound();
        }

        // O histórico de execuções da regra vai junto (cascata); as mudanças já feitas nos chamados ficam.
        _context.AutomationRules.Remove(rule);
        await _context.SaveChangesAsync();

        TempData["Notification"] = $"Regra \"{rule.Name}\" excluída.";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Valida o formulário e copia para a regra. Devolve false (com os erros no ModelState) se algo estiver errado.</summary>
    private async Task<bool> TryApplyAsync(AutomationRuleForm form, AutomationRule rule)
    {
        form.ApplyTo(rule);
        AutomationRuleValidator.Normalize(rule);

        foreach (var error in AutomationRuleValidator.Validate(rule))
        {
            ModelState.AddModelError(error.Field, error.Message);
        }

        if (rule.ConditionCategoryId is { } categoryId && !await _context.Categories.AnyAsync(c => c.Id == categoryId))
        {
            ModelState.AddModelError(nameof(form.ConditionCategoryId), "Categoria inválida.");
        }

        if (rule.ActionAgentId is { } agentId)
        {
            var agents = await _userManager.GetUsersInRoleAsync(RoleNames.Agent);
            if (agents.All(a => a.Id != agentId))
            {
                ModelState.AddModelError(nameof(form.ActionAgentId), "Escolha um usuário com o papel de Atendente.");
            }
        }

        return ModelState.IsValid;
    }

    private async Task FillListsAsync(AutomationRuleForm form)
    {
        form.Categories = await _context.Categories.AsNoTracking()
            .OrderBy(c => c.Name)
            .Select(c => new SelectListItem(c.Name, c.Id.ToString()))
            .ToListAsync();

        form.Agents = (await _userManager.GetUsersInRoleAsync(RoleNames.Agent))
            .OrderBy(a => a.FullName)
            .Select(a => new SelectListItem(a.FullName, a.Id))
            .ToList();
    }
}
