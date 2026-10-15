using System.ComponentModel.DataAnnotations;

namespace gestao_chamados.Models;

public enum TicketStatus
{
    [Display(Name = "Aberto")]
    Open = 1,

    [Display(Name = "Em andamento")]
    InProgress = 2,

    [Display(Name = "Aguardando usuário")]
    WaitingUser = 3,

    [Display(Name = "Resolvido")]
    Resolved = 4,

    [Display(Name = "Fechado")]
    Closed = 5
}
