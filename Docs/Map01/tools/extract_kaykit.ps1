param([string]$AssetsRoot = 'E:\作品集\Game\Assets\ThirdPartyAssets\itch.io\KayLousberg', [string]$ZipDir = 'E:\作品集\美术资源包')
Add-Type -AssemblyName System.IO.Compression.FileSystem
$ErrorActionPreference = 'Stop'

function Extract-Pack($zipName, $packName, $version, $url, $prefixRegex, $includeRegex, $excludeRegex, $textureRegex) {
    $zipPath = Join-Path $ZipDir $zipName
    $dest = Join-Path $AssetsRoot "$packName\$version"
    New-Item -ItemType Directory -Force "$dest\Models" | Out-Null
    New-Item -ItemType Directory -Force "$dest\Textures" | Out-Null
    $z = [IO.Compression.ZipFile]::OpenRead($zipPath)
    $files = @()
    try {
        foreach ($e in $z.Entries) {
            if ($e.Name -eq '') { continue }
            if ($e.FullName -match $textureRegex) {
                $out = Join-Path "$dest\Textures" $e.Name
                [IO.Compression.ZipFileExtensions]::ExtractToFile($e, $out, $true)
                $files += $out
                continue
            }
            if ($e.FullName -notmatch $prefixRegex) { continue }
            $rel = $e.FullName -replace $prefixRegex, ''
            if ($e.Name -match '\.fbx$') {
                if ($rel -notmatch $includeRegex) { continue }
                if ($excludeRegex -and $rel -match $excludeRegex) { continue }
                $out = Join-Path "$dest\Models" ($rel -replace '/', '\')
            } elseif ($e.FullName -match $textureRegex) {
                $out = Join-Path "$dest\Textures" $e.Name
            } else { continue }
            New-Item -ItemType Directory -Force (Split-Path $out) | Out-Null
            [IO.Compression.ZipFileExtensions]::ExtractToFile($e, $out, $true)
            $files += $out
        }
        $lic = $z.Entries | Where-Object { $_.Name -eq 'License.txt' } | Select-Object -First 1
        [IO.Compression.ZipFileExtensions]::ExtractToFile($lic, "$dest\LICENSE.txt", $true)
    } finally { $z.Dispose() }

    $zipHash = (Get-FileHash $zipPath -Algorithm SHA256).Hash
    Set-Content "$dest\SOURCE_URL.txt" $url -Encoding UTF8
    Set-Content "$dest\VERSION.txt" "$packName $version (from $zipName)" -Encoding UTF8
    $sums = @("$zipHash  $zipName (original archive, kept outside the repo)")
    foreach ($f in $files) { $sums += "$((Get-FileHash $f -Algorithm SHA256).Hash)  $($f.Substring($dest.Length + 1) -replace '\\','/')" }
    Set-Content "$dest\SHA256SUMS.txt" $sums -Encoding UTF8
    "$packName : $($files.Count) files -> $dest"
}

# Forest：只取深绿（Color2）与枯黄（Color5）两套，树、灌木、岩石、草。
Extract-Pack 'KayKit – Forest Nature Pack.zip' 'ForestNaturePack' '1.0_SOURCE' 'https://kaylousberg.itch.io/kaykit-forest' `
    '^KayKit_Forest_Nature_Pack_1\.0_SOURCE/Assets/fbx\(unity\)/' `
    '^(Color2|Color5)/(Tree_[1-7]_[A-F]|Tree_Bare_[12]_[A-C]|Bush_[1-4]_[A-G]|Rock_[1-6]_[A-H]|Grass_[12]_[A-D])_Color\d\.fbx$' `
    $null 'fbx\(unity\)/Color2/forest_texture\.png$'

# Dungeon：中性结构与道具，排除旗帜、蜡烛、火把、金币、金箱、武器、尖刺、魔法装饰。
Extract-Pack 'Dungeon Pack.zip' 'DungeonPack' '1.1_SOURCE' 'https://kaylousberg.itch.io/kaykit-dungeon-pack' `
    '^KayKit_Dungeon_Pack_1\.1_SOURCE/Assets/fbx\(unity\)/' `
    '^(barrel_|crate|box_|bucket\.fbx|shelf_|shelves\.fbx|scaffold_|barrier|post\.fbx|floor_tile_(grate|big_grate|extralarge_grates|large\.fbx|small\.fbx)|table_(long|medium|small|round)|chair\.fbx|bench|stool|bed_(A_|B_|frame|floor)|bookcase_(single|double)\.fbx|floor_wood_|floor_foundation_|ceiling_tile|stairs\.fbx|stairs_wood\.fbx|rubble_|rocks\.fbx|rocks_small|wall\.fbx|wall_half\.fbx|wall_broken|wall_cracked|wall_doorway\.fbx|wall_window_(open|closed)\.fbx|wall_corner\.fbx|trunk_|keg\.fbx|column\.fbx|pillar\.fbx)' `
    '(torch|candle|banner|gold|decorated|tablecloth|spikes)' 'Assets/textures/dungeon_texture\.png$'

# Furniture：旧住宅可用的家具，排除电脑外设、游戏机和仙人掌。
Extract-Pack 'KayKit – Furniture Bits.zip' 'FurnitureBits' '1.0_SOURCE' 'https://kaylousberg.itch.io/furniture-bits' `
    '^KayKit_Furniture_Bits_1\.0_SOURCE/Assets/fbx \(unity\)/' `
    '\.fbx$' '(cactus|gameconsole|monitor|keyboard|mouse|chair_desk|lamp_desk)' 'Assets/textures/furniturebits_texture(_alt_A)?\.png$'
