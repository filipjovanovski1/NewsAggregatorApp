# Local sequential Ollama implementation plan

## Agreed runtime configuration

- Model: `qwen3.5:9b` (`Q4_K_M`)
- Ollama request slots: `1`
- Application workers calling Ollama: `1`
- Context: `2048`
- Output ceiling: `256` tokens
- Thinking: disabled
- Temperature: `0`
- Keep-alive: indefinite
- Prompt version: `article-translation-v4`
- Model output: JSON with a translated `title` and one-sentence `summary`
- UI behavior while pending/failed: wait for two valid translations and omit
  untranslated items

`qwen3.5:9b` currently creates only one request slot in the tested Ollama
configuration, so the application should queue summary work rather than issuing
parallel Ollama calls.

## Language contract

Use a BCP-47 language tag throughout the system. Examples: `mk`, `en`, `de`,
`pt-BR`, and `sr-Latn`.

The same normalized tag must travel through every boundary:

```text
React language selector
  -> articles/search?summaryLanguage=mk
  -> validated SummaryLanguage value object
  -> SummaryJob.Language
  -> New-OllamaArticleBody -Language mk
  -> language-specific system prompt
  -> translated title + summary tagged with ArticleSummary.Language
```

Do not accept an arbitrary prompt fragment as the language. The frontend should
send a code from a supported list, and the backend must independently validate
and normalize it before invoking PowerShell.

## Phase 1: make the scripts repository-owned

1. Create `scripts/ollama` in the repository.
2. Move the tested helpers from the user profile into it:
   - `New-OllamaArticleBody.ps1`
   - `Initialize-OllamaModel.ps1`
   - `Test-OllamaArticleBatch.ps1`
3. Keep `Test-OllamaConcurrency.ps1` only as a diagnostic benchmark. It is not
   part of the application execution path.
4. Add Pester contract tests that decode the byte array returned by
   `New-OllamaArticleBody` and verify:
   - the requested language appears in the system message;
   - HTML tags/entities and the standard RSS footer are removed;
   - publisher is not sent to Ollama;
   - `num_ctx`, `num_predict`, `think`, and `keep_alive` retain the agreed values;
   - invalid language tags fail before an Ollama call.

## Phase 2: add the frontend language choice

1. Add a small language selector to the main header before article search.
2. Represent each option as `{ code, label }`; send only `code` to the backend.
3. Add `summaryLanguage` state in `ClientApp/src/App.tsx` and persist the last
   choice in `localStorage`. Use a deliberate product default such as `mk`; do
   not silently change it from the browser locale after the user chooses.
4. Change these client contracts:
   - `searchArticles(scopeKey, uiPage, summaryLanguage)`
   - `prewarm(scopeKey, providerPage, summaryLanguage)` if prewarming also queues
     summaries.
5. Add translation fields to the article DTO:
   - `translatedTitle?: string`
   - `summary?: string`
   - `summaryLanguage: string`
   - `summaryStatus: 'pending' | 'ready' | 'failed'`
6. Render only ready items containing both fields. Initially reveal two cards,
   then use a responsive one-row carousel with up to three cards on desktop.

## Phase 3: add backend request and persistence contracts

1. Add `summaryLanguage` to `ArticlesController.Search` and validate it against
   one backend-owned allow-list. Return `400` for an unsupported tag.
2. Normalize the tag once, then pass the normalized value to every cache lookup
   and queued job.
3. Add an `ArticleSummary` table/entity rather than overwriting the provider
   description. Recommended columns:

   | Column | Purpose |
   | --- | --- |
   | `ArticleId` | Source article foreign key |
   | `Language` | Normalized BCP-47 tag |
   | `Model` | `qwen3.5:9b` |
   | `PromptVersion` | `article-translation-v4` |
   | `ContentHash` | SHA-256 of cleaned title + description + language + model + prompt version |
   | `TranslatedTitle` | Generated title translation |
   | `Summary` | Generated sentence |
   | `Status` | Pending, Ready, or Failed |
   | `AttemptCount` | One normally, two after a conditional repair |
   | `UpdatedAt` | Cache/diagnostic timestamp |

4. Use a unique key on `(ArticleId, Language, Model, PromptVersion,
   ContentHash)`. Language must be part of the key so switching from `mk` to
   `en` cannot reuse the wrong summary.
5. Extend the search response by left-joining the requested language's ready
   summary. Enqueue missing/stale summaries but return the article page
   immediately.

## Phase 4: add the single-worker summary pipeline

1. Add a bounded `Channel<SummaryJob>` and a hosted `BackgroundService` with one
   reader. Register only one Ollama-consuming worker even if ASP.NET handles many
   HTTP requests concurrently.
2. Deduplicate queued jobs by the full summary cache key.
3. Queue in user-visible priority order:
   - current card;
   - next card;
   - previous card;
   - remaining cards.
4. The worker should use one typed `HttpClient` for Ollama with base address
   `http://localhost:11434` and a two-minute timeout.
5. Preload the model once during application startup. Do not preload per job.
6. Retry exactly once for malformed JSON, a missing translated title/summary,
   `done_reason = length`, more than 350 summary characters, or missing
   sentence-ending punctuation.
7. On the second invalid output or an Ollama/network error, store `Failed` and
   omit that item from the translated carousel.

## Phase 5: invoke the PowerShell body builder efficiently

Because the PowerShell function is required to remain in the application path,
do not launch a new `powershell.exe`/`pwsh.exe` process for every article.

1. Add a singleton PowerShell body-builder adapter in the backend.
2. Create one persistent PowerShell runspace (or one long-lived `pwsh` child
   process) at application startup.
3. Dot-source `scripts/ollama/New-OllamaArticleBody.ps1` once.
4. For each dequeued job, invoke:

   ```powershell
   New-OllamaArticleBody `
       -Title $job.Title `
       -Description $job.Description `
       -Publisher $job.Publisher `
       -Language $job.Language
   ```

5. Require the adapter to return a `byte[]`; post that exact array to
   `/api/chat` using the persistent Ollama `HttpClient`.
6. Serialize access to the runspace alongside the single Ollama worker. Dispose
   it cleanly when the host stops.
7. Make the script path configurable with an absolute local development value
   and a repository-relative production default.

If maintaining a PowerShell host becomes a deployment burden later, port the
body-builder logic to C# behind the same adapter interface and keep the
PowerShell function as the executable prompt-contract test oracle.

## Phase 6: deliver summaries to the UI asynchronously

Implement the simplest transport first:

1. Return initial search results immediately with `summaryStatus`.
2. Poll a focused endpoint such as
   `GET /articles/summaries?articleIds=...&language=mk` while any visible item is
   pending.
3. Stop polling once all visible items are ready/failed or the overlay closes.
4. Replace each description independently as its summary becomes ready.

Server-Sent Events can replace polling later, but are not required for the
first local implementation.

## Phase 7: local configuration and startup

1. Set the Windows user variables:

   ```powershell
   [Environment]::SetEnvironmentVariable("OLLAMA_NUM_PARALLEL", "1", "User")
   [Environment]::SetEnvironmentVariable("OLLAMA_CONTEXT_LENGTH", "2048", "User")
   ```

2. Fully quit and restart Ollama so the process inherits them.
3. Verify `ollama ps` reports `100% GPU`, context `2048`, and indefinite
   keep-alive after preload.
4. Add an `AiSummarization` development configuration section:

   ```json
   {
     "AiSummarization": {
       "Enabled": true,
       "BaseUrl": "http://localhost:11434",
       "Model": "qwen3.5:9b",
       "MaxConcurrency": 1,
       "ContextLength": 2048,
       "OutputTokenLimit": 256,
       "PromptVersion": "article-translation-v4",
       "ScriptPath": "scripts/ollama/New-OllamaArticleBody.ps1"
     }
   }
   ```

5. Keep credentials out of tracked `launchSettings.json`; use ASP.NET user
   secrets or environment variables for the database, provider API key, and dev
   token.
6. Run the backend from `NewsApplication.Web`, then run `npm run dev` from
   `NewsApplication.Web/ClientApp`. The existing Vite proxy targets the local
   HTTPS backend.

## Verification gates

Do not move to the next phase until the current gate passes.

1. **Script gate:** decoded request JSON contains the selected language and the
   agreed Ollama options.
2. **Backend contract gate:** unsupported languages return `400`; supported
   language tags reach the queued job unchanged after normalization.
3. **Queue gate:** six missing summaries create six jobs but never more than one
   active `/api/chat` request.
4. **Cache gate:** the same article/language/content returns the stored summary;
   changing language or prompt version creates a new entry.
5. **UI gate:** the first two translated title/summary pairs appear together,
   desktop uses a one-row carousel, and More news animates horizontally.
6. **Failure gate:** stop Ollama and confirm untranslated cards do not enter the
   carousel and the UI presents a useful unavailable state.
7. **Quality gate:** evaluate 20-30 representative articles per supported
   language for names, numbers, lists, unsupported claims, length, completion,
   and median latency.
