param(
    [Parameter(Mandatory = $true)]
    [string]$BodyBuilderPath
)

$ErrorActionPreference = "Stop"
[Console]::InputEncoding = [System.Text.UTF8Encoding]::new($false)
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
. $BodyBuilderPath

while (($line = [Console]::In.ReadLine()) -ne $null) {
    try {
        $request = $line | ConvertFrom-Json
        $parameters = @{
            Title = [string]$request.Title
            Description = [string]$request.Description
            Publisher = [string]$request.Publisher
            Language = [string]$request.Language
            Model = [string]$request.Model
            ContextLength = [int]$request.ContextLength
            OutputTokenLimit = [int]$request.OutputTokenLimit
            Temperature = [double]$request.Temperature
            Seed = [int]$request.Seed
            Think = [string]$request.Think
        }
        if ($null -ne $request.RepairSummary) {
            $parameters.RepairSummary = [string]$request.RepairSummary
        }

        [byte[]]$bodyBytes = New-OllamaArticleBody @parameters
        $response = @{
            success = $true
            bodyBase64 = [Convert]::ToBase64String($bodyBytes)
            error = $null
        }
    }
    catch {
        $response = @{
            success = $false
            bodyBase64 = $null
            error = $_.Exception.Message
        }
    }

    [Console]::Out.WriteLine(($response | ConvertTo-Json -Compress -Depth 5))
}
