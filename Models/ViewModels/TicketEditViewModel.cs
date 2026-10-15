using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace gestao_chamados.Models.ViewModels;

public class TicketEditViewModel
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Informe o título.")]
    [StringLength(120, ErrorMessage = "O título pode ter no máximo {1} caracteres.")]
    [Display(Name = "Título")]
    public string Title { get; set; } = string.Empty;

    [Required(ErrorMessage = "Descreva o problema.")]
    [StringLength(3000, ErrorMessage = "A descrição pode ter no máximo {1} caracteres.")]
    [Display(Name = "Descrição")]
    public string Description { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Prioridade")]
    public TicketPriority Priority { get; set; }

    [Required]
    [Display(Name = "Categoria")]
    public int CategoryId { get; set; }

    [ValidateNever]
    public List<SelectListItem> Categories { get; set; } = [];
}
