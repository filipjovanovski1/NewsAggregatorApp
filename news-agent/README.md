# News Agent

`news-agent` is the HTTP chatbot service for NewsAggregatorApp. It uses LangGraph and
Ollama, but obtains all news facts through an allowlisted set of backend tools. It never
connects directly to PostgreSQL and does not call Newsdata.

## Runtime flow

```text
frontend /agent/v1/chat -> news-agent -> NewsAggregatorApp API -> PostgreSQL
                                  |
                                  +-> Ollama
```

The service keeps a bounded, expiring in-memory conversation history. Every factual answer
is grounded in RSS articles returned by the backend, and citation metadata is reconstructed
from trusted backend results rather than model-provided URLs.

## Endpoints

```text
GET    /health
POST   /v1/chat
DELETE /v1/conversations/{conversationId}
```

## Local development

```bash
python -m venv .venv
source .venv/bin/activate
pip install -r requirements.txt
NEWS_BACKEND_URL=http://localhost:8082 \
NEWS_BACKEND_TOKEN=local-news-agent-dev-token \
OLLAMA_HOST=http://localhost:11434 \
python main.py
```

The API is available at <http://localhost:8090>, with OpenAPI at `/docs`.
The CLI remains available with `python main.py "What is the latest news in North Macedonia?"`.

Run tests with `python -m unittest discover -s tests -v`.
