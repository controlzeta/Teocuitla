using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Teocuitla.Shared.Helpers
{
    public sealed class PriceParseResult
    {
        public decimal Price { get; init; }
        public string? Currency { get; init; }
        public string RawValue { get; init; } = string.Empty;
    }

    public static class PriceParser
    {
        private static readonly Regex CurrencyRegex = new Regex(
            @"(?<![A-Za-z])(?<currency>MXN|USD|EUR|GBP)(?![A-Za-z])|(?<symbol>[$€£])",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex NumberRegex = new Regex(
            @"(?<![\d.,])(?<amount>\d{1,3}(?:[.,]\d{3})+(?:[.,]\d{1,2})?|\d+(?:[.,]\d{1,2})?)(?![\d.,])",
            RegexOptions.Compiled);

        public static PriceParseResult? Parse(string? rawPrice)
        {
            if (string.IsNullOrWhiteSpace(rawPrice)) return null;

            var matches = NumberRegex.Matches(rawPrice);
            if (matches.Count == 0) return null;

            var match = matches.Cast<Match>().FirstOrDefault(candidate => HasCurrencyNearby(candidate, rawPrice)) ?? matches[0];

            var normalized = NormalizeSeparators(match.Groups["amount"].Value);
            if (!decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var price) || price <= 0)
            {
                return null;
            }

            var currencyMatch = CurrencyRegex.Match(rawPrice);
            return new PriceParseResult
            {
                Price = price,
                Currency = NormalizeCurrency(currencyMatch),
                RawValue = match.Groups["amount"].Value
            };
        }

        private static string NormalizeSeparators(string amount)
        {
            var lastDot = amount.LastIndexOf('.');
            var lastComma = amount.LastIndexOf(',');

            if (lastDot >= 0 && lastComma >= 0)
            {
                return lastDot > lastComma
                    ? amount.Replace(",", string.Empty)
                    : amount.Replace(".", string.Empty).Replace(",", ".");
            }

            var separatorIndex = Math.Max(lastDot, lastComma);
            if (separatorIndex < 0) return amount;

            var fractionLength = amount.Length - separatorIndex - 1;
            if (fractionLength == 3)
            {
                return amount.Replace(".", string.Empty).Replace(",", string.Empty);
            }

            return amount.Replace(",", ".");
        }

        private static bool HasCurrencyNearby(Match numberMatch, string rawPrice)
        {
            var contextStart = Math.Max(0, numberMatch.Index - 6);
            var contextLength = Math.Min(numberMatch.Value.Length + 12, rawPrice.Length - contextStart);
            return CurrencyRegex.IsMatch(rawPrice.Substring(contextStart, contextLength));
        }

        private static string? NormalizeCurrency(Match currencyMatch)
        {
            if (!currencyMatch.Success) return null;
            if (currencyMatch.Groups["currency"].Success)
            {
                return currencyMatch.Groups["currency"].Value.ToUpperInvariant();
            }

            return currencyMatch.Groups["symbol"].Value switch
            {
                "$" => null,
                "€" => "EUR",
                "£" => "GBP",
                _ => null
            };
        }
    }
}
