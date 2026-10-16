using gestao_chamados.Models;

namespace gestao_chamados.Data;

public static class FeedbackPolicy
{
    public const int MinRating = 1;
    public const int MaxRating = 5;
    public const int MaxCommentLength = 500;

    /// <summary>Só o solicitante avalia, uma vez, depois que o chamado foi resolvido ou fechado.</summary>
    public static bool CanRate(TicketStatus status, bool isCreator, bool alreadyRated) =>
        isCreator && !alreadyRated && status is TicketStatus.Resolved or TicketStatus.Closed;

    /// <summary>Devolve a mensagem de erro, ou null se a avaliação é válida.</summary>
    public static string? Validate(int rating, string? comment)
    {
        if (rating is < MinRating or > MaxRating)
        {
            return "Escolha uma nota de 1 a 5 estrelas.";
        }

        if (comment is not null && comment.Trim().Length > MaxCommentLength)
        {
            return $"O comentário pode ter no máximo {MaxCommentLength} caracteres.";
        }

        return null;
    }

    public static string Label(int rating) => rating switch
    {
        1 => "Muito insatisfeito",
        2 => "Insatisfeito",
        3 => "Regular",
        4 => "Satisfeito",
        5 => "Muito satisfeito",
        _ => string.Empty
    };
}
