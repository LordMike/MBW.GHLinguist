namespace MBW.GHLinguist;

/// <summary>Identifies a GitHub Linguist language category.</summary>
/// <seealso href="https://github.com/github-linguist/linguist/blob/196b2a14418cab005065c72c9759370934c184bc/docs/how-linguist-works.md#language-type" />
public enum LanguageType : uint
{
    /// <summary>The runtime returned an unrecognized language type.</summary>
    Unknown = 0,

    /// <summary>A data-oriented language or format.</summary>
    Data = 1,

    /// <summary>A markup language.</summary>
    Markup = 2,

    /// <summary>A programming language.</summary>
    Programming = 3,

    /// <summary>A prose or documentation language.</summary>
    Prose = 4,
}

/// <summary>Specifies the Linguist language categories admitted during classification.</summary>
[Flags]
public enum LanguageTypeMask : uint
{
    /// <summary>Do not admit any language categories.</summary>
    None = 0,

    /// <summary>Admit data-oriented languages and formats.</summary>
    Data = 1U << 0,

    /// <summary>Admit markup languages.</summary>
    Markup = 1U << 1,

    /// <summary>Admit programming languages.</summary>
    Programming = 1U << 2,

    /// <summary>Admit prose and documentation languages.</summary>
    Prose = 1U << 3,

    /// <summary>Admit every supported language category.</summary>
    All = Data | Markup | Programming | Prose,
}

internal enum LanguageLookupKind : uint
{
    Name = 1,
    Alias = 2,
    Filename = 3,
    Extension = 4,
    Interpreter = 5,
}

/// <summary>Identifies the Linguist strategy that selected or considered a language.</summary>
/// <seealso href="https://github.com/github-linguist/linguist/blob/196b2a14418cab005065c72c9759370934c184bc/docs/how-linguist-works.md#how-linguist-detects-the-language-of-a-file" />
public enum DetectionStrategy : uint
{
    /// <summary>No strategy selected a language.</summary>
    None = 0,

    /// <summary>An editor modeline selected the language.</summary>
    Modeline = 1,

    /// <summary>A special filename selected the language.</summary>
    Filename = 2,

    /// <summary>A shebang interpreter selected the language.</summary>
    Shebang = 3,

    /// <summary>A filename extension selected or narrowed the language candidates.</summary>
    Extension = 4,

    /// <summary>An XML declaration selected or narrowed the language candidates.</summary>
    Xml = 5,

    /// <summary>A manpage filename selected the language.</summary>
    Manpage = 6,

    /// <summary>Linguist heuristics selected or narrowed the language candidates.</summary>
    Heuristics = 7,

    /// <summary>The content classifier selected the language.</summary>
    Classifier = 8,
}

/// <summary>Specifies which ordered Linguist detection strategies an analysis may execute.</summary>
/// <remarks>Flags are evaluated in Linguist's normal strategy order, not numeric caller order.</remarks>
[Flags]
public enum DetectionStrategyMask : uint
{
    /// <summary>Disable all language detection strategies.</summary>
    None = 0,

    /// <summary>Enable editor modeline detection.</summary>
    Modeline = 1U << 0,

    /// <summary>Enable special-filename detection.</summary>
    Filename = 1U << 1,

    /// <summary>Enable shebang interpreter detection.</summary>
    Shebang = 1U << 2,

    /// <summary>Enable filename-extension detection.</summary>
    Extension = 1U << 3,

    /// <summary>Enable XML declaration detection.</summary>
    Xml = 1U << 4,

    /// <summary>Enable manpage detection.</summary>
    Manpage = 1U << 5,

    /// <summary>Enable ambiguity-resolving heuristics.</summary>
    Heuristics = 1U << 6,

    /// <summary>Enable content classification.</summary>
    Classifier = 1U << 7,

    /// <summary>Enable every supported detection strategy.</summary>
    All = Modeline | Filename | Shebang | Extension | Xml | Manpage | Heuristics | Classifier,

    /// <summary>Enable the strategies in Linguist's standard detection pipeline, which is currently every strategy.</summary>
    Default = All,
}

[Flags]
internal enum BlobResultFlags : ulong
{
    None = 0,
    LikelyBinary = 1UL << 0,
    Binary = 1UL << 1,
    Text = 1UL << 2,
    Image = 1UL << 3,
    Solid = 1UL << 4,
    Csv = 1UL << 5,
    Pdf = 1UL << 6,
    Large = 1UL << 7,
    Viewable = 1UL << 8,
    SafeToColorize = 1UL << 9,
    HighLongLineRatio = 1UL << 10,
    LfsPointer = 1UL << 11,
    Vendored = 1UL << 12,
    Documentation = 1UL << 13,
    Generated = 1UL << 14,
    Detectable = 1UL << 15,
    IncludeInStatistics = 1UL << 16,
}
