using gestao_chamados.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace gestao_chamados.Data;

public static class DbInitializer
{
    public static async Task SeedAsync(IServiceProvider services, IWebHostEnvironment env, IConfiguration config)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await context.Database.EnsureCreatedAsync();

        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(DbInitializer));

        foreach (var role in new[] { RoleNames.Admin, RoleNames.Agent, RoleNames.User })
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole(role));
            }
        }

        // Bootstrap opcional de um Admin real via configuração (env vars em produção).
        // Só cria conta se as chaves forem explicitamente fornecidas — nunca por padrão.
        var adminEmail = config["SeedData:AdminEmail"];
        var adminPassword = config["SeedData:AdminPassword"];
        if (!string.IsNullOrWhiteSpace(adminEmail) && !string.IsNullOrWhiteSpace(adminPassword))
        {
            await EnsureUserAsync(userManager, adminEmail, config["SeedData:AdminFullName"] ?? "Administrador", RoleNames.Admin, adminPassword);
        }

        // Contas de demonstração com senha conhecida: só em ambiente de desenvolvimento.
        if (env.IsDevelopment())
        {
            var demoPassword = config["SeedData:DemoPassword"] ?? "P@ssw0rd!";
            await EnsureUserAsync(userManager, "admin@helpdesk.local", "Admin Master", RoleNames.Admin, demoPassword);
            await EnsureUserAsync(userManager, "agent@helpdesk.local", "Suporte 01", RoleNames.Agent, demoPassword);
            await EnsureUserAsync(userManager, "user@helpdesk.local", "Usuário Demo", RoleNames.User, demoPassword);
            logger.LogWarning("Contas de demonstração seedadas (ambiente Development). Senha: {DemoPassword}. NÃO use isso em produção.", demoPassword);
        }

        if (!await context.Categories.AnyAsync())
        {
            context.Categories.AddRange(
                new Category { Name = "Infraestrutura" },
                new Category { Name = "Sistemas" },
                new Category { Name = "Acesso" },
                new Category { Name = "Hardware" }
            );

            await context.SaveChangesAsync();
        }
    }

    private static async Task EnsureUserAsync(
        UserManager<ApplicationUser> userManager,
        string email,
        string fullName,
        string role,
        string password)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                FullName = fullName,
                EmailConfirmed = true
            };

            await userManager.CreateAsync(user, password);
        }

        if (!await userManager.IsInRoleAsync(user, role))
        {
            await userManager.AddToRoleAsync(user, role);
        }
    }
}
