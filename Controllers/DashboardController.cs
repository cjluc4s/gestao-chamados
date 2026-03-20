using gestao_chamados.Data;
using gestao_chamados.Models;
using gestao_chamados.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace gestao_chamados.Controllers;

[Authorize]
public class DashboardController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;

    public DashboardController(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
    {
        _context = context;
        _userManager = userManager;
    }

    public async Task<IActionResult> Index()
    {
        var query = _context.Tickets.AsNoTracking().AsQueryable();
        var currentUserId = _userManager.GetUserId(User);

        if (currentUserId is null)
        {
            return Challenge();
        }

        if (User.IsInRole(RoleNames.User))
        {
            query = query.Where(t => t.CreatedByUserId == currentUserId);
        }
        else if (User.IsInRole(RoleNames.Agent))
        {
            query = query.Where(t => t.AssignedToUserId == currentUserId);
        }

        var data = await query.ToListAsync();

        var model = new DashboardViewModel
        {
            TotalTickets = data.Count,
            OpenTickets = data.Count(t => t.Status == TicketStatus.Open || t.Status == TicketStatus.InProgress || t.Status == TicketStatus.WaitingUser),
            ResolvedTickets = data.Count(t => t.Status == TicketStatus.Resolved || t.Status == TicketStatus.Closed),
            SlaBreachedTickets = data.Count(t => t.DueAtUtc.HasValue && t.DueAtUtc.Value < DateTime.UtcNow && t.Status != TicketStatus.Resolved && t.Status != TicketStatus.Closed),
            ByPriority = data
                .GroupBy(t => t.Priority)
                .ToDictionary(g => g.Key, g => g.Count())
        };

        return View(model);
    }
}
