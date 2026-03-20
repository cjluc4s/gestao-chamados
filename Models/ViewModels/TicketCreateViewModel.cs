using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace gestao_chamados.Models.ViewModels;

public class TicketCreateViewModel
{
    [Required]
    [StringLength(120)]
    public string Title { get; set; } = string.Empty;

    [Required]
    [StringLength(3000)]
    public string Description { get; set; } = string.Empty;

    [Required]
    public TicketPriority Priority { get; set; } = TicketPriority.Medium;

    [Required]
    public int CategoryId { get; set; }

    public List<SelectListItem> Categories { get; set; } = [];
}
