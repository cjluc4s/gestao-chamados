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

        // Chamado cancelado é exclusão lógica: não entra nos indicadores
        // (continua visível na lista de chamados, filtrando por "Cancelado").
        var query = TicketAccessPolicy.ApplyVisibility(
            _context.Tickets.AsNoTracking().Where(t => t.Status != TicketStatus.Cancelled),
            TicketAccessPolicy.ResolveRole(User),
            currentUserId);

        // Contagens agregadas no banco, em vez de carregar todos os chamados na memória.
        var nowUtc = DateTime.UtcNow;

        // Só chamados que ainda pedem ação entram em "vencido" e "em risco". Mesma conta de
        // SlaPolicy.IsBreached: com o relógio parado vale o instante em que ele parou.
        var pending = query.Where(t => !SlaPolicy.DoneStatuses.Contains(t.Status));

        var model = new DashboardViewModel
        {
            TotalTickets = await query.CountAsync(),
            OpenTickets = await query.CountAsync(t => t.Status == TicketStatus.Open || t.Status == TicketStatus.InProgress || t.Status == TicketStatus.WaitingUser),
            ResolvedTickets = await query.CountAsync(t => t.Status == TicketStatus.Resolved || t.Status == TicketStatus.Closed),
            SlaBreachedTickets = await pending.CountAsync(t => t.DueAtUtc != null && t.DueAtUtc < (t.SlaPausedAtUtc ?? nowUtc)),
            SlaAtRiskTickets = await pending.CountAsync(t => t.SlaWarnedAtUtc != null && t.DueAtUtc != null && t.DueAtUtc >= (t.SlaPausedAtUtc ?? nowUtc)),
            ByPriority = await query
                .GroupBy(t => t.Priority)
                .Select(g => new { Priority = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Priority, x => x.Count)
        };

        return View(model);
    }
}
