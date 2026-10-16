#requires -version 5.1
<#
.SYNOPSIS
Serves docs\ on http://localhost:<Port>/ so the preview page can be opened locally.
.DESCRIPTION
A minimal read-only static file server (GET/HEAD only, files under docs\ only),
bound to localhost. Stop it with Ctrl+C.
.EXAMPLE
powershell -ExecutionPolicy Bypass -File tools\Serve-Docs.ps1 -Port 8765
#>
[CmdletBinding()]
param([int]$Port = 8765)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path (Split-Path $PSScriptRoot -Parent) 'docs'))
$types = @{
    '.html' = 'text/html; charset=utf-8'; '.js' = 'text/javascript; charset=utf-8'; '.css' = 'text/css; charset=utf-8'
    '.png' = 'image/png'; '.gif' = 'image/gif'; '.svg' = 'image/svg+xml'; '.json' = 'application/json'; '.ico' = 'image/x-icon'
}
$listener = [Net.HttpListener]::new()
$listener.Prefixes.Add("http://localhost:$Port/")
$listener.Start()
Write-Host "Serving $root at http://localhost:$Port/ (Ctrl+C to stop)"
try {
    while ($listener.IsListening) {
        $context = $listener.GetContext()
        $response = $context.Response
        try {
            $request = $context.Request
            $relative = [Uri]::UnescapeDataString($request.Url.AbsolutePath).TrimStart('/')
            if ($relative -eq '' -or $relative.EndsWith('/')) { $relative += 'index.html' }
            $path = [IO.Path]::GetFullPath((Join-Path $root $relative))
            if ($request.HttpMethod -notin @('GET', 'HEAD')) { $response.StatusCode = 405 }
            elseif (-not $path.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or -not [IO.File]::Exists($path)) {
                $response.StatusCode = 404
            }
            else {
                $bytes = [IO.File]::ReadAllBytes($path)
                $type = $types[[IO.Path]::GetExtension($path).ToLowerInvariant()]
                $response.ContentType = $(if ($type) { $type } else { 'application/octet-stream' })
                $response.Headers['Cache-Control'] = 'no-cache'
                $response.ContentLength64 = $bytes.Length
                if ($request.HttpMethod -eq 'GET') { $response.OutputStream.Write($bytes, 0, $bytes.Length) }
            }
            Write-Host ('{0} {1} {2}' -f $response.StatusCode, $request.HttpMethod, $request.Url.AbsolutePath)
        }
        catch { Write-Warning $_.Exception.Message; try { $response.StatusCode = 500 } catch { } }
        finally { $response.Close() }
    }
}
finally { $listener.Stop(); $listener.Close() }
