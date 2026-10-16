namespace gestao_chamados.Models;

public static class DurationFormat
{
    /// <summary>"45 min", "3 h", "3 h 20 min". Sem dias: em horário comercial "1 dia" seria ambíguo.</summary>
    public static string ToShort(this TimeSpan value)
    {
        if (value < TimeSpan.FromMinutes(1))
        {
            return "menos de 1 min";
        }

        var hours = (int)value.TotalHours;
        var minutes = value.Minutes;

        if (hours == 0)
        {
            return $"{minutes} min";
        }

        return minutes == 0 ? $"{hours} h" : $"{hours} h {minutes} min";
    }
}
