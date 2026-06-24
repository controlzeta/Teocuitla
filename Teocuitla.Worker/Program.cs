using Microsoft.EntityFrameworkCore;
using Teocuitla.Shared.Data;
using Teocuitla.Worker;
using Teocuitla.Worker.Services;

var builder = Host.CreateApplicationBuilder(args);

// Configurar base de datos SQL Server compartida
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
builder.Services.AddDbContextFactory<TeocuitlaDbContext>(options =>
    options.UseSqlServer(connectionString));

// También registrar DbContext normal para inyecciones estándar
builder.Services.AddDbContext<TeocuitlaDbContext>(options =>
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
