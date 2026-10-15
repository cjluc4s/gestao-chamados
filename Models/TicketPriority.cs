using System.ComponentModel.DataAnnotations;

namespace gestao_chamados.Models;

public enum TicketPriority
{
    [Display(Name = "Baixa")]
    Low = 1,

    [Display(Name = "Média")]
    Medium = 2,

    [Display(Name = "Alta")]
    High = 3,

    [Display(Name = "Crítica")]
    Critical = 4
}
