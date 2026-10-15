using gestao_chamados.Data;
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

    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;

    public TicketsApiController(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
    {
        _context = context;
        _userManager = userManager;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(int page = 1, int pageSize = 20)
    {
        var currentUserId = _userManager.GetUserId(User);
        if (currentUserId is null)
        {
            return Unauthorized();
        }

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var query = TicketAccessPolicy.ApplyVisibility(
                _context.Tickets.AsNoTracking(),
                TicketAccessPolicy.ResolveRole(User),
                currentUserId)
            .OrderByDescending(t => t.CreatedAtUtc);

        var totalCount = await query.CountAsync();
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
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
            .ToListAsync();

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
        var currentUserId = _userManager.GetUserId(User);
        if (currentUserId is null)
        {
            return Unauthorized();
        }

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

        var canAccess = TicketAccessPolicy.CanAccess(
            TicketAccessPolicy.ResolveRole(User), currentUserId, ticket.CreatedByUserId, ticket.AssignedToUserId);

        return canAccess ? Ok(ticket) : Forbid();
    }
}
