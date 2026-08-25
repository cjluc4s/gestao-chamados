using System.ComponentModel.DataAnnotations;
using System.Text;
using gestao_chamados.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;

namespace gestao_chamados.Areas.Identity.Pages.Account;

[AllowAnonymous]
public class ForgotPasswordModel : PageModel
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<ForgotPasswordModel> _logger;

    public ForgotPasswordModel(UserManager<ApplicationUser> userManager, IWebHostEnvironment env, ILogger<ForgotPasswordModel> logger)
    {
        _userManager = userManager;
        _env = env;
        _logger = logger;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public class InputModel
    {
        [Required(ErrorMessage = "Informe seu e-mail.")]
        [EmailAddress]
        [Display(Name = "E-mail")]
        public string Email { get; set; } = string.Empty;
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var user = await _userManager.FindByEmailAsync(Input.Email);
        if (user is null || !await _userManager.IsEmailConfirmedAsync(user))
        {
            // Não revela se o e-mail existe ou não na base.
            return RedirectToPage("./ForgotPasswordConfirmation");
        }

        var code = await _userManager.GeneratePasswordResetTokenAsync(user);
        code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));
        var callbackUrl = Url.Page(
            "/Account/ResetPassword",
            pageHandler: null,
            values: new { area = "Identity", code, email = user.Email },
            protocol: Request.Scheme)!;

        // Este projeto não tem um provedor de e-mail configurado (sem SMTP).
        // Em produção, troque isto por um envio de e-mail de verdade.
        _logger.LogWarning("Link de redefinição de senha para {Email}: {Link}", user.Email, callbackUrl);

        if (_env.IsDevelopment())
        {
            TempData["DevResetLink"] = callbackUrl;
        }

        return RedirectToPage("./ForgotPasswordConfirmation");
    }
}
