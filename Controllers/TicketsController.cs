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
    private readonly IHubContext<NotificationHub> _hub;

    public TicketsController(ApplicationDbContext context, UserManager<ApplicationUser> userManager, IWebHostEnvironment env, IHubContext<NotificationHub> hub)
    {
        _context = context;
        _userManager = userManager;
        _env = env;
        _hub = hub;
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
                t.IsSlaBreached ? "Vencido" : "OK"
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
                            t.IsSlaBreached ? "Vencido" : "OK"
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
        if (!Enum.IsDefined(model.Priority))
        {
            ModelState.AddModelError(nameof(model.Priority), "Selecione uma prioridade válida.");
        }

        if (!await _context.Categories.AnyAsync(c => c.Id == model.CategoryId))
        {
            ModelState.AddModelError(nameof(model.CategoryId), "Selecione uma categoria válida.");
        }

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
            CreatedAtUtc = now,
            DueAtUtc = now.Add(SlaPolicy.GetDeadline(model.Priority))
        };

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
        var canChangeStatus = isAdmin || User.IsInRole(RoleNames.Agent);

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
            IsSlaBreached = SlaPolicy.IsBreached(ticket.DueAtUtc, ticket.Status, DateTime.UtcNow),
            AllowedStatuses = canChangeStatus
                ? TicketWorkflowPolicy.GetAllowedTransitions(ticket.Status, isAdmin).ToList()
                : [],
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

        TempData["Notification"] = "Chamado atribuído a você.";
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

    private Task NotifyAsync(string group, string message) =>
        _hub.Clients.Group(group).SendAsync("ReceiveNotification", message);

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
            IsSlaBreached = t.DueAtUtc.HasValue && t.DueAtUtc.Value < nowUtc
                && t.Status != TicketStatus.Resolved && t.Status != TicketStatus.Closed
        });
}
