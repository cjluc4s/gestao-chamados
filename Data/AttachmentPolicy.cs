namespace gestao_chamados.Data;

public static class AttachmentPolicy
{
    public const long MaxSizeInBytes = 10 * 1024 * 1024;

    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".txt", ".zip", ".rar"
    };

    public static bool IsAllowed(string fileName, long sizeInBytes)
    {
        if (sizeInBytes <= 0 || sizeInBytes > MaxSizeInBytes)
        {
            return false;
        }

        var extension = Path.GetExtension(fileName);
        return !string.IsNullOrEmpty(extension) && AllowedExtensions.Contains(extension);
    }
}
