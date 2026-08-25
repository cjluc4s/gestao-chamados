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

    public AdminController(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public IActionResult Priorities()
    {
        var model = Enum.GetValues<TicketPriority>()
            .ToDictionary(p => p, SlaPolicy.GetDeadline);

        return View(model);
    }

    public async Task<IActionResult> Users()
    {
        var users = _userManager.Users
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
        if (user is null)
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
            await _userManager.RemoveFromRolesAsync(user, currentRoles);
        }
        await _userManager.AddToRoleAsync(user, role);

        TempData["Notification"] = $"Papel de {user.FullName} atualizado para {role}.";
        return RedirectToAction(nameof(Users));
    }
}
