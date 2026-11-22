$ErrorActionPreference = "Stop"

$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$itemFile = Join-Path $projectRoot "Content\items.csv"
$recipeFile = Join-Path $projectRoot "Content\recipes.csv"
$smeltingFile = Join-Path $projectRoot "Content\smelting.csv"
$lootFile = Join-Path $projectRoot "LootTables\overworld.json"
$textureDirectory = Join-Path $projectRoot "Textures\Items"
$localizationFiles = @{
    zh = Join-Path $projectRoot "Localization\zh.csv"
    en = Join-Path $projectRoot "Localization\en.csv"
}
$errors = [System.Collections.Generic.List[string]]::new()

function Add-Error([string]$message) {
    $errors.Add($message)
}

function Read-DataRows([string]$path) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        Add-Error "Missing data file: $path"
        return @()
    }

    $rows = [System.Collections.Generic.List[object]]::new()
    $lineNumber = 0
    foreach ($line in Get-Content -LiteralPath $path) {
        $lineNumber++
        $trimmed = $line.Trim()
        if ($trimmed.Length -eq 0 -or $trimmed.StartsWith("#")) { continue }
        $rows.Add([pscustomobject]@{
            Line = $lineNumber
            Fields = @($trimmed.Split(','))
        })
    }
    return $rows.ToArray()
}

function Read-Localization([string]$path, [string]$locale) {
    $keys = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        Add-Error "Missing localization file: $path"
        return ,$keys
    }

    $lineNumber = 0
    foreach ($line in Get-Content -LiteralPath $path) {
        $lineNumber++
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        $separator = $line.IndexOf(',')
        if ($separator -le 0) {
            Add-Error "$locale localization line $lineNumber has no key/value separator."
            continue
        }
        $key = $line.Substring(0, $separator).Trim()
        if ([string]::IsNullOrWhiteSpace($key) -or [string]::IsNullOrWhiteSpace($line.Substring($separator + 1))) {
            Add-Error "$locale localization line $lineNumber has a blank key or value."
        } elseif (-not $keys.Add($key)) {
            Add-Error "$locale localization key '$key' is duplicated (line $lineNumber)."
        }
    }
    return ,$keys
}

function Test-LocalizedKey($tables, [string]$key, [string]$context) {
    foreach ($locale in @("zh", "en")) {
        if (-not $tables[$locale].Contains($key)) {
            Add-Error "$context is missing '$key' in Localization/$locale.csv."
        }
    }
}

function Get-PngSize([string]$path) {
    $bytes = [System.IO.File]::ReadAllBytes($path)
    if ($bytes.Length -lt 24 -or $bytes[0] -ne 137 -or $bytes[1] -ne 80 -or
        $bytes[2] -ne 78 -or $bytes[3] -ne 71) {
        return $null
    }
    $width = ([int64]$bytes[16] -shl 24) -bor ([int64]$bytes[17] -shl 16) -bor
        ([int64]$bytes[18] -shl 8) -bor [int64]$bytes[19]
    $height = ([int64]$bytes[20] -shl 24) -bor ([int64]$bytes[21] -shl 16) -bor
        ([int64]$bytes[22] -shl 8) -bor [int64]$bytes[23]
    return [pscustomobject]@{ Width = [int]$width; Height = [int]$height }
}

$localization = @{}
foreach ($locale in $localizationFiles.Keys) {
    $localization[$locale] = Read-Localization $localizationFiles[$locale] $locale
}

$items = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
$gameTypes = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
foreach ($gameType in @("FOOD", "MATERIAL", "TOOL", "LUXURY_ITEM", "ART", "MACHINE", "HOUSEHOLD_GOOD", "STORAGE")) {
    [void]$gameTypes.Add($gameType)
}
foreach ($row in Read-DataRows $itemFile) {
    $fields = $row.Fields
    if ($fields.Count -lt 5 -or $fields.Count -gt 6) {
        Add-Error "Content/items.csv line $($row.Line) must have 5 or 6 columns."
        continue
    }
    $id, $sprite, $tier = $fields[0].Trim(), $fields[1].Trim(), $fields[2].Trim()
    $width, $height = 0, 0
    if ([string]::IsNullOrWhiteSpace($id) -or $items.Contains($id)) {
        Add-Error "Content/items.csv line $($row.Line) has a blank or duplicate item ID '$id'."
        continue
    }
    if ($tier -cnotin @("A", "B", "C", "D", "E")) {
        Add-Error "Content/items.csv line $($row.Line) has unknown value tier '$tier'."
    }
    if (-not [int]::TryParse($fields[3].Trim(), [ref]$width) -or $width -lt 1 -or $width -gt 2 -or
        -not [int]::TryParse($fields[4].Trim(), [ref]$height) -or $height -lt 1 -or $height -gt 2) {
        Add-Error "Content/items.csv line $($row.Line) has an invalid footprint; each side must be 1 or 2."
        continue
    }
    if ($fields.Count -eq 6) {
        $types = @($fields[5].Split('|') | ForEach-Object { $_.Trim() } | Where-Object { $_.Length -gt 0 })
        if ($types.Count -eq 0) {
            Add-Error "Content/items.csv line $($row.Line) has no game item type."
        }
        foreach ($gameType in $types) {
            if (-not $gameTypes.Contains($gameType)) {
                Add-Error "Content/items.csv line $($row.Line) has unknown game item type '$gameType'."
            }
        }
    }
    [void]$items.Add($id)
    Test-LocalizedKey $localization "item_${id}_name" "Item '$id'"
    Test-LocalizedKey $localization "item_${id}_desc" "Item '$id'"

    $texture = Join-Path $textureDirectory "$sprite.png"
    if (-not (Test-Path -LiteralPath $texture -PathType Leaf)) {
        Add-Error "Item '$id' is missing texture Textures/Items/$sprite.png."
        continue
    }
    $size = Get-PngSize $texture
    if ($null -eq $size) {
        Add-Error "Item '$id' texture '$sprite.png' is not a readable PNG."
    } elseif ($size.Width -ne ($width * 16) -or $size.Height -ne ($height * 16)) {
        Add-Error "Item '$id' footprint is ${width}x${height}, but '$sprite.png' canvas is $($size.Width)x$($size.Height); expected $($width * 16)x$($height * 16)."
    }
}

$recipeRows = @(Read-DataRows $recipeFile)
foreach ($row in $recipeRows) {
    $fields = $row.Fields
    if ($fields.Count -lt 6 -or (($fields.Count - 4) % 2) -ne 0) {
        Add-Error "Content/recipes.csv line $($row.Line) must have output fields followed by material ID/quantity pairs."
        continue
    }
    $outputId = $fields[0].Trim()
    $outputAmount = 0
    if (-not $items.Contains($outputId)) {
        Add-Error "Content/recipes.csv line $($row.Line) refers to missing output item '$outputId'."
    }
    if (-not [int]::TryParse($fields[1].Trim(), [ref]$outputAmount) -or $outputAmount -lt 1) {
        Add-Error "Content/recipes.csv line $($row.Line) has an invalid output quantity."
    }
    Test-LocalizedKey $localization $fields[2].Trim() "Recipe on line $($row.Line)"
    Test-LocalizedKey $localization $fields[3].Trim() "Recipe on line $($row.Line)"
    for ($index = 4; $index -lt $fields.Count; $index += 2) {
        $materialId = $fields[$index].Trim()
        $amount = 0
        if (-not $items.Contains($materialId)) {
            Add-Error "Content/recipes.csv line $($row.Line) refers to missing material '$materialId'."
        }
        if (-not [int]::TryParse($fields[$index + 1].Trim(), [ref]$amount) -or $amount -lt 1) {
            Add-Error "Content/recipes.csv line $($row.Line) has an invalid quantity for '$materialId'."
        }
    }
}

$smeltingInputs = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
foreach ($row in Read-DataRows $smeltingFile) {
    $fields = $row.Fields
    if ($fields.Count -ne 2) {
        Add-Error "Content/smelting.csv line $($row.Line) must have input and output IDs."
        continue
    }
    $inputId, $outputId = $fields[0].Trim(), $fields[1].Trim()
    if (-not $items.Contains($inputId)) { Add-Error "Smelting line $($row.Line) refers to missing input '$inputId'." }
    if (-not $items.Contains($outputId)) { Add-Error "Smelting line $($row.Line) refers to missing output '$outputId'." }
    if (-not $smeltingInputs.Add($inputId)) { Add-Error "Content/smelting.csv has more than one recipe for '$inputId'." }
}

if (Test-Path -LiteralPath $lootFile -PathType Leaf) {
    try { $loot = Get-Content -LiteralPath $lootFile -Raw | ConvertFrom-Json }
    catch { Add-Error "LootTables/overworld.json is invalid JSON: $($_.Exception.Message)" }
    if ($null -ne $loot) {
        $lootEntries = @()
        foreach ($table in @($loot.tables)) { $lootEntries += @($table.entries) }
        foreach ($patch in @($loot.patches)) { $lootEntries += @($patch.add) }
        foreach ($entry in $lootEntries) {
            if ([string]$entry.id -like "mc_*" -and -not $items.Contains([string]$entry.id)) {
                Add-Error "Loot table entry refers to missing item '$($entry.id)'."
            }
            if ([double]$entry.weight -le 0) { Add-Error "Loot table entry '$($entry.id)' must have positive weight." }
        }
    }
} else {
    Add-Error "Missing loot data file: $lootFile"
}

if ($errors.Count -gt 0) {
    Write-Host "Content validation failed with $($errors.Count) issue(s):" -ForegroundColor Red
    foreach ($errorMessage in $errors) { Write-Host " - $errorMessage" -ForegroundColor Red }
    exit 1
}

$recipeCount = $recipeRows.Count
$smeltingCount = $smeltingInputs.Count
$lootTableCount = @($loot.tables).Count
Write-Host "Content validation passed: $($items.Count) items, $recipeCount recipes, $smeltingCount smelting recipes, $lootTableCount loot tables; textures and zh/en localization are complete." -ForegroundColor Green
