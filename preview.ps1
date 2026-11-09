<#
.SYNOPSIS
  로컬 미리보기를 한 번에 엽니다. 편집 서버(edit-server.py)를 띄우고 포트폴리오 문서·발표 덱·상세 문서를 브라우저로 엽니다.

.EXAMPLE
  .\preview.ps1                 # 서버 켜고 세 페이지 모두 열기 (첫 프로젝트)
  .\preview.ps1 doc             # 상세 문서만
  .\preview.ps1 deck 3-mini-ftl # 특정 프로젝트의 발표 덱만
  .\preview.ps1 report          # 요약판 report.md와 프로젝트 report 열기
  .\preview.ps1 server          # 서버만 켜기(브라우저 안 엶)
  .\preview.ps1 stop            # 4600 포트의 미리보기 서버 끄기
#>
param(
    [ValidateSet('all', 'portfolio', 'deck', 'doc', 'report', 'server', 'stop')]
    [string]$Page = 'all',
    [string]$Project = ''
)

$ErrorActionPreference = 'Stop'
$Root = $PSScriptRoot
$Port = 4600
$BaseUrl = "http://127.0.0.1:$Port"

# ── 프로젝트 폴더: 지정하지 않으면 src/ 아래 첫 폴더('_'로 시작하는 폴더 제외) ──
if ($Project -eq '') {
    $first = Get-ChildItem -Path (Join-Path $Root 'src') -Directory |
        Where-Object { $_.Name -notlike '_*' } |
        Sort-Object Name |
        Select-Object -First 1
    $Project = $first.Name
}

function Test-Url([string]$url) {
    try {
        $response = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 2
        return $response.StatusCode -eq 200
    } catch {
        return $false
    }
}

function Get-PortProcessId {
    $connection = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -eq $connection) { return $null }
    return $connection.OwningProcess
}

function Stop-PreviewServer {
    $listenerIds = @(Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue |
        Select-Object -ExpandProperty OwningProcess -Unique)
    if ($listenerIds.Count -eq 0) {
        Write-Host "포트 $Port 에 켜진 서버가 없습니다."
        return
    }

    # Microsoft Store 판 python은 실행 창(부모 python.exe)이 진짜 서버(자식)를 띄우므로 부모도 함께 끕니다.
    foreach ($listenerId in $listenerIds) {
        $process = Get-CimInstance Win32_Process -Filter "ProcessId=$listenerId"
        $parent = Get-CimInstance Win32_Process -Filter "ProcessId=$($process.ParentProcessId)"
        Stop-Process -Id $listenerId -Force -Confirm:$false
        Write-Host "포트 $Port 서버(프로세스 $listenerId)를 껐습니다."
        $isPythonParent = ($null -ne $parent) -and ($parent.Name -eq 'python.exe')
        if ($isPythonParent) {
            Stop-Process -Id $parent.ProcessId -Force -Confirm:$false -ErrorAction SilentlyContinue
        }
    }
}

function Start-EditServer {
    $isEditServerRunning = Test-Url "$BaseUrl/__edit/ping"
    if ($isEditServerRunning) {
        Write-Host "편집 서버가 이미 켜져 있습니다: $BaseUrl"
        return
    }

    $isOtherServerRunning = $null -ne (Get-PortProcessId)
    if ($isOtherServerRunning) {
        Write-Warning "포트 $Port 를 다른 서버(예: python -m http.server)가 쓰고 있어 상세 문서 편집 모드가 꺼집니다. '.\preview.ps1 stop' 뒤 다시 실행하세요."
        return
    }

    $python = Get-Command python -ErrorAction SilentlyContinue
    if ($null -eq $python) { $python = Get-Command py -ErrorAction SilentlyContinue }
    if ($null -eq $python) { throw "python(또는 py)을 찾을 수 없습니다. Python 3를 설치하고 PATH에 넣어 주세요." }

    Start-Process -FilePath $python.Source -ArgumentList @('edit-server.py', "$Port") -WorkingDirectory $Root -WindowStyle Minimized
    Write-Host "편집 서버를 켜는 중... (작업 표시줄의 최소화된 창을 닫으면 꺼집니다)"
    for ($i = 0; $i -lt 20; $i++) {
        Start-Sleep -Milliseconds 300
        if (Test-Url "$BaseUrl/__edit/ping") {
            Write-Host "편집 서버 켜짐: $BaseUrl"
            return
        }
    }
    throw "편집 서버가 6초 안에 켜지지 않았습니다. '$($python.Source) edit-server.py' 를 직접 실행해 오류를 확인하세요."
}

function Open-Reports {
    $summary = Join-Path $Root 'report.md'
    $projectReport = Join-Path $Root "report\$Project.md"
    foreach ($path in @($summary, $projectReport)) {
        if (Test-Path $path) { Start-Process $path } else { Write-Warning "없음: $path" }
    }
}

# ── 실행 ──
if ($Page -eq 'stop') { Stop-PreviewServer; return }
if ($Page -eq 'report') { Open-Reports; return }

Start-EditServer
if ($Page -eq 'server') { return }

$pageUrls = [ordered]@{
    portfolio = "$BaseUrl/"
    deck      = "$BaseUrl/project.html?p=$Project"
    doc       = "$BaseUrl/doc.html?p=$Project"
}
foreach ($name in $pageUrls.Keys) {
    $isSelected = ($Page -eq 'all') -or ($Page -eq $name)
    if ($isSelected) {
        Start-Process $pageUrls[$name]
        Write-Host "열기: $($pageUrls[$name])"
    }
}
Write-Host "프로젝트: $Project · 상세 문서 오른쪽 아래 [✎ 편집 모드]로 md를 바로 고칠 수 있습니다."
