function New-OllamaArticleBody {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Title,

        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string]$Description,

        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string]$Publisher,

        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$Language,

        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$Model,

        [Parameter(Mandatory = $true)]
        [ValidateRange(1, [int]::MaxValue)]
        [int]$ContextLength,

        [Parameter(Mandatory = $true)]
        [ValidateRange(1, [int]::MaxValue)]
        [int]$OutputTokenLimit,

        [Parameter(Mandatory = $true)]
        [ValidateRange(0, [double]::MaxValue)]
        [double]$Temperature,

        [Parameter(Mandatory = $true)]
        [int]$Seed,

        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$Think,

        [AllowNull()]
        [AllowEmptyString()]
        [string]$RepairSummary
    )

    $languageTag = $Language.Trim().Replace("_", "-")
    if ($languageTag -notmatch "^[A-Za-z]{2,3}(?:-[A-Za-z]{4})?(?:-(?:[A-Za-z]{2}|[0-9]{3}))?$") {
        throw "Language must be a valid BCP-47 language tag."
    }

    try {
        $culture = [System.Globalization.CultureInfo]::GetCultureInfo($languageTag)
        $languageTag = $culture.Name
        $languageName = $culture.EnglishName
    }
    catch [System.Globalization.CultureNotFoundException] {
        throw "Unsupported language tag '$languageTag'."
    }

    $cleanTitle = [System.Net.WebUtility]::HtmlDecode($Title)
    $cleanDescription = [System.Net.WebUtility]::HtmlDecode($Description)
    $cleanTitle = [regex]::Replace($cleanTitle, "<[^>]+>", " ")
    $cleanDescription = [regex]::Replace($cleanDescription, "<[^>]+>", " ")
    $cleanDescription = [regex]::Replace(
        $cleanDescription,
        "(?is)\bThe post\b.*?\bappeared first on\b.*$",
        " "
    )
    $cleanTitle = [regex]::Replace($cleanTitle, "\s+", " ").Trim()
    $cleanDescription = [regex]::Replace($cleanDescription, "\s+", " ").Trim()

    if (-not [string]::IsNullOrWhiteSpace($cleanTitle)) {
        $escapedTitle = [regex]::Escape($cleanTitle)
        $cleanDescription = [regex]::Replace(
            $cleanDescription,
            "(?i)^$escapedTitle\s*[-:|\u2013\u2014]*\s*",
            ""
        ).Trim()
    }

    $messages = @(
        @{
            role = "system"
            content = @"
Translate the article title and write its summary in $languageName (language tag: $languageTag).

Return one valid JSON object with exactly these two string properties:
{"title":"translated title","summary":"one complete summary sentence"}

The title must be a faithful, natural translation of the supplied title. Preserve names, numbers, quotations and technical terms. Do not summarize the title.
The summary must be one neutral and factual sentence, target 180 to 260 characters, and never exceed 350 characters.
Use only facts from the title and description.
Merge duplicated facts instead of repeating them.
Preserve every person, organization, number, substance, technical term and every item in an explicit list.
When shortening, remove background wording before removing those facts.
Do not infer, generalize or add context.
Do not begin with an introductory phrase such as "The article says".
Both values must use $languageName, except for proper names or terms that should remain unchanged.
Return JSON only, with no Markdown or commentary.
"@
        },
        @{
            role = "user"
            content = @"
Title: $cleanTitle

Description: $cleanDescription
"@
        }
    )

    if ($PSBoundParameters.ContainsKey("RepairSummary")) {
        if (-not [string]::IsNullOrWhiteSpace($RepairSummary)) {
            $messages += @{
                role = "assistant"
                content = $RepairSummary.Trim()
            }
        }
        $messages += @{
            role = "user"
            content = @"
The previous response did not satisfy the required JSON contract.
Return a replacement JSON object with exactly the string properties "title" and "summary".
Translate the title faithfully and write exactly one complete summary sentence under 300 characters.
Use the language required by the system message for both values.
Preserve all names, numbers, substances, technical terms and explicit list items.
Return JSON only, with no Markdown or commentary.
"@
        }
    }

    if ($Think -ieq "true") {
        $thinkValue = $true
    }
    elseif ($Think -ieq "false") {
        $thinkValue = $false
    }
    else {
        $thinkValue = $Think
    }

    $requestObject = @{
        model = $Model
        messages = $messages
        format = "json"
        stream = $false
        think = $thinkValue
        keep_alive = -1
        options = @{
            temperature = $Temperature
            seed = $Seed
            num_ctx = $ContextLength
            num_predict = $OutputTokenLimit
        }
    }

    $json = $requestObject | ConvertTo-Json -Depth 10
    [byte[]]$bytes = [System.Text.Encoding]::UTF8.GetBytes($json)
    $null = $Publisher
    return ,$bytes
}
