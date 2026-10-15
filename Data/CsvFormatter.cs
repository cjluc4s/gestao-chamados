namespace gestao_chamados.Data;

public static class CsvFormatter
{
    // Células iniciadas por estes caracteres são interpretadas como fórmula
    // pelo Excel/LibreOffice (CSV injection).
    private static readonly char[] FormulaPrefixes = ['=', '+', '-', '@', '\t', '\r'];
    private static readonly char[] CharsRequiringQuotes = [';', '"', '\n', '\r'];

    public static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        if (FormulaPrefixes.Contains(value[0]))
        {
            value = "'" + value;
        }

        return value.IndexOfAny(CharsRequiringQuotes) >= 0
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
    }
}
