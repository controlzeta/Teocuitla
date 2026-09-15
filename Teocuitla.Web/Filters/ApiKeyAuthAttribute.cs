using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Teocuitla.Web.Filters
{
    /// <summary>
    /// Filtro de autorización de acción que valida la cabecera 'X-Api-Key' contra la configuración.
    /// Si la clave no está presente o no coincide, devuelve Unauthorized (401) con ProblemDetails.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public class ApiKeyAuthAttribute : Attribute, IAsyncActionFilter
    {
        public const string HeaderName = "X-Api-Key";

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var configuration = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();

            if (!context.HttpContext.Request.Headers.TryGetValue(HeaderName, out var extractedApiKey))
            {
                context.Result = new ObjectResult(new ProblemDetails
                {
                    Status = 401,
                    Title = "Unauthorized",
                    Detail = "Cabecera 'X-Api-Key' no proporcionada."
                })
                {
                    StatusCode = 401
                };
                return;
            }

            var configuredApiKey = configuration["Scraping:ApiKey"];
            if (string.IsNullOrWhiteSpace(configuredApiKey))
            {
                // Fallback seguro: si no está configurada, usar el valor por defecto de desarrollo
                configuredApiKey = "TeocuitlaDefaultApiKeySecret";
            }

            if (!string.Equals(extractedApiKey, configuredApiKey, StringComparison.Ordinal))
            {
                context.Result = new ObjectResult(new ProblemDetails
                {
                    Status = 401,
                    Title = "Unauthorized",
                    Detail = "API Key inválida."
                })
                {
                    StatusCode = 401
                };
                return;
            }

            await next();
        }
    }
}
