using gestao_chamados.Data;
using gestao_chamados.Models;
using gestao_chamados.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace gestao_chamados.Controllers;

[Authorize]
public class TicketsController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;

    public TicketsController(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
    {
        _context = context;
        _userManager = userManager;
    }

    public async Task<IActionResult> Index(TicketStatus? status, string? userId, DateTime? startDate, DateTime? endDate)
    {
        var query = _context.Tickets
            .AsNoTracking()
            .Include(t => t.Category)
            .Include(t => t.CreatedByUser)
            .Include(t => t.AssignedToUser)
            .AsQueryable();

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

        if (status.HasValue)
        {
            query = query.Where(t => t.Status == status.Value);
        }

        if (!string.IsNullOrWhiteSpace(userId) && User.IsInRole(RoleNames.Admin))
        {
            query = query.Where(t => t.CreatedByUserId == userId || t.AssignedToUserId == userId);
        }

        if (startDate.HasValue)
        {
            var startUtc = DateTime.SpecifyKind(startDate.Value.Date, DateTimeKind.Utc);
            query = query.Where(t => t.CreatedAtUtc >= startUtc);
        }

        if (endDate.HasValue)
        {
            var endUtc = DateTime.SpecifyKind(endDate.Value.Date.AddDays(1), DateTimeKind.Utc);
            query = query.Where(t => t.CreatedAtUtc < endUtc);
        }

        var tickets = await query
            .OrderByDescending(t => t.CreatedAtUtc)
            .Select(t => new TicketListItemViewModel
            {
                Id = t.Id,
                Title = t.Title,
                Category = t.Category != null ? t.Category.Name : "Sem categoria",
                Status = t.Status,
                Priority = t.Priority,
                CreatedBy = t.CreatedByUser != null ? t.CreatedByUser.FullName : t.CreatedByUserId,
                AssignedTo = t.AssignedToUser != null ? t.AssignedToUser.FullName : null,
                CreatedAtUtc = t.CreatedAtUtc,
                DueAtUtc = t.DueAtUtc,
                IsSlaBreached = t.DueAtUtc.HasValue && t.DueAtUtc.Value < DateTime.UtcNow && t.Status != TicketStatus.Resolved && t.Status != TicketStatus.Closed
            })
            .ToListAsync();

        return View(tickets);
    }

    public async Task<IActionResult> Create()
    {
        return View(await BuildCreateViewModelAsync());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(TicketCreateViewModel model)
    {
        if (!ModelState.IsValid)
        {
            model.Categories = await GetCategorySelectListAsync();
            return View(model);
        }

        var currentUserId = _userManager.GetUserId(User);
        if (currentUserId is null)
        {
            return Challenge();
        }

        var due = DateTime.UtcNow.Add(SlaPolicy.GetDeadline(model.Priority));

        var ticket = new Ticket
        {
            Title = model.Title,
            Description = model.Description,
            Priority = model.Priority,
            CategoryId = model.CategoryId,
            Status = TicketStatus.Open,
            CreatedByUserId = currentUserId,
            CreatedAtUtc = DateTime.UtcNow,
            DueAtUtc = due
        };

        _context.Tickets.Add(ticket);
        await _context.SaveChangesAsync();

        _context.TicketStatusHistory.Add(new TicketStatusHistory
        {
            TicketId = ticket.Id,
            FromStatus = null,
            ToStatus = TicketStatus.Open,
            ChangedByUserId = currentUserId,
            ChangedAtUtc = DateTime.UtcNow
        });

        await _context.SaveChangesAsync();

        TempData["Notification"] = "Chamado criado com sucesso.";
        return RedirectToAction(nameof(Details), new { id = ticket.Id });
    }

    public async Task<IActionResult> Details(int id)
    {
        var ticket = await _context.Tickets
            .Include(t => t.Category)
            .Include(t => t.CreatedByUser)
            .Include(t => t.AssignedToUser)
            .Include(t => t.Comments)
                .ThenInclude(c => c.AuthorUser)
            .Include(t => t.StatusHistory)
                .ThenInclude(h => h.ChangedByUser)
            .FirstOrDefaultAsync(t => t.Id == id);

        if (ticket is null)
        {
            return NotFound();
        }

        if (!await CanAccessTicketAsync(ticket))
        {
            return Forbid();
        }

        var commentAuthorIds = ticket.Comments
            .Where(c => c.AuthorUser is not null)
            .Select(c => c.AuthorUser!)
            .DistinctBy(u => u.Id)
            .ToList();

        var agentIds = new HashSet<string>();
        foreach (var user in commentAuthorIds)
        {
            if (await _userManager.IsInRoleAsync(user, RoleNames.Agent))
            {
                agentIds.Add(user.Id);
            }
        }

        var model = new TicketDetailsViewModel
        {
            Id = ticket.Id,
            Title = ticket.Title,
            Description = ticket.Description,
            Category = ticket.Category?.Name ?? "Sem categoria",
            Status = ticket.Status,
            Priority = ticket.Priority,
            CreatedBy = ticket.CreatedByUser?.FullName ?? ticket.CreatedByUserId,
            AssignedTo = ticket.AssignedToUser?.FullName,
            CreatedAtUtc = ticket.CreatedAtUtc,
            DueAtUtc = ticket.DueAtUtc,
            IsSlaBreached = ticket.DueAtUtc.HasValue && ticket.DueAtUtc.Value < DateTime.UtcNow && ticket.Status != TicketStatus.Resolved && ticket.Status != TicketStatus.Closed,
            Comments = ticket.Comments
                .OrderBy(c => c.CreatedAtUtc)
                .Select(c => new TicketCommentViewModel
                {
                    Author = c.AuthorUser?.FullName ?? c.AuthorUserId,
                    IsAgent = c.AuthorUser is not null && agentIds.Contains(c.AuthorUser.Id),
                    Message = c.Message,
                    CreatedAtUtc = c.CreatedAtUtc
                })
                .ToList(),
            History = ticket.StatusHistory
                .OrderByDescending(h => h.ChangedAtUtc)
                .Select(h => new TicketStatusHistoryViewModel
                {
                    FromStatus = h.FromStatus,
                    ToStatus = h.ToStatus,
                    ChangedBy = h.ChangedByUser != null ? h.ChangedByUser.FullName : h.ChangedByUserId,
                    ChangedAtUtc = h.ChangedAtUtc
                })
                .ToList()
        };

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = RoleNames.Admin + "," + RoleNames.Agent)]
    public async Task<IActionResult> ChangeStatus(int id, TicketStatus status)
    {
        var ticket = await _context.Tickets.FirstOrDefaultAsync(t => t.Id == id);
        if (ticket is null)
        {
            return NotFound();
        }

        if (!await CanAccessTicketAsync(ticket) && !User.IsInRole(RoleNames.Admin))
        {
            return Forbid();
        }

        if (ticket.Status == status)
        {
            return RedirectToAction(nameof(Details), new { id });
        }

        var currentUserId = _userManager.GetUserId(User);
        if (currentUserId is null)
        {
            return Challenge();
        }

        var previous = ticket.Status;
        ticket.Status = status;
        ticket.UpdatedAtUtc = DateTime.UtcNow;

        _context.TicketStatusHistory.Add(new TicketStatusHistory
        {
            TicketId = ticket.Id,
            FromStatus = previous,
            ToStatus = status,
            ChangedByUserId = currentUserId,
            ChangedAtUtc = DateTime.UtcNow
        });

        await _context.SaveChangesAsync();
        TempData["Notification"] = "Status atualizado.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddComment(int id, string message)
    {
        var ticket = await _context.Tickets.FirstOrDefaultAsync(t => t.Id == id);
        if (ticket is null)
        {
            return NotFound();
        }

        if (!await CanAccessTicketAsync(ticket) || string.IsNullOrWhiteSpace(message))
        {
            return RedirectToAction(nameof(Details), new { id });
        }

        var currentUserId = _userManager.GetUserId(User);
        if (currentUserId is null)
        {
            return Challenge();
        }

        _context.TicketComments.Add(new TicketComment
        {
            TicketId = id,
            Message = message.Trim(),
            AuthorUserId = currentUserId,
            CreatedAtUtc = DateTime.UtcNow
        });

        ticket.UpdatedAtUtc = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        TempData["Notification"] = "Interação registrada.";

        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = RoleNames.Agent)]
    public async Task<IActionResult> AssignToMe(int id)
    {
        var ticket = await _context.Tickets.FirstOrDefaultAsync(t => t.Id == id);
        if (ticket is null)
        {
            return NotFound();
        }

        var currentUserId = _userManager.GetUserId(User);
        if (currentUserId is null)
        {
            return Challenge();
        }

        ticket.AssignedToUserId = currentUserId;
        ticket.UpdatedAtUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        TempData["Notification"] = "Chamado atribuído a você.";
        return RedirectToAction(nameof(Details), new { id });
    }

    private Task<bool> CanAccessTicketAsync(Ticket ticket)
    {
        if (User.IsInRole(RoleNames.Admin))
        {
            return Task.FromResult(true);
        }

        var currentUserId = _userManager.GetUserId(User);
        if (currentUserId is null)
        {
            return Task.FromResult(false);
        }

        if (User.IsInRole(RoleNames.User))
        {
            return Task.FromResult(ticket.CreatedByUserId == currentUserId);
        }

        if (User.IsInRole(RoleNames.Agent))
        {
            return Task.FromResult(ticket.AssignedToUserId == currentUserId);
        }

        return Task.FromResult(false);
    }

    private async Task<TicketCreateViewModel> BuildCreateViewModelAsync()
    {
        return new TicketCreateViewModel
        {
            Categories = await GetCategorySelectListAsync()
        };
    }

    private async Task<List<SelectListItem>> GetCategorySelectListAsync()
    {
        return await _context.Categories
            .AsNoTracking()
            .OrderBy(c => c.Name)
            .Select(c => new SelectListItem(c.Name, c.Id.ToString()))
            .ToListAsync();
    }
}
