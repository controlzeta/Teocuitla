using Microsoft.EntityFrameworkCore;
using Teocuitla.Shared.Data;
using Teocuitla.Worker;
using Teocuitla.Worker.Services;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);

// Configurar Serilog con consola y HTTP sink
var logsIngestionUrl = builder.Configuration["Logging:LogsIngestionUrl"] ?? "http://localhost:5181/api/logs";

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.Http(
        requestUri: logsIngestionUrl,
        queueLimitBytes: null,
        textFormatter: new Serilog.Formatting.Compact.CompactJsonFormatter())
    .CreateLogger();

builder.Logging.ClearProviders();
builder.Services.AddSerilog();

// Configurar base de datos SQL Server compartida (AddDbContextFactory registra la fábrica y el DbContext como Scoped automáticamente)
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
builder.Services.AddDbContextFactory<TeocuitlaDbContext>(options =>
    options.UseSqlServer(connectionString));

// Registrar HttpClientFactory
builder.Services.AddHttpClient();

// Registrar servicios de negocio (Transient/Singleton para evitar dependencias cautivas)
builder.Services.AddTransient<ProxyService>();
builder.Services.AddTransient<ScraperService>();

// Registrar el servicio en segundo plano (Worker)
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
