using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using ProyectoArbitraje.Components;
using ProyectoArbitraje.Components.Pages;
using ProyectoArbitraje.Data;
using ProyectoArbitraje.Models;
using ProyectoArbitraje.Services;

var builder = WebApplication.CreateBuilder(args);

QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddDbContextFactory<TorneoContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("TorneoBadminton")));

builder.Services.AddSingleton<TorneoEventBus>();

builder.Services.AddScoped<TorneoActualService>();

builder.Services.AddScoped<CalendarioService>();

builder.Services.AddScoped<FaseFinalService>();

builder.Services.AddScoped<ExportService>();

// Login/roles (Admin / Arbitro). El login en sí se resuelve con endpoints
// normales (no Blazor interactivo) porque en InteractiveServer no se puede
// escribir la cookie de sesión desde un componente -- ver /login más abajo.
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/login";
        options.ExpireTimeSpan = TimeSpan.FromHours(12);
        options.SlidingExpiration = true;
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.Cookie.SameSite = SameSiteMode.Lax;
    });
builder.Services.AddAuthorization();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.UseAntiforgery();

app.MapStaticAssets();

// =====================================================================
// LOGIN / LOGOUT
// Endpoints normales de ASP.NET Core (no componentes Blazor), para poder
// escribir la cookie de autenticación sin pelear con el render mode.
// =====================================================================

app.MapGet("/login", (string? error, IAntiforgery antiforgery, HttpContext http) =>
{
    var tokens = antiforgery.GetAndStoreTokens(http);

    string mensajeError = error == "1"
        ? "<p style=\"color:red;\">Usuario o contraseña incorrectos.</p>"
        : "";

    string html = $$"""
    <!DOCTYPE html>
    <html lang="es">
    <head>
        <meta charset="utf-8" />
        <title>Iniciar sesión - Arbitraje</title>
        <meta name="viewport" content="width=device-width, initial-scale=1" />
        <style>
            * { box-sizing: border-box; }
            body {
                font-family: 'Inter', 'Helvetica Neue', Helvetica, Arial, sans-serif;
                display: flex;
                justify-content: center;
                align-items: center;
                min-height: 100dvh;
                margin: 0;
                padding: 16px;
                background: #F7F5EF;
            }
            .caja-login {
                background: white;
                padding: 28px 24px;
                border-radius: 10px;
                box-shadow: 0 1px 3px rgba(28,43,41,0.08), 0 1px 2px rgba(28,43,41,0.06);
                width: 100%;
                max-width: 340px;
            }
            .caja-login h2 {
                margin: 0 0 18px 0;
                font-family: 'Barlow Condensed', sans-serif;
                font-weight: 700;
                color: #0B4F4A;
            }
            .campo { margin-bottom: 14px; }
            .campo label { display: block; margin-bottom: 4px; font-weight: 500; }
            .campo input {
                width: 100%;
                padding: 10px;
                font-size: 1em;
                border: 1px solid #DDD8CC;
                border-radius: 6px;
            }
            button[type="submit"] {
                width: 100%;
                padding: 10px;
                font-weight: 700;
                font-size: 1em;
                background: #0B4F4A;
                color: white;
                border: none;
                border-radius: 6px;
                cursor: pointer;
            }
        </style>
    </head>
    <body>
        <div class="caja-login">
            <h2>Iniciar sesión</h2>
            {{mensajeError}}
            <form method="post" action="/login">
                <input type="hidden" name="{{tokens.FormFieldName}}" value="{{tokens.RequestToken}}" />
                <div class="campo">
                    <label>Usuario:</label>
                    <input type="text" name="usuario" required />
                </div>
                <div class="campo">
                    <label>Contraseña:</label>
                    <input type="password" name="password" required />
                </div>
                <button type="submit">Entrar</button>
            </form>
        </div>
    </body>
    </html>
    """;

    return Results.Content(html, "text/html");
});

app.MapPost("/login", async (HttpContext http, IDbContextFactory<TorneoContext> dbFactory, IAntiforgery antiforgery) =>
{
    try
    {
        await antiforgery.ValidateRequestAsync(http);
    }
    catch
    {
        return Results.Redirect("/login?error=1");
    }

    var form = await http.Request.ReadFormAsync();
    string usuario = form["usuario"].ToString();
    string password = form["password"].ToString();

    using var db = await dbFactory.CreateDbContextAsync();
    var user = await db.Usuarios.FirstOrDefaultAsync(u => u.NombreUsuario == usuario && u.Activo);

    if (user == null || !PasswordHasher.Verify(password, user.PasswordHash))
        return Results.Redirect("/login?error=1");

    var claims = new List<Claim>
    {
        new Claim(ClaimTypes.Name, user.NombreUsuario),
        new Claim(ClaimTypes.Role, user.Rol)
    };
    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
    var principal = new ClaimsPrincipal(identity);

    await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, new AuthenticationProperties
    {
        IsPersistent = true,
        ExpiresUtc = DateTimeOffset.UtcNow.AddHours(12)
    });

    return Results.Redirect("/");
});

app.MapPost("/logout", async (HttpContext http) =>
{
    await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/login");
});

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();