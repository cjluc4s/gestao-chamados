using gestao_chamados.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace gestao_chamados.Controllers;

[Authorize(Roles = RoleNames.Admin)]
public class AdminController : Controller
{
    private readonly UserManager<ApplicationUser> _userManager;

    public AdminController(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public IActionResult Priorities()
    {
        var model = new Dictionary<TicketPriority, TimeSpan>
        {
            [TicketPriority.Low] = TimeSpan.FromHours(24),
            [TicketPriority.Medium] = TimeSpan.FromHours(12),
            [TicketPriority.High] = TimeSpan.FromHours(4),
            [TicketPriority.Critical] = TimeSpan.FromHours(2)
        };

        return View(model);
    }

    public IActionResult Users()
    {
        var users = _userManager.Users
            .OrderBy(u => u.FullName)
            .ToList();

        return View(users);
    }
}
