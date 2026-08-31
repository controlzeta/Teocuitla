using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;
using Teocuitla.Web.Components;
using Teocuitla.Shared.Data;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, configuration) => 
    configuration.ReadFrom.Configuration(context.Configuration));

// Configurar compresión de respuestas (Brotli y Gzip)
builder.Services.AddResponseCompression(options =>
{
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
    options.EnableForHttps = true;
});

// Configurar descompresión de peticiones entrantes
builder.Services.AddRequestDecompression();

// Configurar MudBlazor
builder.Services.AddMudServices();

// Configurar el DbContext para SQL Server (con Fábrica, que registra tanto la fábrica como el DbContext de tipo scoped)
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
builder.Services.AddDbContextFactory<TeocuitlaDbContext>(options =>
    options.UseSqlServer(connectionString, x => x.MigrationsAssembly("Teocuitla.Web")));

// Registrar HttpClientFactory
builder.Services.AddHttpClient();

// Configurar CORS para permitir peticiones desde la extensión de Chrome y otros orígenes
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// Registrar controladores para soportar API de ingesta
builder.Services.AddControllers();
builder.Services.AddSingleton<Teocuitla.Web.Services.IngestionNotificationService>();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddHubOptions(options =>
    {
        options.MaximumReceiveMessageSize = 10 * 1024 * 1024; // 10 MB to support pasting large manual HTML source codes
    });

var app = builder.Build();

// Habilitar compresión de respuestas y descompresión de peticiones
app.UseResponseCompression();
app.UseRequestDecompression();

app.UseCors("AllowAll");

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapControllers(); // Habilitar enrutamiento de controladores API
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
