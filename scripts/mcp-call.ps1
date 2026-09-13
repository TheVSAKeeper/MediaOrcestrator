[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [string]$Tool,

    [Parameter(Position = 1)]
    [string]$Arguments = '{}',

    [switch]$List,

    [string]$SettingsPath,

    [int]$TimeoutSec = 120
)

$ErrorActionPreference = 'Stop'

function Read-McpSettings([string]$path) {
    $port = 7655
    $token = ''
    $section = ''

    foreach ($raw in Get-Content -LiteralPath $path -Encoding UTF8) {
        $line = $raw.Trim()

        if ($line -match '^\[(.+)\]$') {
            $section = $Matches[1].Trim()
            continue
        }

        if ($line -match '^([A-Za-z0-9_.]+)\s*=\s*(.+)$') {
            $key = if ($section) { "$section.$($Matches[1])" } else { $Matches[1] }
            $value = $Matches[2].Trim().Trim('"')

            switch ($key) {
                'wpf.mcp.port' { if (-not [int]::TryParse($value, [ref]$port)) { $port = 7655 } }
                'wpf.mcp.token' { $token = $value }
            }
        }
    }

    return [pscustomobject]@{ Port = $port; Token = $token }
}

function Get-JsonBody([string]$raw) {
    if ([string]::IsNullOrWhiteSpace($raw)) {
        return $null
    }

    $data = $raw -split "`n" | Where-Object { $_ -like 'data:*' } | ForEach-Object { $_.Substring(5).Trim() }

    if ($data) {
        return ($data | Select-Object -Last 1) | ConvertFrom-Json -Depth 64
    }

    return $raw | ConvertFrom-Json -Depth 64
}

if (-not $SettingsPath) {
    $root = Split-Path -Parent $PSScriptRoot
    $SettingsPath = Join-Path $root 'MediaOrcestrator.Runner.Wpf\bin\Debug\net10.0-windows10.0.19041.0\settings.toml'
}

if (-not (Test-Path -LiteralPath $SettingsPath)) {
    throw "Не найден settings.toml: $SettingsPath. Запусти приложение хотя бы раз или укажи -SettingsPath."
}

$settings = Read-McpSettings $SettingsPath

if (-not $settings.Token) {
    throw "В $SettingsPath нет wpf.mcp.token: включи MCP-сервер в настройках приложения."
}

$url = "http://127.0.0.1:$($settings.Port)/mcp"
$headers = @{
    'Accept'        = 'application/json, text/event-stream'
    'Authorization' = "Bearer $($settings.Token)"
}

$id = 0

function Send-Rpc([string]$method, $params, [switch]$Notification) {
    $body = [ordered]@{ jsonrpc = '2.0'; method = $method }

    if (-not $Notification) {
        $script:id++
        $body.id = $script:id
    }

    if ($null -ne $params) {
        $body.params = $params
    }

    $json = $body | ConvertTo-Json -Depth 64 -Compress
    $response = Invoke-WebRequest -Uri $url -Method Post -Headers $headers -ContentType 'application/json' -Body ([System.Text.Encoding]::UTF8.GetBytes($json)) -TimeoutSec $TimeoutSec -SkipHttpErrorCheck

    if ($response.StatusCode -eq 401) {
        throw 'Сервер отверг токен (401): токен в settings.toml не совпадает с тем, что у запущенного экземпляра.'
    }

    if ($response.StatusCode -ge 400) {
        throw "HTTP $($response.StatusCode) от $url`n$($response.Content)"
    }

    $sessionId = $response.Headers['Mcp-Session-Id']

    if ($sessionId) {
        $headers['Mcp-Session-Id'] = "$sessionId"
    }

    if ($Notification) {
        return $null
    }

    $parsed = Get-JsonBody ([string]$response.Content)

    if ($parsed.error) {
        throw "MCP: $($parsed.error.message)"
    }

    return $parsed.result
}

try {
    $null = Send-Rpc 'initialize' ([ordered]@{
        protocolVersion = '2025-03-26'
        capabilities    = @{}
        clientInfo      = @{ name = 'mcp-call.ps1'; version = '1.0' }
    })
}
catch [System.Net.Http.HttpRequestException] {
    throw "Приложение не отвечает на $url. Запущен ли MediaOrcestrator.Runner.Wpf с включённым MCP-сервером?"
}

Send-Rpc 'notifications/initialized' $null -Notification

if ($List -or -not $Tool) {
    $tools = Send-Rpc 'tools/list' @{}
    $tools.tools | ForEach-Object { "{0,-18} {1}" -f $_.name, $_.description }
    return
}

$callArguments = $Arguments | ConvertFrom-Json -Depth 64 -AsHashtable
$result = Send-Rpc 'tools/call' ([ordered]@{ name = $Tool; arguments = $callArguments })

if ($result.isError) {
    $text = ($result.content | Where-Object type -eq 'text' | Select-Object -First 1).text
    throw "Инструмент $Tool отказал: $text"
}

if ($result.structuredContent) {
    $result.structuredContent | ConvertTo-Json -Depth 64
    return
}

foreach ($item in $result.content) {
    if ($item.type -eq 'text') {
        try {
            $item.text | ConvertFrom-Json -Depth 64 | ConvertTo-Json -Depth 64
        }
        catch {
            $item.text
        }
    }
    else {
        $item | ConvertTo-Json -Depth 8
    }
}
