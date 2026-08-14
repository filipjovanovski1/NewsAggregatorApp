from langchain_core.tools import tool


@tool
def resolve_scope(q: str, city: str = "", country: str = "") -> dict:
    """Resolves a user search query into a geographic scope. Takes the search query, optional city, and optional country ISO code. Returns a scopeKey used to search articles."""
    return {
        "scopeKey": "city:skopje-mk|country:MK|local:skopje|q:" + q,
        "kind": "city",
        "label": "Skopje, North Macedonia",
        "countryIso2": "MK",
        "countryIso3": "MKD",
        "cityId": "skopje-mk",
        "focusLat": 42.0,
        "focusLng": 21.4,
    }


@tool
def search_articles(scope_key: str, ui_page: int = 1) -> dict:
    """Searches for news articles within a resolved scope. Takes a scopeKey from resolve_scope and a page number. Returns paginated articles."""
    return {
        "scopeKey": scope_key,
        "uiPage": ui_page,
        "pageSize": 6,
        "hasNewer": False,
        "hasOlder": True,
        "totalDistinct": 20,
        "nextUiPage": ui_page + 1,
        "prefetch": {"providerPage": 2, "providerPageSize": 10},
        "items": [
            {
                "articleId": "a1b2c3d4",
                "provider": "NEWSDATA",
                "title": "Komercijalna Banka Reports Record Q1 Profits",
                "description": "Komercijalna Banka AD Skopje announced record-breaking profits for the first quarter of 2026, driven by strong lending growth.",
                "imageUrl": "https://placeholder.example/img1.jpg",
                "publisher": "Kapital.mk",
                "url": "https://placeholder.example/article1",
                "publishedTime": "2026-03-15T10:30:00Z",
                "categories": ["finance"],
            },
            {
                "articleId": "e5f6g7h8",
                "provider": "NEWSDATA",
                "title": "North Macedonia GDP Growth Exceeds Expectations",
                "description": "The country's economy grew by 3.2% in the first half of 2026, outpacing analyst forecasts.",
                "imageUrl": "https://placeholder.example/img2.jpg",
                "publisher": "MIA",
                "url": "https://placeholder.example/article2",
                "publishedTime": "2026-04-20T14:00:00Z",
                "categories": ["finance", "economy"],
            },
            {
                "articleId": "i9j0k1l2",
                "provider": "NEWSDATA",
                "title": "Skopje Stock Exchange Sees Surge in Trading Volume",
                "description": "Trading volumes on the Macedonian Stock Exchange hit a five-year high amid growing investor interest.",
                "imageUrl": "https://placeholder.example/img3.jpg",
                "publisher": "Sloboden Pecat",
                "url": "https://placeholder.example/article3",
                "publishedTime": "2026-05-10T09:15:00Z",
                "categories": ["finance"],
            },
        ],
    }


@tool
def search_preview(q: str, city: str = "", country: str = "") -> dict:
    """Returns a geographic preview for a search query. Shows whether the query resolves to a city, country, or is ambiguous, along with candidate matches. Does NOT return articles."""
    return {
        "kind": "city",
        "isAmbiguous": False,
        "canSearch": True,
        "nonGeoKeywords": ["finance"],
        "countryMatches": [
            {"iso2": "MK", "iso3": "MKD", "name": "North Macedonia"},
        ],
        "cityMatches": [
            {
                "cityId": "skopje-mk",
                "name": "Skopje",
                "countryIso2": "MK",
                "lat": 42.0,
                "lng": 21.4,
            },
        ],
        "targets": [
            {"scopeKey": "city:skopje-mk|country:MK", "label": "Skopje, North Macedonia"},
        ],
    }