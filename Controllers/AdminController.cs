using gestao_chamados.Data;
using gestao_chamados.Models;
using gestao_chamados.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace gestao_chamados.Controllers;

[Authorize(Roles = RoleNames.Admin)]
public class AdminController : Controller
{
    private static readonly string[] AssignableRoles = { RoleNames.Admin, RoleNames.Agent, RoleNames.User };

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SlaCalculator _sla;

    public AdminController(UserManager<ApplicationUser> userManager, SlaCalculator sla)
    {
        _userManager = userManager;
        _sla = sla;
    }

    public IActionResult Priorities()
    {
        var calendar = _sla.Calendar;
        var model = new SlaSettingsViewModel
        {
            Priorities = Enum.GetValues<TicketPriority>()
                .OrderByDescending(p => p)
                .Select(p => new SlaPriorityRow(p, _sla.Budget(p), _sla.UsesBusinessHours(p)))
                .ToList(),
            BusinessHoursEnabled = calendar is not null,
            TimeZone = calendar?.Zone.DisplayName ?? string.Empty,
            Start = calendar?.Start ?? TimeSpan.Zero,
            End = calendar?.End ?? TimeSpan.Zero,
            Days = calendar?.Days.OrderBy(d => d == DayOfWeek.Sunday ? 7 : (int)d).ToList() ?? [],
            Holidays = calendar?.Holidays.OrderBy(h => h).ToList() ?? [],
            WarnAtPercent = (int)Math.Round(_sla.WarnAt * 100),
            MonitorIntervalSeconds = _sla.Options.MonitorIntervalSeconds
        };

        return View(model);
    }

    public async Task<IActionResult> Users()
    {
        var users = _userManager.Users
            .Where(u => u.Id != AutomationUser.Id) // usuário interno das regras de automação
            .OrderBy(u => u.FullName)
            .ToList();

        var model = new List<UserRoleViewModel>();
        foreach (var user in users)
        {
            var roles = await _userManager.GetRolesAsync(user);
            model.Add(new UserRoleViewModel
            {
                Id = user.Id,
                FullName = user.FullName,
                Email = user.Email ?? string.Empty,
                UserName = user.UserName ?? string.Empty,
                Role = roles.FirstOrDefault() ?? "Sem papel"
            });
        }

        ViewBag.AssignableRoles = AssignableRoles;
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangeRole(string userId, string role)
    {
        if (!AssignableRoles.Contains(role))
        {
            return BadRequest();
        }

        var user = await _userManager.FindByIdAsync(userId);
        if (user is null || user.Id == AutomationUser.Id)
        {
            return NotFound();
        }

        var currentUserId = _userManager.GetUserId(User);
        if (user.Id == currentUserId && role != RoleNames.Admin)
        {
            TempData["Notification"] = "Você não pode remover seu próprio papel de Administrador.";
            return RedirectToAction(nameof(Users));
        }

        var currentRoles = await _userManager.GetRolesAsync(user);
        if (currentRoles.Count > 0)
        {
            var removeResult = await _userManager.RemoveFromRolesAsync(user, currentRoles);
            if (!removeResult.Succeeded)
            {
                TempData["Notification"] = $"Não foi possível alterar o papel de {user.FullName}.";
                return RedirectToAction(nameof(Users));
            }
        }

        var addResult = await _userManager.AddToRoleAsync(user, role);
        if (!addResult.Succeeded)
        {
            // Não deixa o usuário sem papel nenhum se a troca falhar no meio.
            await _userManager.AddToRolesAsync(user, currentRoles);
            TempData["Notification"] = $"Não foi possível alterar o papel de {user.FullName}.";
            return RedirectToAction(nameof(Users));
        }

        TempData["Notification"] = $"Papel de {user.FullName} atualizado para {role}.";
        return RedirectToAction(nameof(Users));
    }
}
