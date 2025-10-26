using NewsApplication.Domain.DTOs.Scopes;
using NewsApplication.Domain.Enums;
using NewsApplication.Service.Interfaces;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static System.Formats.Asn1.AsnWriter;

namespace NewsApplication.Service.Implementations
{
    
    public sealed class ScopeResolverService : IScopeResolverService
    {
        private readonly ICityReadService _cities;
        private readonly ICountryReadService _countries;
        private readonly IScopePolicy _policy;
        private readonly IQueryTokenizer _tokenizer;

        private sealed record CountryHit(
        GeoCandidateDTO Dto,
        int TokenIndex,
        bool IsIso2Exact,
        bool IsNameExact,
        bool IsNameStarts);

        private sealed record CityHit(
        GeoCandidateDTO Dto,
        int? BigramStartIndex,   // null if single-token hit; else i where bigram (i,i+1)
            bool FromBigram);

        // thresholds from your spec
        private const double CountryThreshold = 0.6; // or exact ISO hit
        private const double CityThreshold = 0.6;
        private const int MaxCompositeTargets = 12; // reasonable cap for map pins

        public ScopeResolverService(
            IQueryTokenizer tokenizer,
            ICityReadService cities,
            ICountryReadService countries,
            IScopePolicy policy)
        {
            _tokenizer = tokenizer;
            _cities = cities;
            _countries = countries;
            _policy = policy;
        }

        public async Task<ScopePreviewDTO> PreviewAsync(string query, CancellationToken ct)
        {
            var raw = _tokenizer.Split(query);                               // ["San", "José", "CR", "sports"]
            var tokens = raw.Select(t => _tokenizer.Normalize(t)).ToList();  // ["san","jose","cr","sports"]

            if (tokens.Count == 0)
            {
                // Return a minimal, valid preview object for the older DTO shape
                return new ScopePreviewDTO
                {
                    OriginalQuery = query,
                    Kind = ScopeKind.Other,
                    IsAmbiguous = false,
                    NonGeoKeywords = new List<string>(),
                    CountryMatches = new List<GeoCandidateDTO>(),
                    CityMatches = new List<GeoCandidateDTO>(),
                    CitiesGroupedByCountry = new Dictionary<string, List<GeoCandidateDTO>>(),
                    Tokens = new List<ScopeTokenDTO>(),
                    Targets = new List<GeoCandidateDTO>()
                };
            }

            // per-token searches in parallel
            var tokenTasks = tokens.Select(t => ResolveTokenAsync(t, ct)).ToArray();
            await Task.WhenAll(tokenTasks);

            var tokenDtos = tokenTasks.Select(t => t.Result).ToList();

            // Helper local function to promote a token to "city" if it looks geo-capable
            void PromoteCityIfApplicable(int idx)
            {
                var t = tokenDtos[idx];
                if (t.MatchedEntityType == "non-geo" && t.Cities != null && t.Cities.Count > 0)
                {
                    tokenDtos[idx] = new ScopeTokenDTO
                    {
                        Raw = t.Raw,
                        Normalized = t.Normalized,
                        MatchedEntityType = "city",       // <- changed
                        Countries = t.Countries,
                        Cities = t.Cities
                    };
                }
            }

            // 4a) "san" followed by "jose" -> both are geo-capable (helps nonGeo list & UI chips)
            for (int i = 0; i < tokens.Count - 1; i++)
            {
                var a = tokens[i];
                var b = tokens[i + 1];

                // normalized already via tokenizer; "josé" -> "jose"
                if (a == "san" && (b == "jose" || b.StartsWith("jose")))
                {
                    PromoteCityIfApplicable(i);
                    PromoteCityIfApplicable(i + 1);
                }
            }

            // 4b) (optional) ISO2 exact country promotion: if any country in this token's results has ISO2 == token
            for (int i = 0; i < tokens.Count; i++)
            {
                var term = tokens[i];
                var tok = tokenDtos[i];
                if (tok.Countries != null && tok.Countries.Any(c =>
                 (!string.IsNullOrEmpty(c.CountryIso2)
                   && string.Equals(_tokenizer.Normalize(c.CountryIso2), term, StringComparison.Ordinal))
              || (!string.IsNullOrEmpty(c.CountryIso3)
                   && string.Equals(_tokenizer.Normalize(c.CountryIso3), term, StringComparison.Ordinal))
                 ))
                {
                    tokenDtos[i] = new ScopeTokenDTO
                    {
                        Raw = tok.Raw,
                        Normalized = tok.Normalized,
                        MatchedEntityType = "country",      // <- changed
                        Countries = tok.Countries,
                        Cities = tok.Cities
                    };
                }

            }

            // --- collect city hits from single-token queries ---
            var cityHits = new List<CityHit>();
            for (int i = 0; i < tokenDtos.Count; i++)
            {
                var token = tokenDtos[i];
                foreach (var dto in token.Cities.Where(c => c.Score >= CityThreshold))
                    cityHits.Add(new CityHit(dto, null, false));
            }
            // --- bigram city queries, e.g., "san jose" ---
            var bigramTasks = new List<Task<(int start, IReadOnlyList<GeoCandidateDTO> results)>>();

            for (int i = 0; i < tokens.Count - 1; i++)
            {
                var bigram = $"{tokens[i]} {tokens[i + 1]}";
                var start = i;
                bigramTasks.Add(_cities.SearchAsync(bigram, 10, ct)
                .ContinueWith(t => (start, t.Result), ct));
            }

            await Task.WhenAll(bigramTasks);

            // tag bigram results
            foreach (var bt in bigramTasks)
            {
                var (start, results) = bt.Result;
                foreach (var dto in results.Where(c => c.Score >= CityThreshold))
                    cityHits.Add(new CityHit(dto, start, true));
            }

            // --- bigram COUNTRY queries, e.g., "san marino" ---
            var countryBigramTasks = new List<Task<(int start, IReadOnlyList<GeoCandidateDTO> results)>>();

            for (int i = 0; i < tokens.Count - 1; i++)
            {
                var bigram = $"{tokens[i]} {tokens[i + 1]}";
                var start = i;
                countryBigramTasks.Add(_countries.SearchAsync(bigram, 10, ct)
                    .ContinueWith(t => (start, t.Result), ct));
            }

            await Task.WhenAll(countryBigramTasks);

                        // --- merge bigram COUNTRY results into countryHits ---
            var countryHits = new List<CountryHit>();
                        for (int k = 0; k < countryBigramTasks.Count; k++)
                            {
                var(start, results) = await countryBigramTasks[k];
                var bigram = $"{tokens[start]} {tokens[start + 1]}";
                var bigramN = _tokenizer.Normalize(bigram);
                
                                foreach (var dto in results)
                                    {
                    var nameN = _tokenizer.Normalize(dto.Name ?? string.Empty);
                    var iso2N = _tokenizer.Normalize(dto.CountryIso2 ?? dto.Id ?? string.Empty);
                    var iso3N = _tokenizer.Normalize(dto.CountryIso3 ?? string.Empty);

                    var isIsoExact = string.Equals(iso2N, bigramN, StringComparison.Ordinal)
                                   || string.Equals(iso3N, bigramN, StringComparison.Ordinal);
                    var isNameExact = string.Equals(nameN, bigramN, StringComparison.Ordinal);
                    
                                        // keep exacts; otherwise enforce your CountryThreshold
                                        if (!isIsoExact && !isNameExact && dto.Score < CountryThreshold) continue;
                    
                                        // exact → force Score = 1.0 (clone)
                    var adjusted = (isIsoExact || isNameExact)
                                            ? new GeoCandidateDTO
                    {
                        Id = dto.Id,
                        Name = dto.Name,
                        CountryIso2 = dto.CountryIso2,
                        CountryIso3 = dto.CountryIso3,
                        CountryName = dto.CountryName,
                        Lat = dto.Lat,
                        Lng = dto.Lng,
                        Score = 1.0
                    }
                    : dto;
                    
                    countryHits.Add(new CountryHit(
                    Dto: adjusted,
                    TokenIndex: start,                 // bigram begins at `start`
                    IsIso2Exact: isIsoExact,
                    IsNameExact: isNameExact,
                    IsNameStarts: nameN.StartsWith(bigramN, StringComparison.Ordinal)
                                        ));
                                    }
                            }

            // --- add single-token COUNTRY hits ---
            for (int i = 0; i < tokens.Count; i++)
            {
                var term = tokens[i];          // normalized token
                var token = tokenDtos[i];

                foreach (var dto in token.Countries)
                {
                    // normalize fields to mirror DB (via tokenizer)
                    var nameN = _tokenizer.Normalize(dto.Name ?? string.Empty);
                    var iso2N = _tokenizer.Normalize(dto.CountryIso2 ?? dto.Id ?? string.Empty);
                    var iso3N = _tokenizer.Normalize(dto.CountryIso3 ?? string.Empty);

                    var isIsoExact = string.Equals(iso2N, term, StringComparison.Ordinal)
                                  || string.Equals(iso3N, term, StringComparison.Ordinal);

                    var isNameExact = string.Equals(nameN, term, StringComparison.Ordinal);
                    if (!isIsoExact && !isNameExact && dto.Score < CountryThreshold) continue;

                            var adjusted = (isIsoExact || isNameExact)
                          ? new GeoCandidateDTO
                          {
                              Id = dto.Id,
                              Name = dto.Name,
                              CountryIso2 = dto.CountryIso2,
                              CountryIso3 = dto.CountryIso3,
                              CountryName = dto.CountryName,
                              Lat = dto.Lat,
                              Lng = dto.Lng,
                              Score = 1.0
                            }
                          : dto;
                            countryHits.Add(new CountryHit(
                            Dto: adjusted,
                            TokenIndex: i,
                            IsIso2Exact: isIsoExact,
                            IsNameExact: isNameExact,
                            IsNameStarts: nameN.StartsWith(term, StringComparison.Ordinal)
                        ));
                }
            }

            // --- merge bigram COUNTRY results into countryHits ---
            for (int k = 0; k < countryBigramTasks.Count; k++)
            {
                var (start, results) = await countryBigramTasks[k];
                var bigram = $"{tokens[start]} {tokens[start + 1]}";
                var bigramN = _tokenizer.Normalize(bigram);

                foreach (var dto in results)
                {
                    var nameN = _tokenizer.Normalize(dto.Name ?? string.Empty);
                    var iso2N = _tokenizer.Normalize(dto.CountryIso2 ?? dto.Id ?? string.Empty);
                    var iso3N = _tokenizer.Normalize(dto.CountryIso3 ?? string.Empty);

                    var isIsoExact = string.Equals(iso2N, bigramN, StringComparison.Ordinal)
                                  || string.Equals(iso3N, bigramN, StringComparison.Ordinal);
                    var isNameExact = string.Equals(nameN, bigramN, StringComparison.Ordinal);

                    // enforce your rule: keep exact; else must pass threshold
                    if (!isIsoExact && !isNameExact && dto.Score < CountryThreshold) continue;

                    // exact → force Score = 1.0 (clone)
                    var adjusted = (isIsoExact || isNameExact)
                        ? new GeoCandidateDTO
                        {
                            Id = dto.Id,
                            Name = dto.Name,
                            CountryIso2 = dto.CountryIso2,
                            CountryIso3 = dto.CountryIso3,
                            CountryName = dto.CountryName,
                            Lat = dto.Lat,
                            Lng = dto.Lng,
                            Score = 1.0
                        }
                        : dto;

                    countryHits.Add(new CountryHit(
                        Dto: adjusted,
                        TokenIndex: start,                 // bigram starts at `start`
                        IsIso2Exact: isIsoExact,
                        IsNameExact: isNameExact,
                        IsNameStarts: nameN.StartsWith(bigramN, StringComparison.Ordinal)
                    ));
                }
            }


            // Deduplicate per ISO2 (or Id fallback) keeping the best-ranked hit
            var bestCountryByIso = countryHits
                .GroupBy(h => h.Dto.CountryIso2 ?? h.Dto.Id)
                .Select(g => g
                    .OrderByDescending(h => h.IsIso2Exact)
                    .ThenByDescending(h => h.IsNameExact)
                    .ThenByDescending(h => h.IsNameStarts)
                    .ThenByDescending(h => h.Dto.Score)
                    .ThenBy(h => h.Dto.CountryIso2) // stable tiebreak
                    .First())
                .ToList();

            // Final ordered list across all countries
            var countryMatches = bestCountryByIso
                .OrderByDescending(h => h.IsIso2Exact)
                .ThenByDescending(h => h.IsNameExact)
                .ThenByDescending(h => h.IsNameStarts)
                .ThenByDescending(h => h.Dto.Score)
                .ThenBy(h => h.Dto.CountryIso2)
                .Select(h => h.Dto)
                .ToList();

            // pick the best country (prefer ISO2 exact) using the already-ranked hits
            var selectedCountryHit = bestCountryByIso
                .OrderByDescending(h => h.IsIso2Exact)
                .ThenByDescending(h => h.IsNameExact)
                .ThenByDescending(h => h.IsNameStarts)
                .ThenByDescending(h => h.Dto.Score)
                .ThenBy(h => h.Dto.CountryIso2)
                .FirstOrDefault();

            // dedupe: pick winner per city (prefer bigram, then score)
            var selectedCityRows = cityHits
                .GroupBy(h => h.Dto.Id)
                .Select(g => g
                    .OrderByDescending(x => x.FromBigram)
                    .ThenByDescending(x => x.Dto.Score)
                    .First())
                .ToList();

            var nonGeo = tokenDtos
              .Where(t => t.MatchedEntityType == "non-geo")
              .Select(t => t.Normalized)
              .ToList();

            // 2) Trace sink + ask policy for chosen country
            var traceLines = new List<string>();
            string? chosenFromPolicy = (_policy is ScopePolicy concretePolicy)
                ? concretePolicy.DebugChooseCountryIso2(countryMatches, nonGeo, (k, v) => traceLines.Add($"{k}:{v}"))
                : null;

            // 3) Decide which ISO2 (if any) we bias by for ordering/targeting
            string? chosenIso2ForTargets = chosenFromPolicy;
            if (string.IsNullOrWhiteSpace(chosenIso2ForTargets))
                chosenIso2ForTargets = selectedCountryHit?.Dto.CountryIso2;

            // NEW: if still empty, infer from the (raw) city rows when they’re all in one country
            if (string.IsNullOrWhiteSpace(chosenIso2ForTargets))
            {
                var distinctCoFromRaw = cityHits
                    .Select(h => h.Dto.CountryIso2)
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (distinctCoFromRaw.Count == 1)
                {
                    chosenIso2ForTargets = distinctCoFromRaw[0];
                    traceLines.Add($"infer.iso2={chosenIso2ForTargets} source=rawCityHits.singleCountry");
                }
            }
            // 4) Order the CityHit rows with the CHOSEN iso, then bigram, then score, then name
            bool BelongsToChosen(CityHit h) =>
                !string.IsNullOrWhiteSpace(chosenIso2ForTargets) &&
                !string.IsNullOrWhiteSpace(h.Dto.CountryIso2) &&
                string.Equals(h.Dto.CountryIso2, chosenIso2ForTargets, StringComparison.OrdinalIgnoreCase);

            var orderedCityRows = selectedCityRows
               .OrderByDescending(h => BelongsToChosen(h))
               .ThenByDescending(h => h.FromBigram)
               .ThenByDescending(h => h.Dto.Score)
               .ThenBy(h => h.Dto.Name)
               .ToList();

            // 5) Project to DTOs after ordering
            var cityMatches = orderedCityRows.Select(h => h.Dto).ToList();
        

            // 6) If the "chosen" country actually has no cities, drop it so it won't affect targeting
            if (!string.IsNullOrWhiteSpace(chosenIso2ForTargets) &&
                !cityMatches.Any(c => string.Equals(c.CountryIso2, chosenIso2ForTargets, StringComparison.OrdinalIgnoreCase)))
            {
                chosenIso2ForTargets = null;
            }

            // Build Dictionary<string, List<GeoCandidateDTO>> (not IReadOnly*)
            var citiesByCountry = cityMatches
                .Where(c => !string.IsNullOrWhiteSpace(c.CountryIso2))
                .GroupBy(c => c.CountryIso2!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);


            // decide kind + ambiguity
            var kind = _policy.DecideKind(countryMatches, cityMatches);
            var ambiguous = _policy.IsAmbiguous(countryMatches, cityMatches, nonGeo);

            var targets = new List<GeoCandidateDTO>();

            if (!ambiguous && kind == ScopeKind.City &&
            cityMatches.GroupBy(c => c.CountryIso2).Count() == 1 &&
            cityMatches.GroupBy(c => c.Id).Count() > 1)
            {
                kind = ScopeKind.CityInCountry;
                ambiguous = true; // force ambiguity within that country
                traceLines.Add("ambiguous.singleCountryMultipleCities=true");
            }

            if (ambiguous && kind == ScopeKind.Composite)
            {
                // 1) First: include ALL exact matches (score ≈ 1) across countries
                const double exactFloor = 0.999; // tolerate float jitter
                var exactsAcross = cityMatches
                    .Where(c => c.Score >= exactFloor)
                    .GroupBy(c => c.Id)
                    .Select(g => g.First())
                    .OrderByDescending(c => c.Score)
                    .ThenBy(c => c.Name)
                    .ToList();

                if (exactsAcross.Count > 0)
                {
                    targets.AddRange(exactsAcross.Take(MaxCompositeTargets));
                    traceLines.Add($"targets.composite.exacts={exactsAcross.Count}");
                }
                else
                {
                    // 2) Fallback: your existing "strong city pins" logic
                    var compositeCityTargets = cityMatches
                        .Where(c => c.Score >= CityThreshold)
                        .GroupBy(c => c.Id)
                        .Select(g => g.First())
                        .OrderByDescending(c => c.Score)
                        .ThenBy(c => c.Name)
                        .Take(MaxCompositeTargets)
                        .ToList();

                    if (compositeCityTargets.Count > 0)
                    {
                        targets.AddRange(compositeCityTargets);
                        traceLines.Add($"targets.composite.cities={compositeCityTargets.Count}");
                    }
                }

                // 3) Last-resort: if still nothing, show country pins (centroids) when you have multiple countries
                if (targets.Count == 0 && countryMatches.Count >= 2)
                {
                    var compositeCountryTargets = countryMatches
                        .Where(c => c.Score >= CountryThreshold && c.Lat.HasValue && c.Lng.HasValue)
                        .Select(c => new GeoCandidateDTO
                        {
                            Id = c.Id,
                            Name = c.Name,
                            CountryIso2 = c.CountryIso2,
                            CountryName = c.CountryName,
                            Lat = c.Lat,
                            Lng = c.Lng,
                            Score = c.Score
                        })
                        .Take(MaxCompositeTargets)
                        .ToList();

                    if (compositeCountryTargets.Count > 0)
                    {
                        targets.AddRange(compositeCountryTargets);
                        traceLines.Add($"targets.composite.countries={compositeCountryTargets.Count}");
                    }
                }

                // (Do not run the old Composite block elsewhere; this replaces it.)
            }

            // Branch by kind & ambiguity
            else if (kind == ScopeKind.CityInCountry && ambiguous && !string.IsNullOrWhiteSpace(chosenIso2ForTargets))
            {
                var inCountry = cityMatches
                    .Where(c => string.Equals(c.CountryIso2, chosenIso2ForTargets, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                // 1) Include ALL exact matches first (score ~ 1.0)
                const double exactFloor = 0.999; // tolerate tiny float drift
                var exact = inCountry
                    .Where(c => c.Score >= exactFloor)
                    .GroupBy(c => c.Id)
                    .Select(g => g.First())
                    .OrderByDescending(c => c.Score)  // stable
                    .ThenBy(c => c.Name)
                    .ToList();

                if (exact.Count > 0)
                {
                    // Cap if you have a UI limit
                    targets.AddRange(exact.Take(MaxCompositeTargets));
                    traceLines.Add($"targets.cityincountry.exacts={exact.Count} iso={chosenIso2ForTargets}");
                }
                else
                {
                    // 2) Fallback – pick one of your existing strategies:

                    // (a) ALL in-country cities:
                    var allInCountry = inCountry
                        .GroupBy(c => c.Id)
                        .Select(g => g.First())
                        .OrderByDescending(c => c.Score)
                        .ThenBy(c => c.Name)
                        .ToList();
                    targets.AddRange(allInCountry.Take(MaxCompositeTargets));
                    traceLines.Add($"targets.cityincountry.all={allInCountry.Count} iso={chosenIso2ForTargets}");

                    // (b) Top-tied with "at least one" fail-safe:
                    //var grouped = inCountry
                    //    .GroupBy(c => c.Id)
                    //    .Select(g => g.First())
                    //    .ToList();

                    //var top = grouped.Max(c => c.Score);
                    //const double hi = 0.90;
                    //const double eps = 0.01;
                    //var threshold = (top >= hi) ? Math.Max(hi, top - eps) : top;

                    //var tied = grouped
                    //    .Where(c => c.Score >= threshold)
                    //    .OrderByDescending(c => c.Score)
                    //    .ThenBy(c => c.Name)
                    //    .ToList();

                    //if (tied.Count == 0)
                    //    tied.Add(grouped.OrderByDescending(c => c.Score).First());

                    //targets.AddRange(tied.Take(MaxCompositeTargets));
                    //traceLines.Add($"targets.cityincountry.tied={tied.Count} threshold={threshold:F2} iso={chosenIso2ForTargets}");
                }
            }

            else if (!ambiguous)
            {
                // Non-ambiguous flows:
                // – If we resolved to a single concrete city, return just that city
                // – If you later allow unambiguous Composite, include its resolved set
                // Reuse the order already established in cityMatches (which came from bigram-first → score → name)
                GeoCandidateDTO? bestCity = string.IsNullOrWhiteSpace(chosenIso2ForTargets)
                    ? cityMatches.FirstOrDefault()
                    : cityMatches.FirstOrDefault(c =>
                          string.Equals(c.CountryIso2, chosenIso2ForTargets, StringComparison.OrdinalIgnoreCase))
                      ?? cityMatches.FirstOrDefault();

                if (bestCity != null) targets.Add(bestCity);
                traceLines.Add(bestCity != null ? "targets.single=1" : "targets.single=0");
            }
            else
                {
                // Other ambiguous kinds → no pins
                traceLines.Add("targets.suppressed:ambiguous");
            }

            return new ScopePreviewDTO
            {
                OriginalQuery = query,                 // <— older DTO expects this
                Kind = kind,
                IsAmbiguous = ambiguous,
                NonGeoKeywords = nonGeo,              // List<string>
                CountryMatches = countryMatches,      // List<GeoCandidateDTO>
                CityMatches = cityMatches,            // List<GeoCandidateDTO>
                CitiesGroupedByCountry = citiesByCountry, // Dictionary<string, List<GeoCandidateDTO>>
                Tokens = tokenDtos,                   // List<ScopeTokenDTO>
                Targets = targets,

                Diagnostics = new Dictionary<string, object?>
                {
                    ["policyVersion"] = ScopePolicy.PolicyVersion,
                    ["chosenIso2"] = chosenFromPolicy,
                    ["targetIds"] = targets.Select(t => t.Id).ToList(),
                    ["topCountries"] = countryMatches
                    .Take(3)
                    .Select(c => new { c.CountryIso2, c.Name, c.Score })
                    .ToList(),
                    ["topCitiesInChosen"] = string.IsNullOrWhiteSpace(chosenFromPolicy)
                    ? null
                    : cityMatches
                        .Where(c => string.Equals(c.CountryIso2, chosenFromPolicy, StringComparison.OrdinalIgnoreCase))
                        .OrderByDescending(c => c.Score)
                        .Take(5)
                        .Select(c => new { c.Id, c.Name, c.Score })
                        .ToList(),
                            ["trace"] = traceLines
                }
            };
        }


        private async Task<ScopeTokenDTO> ResolveTokenAsync(string token, CancellationToken ct)
        {
            var norm = _tokenizer.Normalize(token);

            var (countryTask, cityTask) = (
                _countries.SearchAsync(norm, 10, ct),
                _cities.SearchAsync(norm, 10, ct)
            );

            await Task.WhenAll(countryTask, cityTask);
            var rawCountries = countryTask.Result;
            var cities = cityTask.Result;

            // === COUNTRY POST-PROCESSING ===
            // 1) Boost exact ISO2 or exact name to Score = 1.0
            // 2) Enforce CountryThreshold for the rest
            var countries = new List<GeoCandidateDTO>(rawCountries.Count);
            foreach (var co in rawCountries)
            {
                var nameN = _tokenizer.Normalize(co.Name ?? string.Empty);
                var iso2N = _tokenizer.Normalize(co.CountryIso2 ?? co.Id ?? string.Empty);
                var iso3N = _tokenizer.Normalize(co.CountryIso3 ?? string.Empty);

                var isIsoExact = string.Equals(iso2N, norm, StringComparison.Ordinal)
               || string.Equals(iso3N, norm, StringComparison.Ordinal);
                var isNameExact = string.Equals(nameN, norm, StringComparison.Ordinal);
                var isExact = isIsoExact || isNameExact;

                // clone/adjust: if your DTO is a record/class with settable Score, use a copy;
                // otherwise, create a new instance with the same fields except Score.
                var adjusted = co;
                if (isExact)
                {
                    adjusted = new GeoCandidateDTO
                    {
                        Id = co.Id,
                        Name = co.Name,
                        CountryIso2 = co.CountryIso2,
                        CountryIso3 = co.CountryIso3,   // keep threading
                        CountryName = co.CountryName,
                        Lat = co.Lat,
                        Lng = co.Lng,
                        Score = 1.0
                    };
                }

                // Keep if exact OR above threshold
                if (isExact || adjusted.Score >= CountryThreshold)
                    countries.Add(adjusted);
            }

            // === TOKEN CLASSIFICATION (unchanged logic) ===
            var matchedType = "non-geo";

            var topCountry = countries.FirstOrDefault();
            var isoExactTop = topCountry != null && (
            string.Equals(_tokenizer.Normalize(topCountry.Id ?? topCountry.CountryIso2 ?? string.Empty), norm, StringComparison.Ordinal)
         || string.Equals(_tokenizer.Normalize(topCountry.CountryIso3 ?? string.Empty), norm, StringComparison.Ordinal)
            );

            if (topCountry != null && (isoExactTop || topCountry.Score >= CountryThreshold))
            {
                matchedType = "country";
            }
            else
            {
                var topCity = cities.FirstOrDefault();
                if (topCity != null && topCity.Score >= CityThreshold)
                    matchedType = "city";
            }

            return new ScopeTokenDTO
            {
                Raw = token,
                Normalized = norm,
                MatchedEntityType = matchedType,
                Countries = countries,   // <- filtered + boosted
                Cities = cities
            };
        }

    }
}
