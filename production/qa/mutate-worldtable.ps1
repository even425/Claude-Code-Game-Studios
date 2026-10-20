$ErrorActionPreference = 'Continue'
$root = 'D:\develop\Claude-Code-Game-Studios'
$src  = Join-Path $root 'src\HeroFactory.cs'
$bak  = Join-Path $env:TEMP 'HeroFactory.cs.bak'
$testProj = Join-Path $root 'tests\Claude-Code-Game-Studios.Tests.csproj'
$outLog = Join-Path $env:TEMP 'mutate-test-out.txt'
$errLog = Join-Path $env:TEMP 'mutate-test-err.txt'

Copy-Item $src $bak -Force
$pristineHash = (Get-FileHash $src -Algorithm SHA256).Hash
function Restore {
    Copy-Item $bak $src -Force
    $h = (Get-FileHash $src -Algorithm SHA256).Hash
    if ($h -ne $pristineHash) { Write-Output "!! RESTORE MISMATCH ($h)" }
}

# 把源文件换行统一成 \n 做模式匹配，写回时再还原成原换行符
function Read-Norm { [System.IO.File]::ReadAllText($src).Replace("`r`n", "`n") }
function Write-Norm {
    param([string]$Text)
    $orig = [System.IO.File]::ReadAllText($src)
    $nl = if ($orig.Contains("`r`n")) { "`r`n" } else { "`n" }
    [System.IO.File]::WriteAllText($src, $Text.Replace("`n", $nl), (New-Object System.Text.UTF8Encoding($false)))
}

function Run-Mutation {
    param([string]$Label, [string]$From, [string]$To)
    $text = Read-Norm
    $from = $From.Replace("`r`n", "`n")
    $to   = $To.Replace("`r`n", "`n")
    if (-not $text.Contains($from)) {
        Write-Output "MUTATION [$Label] : PATTERN NOT FOUND -> SKIP  ** 变异未生效，这条不算数 **"
        return
    }
    Write-Norm ($text.Replace($from, $to))
    & dotnet test $testProj -c Release --nologo -v q 1>$outLog 2>$errLog
    $combined = (Get-Content $outLog -Raw -ErrorAction SilentlyContinue) + (Get-Content $errLog -Raw -ErrorAction SilentlyContinue)
    $m = [regex]::Match($combined, '失败:\s*(\d+)')
    $m2 = [regex]::Match($combined, '错误:\s*(\d+)')
    $m3 = [regex]::Match($combined, 'Failed:\s*(\d+)')
    $failed = 0
    foreach ($mm in @($m, $m2, $m3)) {
        if ($mm.Success) { $failed = [Math]::Max($failed, [int]$mm.Groups[1].Value) }
    }
    if ($combined -notmatch '失败:\s*\d+' -and $combined -notmatch '错误:\s*\d+') { $failed = -1 }
    $color = if ($failed -gt 0) { 'RED  ' } elseif ($failed -eq 0) { 'GREEN' } else { '???  ' }
    Write-Output "MUTATION [$Label] -> $color (failed=$failed, expect RED)"
    Restore
}

function Run-ComboMutation {
    param([string]$Label, [string[]]$Froms, [string[]]$Tos)
    $text = Read-Norm
    for ($i = 0; $i -lt $Froms.Count; $i++) {
        $f = $Froms[$i].Replace("`r`n", "`n")
        $t = $Tos[$i].Replace("`r`n", "`n")
        if (-not $text.Contains($f)) { Write-Output "MUTATION [$Label] : PATTERN #$i NOT FOUND -> SKIP"; return }
        $text = $text.Replace($f, $t)
    }
    Write-Norm $text
    & dotnet test $testProj -c Release --nologo -v q 1>$outLog 2>$errLog
    $combined = (Get-Content $outLog -Raw -ErrorAction SilentlyContinue) + (Get-Content $errLog -Raw -ErrorAction SilentlyContinue)
    $failed = 0
    foreach ($mm in @([regex]::Match($combined,'失败:\s*(\d+)'), [regex]::Match($combined,'错误:\s*(\d+)'), [regex]::Match($combined,'Failed:\s*(\d+)'))) {
        if ($mm.Success) { $failed = [Math]::Max($failed, [int]$mm.Groups[1].Value) }
    }
    $color = if ($failed -gt 0) { 'RED  ' } elseif ($failed -eq 0) { 'GREEN' } else { '???  ' }
    Write-Output "MUTATION [$Label] -> $color (failed=$failed, expect RED)"
    Restore
}

Run-Mutation -Label 'weight 0 allowed' `
    -From '|| weight <= 0)' -To '|| weight < 0)'

Run-Mutation -Label 'traits array not required' `
    -From 'if (surname.Count == 0 || given.Count == 0
                        || traits.Count == 0 || personalities.Count == 0)' `
    -To 'if (surname.Count == 0 || given.Count == 0
                        || personalities.Count == 0)'

Run-Mutation -Label 'empty table accepted' `
    -From '|| arr.GetArrayLength() == 0)' -To '|| false)'

Run-Mutation -Label 'no builtin fallback on bad json' `
    -From 'if (!TryLoadFromJson(System.IO.File.ReadAllText(path), out var worlds, out _))
                {
                    All = Builtin;
                    return false;
                }' `
    -To 'if (!TryLoadFromJson(System.IO.File.ReadAllText(path), out var worlds, out _))
                {
                    All = worlds;
                    return false;
                }'

Run-Mutation -Label 'IOException catch removed' `
    -From '            catch (System.IO.IOException)
            {
                All = Builtin;
                return false;
            }' `
    -To '            catch (System.IO.IOException)
            {
                throw;
            }'

Run-Mutation -Label 'missing file throws (uncaught)' `
    -From '            catch (System.IO.IOException)
            {
                All = Builtin;
                return false;
            }' `
    -To '            catch (System.IO.IOException)
            {
                throw;
            }'

Run-ComboMutation -Label 'both nets down (no Exists guard AND IOException rethrows)' `
    -Froms @('if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
                {
                    All = Builtin;
                    return false;
                }',
              'catch (System.IO.IOException)
            {
                All = Builtin;
                return false;
            }') `
    -Tos   @('if (string.IsNullOrWhiteSpace(path))
                {
                    All = Builtin;
                    return false;
                }',
              'catch (System.IO.IOException)
            {
                throw;
            }')

Run-Mutation -Label 'name may be empty' `
    -From 'if (string.IsNullOrEmpty(name))
                    {
                        error = "世界表里有名字为空的世界。";
                        return false;
                    }' `
    -To 'if (false)
                    {
                        error = "世界表里有名字为空的世界。";
                        return false;
                    }'

Restore
$finalHash = (Get-FileHash $src -Algorithm SHA256).Hash
if ($finalHash -ne $pristineHash) { Write-Output "!! FINAL HASH MISMATCH -- FILE NOT PRISTINE" }
else { Write-Output "PRISTINE RESTORED (sha256=$finalHash)" }
# 主工程 + 测试工程都要重建：dotnet test 只重建它自己那层，
# 陈旧的 Claude-Code-Game-Studios.dll 会让「变异看红」出现假绿/假红。
& dotnet build $root\Claude-Code-Game-Studios.csproj -c Release --nologo -v q 1>$outLog 2>$errLog
& dotnet build $testProj -c Release --nologo -v q 1>$outLog 2>$errLog
$e = Get-Content $errLog -Raw -ErrorAction SilentlyContinue
if ($e -match ': error') { Write-Output 'REBUILD: HAS ERRORS' } else { Write-Output 'REBUILD CLEAN (main + tests)' }
