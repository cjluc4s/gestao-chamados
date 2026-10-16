using gestao_chamados.Models;

namespace gestao_chamados.Data;

/// <summary>Os dados de um chamado de que as métricas precisam (já lidos do banco).</summary>
public sealed record TicketMetricRow(
    int Id,
    TicketPriority Priority,
    string Category,
    TicketStatus Status,
    DateTime CreatedAtUtc,
    DateTime? FirstResponseAtUtc,
    DateTime? ResolvedAtUtc,
    long PausedSeconds,
    DateTime? SlaPausedAtUtc,
    DateTime? DueAtUtc,
    string? AssignedToUserId,
    string? AssignedToName,
    int? Rating);

public sealed record DailyPoint(DateOnly Day, int Opened, int Resolved);

public sealed record AgentMetrics(
    string UserId,
    string Name,
    int Resolved,
    TimeSpan? AvgFirstResponse,
    TimeSpan? AvgResolution,
    double? SlaMetPercent,
    double? AvgRating,
    int Ratings);

public sealed record MetricsReport(
    int Opened,
    int Resolved,
    int Backlog,
    TimeSpan? AvgFirstResponse,
    TimeSpan? AvgResolution,
    double? SlaMetPercent,
    int SlaEvaluated,
    double? AvgRating,
    int RatingCount,
    double? RatingResponsePercent,
    IReadOnlyList<DailyPoint> Daily,
    IReadOnlyList<KeyValuePair<TicketPriority, int>> ByPriority,
    IReadOnlyList<KeyValuePair<string, int>> ByCategory,
    IReadOnlyList<int> RatingDistribution,
    IReadOnlyList<AgentMetrics> Agents);

/// <summary>
/// Cálculo das métricas de atendimento. Puro: recebe as linhas já lidas e devolve o relatório.
///
/// Definições:
/// • Abertos = criados no período. Resolvidos = com data de resolução no período (Resolvido ou Fechado).
/// • Backlog = chamados que ainda pedem atendimento agora (qualquer data de abertura).
/// • 1ª resposta e resolução usam o mesmo relógio do SLA (horário comercial quando aplicável) e a
///   resolução desconta o tempo em que o relógio ficou parado (aguardando o solicitante).
/// • SLA cumprido = % dos resolvidos no período cujo limite não foi estourado antes de o relógio parar.
/// • Satisfação = média das notas dos chamados resolvidos no período.
/// </summary>
public static class MetricsCalculator
{
    public static MetricsReport Compute(
        IReadOnlyCollection<TicketMetricRow> rows, DateTime fromUtc, DateTime toUtc, SlaCalculator sla, TimeZoneInfo zone)
    {
        var opened = rows.Where(r => r.CreatedAtUtc >= fromUtc && r.CreatedAtUtc <= toUtc).ToList();
        var resolved = rows
            .Where(r => r.ResolvedAtUtc.HasValue
                && r.ResolvedAtUtc.Value >= fromUtc && r.ResolvedAtUtc.Value <= toUtc
                && r.Status is TicketStatus.Resolved or TicketStatus.Closed)
            .ToList();

        var rated = resolved.Where(r => r.Rating.HasValue).ToList();

        var distribution = new int[5];
        foreach (var row in rated)
        {
            distribution[row.Rating!.Value - 1]++;
        }

        var evaluable = resolved.Where(r => r.DueAtUtc.HasValue).ToList();

        return new MetricsReport(
            Opened: opened.Count,
            Resolved: resolved.Count,
            Backlog: rows.Count(r => !SlaPolicy.IsDone(r.Status)),
            AvgFirstResponse: AverageFirstResponse(opened, sla),
            AvgResolution: AverageResolution(resolved, sla),
            SlaMetPercent: evaluable.Count == 0 ? null : 100.0 * evaluable.Count(IsSlaMet) / evaluable.Count,
            SlaEvaluated: evaluable.Count,
            AvgRating: rated.Count == 0 ? null : rated.Average(r => r.Rating!.Value),
            RatingCount: rated.Count,
            RatingResponsePercent: resolved.Count == 0 ? null : 100.0 * rated.Count / resolved.Count,
            Daily: BuildDaily(opened, resolved, fromUtc, toUtc, zone),
            ByPriority: opened.GroupBy(r => r.Priority)
                .OrderByDescending(g => g.Key)
                .Select(g => KeyValuePair.Create(g.Key, g.Count()))
                .ToList(),
            ByCategory: opened.GroupBy(r => r.Category)
                .OrderByDescending(g => g.Count()).ThenBy(g => g.Key)
                .Take(6)
                .Select(g => KeyValuePair.Create(g.Key, g.Count()))
                .ToList(),
            RatingDistribution: distribution,
            Agents: resolved
                .Where(r => r.AssignedToUserId is not null)
                .GroupBy(r => r.AssignedToUserId!)
                .Select(g => BuildAgent(g.Key, g.First().AssignedToName ?? "—", g.ToList(), sla))
                .OrderByDescending(a => a.Resolved).ThenBy(a => a.Name)
                .ToList());
    }

    private static AgentMetrics BuildAgent(string userId, string name, List<TicketMetricRow> resolved, SlaCalculator sla)
    {
        var rated = resolved.Where(r => r.Rating.HasValue).ToList();
        var evaluable = resolved.Where(r => r.DueAtUtc.HasValue).ToList();

        return new AgentMetrics(
            userId,
            name,
            resolved.Count,
            AverageFirstResponse(resolved, sla),
            AverageResolution(resolved, sla),
            evaluable.Count == 0 ? null : 100.0 * evaluable.Count(IsSlaMet) / evaluable.Count,
            rated.Count == 0 ? null : rated.Average(r => r.Rating!.Value),
            rated.Count);
    }

    /// <summary>
    /// O limite foi cumprido se não tinha estourado quando o relógio parou.
    /// (<see cref="TicketMetricRow.SlaPausedAtUtc"/> é o instante em que ele parou; sem ele, usa a resolução.)
    /// </summary>
    public static bool IsSlaMet(TicketMetricRow row) =>
        !SlaPolicy.IsBreached(row.DueAtUtc, row.SlaPausedAtUtc ?? row.ResolvedAtUtc, DateTime.MaxValue);

    private static TimeSpan? AverageFirstResponse(IEnumerable<TicketMetricRow> rows, SlaCalculator sla)
    {
        var durations = rows
            .Where(r => r.FirstResponseAtUtc.HasValue)
            .Select(r => sla.Between(r.CreatedAtUtc, r.FirstResponseAtUtc!.Value, r.Priority))
            .ToList();

        return durations.Count == 0 ? null : TimeSpan.FromSeconds(durations.Average(d => d.TotalSeconds));
    }

    private static TimeSpan? AverageResolution(IEnumerable<TicketMetricRow> rows, SlaCalculator sla)
    {
        var durations = rows
            .Where(r => r.ResolvedAtUtc.HasValue)
            .Select(r =>
            {
                var elapsed = sla.Between(r.CreatedAtUtc, r.ResolvedAtUtc!.Value, r.Priority) - TimeSpan.FromSeconds(r.PausedSeconds);
                return elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed;
            })
            .ToList();

        return durations.Count == 0 ? null : TimeSpan.FromSeconds(durations.Average(d => d.TotalSeconds));
    }

    private static List<DailyPoint> BuildDaily(
        List<TicketMetricRow> opened, List<TicketMetricRow> resolved, DateTime fromUtc, DateTime toUtc, TimeZoneInfo zone)
    {
        DateOnly Day(DateTime utc) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(AsUtc(utc), zone));

        var openedByDay = opened.GroupBy(r => Day(r.CreatedAtUtc)).ToDictionary(g => g.Key, g => g.Count());
        var resolvedByDay = resolved.GroupBy(r => Day(r.ResolvedAtUtc!.Value)).ToDictionary(g => g.Key, g => g.Count());

        var points = new List<DailyPoint>();
        for (var day = Day(fromUtc); day <= Day(toUtc); day = day.AddDays(1))
        {
            points.Add(new DailyPoint(
                day,
                openedByDay.GetValueOrDefault(day),
                resolvedByDay.GetValueOrDefault(day)));
        }

        return points;
    }

    private static DateTime AsUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
