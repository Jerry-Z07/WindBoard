param(
    [Parameter(Mandatory = $true)]
    [string]$ProjectDir,

    [Parameter(Mandatory = $true)]
    [string]$OutputFile
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Append-Line {
    param(
        [System.Text.StringBuilder]$Builder,
        [string]$Text = ''
    )

    [void]$Builder.AppendLine($Text)
}

function Escape-CSharpString {
    param([string]$Text)

    return $Text.Replace('\\', '\\\\').Replace('"', '\\"')
}

$stringsDir = Join-Path $ProjectDir 'Strings'
$featureNamesByCulture = [ordered]@{}

if (Test-Path -LiteralPath $stringsDir) {
    foreach ($cultureDir in Get-ChildItem -LiteralPath $stringsDir -Directory | Sort-Object Name) {
        $featureNames = @(
            Get-ChildItem -LiteralPath $cultureDir.FullName -File -Filter '*.resw'
            | Sort-Object BaseName
            | ForEach-Object { $_.BaseName }
        )

        if ($featureNames.Count -gt 0) {
            $featureNamesByCulture[$cultureDir.Name] = $featureNames
        }
    }
}

$builder = [System.Text.StringBuilder]::new()
Append-Line $builder '#nullable enable'
Append-Line $builder '#pragma warning disable CS8600'
Append-Line $builder ''
Append-Line $builder 'using System;'
Append-Line $builder 'using System.Collections.Generic;'
Append-Line $builder ''
Append-Line $builder 'namespace WindBoard.Localization'
Append-Line $builder '{'
Append-Line $builder '    internal static class L10nResourceMetadata'
Append-Line $builder '    {'
Append-Line $builder '        internal static IReadOnlyList<string> SupportedCultureNames { get; } = new[]'
Append-Line $builder '        {'

foreach ($cultureName in $featureNamesByCulture.Keys) {
    Append-Line $builder ('            "{0}",' -f (Escape-CSharpString $cultureName))
}

Append-Line $builder '        };'
Append-Line $builder ''
Append-Line $builder '        private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> FeaturesByCultureName = new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase)'
Append-Line $builder '        {'

foreach ($cultureName in $featureNamesByCulture.Keys) {
    Append-Line $builder ('            ["{0}"] = new HashSet<string>(StringComparer.Ordinal)' -f (Escape-CSharpString $cultureName))
    Append-Line $builder '            {'

    foreach ($featureName in $featureNamesByCulture[$cultureName]) {
        Append-Line $builder ('                "{0}",' -f (Escape-CSharpString $featureName))
    }

    Append-Line $builder '            },'
}

Append-Line $builder '        };'
Append-Line $builder ''
Append-Line $builder '        internal static bool HasCulture(string cultureName)'
Append-Line $builder '        {'
Append-Line $builder '            return !string.IsNullOrWhiteSpace(cultureName) && FeaturesByCultureName.ContainsKey(cultureName);'
Append-Line $builder '        }'
Append-Line $builder ''
Append-Line $builder '        internal static bool HasFeature(string cultureName, string featureName)'
Append-Line $builder '        {'
Append-Line $builder '            return FeaturesByCultureName.TryGetValue(cultureName, out IReadOnlySet<string>? featureNames)'
Append-Line $builder '                && featureNames is not null'
Append-Line $builder '                && featureNames.Contains(featureName);'
Append-Line $builder '        }'
Append-Line $builder ''
Append-Line $builder '        internal static IReadOnlyCollection<string> GetFeatures(string cultureName)'
Append-Line $builder '        {'
Append-Line $builder '            if (FeaturesByCultureName.TryGetValue(cultureName, out IReadOnlySet<string>? featureNames)'
Append-Line $builder '                && featureNames is not null)'
Append-Line $builder '            {'
Append-Line $builder '                return featureNames;'
Append-Line $builder '            }'
Append-Line $builder ''
Append-Line $builder '            return Array.Empty<string>();'
Append-Line $builder '        }'
Append-Line $builder '    }'
Append-Line $builder '}'
Append-Line $builder ''
Append-Line $builder '#pragma warning restore CS8600'
Append-Line $builder '#nullable restore'

$content = $builder.ToString()
$outputDirectory = Split-Path -Parent $OutputFile
if (![string]::IsNullOrWhiteSpace($outputDirectory)) {
    [System.IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
}

if ((Test-Path -LiteralPath $OutputFile) -and ([System.IO.File]::ReadAllText($OutputFile) -ceq $content)) {
    return
}

$utf8NoBom = [System.Text.UTF8Encoding]::new($false)
[System.IO.File]::WriteAllText($OutputFile, $content, $utf8NoBom)
