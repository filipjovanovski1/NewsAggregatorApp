# Running the local Ollama translator

## What the application now does

The React client sends the desired BCP-47 language tag selected in the main
header with each article search. ASP.NET queues missing translations and
processes exactly one Ollama request at a time. The client polls once per second,
waits until the first two articles have both a translated title and summary,
then reveals them in the one-row carousel.

The backend keeps one PowerShell process open, dot-sources the request-builder
once, and sends the generated UTF-8 JSON bytes to the model configured in
`NewsApplication.Web/appsettings.json` through Ollama.
The model must return a JSON object containing both `title` and `summary` in the
requested language. Invalid output is repaired once. Pending or failed items are
not shown as untranslated cards, so an original-language item cannot leak into
the translated carousel.

The summary cache is currently in memory. It is keyed by article content,
language, model, and prompt version, and resets when the backend restarts.

## One-time setup

1. Install Ollama and pull the model named by `AiSummarization:Model` in
   `NewsApplication.Web/appsettings.json`:

   ```powershell
   ollama pull qwen3.5:9b
   ```

2. Configure the supported single request slot:

   ```powershell
   [Environment]::SetEnvironmentVariable("OLLAMA_NUM_PARALLEL", "1", "User")
   [Environment]::SetEnvironmentVariable("OLLAMA_CONTEXT_LENGTH", "2048", "User")
   ```

3. Fully quit and restart Ollama so it inherits those variables.

4. Confirm the API and model:

   ```powershell
   Invoke-RestMethod http://localhost:11434/api/tags
   ollama list
   ```

5. Ensure the backend has a PostgreSQL connection string and Newsdata API key.
   Keep these local values in ASP.NET user secrets; the tracked
   `launchSettings.json` intentionally contains no credentials:

   ```powershell
   cd C:\Users\user\source\repos\NewsAggregatorApp\NewsApplication.Web
   dotnet user-secrets set "ConnectionStrings:Default" "YOUR_CONNECTION_STRING"
   dotnet user-secrets set "Newsdata:ApiKey" "YOUR_NEWSDATA_KEY"
   dotnet user-secrets set "Dev:AdminToken" "YOUR_DEV_TOKEN"
   ```

## Change or tune the model

The `AiSummarization` section in `NewsApplication.Web/appsettings.json` is the
single source of truth for the Ollama model used by the application:

```json
"AiSummarization": {
  "Model": "qwen3.5:9b",
  "ContextLength": 2048,
  "OutputTokenLimit": 256,
  "Temperature": 0,
  "Seed": 42,
  "Think": false
}
```

To switch models, pull the new Ollama model, change these values, and restart the
backend. The same configuration is used for preload, the summary cache key, and
every `/api/chat` request. `Think` may be `true`, `false`, or a supported thinking
level such as `"low"`, `"medium"`, or `"high"`. `New-OllamaArticleBody.ps1` still
owns the prompt and fixed JSON response contract, but it no longer selects or
tunes the model. `Initialize-OllamaModel.ps1` is an optional manual helper and is
not invoked by the application.

## Start the application

Open three terminals.

### Terminal 1: Ollama

Start Ollama from the Windows Start menu. If you intentionally run it without
the tray application, use:

```powershell
ollama serve
```

Do not run both servers at once because both use port `11434`.

### Terminal 2: ASP.NET backend

Stop any older Debug session first with Visual Studio **Shift+F5** or `Ctrl+C`
in its terminal, then run:

```powershell
cd C:\Users\user\source\repos\NewsAggregatorApp\NewsApplication.Web
dotnet run --launch-profile https
```

The backend listens at `https://localhost:7146`. On startup it attempts to
preload the configured model; a successful log contains `Preloaded Ollama model`.

### Terminal 3: React frontend

```powershell
cd C:\Users\user\source\repos\NewsAggregatorApp\NewsApplication.Web\ClientApp
npm install
npm run dev
```

Open `http://localhost:5173`.

## Test the feature

1. Search for a country, city, or topic and open the article overlay.
2. Confirm cards appear immediately with their provider descriptions.
3. Select the desired language in the main header before searching.
4. Confirm the overlay waits for two completed translations and initially shows
   those two cards with translated titles and summaries.
5. Use **More news**, the right arrow, or a left swipe and confirm the row moves
   as a carousel. Six cards should never produce more than one active
   `/api/chat` call.
6. Switch languages and confirm a new search/translation cycle starts for the
   selected language.
7. Run `ollama ps` and confirm the model shows context `2048`, indefinite
   keep-alive, and `100% GPU` on the tested machine.

## Open it from a phone

The Vite server is configured to listen on the local network. Start the backend
and frontend as described above, connect the phone and PC to the same Wi-Fi,
and find the PC's active IPv4 address:

```powershell
Get-NetIPConfiguration |
    Where-Object IPv4DefaultGateway |
    Select-Object -ExpandProperty IPv4Address |
    Select-Object -ExpandProperty IPAddress
```

Open `http://YOUR_PC_IP:5173` on the phone. If Windows Defender Firewall asks,
allow Node.js on **Private networks**. Do not expose port `5173` to the public
internet; this is a development-only LAN link.

## Verification commands

```powershell
cd C:\Users\user\source\repos\NewsAggregatorApp
dotnet build NewsApp.sln -c Release --no-restore
dotnet test NewsApplication.Tests\NewsApplication.Tests.csproj `
    -c Release `
    --no-build `
    --filter "FullyQualifiedName~Summarization"

cd NewsApplication.Web\ClientApp
npm run build
```

## Troubleshooting

- **Cards never leave pending:** verify
  `Invoke-RestMethod http://localhost:11434/api/tags`, then inspect the backend
  log for Ollama or PowerShell bridge warnings.
- **Ollama was started after a failed search:** restart the backend; failed
  entries are intentionally cached for the current process.
- **The backend executable is locked during build:** stop the old Debug session
  or build with `-c Release`.
- **Only one translated card appears:** the second item may still be pending or
  another item may have failed validation. Inspect the backend warning log; the
  carousel reveals as soon as two valid translations are ready.
- **The phone cannot connect:** verify the frontend terminal reports a Network
  URL, both devices are on the same Wi-Fi, and the firewall permits Node.js on
  Private networks.
- **PowerShell execution policy errors in a manual terminal:** the application
  bridge already uses `-ExecutionPolicy Bypass` for its repository-owned script;
  no machine-wide policy change is required.
