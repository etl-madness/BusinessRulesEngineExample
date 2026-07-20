using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using BusinessRulesEngineExample.Services;
using BusinessRulesEngineExample.Models;
using Radzen;
using EtlAnalytics.RulesEngine.Services;
using EtlAnalytics.RulesEngine.Interfaces;
using EtlAnalytics.RulesEngine.Providers;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpClient();



// Radzen Services
builder.Services.AddScoped<DialogService>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddScoped<TooltipService>();
builder.Services.AddScoped<ContextMenuService>();

// Application Services
builder.Services.AddSingleton<IEncryptionService, NoEncryptionService>();
builder.Services.AddScoped<SqlDatabaseService>();
builder.Services.AddScoped<IBusinessRuleStore>(sp => sp.GetRequiredService<SqlDatabaseService>());
builder.Services.AddScoped<IRuleDbProvider, SqlServerRuleDbProvider>();
builder.Services.AddScoped<BusinessRuleEngine<BusinessRuleContext>>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseStaticFiles();


app.UseRouting();

app.MapBlazorHub();
app.MapControllers();

app.MapFallbackToPage("/_Host");

// Initialize database tables
using (var scope = app.Services.CreateScope())
{
    var dbService = scope.ServiceProvider.GetRequiredService<SqlDatabaseService>();
    try 
    {
        await dbService.CreateBusinessRuleTablesIfNotExistsAsync();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Database initialization failed: {ex.Message}");
    }
}

app.Run();
