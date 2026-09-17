using Microsoft.EntityFrameworkCore;
using Teocuitla.Shared.Data;
using Teocuitla.Shared.Models;

namespace Teocuitla.Shared.Helpers
{
    public sealed record SelectorCandidateRegistrationResult(bool Added, bool Promoted, int ValidationCount);

    public static class SelectorCandidateRegistrar
    {
        public const int PromotionThreshold = 3;

        public static async Task<SelectorCandidateRegistrationResult> RegisterAsync(
            TeocuitlaDbContext context,
            int siteId,
            int variantId,
            string xpath,
            decimal price,
            string productUrl,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(xpath)) throw new ArgumentException("El selector no puede estar vacío.", nameof(xpath));
            if (price <= 0) throw new ArgumentOutOfRangeException(nameof(price), "El precio debe ser mayor a cero.");

            var alreadyValidated = await context.SelectoresCandidatos.AnyAsync(candidate =>
                candidate.CatalogoSitioId == siteId &&
                candidate.XPath == xpath &&
                candidate.VarianteComercialId == variantId,
                cancellationToken);

            if (!alreadyValidated)
            {
                context.SelectoresCandidatos.Add(new SelectorCandidato
                {
                    CatalogoSitioId = siteId,
                    VarianteComercialId = variantId,
                    XPath = xpath,
                    PrecioVerificado = price,
                    UrlProducto = productUrl
                });
                await context.SaveChangesAsync(cancellationToken);
            }

            var validationCount = await context.SelectoresCandidatos
                .Where(candidate => candidate.CatalogoSitioId == siteId && candidate.XPath == xpath)
                .Select(candidate => candidate.VarianteComercialId)
                .Distinct()
                .CountAsync(cancellationToken);

            var site = await context.CatalogoSitios.FindAsync([siteId], cancellationToken);
            var promoted = false;
            if (site is not null &&
                validationCount >= PromotionThreshold &&
                site.SelectorPrecioXPath != xpath)
            {
                site.SelectorPrecioXPath = xpath;
                await context.SaveChangesAsync(cancellationToken);
                promoted = true;
            }

            return new SelectorCandidateRegistrationResult(!alreadyValidated, promoted, validationCount);
        }
    }
}
