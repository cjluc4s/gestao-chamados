using gestao_chamados.Models;

namespace gestao_chamados.Data;

/// <summary>
/// Configuração do SLA (seção "Sla" do appsettings).
/// Listas ficam vazias no código e recebem os valores padrão do appsettings.json:
/// a configuração .NET soma itens a listas já preenchidas, o que impediria remover um padrão.
/// </summary>
public class SlaOptions
{
    public const string Section = "Sla";

    public BusinessHoursOptions BusinessHours { get; set; } = new();

    /// <summary>Prioridades cujo relógio corre 24x7, mesmo com horário comercial ligado.</summary>
    public List<TicketPriority> AlwaysOnPriorities { get; set; } = [];

    /// <summary>Percentual do prazo consumido a partir do qual o chamado entra "em risco".</summary>
    public int WarnAtPercent { get; set; } = 80;

    /// <summary>Intervalo, em segundos, da verificação que gera alertas e escalonamentos.</summary>
    public int MonitorIntervalSeconds { get; set; } = 60;
}

public class BusinessHoursOptions
{
    public bool Enabled { get; set; } = true;

    /// <summary>Id do fuso (IANA ou do Windows). Se não existir na máquina, usa o fuso local.</summary>
    public string TimeZone { get; set; } = "America/Sao_Paulo";

    public string Start { get; set; } = "08:00";
    public string End { get; set; } = "18:00";

    /// <summary>Dias úteis. Vazio equivale a segunda a sexta.</summary>
    public List<DayOfWeek> Days { get; set; } = [];

    /// <summary>Feriados no formato yyyy-MM-dd.</summary>
    public List<string> Holidays { get; set; } = [];
}
