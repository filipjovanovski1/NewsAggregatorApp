function Initialize-OllamaModel {
    [CmdletBinding()]
    param(
        [ValidateRange(512, 2048)]
        [int]$ContextLength = 2048,
        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$Model,
        [ValidateNotNullOrEmpty()]
        [string]$BaseUri = "http://localhost:11434"
    )

    $preloadObject = @{
        model = $Model
        prompt = ""
        keep_alive = -1
        stream = $false
        options = @{ num_ctx = $ContextLength }
    }
    $preloadJson = $preloadObject | ConvertTo-Json -Depth 10
    [byte[]]$preloadBytes = [System.Text.Encoding]::UTF8.GetBytes($preloadJson)

    Invoke-RestMethod `
        -Method Post `
        -Uri "$($BaseUri.TrimEnd('/'))/api/generate" `
        -ContentType "application/json; charset=utf-8" `
        -Body $preloadBytes `
        -TimeoutSec 300 `
        -ErrorAction Stop | Out-Null

    Write-Host "Model '$Model' is loaded with a $ContextLength-token context."
}
