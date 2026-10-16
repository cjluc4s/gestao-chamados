using gestao_chamados.Data;
using gestao_chamados.Hubs;
using gestao_chamados.Models;
using gestao_chamados.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Infrastructure;

QuestPDF.Settings.License = LicenseType.Community;

var builder = WebApplication.CreateBuilder(args);

// Adiciona serviços ao contêiner.
builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages();
builder.Services.AddSignalR();
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services
    .AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        options.Password.RequireDigit = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = true;
        options.Password.RequiredLength = 8;

        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultUI()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.ExpireTimeSpan = TimeSpan.FromDays(7);
    options.SlidingExpiration = true;
    options.AccessDeniedPath = "/Identity/Account/AccessDenied";
});

// Revalida o cookie a cada minuto: quando um Admin troca o papel de alguém,
// a mudança vale quase imediatamente (o padrão é 30 minutos).
builder.Services.Configure<SecurityStampValidatorOptions>(options =>
{
    options.ValidationInterval = TimeSpan.FromMinutes(1);
});

builder.Services.Configure<SlaOptions>(builder.Configuration.GetSection(SlaOptions.Section));
builder.Services.AddSingleton<SlaCalculator>();
builder.Services.AddSingleton<TicketNotifier>();
builder.Services.AddHostedService<SlaMonitorService>();

var app = builder.Build();

// Falha já na inicialização se o calendário do SLA estiver mal configurado.
_ = app.Services.GetRequiredService<SlaCalculator>();

await DbInitializer.SeedAsync(app.Services, app.Environment, app.Configuration);

// Configura o pipeline de requisições HTTP.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // O valor padrão de HSTS é 30 dias. Você pode alterar para cenários de produção: https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();
app.MapRazorPages();
app.MapHub<NotificationHub>("/hubs/notifications");


app.Run();
