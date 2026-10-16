using System.Globalization;
using System.Text;
using gestao_chamados.Data;
using gestao_chamados.Models;
using gestao_chamados.Models.ViewModels;
using gestao_chamados.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace gestao_chamados.Controllers;

[Authorize]
public class TicketsController : Controller
{
    private const int PageSize = 20;
    private const int MaxCommentLength = 2000;
    private const int MaxFileNameLength = 200;
    private const string UploadsFolder = "uploads";

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly FileExtensionContentTypeProvider ContentTypeProvider = new();

    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IWebHostEnvironment _env;
    private readonly TicketNotifier _notifier;
    private readonly SlaCalculator _sla;

    public TicketsController(ApplicationDbContext context, UserManager<ApplicationUser> userManager, IWebHostEnvironment env, TicketNotifier notifier, SlaCalculator sla)
    {
        _context = context;
        _userManager = userManager;
        _env = env;
        _notifier = notifier;
        _sla = sla;
    }

    public async Task<IActionResult> Index(TicketStatus? status, string? userId, DateTime? startDate, DateTime? endDate, int page = 1)
    {
        var query = BuildFilteredQuery(status, userId, startDate, endDate);
        if (query is null) return Challenge();

        var totalCount = await query.CountAsync();
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)PageSize));
        page = Math.Clamp(page, 1, totalPages);

        var items = await ProjectToListItems(query.Skip((page - 1) * PageSize).Take(PageSize), DateTime.UtcNow).ToListAsync();

        var model = new PagedTicketListViewModel
        {
            Items = items,
            Page = page,
            PageSize = PageSize,
            TotalCount = totalCount,
            Status = status,
            StartDate = startDate,
            EndDate = endDate
        };

        return View(model);
    }

    public async Task<IActionResult> ExportCsv(TicketStatus? status, string? userId, DateTime? startDate, DateTime? endDate)
    {
        var query = BuildFilteredQuery(status, userId, startDate, endDate);
        if (query is null) return Challenge();
        var tickets = await ProjectToListItems(query, DateTime.UtcNow).ToListAsync();

        var sb = new StringBuilder();
        sb.AppendLine("ID;Título;Categoria;Status;Prioridade;Criado por;Atribuído para;Criado em;Limite SLA;SLA");

        foreach (var t in tickets)
        {
            sb.AppendLine(string.Join(";",
                t.Id,
                CsvFormatter.Escape(t.Title),
                CsvFormatter.Escape(t.Category),
                t.Status.Label(),
                t.Priority.Label(),
                CsvFormatter.Escape(t.CreatedBy),
                CsvFormatter.Escape(t.AssignedTo ?? "—"),
                t.CreatedAtUtc.ToLocalTime().ToString("g", PtBr),
                t.DueAtUtc?.ToLocalTime().ToString("g", PtBr) ?? "—",
                t.SlaLabel
            ));
        }

        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        return File(bytes, "text/csv", $"chamados_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
    }

    public async Task<IActionResult> ExportPdf(TicketStatus? status, string? userId, DateTime? startDate, DateTime? endDate)
    {
        var query = BuildFilteredQuery(status, userId, startDate, endDate);
        if (query is null) return Challenge();
        var tickets = await ProjectToListItems(query, DateTime.UtcNow).ToListAsync();

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

                    for (var i = 0; i < tickets.Count; i++)
                    {
                        var t = tickets[i];
                        var bg = i % 2 == 0 ? Colors.White : Colors.Grey.Lighten4;
                        var cells = new[]
                        {
                            t.Id.ToString(),
                            t.Title,
                            t.Category,
                            t.Status.Label(),
                            t.Priority.Label(),
                            t.CreatedBy,
                            t.AssignedTo ?? "—",
                            t.CreatedAtUtc.ToLocalTime().ToString("dd/MM/yy HH:mm"),
                            t.SlaLabel
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
        await ValidatePriorityAndCategoryAsync(model.Priority, model.CategoryId);

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

        var now = DateTime.UtcNow;
        var ticket = new Ticket
        {
            Title = model.Title.Trim(),
            Description = model.Description.Trim(),
            Priority = model.Priority,
            CategoryId = model.CategoryId,
            Status = TicketStatus.Open,
            CreatedByUserId = currentUserId,
            CreatedAtUtc = now
        };
        _sla.Start(ticket);

        // Chamado e histórico inicial no mesmo SaveChanges: ou grava os dois, ou nenhum.
        ticket.StatusHistory.Add(new TicketStatusHistory
        {
            FromStatus = null,
            ToStatus = TicketStatus.Open,
            ChangedByUserId = currentUserId,
            ChangedAtUtc = now
        });

        _context.Tickets.Add(ticket);
        await _context.SaveChangesAsync();

        var notification = "Chamado criado com sucesso.";
        if (model.Files is { Count: > 0 })
        {
            var (_, rejected) = await SaveAttachmentsAsync(ticket.Id, currentUserId, model.Files);
            if (rejected > 0)
            {
                notification += " " + RejectedFilesMessage(rejected);
            }
        }

        TempData["Notification"] = notification;
        await NotifyAsync("Agents", $"Novo chamado #{ticket.Id}: {ticket.Title}");
        await NotifyAsync("Admins", $"Novo chamado #{ticket.Id}: {ticket.Title}");
        await NotifyChangedAsync(ticket, "created");
        return RedirectToAction(nameof(Details), new { id = ticket.Id });
    }

    public async Task<IActionResult> Details(int id)
    {
        var ticket = await _context.Tickets
            .AsNoTracking()
            .AsSplitQuery()
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

        if (!CanAccessTicket(ticket))
        {
            return Forbid();
        }

        var commentAuthors = ticket.Comments
            .Where(c => c.AuthorUser is not null)
            .Select(c => c.AuthorUser!)
            .DistinctBy(u => u.Id)
            .ToList();

        // Admin e Atendente respondem pela equipe de suporte.
        var staffIds = new HashSet<string>();
        foreach (var user in commentAuthors)
        {
            if (await _userManager.IsInRoleAsync(user, RoleNames.Agent) || await _userManager.IsInRoleAsync(user, RoleNames.Admin))
            {
                staffIds.Add(user.Id);
            }
        }

        var isAdmin = User.IsInRole(RoleNames.Admin);
        var isCreator = ticket.CreatedByUserId == _userManager.GetUserId(User);
        var canChangeStatus = isAdmin || User.IsInRole(RoleNames.Agent);
        var canAssign = isAdmin && !TicketWorkflowPolicy.IsFinal(ticket.Status);
        var sla = _sla.Evaluate(ticket, DateTime.UtcNow);

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
            IsSlaBreached = sla.Breached,
            Sla = sla,
            IsSlaEscalated = ticket.SlaEscalatedAtUtc.HasValue,
            SlaUsesBusinessHours = _sla.UsesBusinessHours(ticket.Priority),
            AllowedStatuses = canChangeStatus
                ? TicketWorkflowPolicy.GetAllowedTransitions(ticket.Status, isAdmin).ToList()
                : [],
            CanEdit = TicketWorkflowPolicy.CanEdit(ticket.Status, isAdmin, isCreator),
            CanCancel = TicketWorkflowPolicy.CanCancel(ticket.Status, isAdmin, isCreator),
            CanAssign = canAssign,
            AssignedToUserId = ticket.AssignedToUserId,
            AssignableAgents = canAssign ? await GetAgentSelectListAsync(ticket.AssignedToUser) : [],
            Comments = ticket.Comments
                .OrderBy(c => c.CreatedAtUtc)
                .Select(c => new TicketCommentViewModel
                {
                    Author = c.AuthorUser?.FullName ?? c.AuthorUserId,
                    IsStaff = staffIds.Contains(c.AuthorUserId),
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

        if (!CanAccessTicket(ticket))
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

        var isAdmin = User.IsInRole(RoleNames.Admin);
        if (!TicketWorkflowPolicy.CanTransition(ticket.Status, status, isAdmin))
        {
            TempData["Notification"] = $"Não é possível mudar o status de \"{ticket.Status.Label()}\" para \"{status.Label()}\".";
            return RedirectToAction(nameof(Details), new { id });
        }

        var now = DateTime.UtcNow;
        var previous = ticket.Status;
        ticket.Status = status;
        ticket.UpdatedAtUtc = now;
        _sla.OnStatusChanged(ticket, previous, status, now);

        // Atendente que movimenta um chamado sem dono passa a ser o responsável,
        // senão ele fica "Em andamento" sem ninguém atribuído.
        if (ticket.AssignedToUserId is null && TicketAccessPolicy.ResolveRole(User) == RoleNames.Agent)
        {
            ticket.AssignedToUserId = currentUserId;
        }

        _context.TicketStatusHistory.Add(new TicketStatusHistory
        {
            TicketId = ticket.Id,
            FromStatus = previous,
            ToStatus = status,
            ChangedByUserId = currentUserId,
            ChangedAtUtc = now
        });

        await _context.SaveChangesAsync();

        var message = $"Chamado #{id} alterado para: {status.Label()}";
        await NotifyAsync(ticket.CreatedByUserId, message);
        if (ticket.AssignedToUserId is not null && ticket.AssignedToUserId != currentUserId)
        {
            await NotifyAsync(ticket.AssignedToUserId, message);
        }

        await NotifyChangedAsync(ticket, "status");
        TempData["Notification"] = "Status atualizado.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddComment(int id, string? message)
    {
        var ticket = await _context.Tickets.FirstOrDefaultAsync(t => t.Id == id);
        if (ticket is null)
        {
            return NotFound();
        }

        if (!CanAccessTicket(ticket))
        {
            return Forbid();
        }

        var currentUserId = _userManager.GetUserId(User);
        if (currentUserId is null)
        {
            return Challenge();
        }

        message = message?.Trim();
        if (string.IsNullOrEmpty(message))
        {
            TempData["Notification"] = "Digite uma mensagem antes de responder.";
            return RedirectToAction(nameof(Details), new { id });
        }

        if (message.Length > MaxCommentLength)
        {
            TempData["Notification"] = $"A mensagem pode ter no máximo {MaxCommentLength} caracteres.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var now = DateTime.UtcNow;
        _context.TicketComments.Add(new TicketComment
        {
            TicketId = id,
            Message = message,
            AuthorUserId = currentUserId,
            CreatedAtUtc = now
        });

        ticket.UpdatedAtUtc = now;

        // Resposta do solicitante encerra a espera: o chamado volta para o atendimento.
        if (ticket.Status == TicketStatus.WaitingUser && ticket.CreatedByUserId == currentUserId)
        {
            ticket.Status = TicketStatus.InProgress;
            _sla.OnStatusChanged(ticket, TicketStatus.WaitingUser, TicketStatus.InProgress, now);
            _context.TicketStatusHistory.Add(new TicketStatusHistory
            {
                TicketId = id,
                FromStatus = TicketStatus.WaitingUser,
                ToStatus = TicketStatus.InProgress,
                ChangedByUserId = currentUserId,
                ChangedAtUtc = now
            });
        }

        await _context.SaveChangesAsync();

        var authorName = (await _userManager.GetUserAsync(User))?.FullName ?? "Alguém";
        var notification = $"{authorName} comentou no chamado #{id}";
        if (ticket.CreatedByUserId != currentUserId)
        {
            await NotifyAsync(ticket.CreatedByUserId, notification);
        }
        if (ticket.AssignedToUserId is null)
        {
            await NotifyAsync("Agents", notification);
        }
        else if (ticket.AssignedToUserId != currentUserId)
        {
            await NotifyAsync(ticket.AssignedToUserId, notification);
        }

        await NotifyChangedAsync(ticket, "comment");
        TempData["Notification"] = "Interação registrada.";

        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = RoleNames.Agent)]
    public async Task<IActionResult> AssignToMe(int id)
    {
        var ticket = await _context.Tickets.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id);
        if (ticket is null)
        {
            return NotFound();
        }

        // CanAccessTicket já garante, para Agente, que o chamado está
        // sem atribuição ou já é dele — bloqueia "roubar" chamado de outro agente.
        if (!CanAccessTicket(ticket))
        {
            return Forbid();
        }

        var currentUserId = _userManager.GetUserId(User);
        if (currentUserId is null)
        {
            return Challenge();
        }

        if (ticket.AssignedToUserId == currentUserId)
        {
            return RedirectToAction(nameof(Details), new { id });
        }

        if (TicketWorkflowPolicy.IsFinal(ticket.Status))
        {
            TempData["Notification"] = "Chamado encerrado não pode ser assumido.";
            return RedirectToAction(nameof(Details), new { id });
        }

        // Update condicional e atômico: se dois atendentes clicarem ao mesmo
        // tempo, só o primeiro assume o chamado.
        var updated = await _context.Tickets
            .Where(t => t.Id == id && t.AssignedToUserId == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.AssignedToUserId, currentUserId)
                .SetProperty(t => t.UpdatedAtUtc, DateTime.UtcNow));

        if (updated == 0)
        {
            TempData["Notification"] = "Este chamado acabou de ser assumido por outro atendente.";
            return RedirectToAction(nameof(Index));
        }

        var agentName = (await _userManager.GetUserAsync(User))?.FullName ?? "Um atendente";
        await NotifyAsync(ticket.CreatedByUserId, $"{agentName} assumiu o chamado #{id}");

        await _notifier.TicketChangedAsync(id, "assignment", currentUserId, ticket.CreatedByUserId, currentUserId, ticket.AssignedToUserId, assignmentChanged: true);
        TempData["Notification"] = "Chamado atribuído a você.";
        return RedirectToAction(nameof(Details), new { id });
    }

    public async Task<IActionResult> Edit(int id)
    {
        var ticket = await _context.Tickets.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id);
        if (ticket is null)
        {
            return NotFound();
        }

        if (!CanAccessTicket(ticket))
        {
            return Forbid();
        }

        if (!CanEditTicket(ticket))
        {
            TempData["Notification"] = "Este chamado não pode mais ser editado.";
            return RedirectToAction(nameof(Details), new { id });
        }

        return View(new TicketEditViewModel
        {
            Id = ticket.Id,
            Title = ticket.Title,
            Description = ticket.Description,
            Priority = ticket.Priority,
            CategoryId = ticket.CategoryId,
            Categories = await GetCategorySelectListAsync()
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, TicketEditViewModel model)
    {
        var ticket = await _context.Tickets.FirstOrDefaultAsync(t => t.Id == id);
        if (ticket is null)
        {
            return NotFound();
        }

        if (!CanAccessTicket(ticket))
        {
            return Forbid();
        }

        var currentUserId = _userManager.GetUserId(User);
        if (currentUserId is null)
        {
            return Challenge();
        }

        // Revalida no POST: o chamado pode ter sido assumido enquanto o formulário estava aberto.
        if (!CanEditTicket(ticket))
        {
            TempData["Notification"] = "Este chamado não pode mais ser editado.";
            return RedirectToAction(nameof(Details), new { id });
        }

        await ValidatePriorityAndCategoryAsync(model.Priority, model.CategoryId);

        if (!ModelState.IsValid)
        {
            model.Id = id;
            model.Categories = await GetCategorySelectListAsync();
            return View(model);
        }

        var now = DateTime.UtcNow;
        var priorityChanged = ticket.Priority != model.Priority;
        ticket.Title = model.Title.Trim();
        ticket.Description = model.Description.Trim();
        ticket.CategoryId = model.CategoryId;
        ticket.Priority = model.Priority;
        ticket.UpdatedAtUtc = now;
        if (priorityChanged)
        {
            // O prazo sempre conta a partir da abertura: subir a prioridade de um chamado antigo
            // pode deixá-lo imediatamente vencido, que é o que se quer sinalizar.
            _sla.RecalculateDue(ticket, now);
        }

        await _context.SaveChangesAsync();

        var message = priorityChanged
            ? $"Chamado #{id} editado: prioridade agora é {model.Priority.Label()}"
            : $"Chamado #{id} editado";
        await NotifyOthersAsync(currentUserId, message, ticket.CreatedByUserId, ticket.AssignedToUserId ?? "Agents");

        await NotifyChangedAsync(ticket, "edit");
        TempData["Notification"] = priorityChanged
            ? "Chamado atualizado. O limite do SLA foi recalculado para a nova prioridade."
            : "Chamado atualizado.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int id)
    {
        var ticket = await _context.Tickets.FirstOrDefaultAsync(t => t.Id == id);
        if (ticket is null)
        {
            return NotFound();
        }

        if (!CanAccessTicket(ticket))
        {
            return Forbid();
        }

        var currentUserId = _userManager.GetUserId(User);
        if (currentUserId is null)
        {
            return Challenge();
        }

        var isAdmin = User.IsInRole(RoleNames.Admin);
        if (!TicketWorkflowPolicy.CanCancel(ticket.Status, isAdmin, ticket.CreatedByUserId == currentUserId))
        {
            TempData["Notification"] = "Este chamado não pode ser cancelado.";
            return RedirectToAction(nameof(Details), new { id });
        }

        // Exclusão lógica: nada é apagado, o chamado só sai do fluxo de atendimento.
        var now = DateTime.UtcNow;
        var previous = ticket.Status;
        ticket.Status = TicketStatus.Cancelled;
        ticket.UpdatedAtUtc = now;
        _sla.OnStatusChanged(ticket, previous, TicketStatus.Cancelled, now);

        _context.TicketStatusHistory.Add(new TicketStatusHistory
        {
            TicketId = ticket.Id,
            FromStatus = previous,
            ToStatus = TicketStatus.Cancelled,
            ChangedByUserId = currentUserId,
            ChangedAtUtc = now
        });

        await _context.SaveChangesAsync();

        await NotifyOthersAsync(currentUserId, $"Chamado #{id} foi cancelado", ticket.CreatedByUserId, ticket.AssignedToUserId);

        await NotifyChangedAsync(ticket, "cancel");
        TempData["Notification"] = "Chamado cancelado.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = RoleNames.Admin)]
    public async Task<IActionResult> Assign(int id, string? agentId)
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

        agentId = string.IsNullOrWhiteSpace(agentId) ? null : agentId;
        var agents = await _userManager.GetUsersInRoleAsync(RoleNames.Agent);

        switch (TicketAssignmentPolicy.Check(ticket.Status, ticket.AssignedToUserId, agentId, agents.Select(a => a.Id).ToList()))
        {
            case AssignmentCheck.Unchanged:
                return RedirectToAction(nameof(Details), new { id });
            case AssignmentCheck.TicketFinal:
                TempData["Notification"] = "Chamado encerrado não pode ser atribuído.";
                return RedirectToAction(nameof(Details), new { id });
            case AssignmentCheck.NotAnAgent:
                TempData["Notification"] = "Selecione um atendente válido.";
                return RedirectToAction(nameof(Details), new { id });
        }

        var previousAssigneeId = ticket.AssignedToUserId;
        ticket.AssignedToUserId = agentId;
        ticket.UpdatedAtUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        var newAgent = agents.FirstOrDefault(a => a.Id == agentId);
        if (newAgent is not null)
        {
            await NotifyOthersAsync(currentUserId, $"O chamado #{id} foi atribuído a você: {ticket.Title}", newAgent.Id);
            await NotifyOthersAsync(currentUserId, $"Chamado #{id} atribuído a {newAgent.FullName}", ticket.CreatedByUserId);
            TempData["Notification"] = $"Chamado atribuído a {newAgent.FullName}.";
        }
        else
        {
            await NotifyAsync("Agents", $"Chamado #{id} voltou para a fila: {ticket.Title}");
            await NotifyOthersAsync(currentUserId, $"Chamado #{id} voltou para a fila de atendimento", ticket.CreatedByUserId);
            TempData["Notification"] = "Chamado devolvido à fila de atendimento.";
        }

        await NotifyChangedAsync(ticket, "assignment", previousAssigneeId, assignmentChanged: true);

        if (previousAssigneeId is not null)
        {
            await NotifyOthersAsync(currentUserId, $"O chamado #{id} foi reatribuído e não está mais com você", previousAssigneeId);
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    public async Task<IActionResult> Download(int id)
    {
        var attachment = await _context.Attachments
            .AsNoTracking()
            .Include(a => a.Ticket)
            .FirstOrDefaultAsync(a => a.Id == id);

        if (attachment?.Ticket is null)
        {
            return NotFound();
        }

        if (!CanAccessTicket(attachment.Ticket))
        {
            return Forbid();
        }

        var fullPath = ResolveStoragePath(attachment.StoredPath);
        if (!System.IO.File.Exists(fullPath))
        {
            return NotFound();
        }

        // Content-Type derivado da extensão (já validada), nunca do valor enviado pelo navegador.
        return PhysicalFile(fullPath, GetContentType(attachment.FileName), attachment.FileName);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UploadToTicket(int id, List<IFormFile> files)
    {
        var ticket = await _context.Tickets.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id);
        if (ticket is null)
        {
            return NotFound();
        }

        if (!CanAccessTicket(ticket))
        {
            return Forbid();
        }

        var currentUserId = _userManager.GetUserId(User);
        if (currentUserId is null)
        {
            return Challenge();
        }

        if (files is not { Count: > 0 })
        {
            TempData["Notification"] = "Selecione ao menos um arquivo.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var (saved, rejected) = await SaveAttachmentsAsync(ticket.Id, currentUserId, files);
        if (saved > 0)
        {
            await NotifyChangedAsync(ticket, "attachment");
        }

        var notification = saved > 0 ? $"{saved} anexo(s) enviado(s) com sucesso." : "Nenhum anexo foi enviado.";
        if (rejected > 0)
        {
            notification += " " + RejectedFilesMessage(rejected);
        }
        TempData["Notification"] = notification;

        return RedirectToAction(nameof(Details), new { id });
    }

    private bool CanAccessTicket(Ticket ticket) =>
        TicketAccessPolicy.CanAccess(
            TicketAccessPolicy.ResolveRole(User),
            _userManager.GetUserId(User),
            ticket.CreatedByUserId,
            ticket.AssignedToUserId);

    private bool CanEditTicket(Ticket ticket) =>
        TicketWorkflowPolicy.CanEdit(
            ticket.Status,
            User.IsInRole(RoleNames.Admin),
            ticket.CreatedByUserId == _userManager.GetUserId(User));

    private Task NotifyAsync(string group, string message) =>
        _notifier.NotifyAsync(group, message);

    /// <summary>Avisa as telas abertas (detalhes, lista, dashboard) para se atualizarem sozinhas.</summary>
    private Task NotifyChangedAsync(Ticket ticket, string kind, string? previousAssigneeId = null, bool assignmentChanged = false) =>
        _notifier.TicketChangedAsync(ticket.Id, kind, _userManager.GetUserId(User), ticket.CreatedByUserId, ticket.AssignedToUserId, previousAssigneeId, assignmentChanged);

    /// <summary>
    /// Notifica cada grupo/usuário uma única vez, sem avisar quem fez a ação.
    /// </summary>
    private Task NotifyOthersAsync(string currentUserId, string message, params string?[] groups) =>
        _notifier.NotifyOthersAsync(currentUserId, message, groups);

    private async Task ValidatePriorityAndCategoryAsync(TicketPriority priority, int categoryId)
    {
        if (!Enum.IsDefined(priority))
        {
            ModelState.AddModelError("Priority", "Selecione uma prioridade válida.");
        }

        if (!await _context.Categories.AnyAsync(c => c.Id == categoryId))
        {
            ModelState.AddModelError("CategoryId", "Selecione uma categoria válida.");
        }
    }

    private async Task<List<SelectListItem>> GetAgentSelectListAsync(ApplicationUser? currentAssignee)
    {
        var items = (await _userManager.GetUsersInRoleAsync(RoleNames.Agent))
            .OrderBy(a => a.FullName)
            .Select(a => new SelectListItem(a.FullName, a.Id))
            .ToList();

        // Responsável que perdeu o papel de Atendente continua visível como valor atual,
        // senão o select mostraria "Não atribuído" e um envio desatento o tiraria do chamado.
        if (currentAssignee is not null && items.All(i => i.Value != currentAssignee.Id))
        {
            items.Insert(0, new SelectListItem($"{currentAssignee.FullName} (não é mais atendente)", currentAssignee.Id));
        }

        return items;
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

    private async Task<(int Saved, int Rejected)> SaveAttachmentsAsync(int ticketId, string userId, List<IFormFile> files)
    {
        // Caminho gravado relativo ao ContentRoot, para o banco continuar válido
        // se a aplicação for movida de pasta/servidor.
        var relativeDir = Path.Combine(UploadsFolder, ticketId.ToString());
        var absoluteDir = Path.Combine(_env.ContentRootPath, relativeDir);

        int saved = 0, rejected = 0;
        foreach (var file in files)
        {
            var fileName = SanitizeFileName(file.FileName);
            if (!AttachmentPolicy.IsAllowed(fileName, file.Length))
            {
                rejected++;
                continue;
            }

            Directory.CreateDirectory(absoluteDir);
            var storedName = $"{Guid.NewGuid()}{Path.GetExtension(fileName).ToLowerInvariant()}";

            await using (var stream = new FileStream(Path.Combine(absoluteDir, storedName), FileMode.CreateNew))
            {
                await file.CopyToAsync(stream);
            }

            _context.Attachments.Add(new Attachment
            {
                TicketId = ticketId,
                FileName = fileName,
                StoredPath = Path.Combine(relativeDir, storedName),
                ContentType = GetContentType(fileName),
                SizeInBytes = file.Length,
                UploadedByUserId = userId,
                UploadedAtUtc = DateTime.UtcNow
            });
            saved++;
        }

        if (saved > 0)
        {
            await _context.SaveChangesAsync();
        }

        return (saved, rejected);
    }

    private string ResolveStoragePath(string storedPath) =>
        Path.IsPathRooted(storedPath) ? storedPath : Path.Combine(_env.ContentRootPath, storedPath);

    private static string SanitizeFileName(string fileName)
    {
        // Alguns navegadores enviam o caminho completo ("C:\pasta\arquivo.pdf").
        var name = Path.GetFileName(fileName.Replace('\\', '/'));
        if (name.Length <= MaxFileNameLength)
        {
            return name;
        }

        var extension = Path.GetExtension(name);
        return extension.Length < MaxFileNameLength
            ? name[..(MaxFileNameLength - extension.Length)] + extension
            : name[..MaxFileNameLength];
    }

    private static string GetContentType(string fileName) =>
        ContentTypeProvider.TryGetContentType(fileName, out var contentType) ? contentType : "application/octet-stream";

    private static string RejectedFilesMessage(int rejected) =>
        $"{rejected} arquivo(s) ignorado(s): formato não permitido ou acima de {AttachmentPolicy.MaxSizeInBytes / (1024 * 1024)} MB.";

    private IQueryable<Ticket>? BuildFilteredQuery(
        TicketStatus? status, string? userId, DateTime? startDate, DateTime? endDate)
    {
        var currentUserId = _userManager.GetUserId(User);
        if (currentUserId is null) return null;

        var role = TicketAccessPolicy.ResolveRole(User);
        var query = TicketAccessPolicy.ApplyVisibility(_context.Tickets.AsNoTracking(), role, currentUserId);

        if (status.HasValue)
            query = query.Where(t => t.Status == status.Value);

        if (!string.IsNullOrWhiteSpace(userId) && role == RoleNames.Admin)
            query = query.Where(t => t.CreatedByUserId == userId || t.AssignedToUserId == userId);

        // As datas do filtro são dias no fuso do servidor (o mesmo usado para exibir),
        // então os limites são convertidos para UTC antes de comparar.
        if (startDate.HasValue)
        {
            var startUtc = LocalDateToUtc(startDate.Value);
            query = query.Where(t => t.CreatedAtUtc >= startUtc);
        }

        if (endDate.HasValue && endDate.Value.Date < DateTime.MaxValue.Date)
        {
            var endUtc = LocalDateToUtc(endDate.Value.AddDays(1));
            query = query.Where(t => t.CreatedAtUtc < endUtc);
        }

        return query.OrderByDescending(t => t.CreatedAtUtc);
    }

    private static DateTime LocalDateToUtc(DateTime date) =>
        DateTime.SpecifyKind(date.Date, DateTimeKind.Local).ToUniversalTime();

    private static IQueryable<TicketListItemViewModel> ProjectToListItems(IQueryable<Ticket> query, DateTime nowUtc) =>
        query.Select(t => new TicketListItemViewModel
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
            // Mesma conta de SlaPolicy.IsBreached: com o relógio parado vale o instante em que parou.
            IsSlaBreached = t.DueAtUtc.HasValue && t.DueAtUtc.Value < (t.SlaPausedAtUtc ?? nowUtc),
            IsSlaWarned = t.SlaWarnedAtUtc != null,
            IsSlaEscalated = t.SlaEscalatedAtUtc != null
        });
}
