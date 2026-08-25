using gestao_chamados.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace gestao_chamados.Controllers.Api;

[Authorize]
[ApiController]
[Route("api/tickets")]
public class TicketsApiController : ControllerBase
{
    private const int MaxPageSize = 100;

    private readonly Data.ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;

    public TicketsApiController(Data.ApplicationDbContext context, UserManager<ApplicationUser> userManager)
    {
        _context = context;
        _userManager = userManager;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(int page = 1, int pageSize = 20)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var query = _context.Tickets
            .AsNoTracking()
            .Select(t => new
            {
                t.Id,
                t.Title,
                t.Status,
                t.Priority,
                t.CreatedAtUtc,
                t.DueAtUtc,
                t.CreatedByUserId,
                t.AssignedToUserId
            })
            .AsQueryable();

        var currentUserId = _userManager.GetUserId(User);
        if (currentUserId is null)
        {
            return Unauthorized();
        }

        if (User.IsInRole(RoleNames.Admin))
        {
            // sem filtro adicional: Admin enxerga todos os chamados
        }
        else if (User.IsInRole(RoleNames.User))
        {
            query = query.Where(t => t.CreatedByUserId == currentUserId);
        }
        else if (User.IsInRole(RoleNames.Agent))
        {
            query = query.Where(t => t.AssignedToUserId == currentUserId || t.AssignedToUserId == null);
        }
        else
        {
            // Usuário autenticado sem papel reconhecido: nega acesso por padrão.
            query = query.Where(t => false);
        }

        query = query.OrderByDescending(t => t.CreatedAtUtc);

        var totalCount = await query.CountAsync();
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        return Ok(new
        {
            items,
            page,
            pageSize,
            totalCount,
            totalPages = totalCount == 0 ? 1 : (int)Math.Ceiling(totalCount / (double)pageSize)
        });
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var ticket = await _context.Tickets
            .AsNoTracking()
            .Where(t => t.Id == id)
            .Select(t => new
            {
                t.Id,
                t.Title,
                t.Description,
                t.Status,
                t.Priority,
                t.CreatedAtUtc,
                t.DueAtUtc,
                t.CreatedByUserId,
                t.AssignedToUserId
            })
            .FirstOrDefaultAsync();

        if (ticket is null)
        {
            return NotFound();
        }

        var currentUserId = _userManager.GetUserId(User);
        if (currentUserId is null)
        {
            return Unauthorized();
        }

        if (User.IsInRole(RoleNames.Admin)
            || (User.IsInRole(RoleNames.User) && ticket.CreatedByUserId == currentUserId)
            || (User.IsInRole(RoleNames.Agent) && (ticket.AssignedToUserId == currentUserId || ticket.AssignedToUserId == null)))
        {
            return Ok(ticket);
        }

        return Forbid();
    }
}
