using gestao_chamados.Data;

namespace gestao_chamados.Services;

/// <summary>
/// Serviço em segundo plano que, a cada intervalo, avisa quando um chamado entra "em risco"
/// e escalona ao Admin quando o prazo estoura.
/// </summary>
public sealed class SlaMonitorService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly SlaCalculator _sla;
    private readonly ILogger<SlaMonitorService> _logger;

    public SlaMonitorService(IServiceScopeFactory scopes, SlaCalculator sla, ILogger<SlaMonitorService> logger)
    {
        _scopes = scopes;
        _sla = sla;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(5, _sla.Options.MonitorIntervalSeconds));
        using var timer = new PeriodicTimer(interval);

        try
        {
            do
            {
                try
                {
                    await RunOnceAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Uma varredura com problema não pode derrubar o monitor.
                    _logger.LogError(ex, "Falha na verificação de SLA.");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // Encerramento normal da aplicação.
        }
    }

    public async Task RunOnceAsync(CancellationToken ct)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var notifier = scope.ServiceProvider.GetRequiredService<TicketNotifier>();

        var alerts = await SlaAlertProcessor.ProcessAsync(db, _sla, DateTime.UtcNow, ct);

        foreach (var alert in alerts)
        {
            var responsible = alert.AssignedToUserId ?? "Agents";

            if (alert.Kind == SlaAlertKind.Warning)
            {
                await notifier.NotifyOthersAsync(null,
                    $"SLA em risco: o chamado #{alert.TicketId} já consumiu {(int)Math.Round(_sla.WarnAt * 100)}% do prazo — {alert.Title}",
                    responsible, "Admins");
            }
            else
            {
                await notifier.NotifyOthersAsync(null,
                    $"SLA vencido: o chamado #{alert.TicketId} foi escalonado — {alert.Title}",
                    responsible, "Admins", alert.CreatedByUserId);
            }

            await notifier.TicketChangedAsync(alert.TicketId, "sla", null, alert.CreatedByUserId, alert.AssignedToUserId);

            _logger.LogInformation("SLA {Kind} no chamado #{TicketId}.", alert.Kind, alert.TicketId);
        }
    }
}
