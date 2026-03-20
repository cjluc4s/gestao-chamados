using System.ComponentModel.DataAnnotations;

namespace gestao_chamados.Models;

public class Category
{
    public int Id { get; set; }

    [Required]
    [StringLength(80)]
    public string Name { get; set; } = string.Empty;
}
