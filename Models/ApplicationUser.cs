using Microsoft.AspNetCore.Identity;

namespace gestao_chamados.Models;

public class ApplicationUser : IdentityUser
{
    public string FullName { get; set; } = string.Empty;
}
