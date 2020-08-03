﻿using SpreadsheetLedger.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace SpreadsheetLedger.Core
{
    public sealed class CurrencyConverterV2: ICurrencyConverter
    {
        private Dictionary<string, (char op, string source)[]> _currencyRules;
        private Dictionary<string, List<PriceRecord>> _priceIndex;


        public CurrencyConverterV2(
            IEnumerable<CurrencyRecord> currencies,
            IEnumerable<PriceRecord> prices)
        {
            _currencyRules = currencies
                .Where(c => !string.IsNullOrEmpty(c.Currency))
                .ToDictionary(
                    c => c.Currency,
                    c => ParseRule(c.ConvertationRule ?? ""));

            _priceIndex = prices
                .Where(p => p.Date.HasValue && p.Price.HasValue && !string.IsNullOrEmpty(p.Symbol) && !string.IsNullOrEmpty(p.Currency))
                .GroupBy(p => $"{p.Symbol}/{p.Currency}:{p.Provider}".TrimEnd(':'))
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderBy(p => p.Date.Value).ToList());
        }

        internal static (char op, string source)[] ParseRule(string rule)
        {
            var match = Regex.Match(
                rule,
                @"^(\s*([*/])\s*\((\s*[^(),/:]+\s*/\s*[^(),/:]+\s*:\s*[^(),/:]*)\))*\s*$",
                RegexOptions.CultureInvariant);

            if (!match.Success)
                throw new LedgerException("Can't parse currency convertation rule: " + rule);

            var result = new (char op, string source)[match.Groups[2].Captures.Count];
            for (var i = 0; i < result.Length; i++)
            {
                result[i].op = match.Groups[2].Captures[i].Value[0];
                result[i].source = Regex.Replace(match.Groups[3].Captures[i].Value, @"\s*", "");
            }
            return result;
        }

        public decimal Convert(DateTime date, decimal amount, string symbol)
        {
            if (!_currencyRules.TryGetValue(symbol, out var rules))
                throw new LedgerException($"Can't find currency convertation rules for '{symbol}'.");

            var result = amount;
            foreach (var rule in rules)
            {
                // Find price 

                if (!_priceIndex.TryGetValue(rule.source, out var pl))
                    throw new LedgerException($"Can't find pricelist '{rule.source}' for '{symbol}' convertation.");

                if (date < pl[0].Date.Value)
                    throw new LedgerException($"Can't find '{rule.source}' price on {date:yyyy-MM-dd}.");

                decimal price = pl[pl.Count - 1].Price.Value;
                if (date < pl[pl.Count - 1].Date.Value)
                {
                    for (var i = 0; i < pl.Count; i++)
                    {
                        if (date == pl[i].Date.Value)
                        {
                            price = pl[i].Price.Value;
                            break;
                        }

                        if (date < pl[i].Date.Value)
                        {
                            price = pl[i - 1].Price.Value;
                            break;
                        }
                    }
                }

                // Apply price

                switch (rule.op)
                {
                    case '*':
                        result *= price;
                        break;
                    case '/':
                        result /= price;
                        break;
                    default:
                        throw new InvalidOperationException();
                }
            }
            return Math.Round(result, 4);
        }



    }
}
