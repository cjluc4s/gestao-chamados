using gestao_chamados.Data;
using gestao_chamados.Models;
using gestao_chamados.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace gestao_chamados.Controllers;

/// <summary>
/// Métricas de atendimento. O Admin vê tudo (e o ranking por atendente); o Atendente vê só os
/// chamados atribuídos a ele. Usuário comum não tem acesso.
/// </summary>
[Authorize(Roles = RoleNames.Admin + "," + RoleNames.Agent)]
public class MetricsController : Controller
{
    private static readonly int[] AllowedPeriods = [7, 30, 90];

    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SlaCalculator _sla;

    public MetricsController(ApplicationDbContext context, UserManager<ApplicationUser> userManager, SlaCalculator sla)
    {
        _context = context;
        _userManager = userManager;
        _sla = sla;
    }

    public async Task<IActionResult> Index(int days = 30)
    {
        var currentUserId = _userManager.GetUserId(User);
        if (currentUserId is null)
        {
            return Challenge();
        }

        if (!AllowedPeriods.Contains(days))
        {
            days = 30;
        }

        var isAdmin = User.IsInRole(RoleNames.Admin);
        var to = DateTime.UtcNow;
        var from = to.AddDays(-days);

        // Chamado cancelado é exclusão lógica: não conta em nenhuma métrica.
        var query = _context.Tickets.AsNoTracking().Where(t => t.Status != TicketStatus.Cancelled);
        if (!isAdmin)
        {
            query = query.Where(t => t.AssignedToUserId == currentUserId);
        }

        var rows = await query
            .Where(t => t.CreatedAtUtc >= from
                || (t.ResolvedAtUtc != null && t.ResolvedAtUtc >= from)
                || !SlaPolicy.DoneStatuses.Contains(t.Status))
            .Select(t => new TicketMetricRow(
                t.Id,
                t.Priority,
                t.Category != null ? t.Category.Name : "Sem categoria",
                t.Status,
                t.CreatedAtUtc,
                t.FirstResponseAtUtc,
                t.ResolvedAtUtc,
                t.SlaPausedSeconds,
                t.SlaPausedAtUtc,
                t.DueAtUtc,
                t.AssignedToUserId,
                t.AssignedToUser != null ? t.AssignedToUser.FullName : null,
                t.Feedback != null ? t.Feedback.Rating : (int?)null))
            .ToListAsync();

        var zone = _sla.Calendar?.Zone ?? TimeZoneInfo.Local;

        return View(new MetricsViewModel
        {
            Days = days,
            IsAdmin = isAdmin,
            UsesBusinessHours = _sla.Calendar is not null,
            FromUtc = from,
            ToUtc = to,
            Report = MetricsCalculator.Compute(rows, from, to, _sla, zone)
        });
    }
}
