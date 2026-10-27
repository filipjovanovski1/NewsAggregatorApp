import os


OUTPUT_DIR = "output"

# --- Run tracking -----------------------------------------------------------
# Bumped when the shape of run.json changes.
RUN_SCHEMA_VERSION = 1

# A run whose stages all failed individually still reaches the end and reports
# success — an unreachable Ollama yields a full set of default REJECTs, a
# rate-limited DDG yields zero candidates. Both look like a clean empty result,
# and the .NET side would upsert them as fact. Past these ratios a stage is
# reported as failed instead.
EMPTY_QUERY_FAIL_RATIO = 0.8
CLASSIFY_ERROR_FAIL_RATIO = 0.5

# Discovery is the only stage backed by a rate-limited external quota, so its
# output is cached per location and reused by a retry. Keyed on the location,
# not the job: .NET issues a fresh job_id per attempt (§4.1), so a job-scoped
# cache would never be hit by the retry it exists to serve.
DISCOVER_CACHE_TTL_HOURS = 24

# --- Result payload ---------------------------------------------------------
# The wire contract with .NET (docs/integration-plan.md §2), versioned
# separately from RUN_SCHEMA_VERSION: run.json is a local debug artifact that
# can change freely, this one cannot change without a coordinated deploy.
RESULT_SCHEMA_VERSION = 1

# Page titles are taglines as often as they are names: "Covering politics,
# society and business in Southern and Eastern Europe | Balkan Insight". This
# splits a title into segments so the name can be recovered from one of them.
#
# Three alternatives, and the whitespace in the last two is load-bearing:
# symbol separators can be bare because they do not occur inside words, but a
# colon must be followed by space or "Vesti 19:30" splits into "Vesti 19" and
# "30", and a hyphen needs space on both sides or "north-macedonia" comes
# apart. Both were live bugs — the time one resolved a title to the name "30".
NAME_SEPARATOR_PATTERN = r"\s*[|•·»‹›«–—]\s*|:\s+|\s+-\s+"

# Trailing domain labels that carry no brand: TLDs and the second-level
# registry labels under them. Everything <= 3 chars is dropped as a TLD without
# being listed, which covers every ccTLD and com/net/org; this set is only the
# longer ones. These are registry strings, not words in any language.
DOMAIN_GENERIC_LABELS = {"info", "name", "site", "online", "press", "blog"}

# Below this, a segment has too little alphanumeric substance to be matched
# against a domain — a two-character run hits by chance.
NAME_DOMAIN_MATCH_MIN_CHARS = 4

QUERY_TEMPLATES = [
    "{city} news",
    "{city} newspaper",
    "{city} local news",
    "{country} news website",
    "{country} national newspaper",
    "{country} news agency",
    "{country} online news portal",
    "top news sites in {country}",
    "most read newspaper {country}",
    "reliable news source {country}",
    "list of newspapers in {city}",
]

COUNTRY_ONLY_TEMPLATES = [
    "{country} news website",
    "{country} national newspaper",
    "{country} news agency",
    "{country} online news portal",
    "top news sites in {country}",
    "most read newspaper {country}",
    "reliable news source {country}",
]

# Every country dict below is keyed on ISO 3166-1 alpha-2, matching the primary
# key of the .NET `Country` table. Country *names* are never used as keys: the
# app's two seed CSVs spell the same country differently ("North Macedonia" vs
# "Macedonia, The former Yugoslav Rep. of"), so a name-keyed lookup cannot
# reliably hit. Names arrive on the request and are only ever query text.
LOCAL_NEWS_TERMS: dict[str, list[str]] = {
    "MK": ["вести", "весник", "портал", "дневник"],
    "RS": ["вести", "новине", "новости"],
    "HR": ["vijesti", "novine", "portal"],
    "BA": ["vijesti", "novine"],
    "SI": ["novice", "časopis"],
    "BG": ["новини", "вестник"],
    "AL": ["lajme", "gazetë"],
    "XK": ["lajme", "gazetë"],
    "GR": ["ειδήσεις", "εφημερίδα"],
    "TR": ["haberler", "gazete"],
    "DE": ["Nachrichten", "Zeitung"],
    "FR": ["actualités", "journal"],
    "ES": ["noticias", "periódico"],
    "IT": ["notizie", "giornale"],
    "PT": ["notícias", "jornal"],
    "NL": ["nieuws", "krant"],
    "PL": ["wiadomości", "gazeta"],
    "RO": ["știri", "ziar"],
    "CZ": ["zprávy", "noviny"],
    "HU": ["hírek", "újság"],
    "SE": ["nyheter", "tidning"],
    "NO": ["nyheter", "avis"],
    "DK": ["nyheder", "avis"],
    "FI": ["uutiset", "sanomalehti"],
    "UA": ["новини", "газета"],
    "RU": ["новости", "газета"],
    "JP": ["ニュース", "新聞"],
    "KR": ["뉴스", "신문"],
    "CN": ["新闻", "报纸"],
    "IN": ["समाचार", "अखबार"],
    "BR": ["notícias", "jornal"],
    "MX": ["noticias", "periódico"],
    "AR": ["noticias", "diario"],
    "EG": ["أخبار", "جريدة"],
    "SA": ["أخبار", "جريدة"],
    "IR": ["اخبار", "روزنامه"],
    "IL": ["חדשות", "עיתון"],
}

REJECTED_DOMAINS = {
    # Social media
    "facebook.com", "twitter.com", "x.com", "instagram.com", "tiktok.com",
    "linkedin.com", "reddit.com", "threads.net", "mastodon.social",
    "pinterest.com", "snapchat.com", "tumblr.com",
    # Video platforms
    "youtube.com", "youtu.be", "vimeo.com", "dailymotion.com", "twitch.tv",
    # E-commerce
    "amazon.com", "ebay.com", "aliexpress.com", "alibaba.com", "etsy.com",
    "shopify.com", "walmart.com",
    # Search engines / aggregators
    "google.com", "bing.com", "yahoo.com", "duckduckgo.com", "yandex.com",
    "baidu.com",
    # Wikipedia (not a news source)
    "wikipedia.org", "wikimedia.org",
    # Misc
    "github.com", "stackoverflow.com", "medium.com", "substack.com",
    "quora.com", "archive.org",
}

REJECTED_PATTERNS = [
    r"\.gov(\.[a-z]{2})?$",
    r"\.edu(\.[a-z]{2})?$",
]


OLLAMA_BASE_URL = os.getenv("OLLAMA_BASE_URL", "http://localhost:11434").rstrip("/")
OLLAMA_URL = f"{OLLAMA_BASE_URL}/api/chat"
MODEL = os.getenv("DISCOVERY_MODEL", "gpt-oss:120b-cloud")

# Homepage markdown is truncated before being sent for extraction.
LLM_EXTRACTION_MAX_CHARS = 12000

SYSTEM_PROMPT = """\
You are a news source classifier. You will receive information about a website \
that was discovered while searching for news sources in a specific location. \
Based on the signals provided, classify the website into exactly one of three categories:

1. NEWS_SOURCE — A website that directly publishes news articles. \
Examples: newspapers, news agencies, TV/radio station news sites, online-only news portals. \
Must produce original journalistic content on a regular basis.

2. DISCOVERY_SOURCE — A website that is not itself a news source, but is useful \
for discovering news sources. Examples: newspaper directories, media lists, \
press freedom indexes, journalism organizations, Wikipedia/wiki pages listing media outlets.

3. REJECT — A website that is neither a news source nor useful for discovering them. \
Examples: hotels, tourism sites, app stores, SEO tools, government portals, \
academic papers, e-commerce, social media, encyclopedias (unless listing media).

Weigh feed evidence heavily — every feed listed has been fetched and parsed, so \
its entry counts and dates are facts, not guesses:
- A feed with recent entries that originate on the site's own domain, from a \
single outlet, is strong evidence of NEWS_SOURCE.
- A feed whose entries mostly originate OFF the site's own domain, or that names \
many distinct originating outlets, means the site republishes other people's \
reporting. Classify it DISCOVERY_SOURCE, not NEWS_SOURCE, however much it looks \
like a news portal. One originating outlet means a publisher; dozens means an \
aggregator.
- "headlines only" feeds combined with a high off-domain ratio point the same \
way: aggregator, so DISCOVERY_SOURCE.
- No valid feed is weak evidence at most. Plenty of real news sites publish none.

Likewise treat an LLM analysis of content_type "news_aggregator", or \
"Has original articles: False", as strong evidence for DISCOVERY_SOURCE rather \
than NEWS_SOURCE.

Respond with a JSON object containing exactly these fields:
- "classification": one of "NEWS_SOURCE", "DISCOVERY_SOURCE", or "REJECT"
- "confidence": a number from 0.0 to 1.0
- "reason": a brief explanation (one sentence)

Respond ONLY with the JSON object, no other text."""

CLASSIFICATION_SCHEMA = {
    "type": "object",
    "properties": {
        "classification": {
            "type": "string",
            "enum": ["NEWS_SOURCE", "DISCOVERY_SOURCE", "REJECT"],
        },
        "confidence": {"type": "number"},
        "reason": {"type": "string"},
    },
    "required": ["classification", "confidence", "reason"],
}

LLM_EXTRACTION_INSTRUCTION = """\
You analyze website homepages and extract structured information about them. \
The website may be in any language — analyze the actual content regardless of language.

Respond with a JSON object containing exactly these fields:
- "is_news_site": boolean — does this website publish news articles?
- "has_original_articles": boolean — does it publish original reporting, or \
does it only aggregate and link to other sources?
- "language": string — ISO 639-1 code of the content, e.g. "mk", "en", "ru"
- "content_type": one of "news_portal", "news_aggregator", "newspaper", \
"tv_station", "radio_station", "blog", "directory", "government", "other"
- "sections": array of strings — editorial sections/categories found on the \
site, e.g. ["politics", "sport", "economy"]
- "site_description": string — one sentence describing what the site is about

Use exactly these field names. Respond ONLY with the JSON object, no other text."""

LLM_EXTRACTION_SCHEMA = {
    "type": "object",
    "properties": {
        "is_news_site": {
            "type": "boolean",
            "description": "Whether this website publishes news content",
        },
        "has_original_articles": {
            "type": "boolean",
            "description": "Whether the site publishes original articles vs just aggregating links",
        },
        "language": {
            "type": "string",
            "description": "ISO 639-1 language code of the content (e.g. mk, en, ru, sq)",
        },
        "content_type": {
            "type": "string",
            "enum": ["news_portal", "news_aggregator", "newspaper", "tv_station", "radio_station", "blog", "directory", "government", "other"],
        },
        "sections": {
            "type": "array",
            "items": {"type": "string"},
            "description": "Editorial sections/categories found on the site",
        },
        "site_description": {
            "type": "string",
            "description": "One sentence describing what this website is about",
        },
    },
    "required": ["is_news_site", "has_original_articles", "language", "content_type", "sections", "site_description"],
}

# Probed when a homepage's <head> exposes no feed links.
FEED_PATHS = [
    "/feed",
    "/rss",
    "/rss.xml",
    "/feed.xml",
    "/atom.xml",
    "/index.xml",
]

# WordPress comment feeds carry no articles — useless for ingestion.
FEED_URL_BLOCKLIST = ["/comments/feed"]

# --- Article path detection -------------------------------------------------
# Keyword patterns alone were English-only, which cost every local-language site
# the full 15-point `article_paths` weight while aljazeera.com and apnews.com
# collected it — a systematic bias against exactly the sites this pipeline
# exists to find. Measured on the MK/Skopje run: skopjeinfo.mk, novamakedonija,
# meta.mk, prv.mk, skopje1.mk, mrt.com.mk and arno.mk all scored 0.
#
# Keywords cover the languages we can enumerate; ARTICLE_SLUG_* is the
# language-agnostic backstop, since a long hyphenated final path segment is an
# article headline in any language.
ARTICLE_PATH_PATTERNS = [
    r"/\d{4}/\d{2}/",
    r"/\d{4}/\d{2}/\d{2}/",
    # English
    r"/news/", r"/article/", r"/story/", r"/post/", r"/breaking/",
    r"/opinion/", r"/editorial/",
    # Slavic (latin + cyrillic)
    r"/vesti/", r"/novosti/", r"/aktuelno/", r"/clanak/", r"/wiadomosci/",
    r"/zpravy/", r"/novini/",
    r"/вести/", r"/новости/", r"/актуелно/", r"/статья/", r"/статии/",
    # Germanic / Romance / other European
    r"/nachrichten/", r"/artikel/", r"/nieuws/", r"/nyheter/", r"/uutiset/",
    r"/actualites?/", r"/noticias?/", r"/notizie/", r"/articolo/",
    r"/stiri/", r"/hirek/", r"/lajme/",
    # Turkish / Greek / Arabic
    r"/haber(ler)?/", r"/eidiseis/", r"/akhbar/", r"/أخبار/",
]

# A final path segment of at least this many hyphen-separated tokens, and this
# many characters, is treated as an article slug regardless of language.
ARTICLE_SLUG_MIN_TOKENS = 3
ARTICLE_SLUG_MIN_LENGTH = 20

# --- Section extraction -----------------------------------------------------
# `sections` becomes NewsSource.Categories[] on the .NET side, which backs the
# `'sport' = ANY(Categories)` filter — so junk here poisons search. The MK run
# produced ".Nspx" filenames (nbrm.mk), URL-encoded Cyrillic (macedonism.org),
# pagination chrome (abnase.com: "811", "Next", "« Jul") and slugified article
# headlines (skopjeinfo.mk).
SECTION_STOPWORDS = {
    # navigation chrome
    "home", "next", "previous", "prev", "back", "top", "more", "menu",
    "search", "login", "log in", "sign in", "register", "subscribe",
    "newsletter", "newsletters", "author", "category", "categories",
    "featured", "all", "tag", "tags", "archive", "arhiva", "widget",
    # boilerplate pages
    "about", "about us", "contact", "contact us", "privacy", "privacy policy",
    "terms", "advertise", "marketing", "impressum", "impresum", "faq",
    "kontakt", "pretplata", "info",
    # CMS artifacts that reach the path fallback
    "wp content", "wp admin", "wp includes", "assets", "static", "media",
}

# A path segment with this many hyphen-separated tokens is an article headline,
# not a section. Applied only to the path fallback, never to nav link text —
# "Science & Technology" is a legitimate three-word section.
SECTION_MAX_SLUG_TOKENS = 3

# Feed validation. A nominated URL is only kept once it has been fetched and
# parsed as a real feed carrying entries — HTTP 200 alone proves nothing, some
# sites serve text/html for /rss instead of a 404.
FEED_FETCH_TIMEOUT = 10.0
FEED_MAX_BYTES = 2_000_000
FEED_MAX_PER_CANDIDATE = 3
FEED_MIN_ENTRIES = 1
# --- Job queue (integration-plan §4.1) --------------------------------------
# Workers are the concurrency limit: one Chromium at a time (§6.5), which is
# also why the pipeline runs at exactly one replica. Raising this *multiplies*
# the crawler's own per-job concurrency (5 pages, 3 LLM calls) rather than
# adding to it, and the model is `*-cloud`, so check the Ollama account's rate
# limit — not local VRAM — before going above 1.
PIPELINE_WORKERS = 1
# The queue is a *buffer*, not a backlog. The durable record of pending work is
# `DiscoveryTarget.NextDueAt` in Postgres (§6.7); a deep in-process queue would
# be a second, non-durable answer to "what still needs running", and it is
# reconciling those two after a restart that costs a weekend. Sized to a few
# jobs so the §8 bootstrap stays paced by .NET rather than accepted wholesale
# into memory — which means 429 is rarer, not gone, and .NET still needs it.
JOB_QUEUE_MAX = 10
# Bounds the in-process job registry. Finished entries are kept only to answer
# "already seen this job_id" with a 409.
JOB_REGISTRY_MAX = 1000
# Sent as `Retry-After` on a 429. .NET adds it to `NextDueAt` and leaves the job
# Pending — a 429 is backpressure, not a failure.
RETRY_AFTER_SECONDS = 600
# How long shutdown waits for a *running* job before cancelling it and calling
# back a failure. Must stay below the deployment's
# `terminationGracePeriodSeconds`: if k8s SIGKILLs us mid-drain then no callback
# is sent at all, which is strictly worse than not trying — .NET waits out the
# §4.3 sweep instead of re-dispatching immediately. The k8s default is 30s,
# hence 25 here. Raise both in lockstep (~600) to let an in-flight crawl survive
# a rolling update; queued jobs are handed back immediately either way.
SHUTDOWN_GRACE_SECONDS = 25.0

# --- Result callback (integration-plan §4.2) --------------------------------
# The payload cost minutes of crawling and LLM calls, so it is worth retrying
# hard for. Safe to repeat: .NET keys on `job_id` and returns 200 for a job it
# has already completed.
CALLBACK_MAX_ATTEMPTS = 5
CALLBACK_BACKOFF_BASE = 2.0
CALLBACK_TIMEOUT = 30.0
# Bearer token for the callback. Read from the environment, never committed —
# the endpoint it authenticates writes rows into the sources table.
CALLBACK_TOKEN_ENV = "PIPELINE_CALLBACK_TOKEN"

# --- Feed revalidation endpoint (integration-plan §4.4) ---------------------
# The batch cap bounds one request's fan-out; the concurrency cap bounds how
# much of it is in flight at once. Without the second, a 500-feed batch opens
# 500 sockets against 500 hosts simultaneously.
REVALIDATE_MAX_BATCH = 500
REVALIDATE_MAX_CONCURRENT = 10

# Entry ages used by the scorer to grade feed freshness (days).
FEED_FRESH_DAYS = 2
FEED_RECENT_DAYS = 7
FEED_STALE_DAYS = 30

SCORING_WEIGHTS = {
    "tld_match": 15,
    "language_match": 15,
    "search_occurrences": 15,
    "rss_available": 10,
    "article_paths": 15,
    "sections": 5,
    "has_dates": 5,
    "has_authors": 5,
    "classification_confidence": 10,
    "location_match": 5,
}

POLLING_TIERS = [
    (80, "high",   "poll every 15 min"),
    (50, "medium", "poll every 1 hour"),
    (20, "low",    "poll every 6 hours"),
    (0,  "backup", "no scheduled polling"),
]

COUNTRY_TLDS: dict[str, list[str]] = {
    "MK": [".mk"],
    "RS": [".rs"],
    "HR": [".hr"],
    "BA": [".ba"],
    "SI": [".si"],
    "BG": [".bg"],
    "AL": [".al"],
    "XK": [".xk"],
    "GR": [".gr"],
    "TR": [".tr"],
    "DE": [".de"],
    "FR": [".fr"],
    "ES": [".es"],
    "IT": [".it"],
    "PT": [".pt"],
    "NL": [".nl"],
    "PL": [".pl"],
    "RO": [".ro"],
    "CZ": [".cz"],
    "HU": [".hu"],
    "SE": [".se"],
    "NO": [".no"],
    "DK": [".dk"],
    "FI": [".fi"],
    "UA": [".ua"],
    "RU": [".ru"],
    "JP": [".jp"],
    "KR": [".kr"],
    "CN": [".cn"],
    "IN": [".in"],
    "BR": [".br"],
    "MX": [".mx"],
    "AR": [".ar"],
    "EG": [".eg"],
    "SA": [".sa"],
    "IR": [".ir"],
    "IL": [".il"],
    # Was two rows, "England" and "United Kingdom", both meaning GB.
    "GB": [".uk", ".co.uk"],
    "US": [".us"],
}

# COUNTRY_ISO2 used to live here, mapping full name -> "MK". It existed only
# to translate the pipeline's name-driven input into the codes that schema.org
# `addressCountry` / `geo.country` actually carry. The ISO code now arrives on
# the request, so the translation has already happened upstream.

COUNTRY_LANGUAGES: dict[str, list[str]] = {
    "MK": ["mk", "sq"],
    "RS": ["sr"],
    "HR": ["hr"],
    "BA": ["bs", "hr", "sr"],
    "SI": ["sl"],
    "BG": ["bg"],
    "AL": ["sq"],
    "XK": ["sq", "sr"],
    "GR": ["el"],
    "TR": ["tr"],
    "DE": ["de"],
    "FR": ["fr"],
    "ES": ["es"],
    "IT": ["it"],
    "PT": ["pt"],
    "NL": ["nl"],
    "PL": ["pl"],
    "RO": ["ro"],
    "CZ": ["cs"],
    "HU": ["hu"],
    "SE": ["sv"],
    "NO": ["no", "nb", "nn"],
    "DK": ["da"],
    "FI": ["fi"],
    "UA": ["uk"],
    "RU": ["ru"],
    "JP": ["ja"],
    "KR": ["ko"],
    "CN": ["zh"],
    "IN": ["hi", "en"],
    "BR": ["pt"],
    "MX": ["es"],
    "AR": ["es"],
    "EG": ["ar"],
    "SA": ["ar"],
    "IR": ["fa"],
    "IL": ["he"],
    "GB": ["en"],
    "US": ["en"],
}
