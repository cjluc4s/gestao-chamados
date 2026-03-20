using System.Globalization;
using System.Text;
using gestao_chamados.Data;
using gestao_chamados.Hubs;
using gestao_chamados.Models;
using gestao_chamados.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace gestao_chamados.Controllers;

[Authorize]
public class TicketsController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IWebHostEnvironment _env;
    private readonly IHubContext<NotificationHub> _hub;

    public TicketsController(ApplicationDbContext context, UserManager<ApplicationUser> userManager, IWebHostEnvironment env, IHubContext<NotificationHub> hub)
    {
        _context = context;
        _userManager = userManager;
        _env = env;
        _hub = hub;
    }

    public async Task<IActionResult> Index(TicketStatus? status, string? userId, DateTime? startDate, DateTime? endDate)
    {
        var tickets = await BuildFilteredListAsync(status, userId, startDate, endDate);
        if (tickets is null) return Challenge();
        return View(tickets);
    }

    public async Task<IActionResult> ExportCsv(TicketStatus? status, string? userId, DateTime? startDate, DateTime? endDate)
    {
        var tickets = await BuildFilteredListAsync(status, userId, startDate, endDate);
        if (tickets is null) return Challenge();

        var sb = new StringBuilder();
        sb.AppendLine("ID;Título;Categoria;Status;Prioridade;Criado por;Atribuído para;Criado em;Limite SLA;SLA");

        foreach (var t in tickets)
        {
            sb.AppendLine(string.Join(";",
                t.Id,
                EscapeCsv(t.Title),
                EscapeCsv(t.Category),
                StatusLabel(t.Status),
                PriorityLabel(t.Priority),
                EscapeCsv(t.CreatedBy),
                EscapeCsv(t.AssignedTo ?? "—"),
                t.CreatedAtUtc.ToLocalTime().ToString("g", CultureInfo.GetCultureInfo("pt-BR")),
                t.DueAtUtc?.ToLocalTime().ToString("g", CultureInfo.GetCultureInfo("pt-BR")) ?? "—",
                t.IsSlaBreached ? "Vencido" : "OK"
            ));
        }

        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        return File(bytes, "text/csv", $"chamados_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
    }

    public async Task<IActionResult> ExportPdf(TicketStatus? status, string? userId, DateTime? startDate, DateTime? endDate)
    {
        var tickets = await BuildFilteredListAsync(status, userId, startDate, endDate);
        if (tickets is null) return Challenge();

        QuestPDF.Settings.License = LicenseType.Community;

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(30);
                page.DefaultTextStyle(x => x.FontSize(9));

                page.Header().Column(col =>
                {
                    col.Item().Text("Help Desk Corporativo — Relatório de Chamados")
                        .Bold().FontSize(16).FontColor(Colors.Blue.Darken2);
                    col.Item().Text($"Gerado em {DateTime.Now:dd/MM/yyyy HH:mm}")
                        .FontSize(8).FontColor(Colors.Grey.Medium);
                    col.Item().PaddingBottom(10);
                });

                page.Content().Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        c.ConstantColumn(30);   // ID
                        c.RelativeColumn(3);     // Título
                        c.RelativeColumn(1.5f);  // Categoria
                        c.RelativeColumn(1.2f);  // Status
                        c.RelativeColumn(1);     // Prioridade
                        c.RelativeColumn(1.5f);  // Criado por
                        c.RelativeColumn(1.5f);  // Atribuído
                        c.RelativeColumn(1.3f);  // Data
                        c.ConstantColumn(45);    // SLA
                    });

                    table.Header(header =>
                    {
                        var headers = new[] { "#", "Título", "Categoria", "Status", "Prioridade", "Criado por", "Atribuído", "Criado em", "SLA" };
                        foreach (var h in headers)
                        {
                            header.Cell().Background(Colors.Blue.Darken2).Padding(4)
                                .Text(h).Bold().FontSize(8).FontColor(Colors.White);
                        }
                    });

                    foreach (var t in tickets)
                    {
                        var bg = tickets.IndexOf(t) % 2 == 0 ? Colors.White : Colors.Grey.Lighten4;
                        var cells = new[]
                        {
                            t.Id.ToString(),
                            t.Title,
                            t.Category,
                            StatusLabel(t.Status),
                            PriorityLabel(t.Priority),
                            t.CreatedBy,
                            t.AssignedTo ?? "—",
                            t.CreatedAtUtc.ToLocalTime().ToString("dd/MM/yy HH:mm"),
                            t.IsSlaBreached ? "⚠ Vencido" : "✓ OK"
                        };
                        foreach (var cell in cells)
                        {
                            table.Cell().Background(bg).Padding(4).Text(cell).FontSize(8);
                        }
                    }
                });

                page.Footer().AlignCenter()
                    .Text(t => { t.Span("Página "); t.CurrentPageNumber(); t.Span(" de "); t.TotalPages(); });
            });
        });

        var pdfBytes = document.GeneratePdf();
        return File(pdfBytes, "application/pdf", $"chamados_{DateTime.Now:yyyyMMdd_HHmmss}.pdf");
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

        if (model.Files is { Count: > 0 })
        {
            await SaveAttachmentsAsync(ticket.Id, currentUserId, model.Files);
        }

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
        await _hub.Clients.Group("Agents").SendAsync("ReceiveNotification", $"Novo chamado #{ticket.Id}: {ticket.Title}");
        await _hub.Clients.Group("Admins").SendAsync("ReceiveNotification", $"Novo chamado #{ticket.Id}: {ticket.Title}");
        return RedirectToAction(nameof(Details), new { id = ticket.Id });
    }

    public async Task<IActionResult> Details(int id)
    {
        var ticket = await _context.Tickets
            .Include(t => t.Category)
            .Include(t => t.CreatedByUser)
            .Include(t => t.AssignedToUser)
            .Include(t => t.Attachments)
                .ThenInclude(a => a.UploadedByUser)
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
                .ToList(),
            Attachments = ticket.Attachments
                .OrderBy(a => a.UploadedAtUtc)
                .Select(a => new AttachmentViewModel
                {
                    Id = a.Id,
                    FileName = a.FileName,
                    ContentType = a.ContentType,
                    SizeInBytes = a.SizeInBytes,
                    UploadedBy = a.UploadedByUser?.FullName ?? a.UploadedByUserId,
                    UploadedAtUtc = a.UploadedAtUtc
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

        var statusLabel = status switch
        {
            TicketStatus.Open => "Aberto",
            TicketStatus.InProgress => "Em andamento",
            TicketStatus.WaitingUser => "Aguardando usuário",
            TicketStatus.Resolved => "Resolvido",
            TicketStatus.Closed => "Fechado",
            _ => status.ToString()
        };
        await _hub.Clients.Group(ticket.CreatedByUserId).SendAsync("ReceiveNotification", $"Chamado #{id} alterado para: {statusLabel}");

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

        var authorName = (await _userManager.GetUserAsync(User))?.FullName ?? "Alguém";
        if (ticket.CreatedByUserId != currentUserId)
        {
            await _hub.Clients.Group(ticket.CreatedByUserId).SendAsync("ReceiveNotification", $"{authorName} comentou no chamado #{id}");
        }
        if (ticket.AssignedToUserId is not null && ticket.AssignedToUserId != currentUserId)
        {
            await _hub.Clients.Group(ticket.AssignedToUserId).SendAsync("ReceiveNotification", $"{authorName} comentou no chamado #{id}");
        }

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

        var agentName = (await _userManager.GetUserAsync(User))?.FullName ?? "Um atendente";
        await _hub.Clients.Group(ticket.CreatedByUserId).SendAsync("ReceiveNotification", $"{agentName} assumiu o chamado #{id}");

        TempData["Notification"] = "Chamado atribuído a você.";
        return RedirectToAction(nameof(Details), new { id });
    }

    public async Task<IActionResult> Download(int id)
    {
        var attachment = await _context.Attachments
            .Include(a => a.Ticket)
            .FirstOrDefaultAsync(a => a.Id == id);

        if (attachment?.Ticket is null)
        {
            return NotFound();
        }

        if (!await CanAccessTicketAsync(attachment.Ticket))
        {
            return Forbid();
        }

        if (!System.IO.File.Exists(attachment.StoredPath))
        {
            return NotFound();
        }

        var bytes = await System.IO.File.ReadAllBytesAsync(attachment.StoredPath);
        return File(bytes, attachment.ContentType, attachment.FileName);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UploadToTicket(int id, List<IFormFile> files)
    {
        var ticket = await _context.Tickets.FirstOrDefaultAsync(t => t.Id == id);
        if (ticket is null)
        {
            return NotFound();
        }

        if (!await CanAccessTicketAsync(ticket))
        {
            return Forbid();
        }

        var currentUserId = _userManager.GetUserId(User);
        if (currentUserId is null)
        {
            return Challenge();
        }

        if (files is { Count: > 0 })
        {
            await SaveAttachmentsAsync(ticket.Id, currentUserId, files);
            TempData["Notification"] = "Anexo(s) enviado(s) com sucesso.";
        }

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
            return Task.FromResult(ticket.AssignedToUserId == currentUserId || ticket.AssignedToUserId == null);
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

    private async Task SaveAttachmentsAsync(int ticketId, string userId, List<IFormFile> files)
    {
        var uploadsDir = Path.Combine(_env.ContentRootPath, "uploads", ticketId.ToString());
        Directory.CreateDirectory(uploadsDir);

        foreach (var file in files)
        {
            if (file.Length <= 0 || file.Length > 10 * 1024 * 1024)
                continue;

            var storedName = $"{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";
            var filePath = Path.Combine(uploadsDir, storedName);

            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            _context.Attachments.Add(new Attachment
            {
                TicketId = ticketId,
                FileName = file.FileName,
                StoredPath = filePath,
                ContentType = file.ContentType,
                SizeInBytes = file.Length,
                UploadedByUserId = userId,
                UploadedAtUtc = DateTime.UtcNow
            });
        }

        await _context.SaveChangesAsync();
    }

    private async Task<List<TicketListItemViewModel>?> BuildFilteredListAsync(
        TicketStatus? status, string? userId, DateTime? startDate, DateTime? endDate)
    {
        var query = _context.Tickets
            .AsNoTracking()
            .Include(t => t.Category)
            .Include(t => t.CreatedByUser)
            .Include(t => t.AssignedToUser)
            .AsQueryable();

        var currentUserId = _userManager.GetUserId(User);
        if (currentUserId is null) return null;

        if (User.IsInRole(RoleNames.User))
            query = query.Where(t => t.CreatedByUserId == currentUserId);
        else if (User.IsInRole(RoleNames.Agent))
            query = query.Where(t => t.AssignedToUserId == currentUserId || t.AssignedToUserId == null);

        if (status.HasValue)
            query = query.Where(t => t.Status == status.Value);

        if (!string.IsNullOrWhiteSpace(userId) && User.IsInRole(RoleNames.Admin))
            query = query.Where(t => t.CreatedByUserId == userId || t.AssignedToUserId == userId);

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

        return await query
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
                IsSlaBreached = t.DueAtUtc.HasValue && t.DueAtUtc.Value < DateTime.UtcNow
                    && t.Status != TicketStatus.Resolved && t.Status != TicketStatus.Closed
            })
            .ToListAsync();
    }

    private static string StatusLabel(TicketStatus s) => s switch
    {
        TicketStatus.Open => "Aberto",
        TicketStatus.InProgress => "Em andamento",
        TicketStatus.WaitingUser => "Aguardando",
        TicketStatus.Resolved => "Resolvido",
        TicketStatus.Closed => "Fechado",
        _ => s.ToString()
    };

    private static string PriorityLabel(TicketPriority p) => p switch
    {
        TicketPriority.Low => "Baixa",
        TicketPriority.Medium => "Média",
        TicketPriority.High => "Alta",
        TicketPriority.Critical => "Crítica",
        _ => p.ToString()
    };

    private static string EscapeCsv(string value) =>
        value.Contains(';') || value.Contains('"') || value.Contains('\n')
            ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
}
