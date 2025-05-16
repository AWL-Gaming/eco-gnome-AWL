using System;
using System.Globalization;
using System.IO;
using ecocraft.Components;
using ecocraft.Extensions;
using ecocraft.Models;
using MudBlazor.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ecocraft.Services;
using ecocraft.Services.DbServices;
using ecocraft.Services.ImportData;

var builder = WebApplication.CreateBuilder(args);

// ───────────────────────────────────────────────────────────────────────────────
// Localization
// ───────────────────────────────────────────────────────────────────────────────
builder.Services.AddLocalization();
builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    options.DefaultRequestCulture = new RequestCulture("en-US");
    options.SupportedCultures = CultureInfo.GetCultures(CultureTypes.AllCultures);
    options.SupportedUICultures = CultureInfo.GetCultures(CultureTypes.AllCultures);
});

// ───────────────────────────────────────────────────────────────────────────────
// Core & UI services
// ───────────────────────────────────────────────────────────────────────────────
// Blazor Server circuits with 30s retention (instead of default 3m)
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents(opts =>
    {
        opts.DisconnectedCircuitRetentionPeriod = TimeSpan.FromSeconds(30);
    });

builder.Services.AddMudServices();

// ───────────────────────────────────────────────────────────────────────────────
// Database
// ───────────────────────────────────────────────────────────────────────────────
builder.Services.AddDbContext<EcoCraftDbContext>(options =>
    options
        .UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection"),
            o => o.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery))
        .EnableSensitiveDataLogging()
        .UseLoggerFactory(LoggerFactory.Create(bd =>
        {
            bd.AddConsole()
              .AddFilter(DbLoggerCategory.Database.Command.Name, LogLevel.Warning);
        }))
);

// ───────────────────────────────────────────────────────────────────────────────
// Controllers
// ───────────────────────────────────────────────────────────────────────────────
builder.Services.AddControllers();

// ───────────────────────────────────────────────────────────────────────────────
// DB Services
// ───────────────────────────────────────────────────────────────────────────────
builder.Services.AddScoped<CraftingTableDbService>();
builder.Services.AddScoped<ElementDbService>();
builder.Services.AddScoped<ItemOrTagDbService>();
builder.Services.AddScoped<PluginModuleDbService>();
builder.Services.AddScoped<RecipeDbService>();
builder.Services.AddScoped<ServerDbService>();
builder.Services.AddScoped<SkillDbService>();
builder.Services.AddScoped<TalentDbService>();
builder.Services.AddScoped<DynamicValueDbService>();
builder.Services.AddScoped<ModifierDbService>();
builder.Services.AddScoped<UserCraftingTableDbService>();
builder.Services.AddScoped<UserDbService>();
builder.Services.AddScoped<UserElementDbService>();
builder.Services.AddScoped<UserPriceDbService>();
builder.Services.AddScoped<UserRecipeDbService>();
builder.Services.AddScoped<UserMarginDbService>();
builder.Services.AddScoped<UserSettingDbService>();
builder.Services.AddScoped<UserTalentDbService>();
builder.Services.AddScoped<UserSkillDbService>();
builder.Services.AddScoped<DataContextDbService>();
builder.Services.AddScoped<ModUploadHistoryDbService>();

// ───────────────────────────────────────────────────────────────────────────────
// Business Services
// ───────────────────────────────────────────────────────────────────────────────
builder.Services.AddScoped<ContextService>();
builder.Services.AddScoped<ImportDataService>();
builder.Services.AddScoped<PriceCalculatorService>();
builder.Services.AddScoped<ServerDataService>();
builder.Services.AddScoped<UserServerDataService>();

// ───────────────────────────────────────────────────────────────────────────────
// Util Services
// ───────────────────────────────────────────────────────────────────────────────
builder.Services.AddScoped<LocalStorageService>();
builder.Services.AddScoped<LocalizationService>();

// ───────────────────────────────────────────────────────────────────────────────
// Memory trimming on circuit close (GC + LOH compaction)
// ───────────────────────────────────────────────────────────────────────────────
builder.Services.AddSingleton<CircuitHandler, MemoryTrimCircuitHandler>();

// ───────────────────────────────────────────────────────────────────────────────
// Authorization
// ───────────────────────────────────────────────────────────────────────────────
builder.Services.AddScoped<Authorization>();
builder.Services.AddAuthorization(config =>
{
    config.AddPolicy("IsServerAdmin", policy =>
        policy.Requirements.Add(new IsServerAdminRequirement()));
});

// ───────────────────────────────────────────────────────────────────────────────
// Build
// ───────────────────────────────────────────────────────────────────────────────
var app = builder.Build();

// Localization middleware
var locOptions = app.Services.GetService<IOptions<RequestLocalizationOptions>>();
app.UseRequestLocalization(locOptions!.Value);

// Auto-apply EF Core migrations
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<EcoCraftDbContext>();
    db.Database.Migrate();
}

// Error handling
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

// Custom static‐files routing for eco‐icons
app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value;
    if (path is not null && path.StartsWith("/assets/eco-icons/"))
    {
        var serverId = context.Request.Query.TryGetValue("serverId", out var sid) ? sid.ToString() : null;
        var wwwRoot = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "assets");
        var candidate = Path.Combine(wwwRoot, serverId ?? "no_found", path["/assets/eco-icons/".Length..]);
        if (serverId != null && File.Exists(candidate))
        {
            context.Request.Path = path.Replace("eco-icons", serverId);
        }
        else
        {
            // fallback to default or mod-icons
            var defaultPath = Path.Combine(wwwRoot, "eco-icons", path["/assets/eco-icons/".Length..]);
            if (!File.Exists(defaultPath))
            {
                var alt = defaultPath.Replace(".png", "Item.png");
                if (File.Exists(alt))
                    context.Request.Path = path.Replace("eco-icons", "mod-icons").Replace(".png", "Item.png");
                else
                    context.Request.Path = path.Replace("eco-icons", "mod-icons");
            }
        }
    }
    await next();
});

app.UseStaticFiles();
app.MapControllers();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Provide IWebHostEnvironment to StaticEnvironmentAccessor
StaticEnvironmentAccessor.WebHostEnvironment =
    app.Services.GetRequiredService<IWebHostEnvironment>();

app.Run();
