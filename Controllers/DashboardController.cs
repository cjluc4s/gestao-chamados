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
        var currentUserId = _userManager.GetUserId(User);
        if (currentUserId is null)
        {
            return Challenge();
        }

        var query = TicketAccessPolicy.ApplyVisibility(
            _context.Tickets.AsNoTracking(),
            TicketAccessPolicy.ResolveRole(User),
            currentUserId);

        // Contagens agregadas no banco, em vez de carregar todos os chamados na memória.
        var nowUtc = DateTime.UtcNow;
        var model = new DashboardViewModel
        {
            TotalTickets = await query.CountAsync(),
            OpenTickets = await query.CountAsync(t => t.Status == TicketStatus.Open || t.Status == TicketStatus.InProgress || t.Status == TicketStatus.WaitingUser),
            ResolvedTickets = await query.CountAsync(t => t.Status == TicketStatus.Resolved || t.Status == TicketStatus.Closed),
            SlaBreachedTickets = await query.CountAsync(t => t.DueAtUtc.HasValue && t.DueAtUtc.Value < nowUtc && t.Status != TicketStatus.Resolved && t.Status != TicketStatus.Closed),
            ByPriority = await query
                .GroupBy(t => t.Priority)
                .Select(g => new { Priority = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Priority, x => x.Count)
        };

        return View(model);
    }
}
