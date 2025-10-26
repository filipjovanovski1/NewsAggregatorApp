using NewsApplication.Service.Interfaces;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace NewsApplication.Service.Implementations
{
    /*
     
    QueryTokenizer is the first step in processing user search text. It splits what the user types 
    (e.g. "San José, CR sports") into individual words (tokens) and cleans each one so the database can 
    match them reliably.

    Split: breaks text by spaces, commas, and dots → ["San", "José", "CR", "sports"]

    Normalize: makes each token lowercase and removes accents → ["san", "jose", "cr", "sports"]

    In short:

    It converts messy, human-typed text into clean, database-ready tokens — acting as a translator between 
    the user’s input and your search logic.

    */
    public sealed class QueryTokenizer : IQueryTokenizer
    {
        // Matches comma, dot, or any whitespace, one or more times.
        private static readonly Regex SplitRegex = new Regex(@"[,\.\s]+",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        
        private static readonly (string From, string To)[] ExtraMaps =
        {
            ("ß", "ss"),   
            ("Æ", "ae"), ("æ", "ae"),
            ("Ø", "o"),  ("ø", "o"),
            ("Ð", "d"),  ("ð", "d"),
            ("Þ", "th"), ("þ", "th"),
            ("Ł", "l"), ("ł", "l"),
            ("İ","i"),  ("ı","i"),
            ("Đ","d"), ("đ","d"),
            ("Ħ","h"), ("ħ","h"),
            ("Å","a"), ("å","a"),
            ("Ĳ","ij"), ("ĳ","ij"),
            ("Ǆ","dz"), ("ǅ","dz"), ("ǆ","dz"),
            ("Ǉ","lj"), ("ǈ","lj"), ("ǉ","lj"),
            ("Ǌ","nj"), ("ǋ","nj"), ("ǌ","nj"),
            ("ŉ","n"),
            ("Ŋ","ng"), ("ŋ","ng"),
            ("Ƒ","f"), ("ƒ","f"),
            ("Ğ","g"), ("ğ","g"),   
            ("Ş","s"), ("ş","s"),
            ("Ə","e"), ("ə","e"),
            ("Ŀ","l"), ("ŀ","l"),   
            ("·",""),
            ("µ","u"),   
            ("ℓ","l"),   
            ("№","No"),  
            ("ª","a"), ("º","o"),




        };

        public IReadOnlyList<string> Split(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return Array.Empty<string>();

            // RAW tokens: preserve original case + diacritics (acceptance requires this)
            var parts = SplitRegex.Split(query);
            var list = new List<string>(parts.Length);
            foreach (var part in parts)
            {
                var t = part.Trim();
                if (t.Length > 0)
                    list.Add(t);
            }
            return list;
        }

        public string Normalize(string token)
        {
            if (string.IsNullOrEmpty(token))
                return string.Empty;

            // 1) fast path: apply explicit maps first (keeps behavior in lockstep with DB if you added such rules)
            var mapped = ApplyExtraMaps(token);

            // 2) strip diacritics using FormD + NonSpacingMark filter (matches common .NET “unaccent” approach)
            var unaccented = RemoveDiacritics(mapped);

            // 3) lowercase invariant (DB side uses lower(unaccent(...)))
            return unaccented.ToLowerInvariant();
        }

        private static string ApplyExtraMaps(string s)
        {
            // Only alloc if we actually replace.
            StringBuilder? sb = null;

            foreach (var (from, to) in ExtraMaps)
            {
                if (s.IndexOf(from, StringComparison.Ordinal) >= 0)
                {
                    sb ??= new StringBuilder(s);
                    sb.Replace(from, to);
                    s = sb.ToString();
                    sb = null; // reset to avoid cascading cost; next map will recreate if needed
                }
            }
            return s;
        }

        private static string RemoveDiacritics(string s)
        {
            // Normalize to FormD to separate base chars & combining marks.
            var formD = s.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(formD.Length);

            for (int i = 0; i < formD.Length; i++)
            {
                var ch = formD[i];
                var cat = CharUnicodeInfo.GetUnicodeCategory(ch);
                if (cat != UnicodeCategory.NonSpacingMark)
                    sb.Append(ch);
            }

            // Recompose to FormC (clean output)
            return sb.ToString().Normalize(NormalizationForm.FormC);
        }
    }
}
