namespace gestao_chamados.Data;

/// <summary>
/// Calendário de horário comercial: soma e mede "tempo útil" (só dias úteis, dentro da janela
/// diária, sem feriados). Lógica pura, sem banco — recebe e devolve datas em UTC.
/// </summary>
public sealed class BusinessCalendar
{
    // Proteção contra configuração absurda: nunca percorre mais que ~10 anos de dias.
    private const int MaxDays = 3660;

    private static readonly DayOfWeek[] DefaultDays =
        [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday];

    private readonly TimeZoneInfo _zone;
    private readonly TimeSpan _start;
    private readonly TimeSpan _end;
    private readonly HashSet<DayOfWeek> _days;
    private readonly HashSet<DateOnly> _holidays;

    public BusinessCalendar(BusinessHoursOptions options)
    {
        if (!TimeSpan.TryParse(options.Start, out _start) || !TimeSpan.TryParse(options.End, out _end))
        {
            throw new InvalidOperationException("Sla:BusinessHours:Start e End devem estar no formato HH:mm.");
        }

        if (_start < TimeSpan.Zero || _end > TimeSpan.FromHours(24) || _end <= _start)
        {
            throw new InvalidOperationException("Sla:BusinessHours:End deve ser maior que Start (entre 00:00 e 24:00).");
        }

        _zone = ResolveZone(options.TimeZone);
        _days = options.Days.Count > 0 ? [.. options.Days] : [.. DefaultDays];

        _holidays = [];
        foreach (var text in options.Holidays)
        {
            if (!DateOnly.TryParseExact(text, "yyyy-MM-dd", out var holiday))
            {
                throw new InvalidOperationException($"Sla:BusinessHours:Holidays contém uma data inválida: \"{text}\" (use yyyy-MM-dd).");
            }

            _holidays.Add(holiday);
        }
    }

    public TimeZoneInfo Zone => _zone;
    public TimeSpan Start => _start;
    public TimeSpan End => _end;
    public IReadOnlyCollection<DayOfWeek> Days => _days;
    public IReadOnlyCollection<DateOnly> Holidays => _holidays;

    /// <summary>Quantas horas úteis existem por dia.</summary>
    public TimeSpan DailyLength => _end - _start;

    public bool IsBusinessDay(DateOnly day) => _days.Contains(day.DayOfWeek) && !_holidays.Contains(day);

    /// <summary>Soma <paramref name="duration"/> de tempo útil a partir de <paramref name="startUtc"/>.</summary>
    public DateTime AddBusinessTime(DateTime startUtc, TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
        {
            return startUtc;
        }

        var remaining = duration;
        var cursor = startUtc;
        var day = LocalDay(startUtc);

        for (var i = 0; i < MaxDays; i++, day = day.AddDays(1))
        {
            if (!TryGetWindow(day, out var windowStart, out var windowEnd))
            {
                continue;
            }

            var from = cursor > windowStart ? cursor : windowStart;
            if (from >= windowEnd)
            {
                continue;
            }

            var available = windowEnd - from;
            if (remaining <= available)
            {
                return from + remaining;
            }

            remaining -= available;
        }

        // Calendário sem nenhum dia útil: não trava, cai para tempo corrido.
        return startUtc.Add(duration);
    }

    /// <summary>Tempo útil decorrido entre <paramref name="fromUtc"/> e <paramref name="toUtc"/>.</summary>
    public TimeSpan BusinessTimeBetween(DateTime fromUtc, DateTime toUtc)
    {
        if (toUtc <= fromUtc)
        {
            return TimeSpan.Zero;
        }

        var total = TimeSpan.Zero;
        var day = LocalDay(fromUtc);
        var lastDay = LocalDay(toUtc);

        for (var i = 0; i < MaxDays && day <= lastDay; i++, day = day.AddDays(1))
        {
            if (!TryGetWindow(day, out var windowStart, out var windowEnd))
            {
                continue;
            }

            var from = fromUtc > windowStart ? fromUtc : windowStart;
            var to = toUtc < windowEnd ? toUtc : windowEnd;
            if (to > from)
            {
                total += to - from;
            }
        }

        return total;
    }

    private DateOnly LocalDay(DateTime utc) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(AsUtc(utc), _zone));

    private bool TryGetWindow(DateOnly day, out DateTime startUtc, out DateTime endUtc)
    {
        if (!IsBusinessDay(day))
        {
            startUtc = endUtc = default;
            return false;
        }

        startUtc = ToUtc(day, _start);
        endUtc = ToUtc(day, _end);
        return true;
    }

    private DateTime ToUtc(DateOnly day, TimeSpan timeOfDay)
    {
        var local = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified).Add(timeOfDay);
        if (_zone.IsInvalidTime(local))
        {
            local = local.AddHours(1); // hora que "não existe" na virada do horário de verão
        }

        return TimeZoneInfo.ConvertTimeToUtc(local, _zone);
    }

    private static DateTime AsUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static TimeZoneInfo ResolveZone(string id)
    {
        foreach (var candidate in new[] { id, WindowsEquivalent(id) })
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(candidate);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        return TimeZoneInfo.Local;
    }

    private static string? WindowsEquivalent(string ianaId) => ianaId switch
    {
        "America/Sao_Paulo" => "E. South America Standard Time",
        "UTC" => "UTC",
        _ => null
    };
}
