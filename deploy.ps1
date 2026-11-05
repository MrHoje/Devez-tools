# DevezTools 배포 (배포.md 참고)
#   .\deploy.ps1          테스트 → publish → 설치 파일 → version.json 까지 만든다(업로드 없음)
#   .\deploy.ps1 -Upload  위 과정 뒤 R2에 올리고 공개 URL로 다시 받아 검증한다
param([switch]$Upload)

$ErrorActionPreference = 'Stop'
$root      = $PSScriptRoot
$project   = Join-Path $root 'src\DevezTools\DevezTools.csproj'
$publish   = Join-Path $root 'artifacts\publish'
$outDir    = Join-Path $root 'artifacts\installer'
$exe       = Join-Path $publish 'devezTools.exe'
$zip       = Join-Path $outDir 'DevezTools_Setup.zip'
$manifest  = Join-Path $outDir 'version.json'
# 앱 Services\UpdateService.cs 의 Base 와 같아야 한다
$publicBase = 'https://pub-37da98a9e72d4514aed3533375fb7368.r2.dev/deveztools'
$utf8 = New-Object System.Text.UTF8Encoding($false)

function Step($text) { Write-Host "`n$text" -ForegroundColor Cyan }
function Sha256($path) { (Get-FileHash $path -Algorithm SHA256).Hash.ToLower() }

# ── 0. 버전과 업데이트 노트 ──
# 버전은 csproj <Version> 하나만 고친다(설치 파일·version.json 은 여기서 받아 쓴다).
$version = ([xml][IO.File]::ReadAllText($project, $utf8)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw "csproj <Version> 이 x.y.z 형식이 아닙니다: '$version'" }

$noteFile = Join-Path $root '업데이트노트.md'
$noteText = [IO.File]::ReadAllText($noteFile, $utf8)
$section = [regex]::Match($noteText, '(?ms)^## v' + [regex]::Escape($version) + '[ \t]*\r?$(.*?)(?=^## |\z)')
$notes = @($section.Groups[1].Value -split '\r?\n' |
    Where-Object { $_ -match '^\s*-\s+\S' } |
    ForEach-Object { ($_ -replace '^\s*-\s+', '' -replace '\*\*', '').Trim() })
if (-not $section.Success -or $notes.Count -eq 0) { throw "업데이트노트.md 에 '## v$version' 항목(- 로 시작하는 줄)이 없습니다. 노트 없이 배포하지 않습니다." }
Write-Host "DevezTools v$version" -ForegroundColor Green
$notes | ForEach-Object { Write-Host "  - $_" }

# 업로드할 때는 라이브 버전보다 높아야 한다(같은 버전 재배포·되돌리기 실수 방지)
if ($Upload) {
    $live = curl.exe -s -w "`n%{http_code}" "$publicBase/version.json?nocache=$(Get-Random)"
    $code = ($live | Select-Object -Last 1)
    if ($code -eq '200') {
        $liveVersion = (($live | Select-Object -SkipLast 1) -join "`n" | ConvertFrom-Json).version
        if ([version]$version -le [version]$liveVersion) { throw "라이브 버전(v$liveVersion)보다 높은 버전으로 올리십시오(지금 v$version)." }
        Write-Host "  라이브 v$liveVersion → v$version"
    }
    elseif ($code -eq '404') { Write-Host '  첫 배포입니다.' }
    else { throw "라이브 version.json 을 확인하지 못했습니다: HTTP $code" }
}

# publish 산출물을 실행 중이면 덮어쓸 수 없다(설치본 %LocalAppData% 실행과는 경로가 달라 상관없다)
if (Get-Process devezTools -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe }) {
    throw "$exe 가 실행 중입니다. 트레이 메뉴의 종료로 끝낸 뒤 다시 실행하십시오."
}

# ── 1. 테스트 ──
Step '[1/4] dotnet test'
# 빌드 중간 결과를 artifacts\build 에 따로 둔다(개발 중 bin\ 에서 띄운 앱이 파일을 잡고 있어도 막히지 않게)
$build = Join-Path $root 'artifacts\build'
dotnet test (Join-Path $root 'tests\DevezTools.Tests\DevezTools.Tests.csproj') -c Release --artifacts-path $build --nologo
if ($LASTEXITCODE -ne 0) { throw '테스트가 실패해 배포를 멈춥니다.' }

# ── 2. publish (single-file, framework-dependent, win-x64) ──
Step '[2/4] dotnet publish'
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
dotnet publish $project -c Release -r win-x64 --no-self-contained -p:PublishSingleFile=true -p:DebugType=none --artifacts-path $build -o $publish --nologo
if ($LASTEXITCODE -ne 0) { throw 'publish 실패' }
$fileVersion = (Get-Item $exe).VersionInfo.FileVersion
if ($fileVersion -ne "$version.0") { throw "exe 파일 버전($fileVersion)이 csproj($version)와 다릅니다." }

# ── 3. 설치 파일 + zip ──
Step '[3/4] Inno Setup'
$iscc = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 이 없습니다. https://jrsoftware.org/isdl.php 에서 설치하십시오.' }
& $iscc /Q "/DAppVersion=$version" (Join-Path $root 'installer\setup.iss')
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup 컴파일 실패' }
Compress-Archive -Path (Join-Path $outDir "DevezTools_Setup_$version.exe") -DestinationPath $zip -Force

# ── 4. version.json (BOM 없는 UTF-8. 노트 한글은 파일에서 읽은 문자열이라 깨지지 않는다) ──
# sha256 은 설치 zip 의 해시다. 앱은 이 zip 을 받아 맞을 때만 설치한다(exe 옆 OCR 라이브러리·모델까지 바뀌도록).
$sha256 = Sha256 $zip
$json = [ordered]@{ version = $version; sha256 = $sha256; notes = ($notes -join "`n") } | ConvertTo-Json
[IO.File]::WriteAllText($manifest, $json, $utf8)

Write-Host ''
Write-Host "설치 zip: $zip ($([math]::Round((Get-Item $zip).Length / 1MB, 1)) MB)"
Write-Host "sha256  : $sha256"

if (-not $Upload) {
    Write-Host "`n업로드 없이 끝냈습니다. 배포하려면 .\deploy.ps1 -Upload" -ForegroundColor Yellow
    return
}

# ── 5. R2 업로드: 설치 zip → version.json(항상 마지막 = 사용자에게 알리는 신호) ──
Step '[4/4] R2 업로드'
$keyPath = @(
    (Join-Path $root '.local\cloudflare-keys.json'),
    (Join-Path (Split-Path $root) 'Devez-code\.local\cloudflare-keys.json'),
    (Join-Path (Split-Path $root) 'Devez\.local\cloudflare-keys.json')
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $keyPath) { throw 'cloudflare-keys.json 이 없습니다(배포.md 의 키 파일 위치 참고).' }
$cf = [IO.File]::ReadAllText($keyPath, $utf8) | ConvertFrom-Json
$api = "https://api.cloudflare.com/client/v4/accounts/$($cf.account_id)/r2/buckets/$($cf.r2_bucket)/objects"

function Put($key, $path, $type) {
    $code = curl.exe -s -o NUL -w '%{http_code}' -X PUT "$api/$key" `
        -H "Authorization: Bearer $($cf.r2_token)" -H "Content-Type: $type" --data-binary "@$path"
    if ($code -ne '200') { throw "$key 업로드 실패: HTTP $code (토큰이 401/403이면 키 파일을 갱신하십시오)" }
    Write-Host "  $key"
}
Put 'deveztools/DevezTools_Setup.zip' $zip 'application/zip'
Put 'deveztools/version.json' $manifest 'application/json'

# ── 6. 공개 URL 검증(캐시 우회): 받은 파일이 방금 만든 파일과 같아야 한다 ──
$check = Join-Path $env:TEMP 'deveztools_verify'
New-Item -ItemType Directory -Path $check -Force | Out-Null
foreach ($name in 'version.json', 'DevezTools_Setup.zip') {
    curl.exe -sf "$publicBase/${name}?nocache=$(Get-Random)" -o (Join-Path $check $name)
    if ($LASTEXITCODE -ne 0) { throw "$name 을 공개 URL에서 받지 못했습니다." }
}
$remote = [IO.File]::ReadAllText((Join-Path $check 'version.json'), $utf8) | ConvertFrom-Json
if ($remote.version -ne $version -or $remote.sha256 -ne $sha256) { throw '공개 version.json 의 버전·해시가 다릅니다.' }
if ($remote.notes -ne ($notes -join "`n")) { throw '공개 version.json 의 노트가 다릅니다(인코딩 확인).' }
if ((Sha256 (Join-Path $check 'DevezTools_Setup.zip')) -ne $sha256) { throw '공개 설치 zip 해시가 다릅니다.' }
Remove-Item $check -Recurse -Force

Write-Host "`nv$version 배포를 확인했습니다. 실행 중인 앱은 1시간 안에 왼쪽 레일 맨 아래에 업데이트 단추를 띄웁니다." -ForegroundColor Green
