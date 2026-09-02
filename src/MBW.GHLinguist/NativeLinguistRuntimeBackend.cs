using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace MBW.GHLinguist;

internal sealed unsafe class NativeLinguistRuntimeBackend : ILinguistRuntimeBackend
{
    private const uint AbiMajor = 1;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly NativeRuntimeHandle _runtime;
    private LinguistCapabilities? _capabilities;
    private IReadOnlyList<LinguistLanguage>? _languages;
    private Dictionary<ulong, LinguistLanguage>? _languagesById;
    private LinguistVersionInfo? _version;

    private NativeLinguistRuntimeBackend(NativeRuntimeHandle runtime)
    {
        _runtime = runtime;
    }

    internal static NativeLinguistRuntimeBackend Create()
    {
        uint nativeMajor = NativeMethods.AbiVersionMajor();
        uint nativeMinor = NativeMethods.AbiVersionMinor();
        if (nativeMajor != AbiMajor)
        {
            throw new LinguistException($"Native ABI version {nativeMajor}.{nativeMinor} is incompatible with managed ABI {AbiMajor}.0.");
        }

        NativeRuntimeOptions options = new()
        {
            StructSize = (uint)Unsafe.SizeOf<NativeRuntimeOptions>(),
        };

        nint runtime = 0;
        nint error = 0;
        NativeStatus status = NativeMethods.RuntimeCreate(&options, &runtime, &error);
        try
        {
            ThrowForStatus(status, error);
        }
        catch
        {
            if (runtime != 0)
            {
                NativeMethods.RuntimeRelease(runtime);
            }

            throw;
        }

        if (runtime == 0)
        {
            throw new LinguistException("The native runtime succeeded without returning a runtime handle.");
        }

        return new NativeLinguistRuntimeBackend(new NativeRuntimeHandle(runtime));
    }

    public LinguistVersionInfo Version
    {
        get
        {
            ThrowIfDisposed();
            return _version ??= ReadVersion();
        }
    }

    public LinguistCapabilities Capabilities
    {
        get
        {
            ThrowIfDisposed();
            return _capabilities ??= (LinguistCapabilities)NativeMethods.RuntimeCapabilities(_runtime);
        }
    }

    public IReadOnlyList<LinguistLanguage> Languages
    {
        get
        {
            ThrowIfDisposed();
            EnsureLanguages();
            return _languages!;
        }
    }

    public LinguistLanguage? FindByName(string name) => FindOne(LanguageLookupKind.Name, name);

    public LinguistLanguage? FindByAlias(string alias) => FindOne(LanguageLookupKind.Alias, alias);

    public IReadOnlyList<LinguistLanguage> FindByFilename(string filenameOrPath) => FindMany(LanguageLookupKind.Filename, filenameOrPath);

    public IReadOnlyList<LinguistLanguage> FindByExtension(string filenameOrPath) => FindMany(LanguageLookupKind.Extension, filenameOrPath);

    public IReadOnlyList<LinguistLanguage> FindByInterpreter(string interpreter) => FindMany(LanguageLookupKind.Interpreter, interpreter);

    public BlobAnalysis Analyze(ReadOnlySpan<byte> data, BlobInput input, BlobAnalysisOptions options)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(options);

        byte[]? pathBytes = EncodeOptional(input.Path, nameof(BlobInput.Path));
        byte[]? nameBytes = EncodeOptional(input.Name, nameof(BlobInput.Name));
        NativeBlobInput blob = new()
        {
            StructSize = (uint)Unsafe.SizeOf<NativeBlobInput>(),
            Flags = (input.IsSymlink ? NativeBlobInputFlags.Symlink : 0) |
                (input.IsLfsTracked ? NativeBlobInputFlags.LfsTracked : 0),
        };
        NativeAnalysisOptions nativeOptions = new()
        {
            StructSize = (uint)Unsafe.SizeOf<NativeAnalysisOptions>(),
            Flags = (options.AllowEmpty ? NativeAnalysisFlags.AllowEmpty : 0) |
                (options.IncludeStrategyTrace ? NativeAnalysisFlags.IncludeTrace : 0) |
                (options.IncludeLineCounts ? NativeAnalysisFlags.IncludeLineCounts : 0),
            Strategies = (uint)options.Strategies,
        };

        fixed (byte* dataPointer = data)
        fixed (byte* pathPointer = pathBytes)
        fixed (byte* namePointer = nameBytes)
        {
            blob.Path = CreateStringView(pathPointer, pathBytes);
            blob.Name = CreateStringView(namePointer, nameBytes);
            blob.Data = new NativeBytesView(dataPointer, (nuint)data.Length);

            nint analysis = 0;
            nint error = 0;
            NativeStatus status = NativeMethods.RuntimeAnalyze(_runtime, &blob, &nativeOptions, &analysis, &error);
            try
            {
                ThrowForStatus(status, error);
            }
            catch
            {
                if (analysis != 0)
                {
                    NativeMethods.AnalysisRelease(analysis);
                }

                throw;
            }

            if (analysis == 0)
            {
                throw new LinguistException("The native runtime succeeded without returning an analysis handle.");
            }

            using NativeAnalysisHandle handle = new(analysis);
            return CopyAnalysis(handle, data.IsEmpty, options);
        }
    }

    public ClassificationResults Classify(ReadOnlySpan<byte> data, ClassificationOptions options)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(options);

        if (options.CandidateLanguageIds is { Count: 0 })
        {
            return new ClassificationResults(0, []);
        }

        if (options.CandidateLanguageIds is not null)
        {
            EnsureLanguages();
            foreach (ulong languageId in options.CandidateLanguageIds)
            {
                if (!_languagesById!.ContainsKey(languageId))
                {
                    throw new ArgumentException(
                        $"Candidate language ID {languageId} does not exist in the loaded Linguist registry.",
                        nameof(ClassificationOptions.CandidateLanguageIds));
                }
            }
        }

        ulong[]? candidateIds = options.CandidateLanguageIds?.ToArray();
        NativeClassifyOptions nativeOptions = new()
        {
            StructSize = (uint)Unsafe.SizeOf<NativeClassifyOptions>(),
            AllowedTypes = (uint)options.AllowedTypes,
            MaximumBytes = checked((uint)options.MaximumBytes),
        };

        fixed (byte* dataPointer = data)
        fixed (ulong* candidatesPointer = candidateIds)
        {
            nativeOptions.CandidateLanguageIds = candidatesPointer;
            nativeOptions.CandidateLanguageCount = checked((nuint)(candidateIds?.Length ?? 0));
            nint classification = 0;
            nint error = 0;
            NativeStatus status = NativeMethods.RuntimeClassify(
                _runtime,
                new NativeBytesView(dataPointer, (nuint)data.Length),
                &nativeOptions,
                &classification,
                &error);
            try
            {
                ThrowForStatus(status, error);
            }
            catch
            {
                if (classification != 0)
                {
                    NativeMethods.ClassificationRelease(classification);
                }

                throw;
            }

            if (classification == 0)
            {
                throw new LinguistException("The native runtime succeeded without returning a classification handle.");
            }

            using NativeClassificationHandle handle = new(classification);
            int count = CheckedLength(NativeMethods.ClassificationCount(handle), "classification result count");
            var results = new ClassificationResult[count];
            for (int index = 0; index < count; index++)
            {
                ulong languageId = 0;
                double score = 0;
                ThrowForStatus(NativeMethods.ClassificationResult(handle, (nuint)index, &languageId, &score), 0);
                if (!double.IsFinite(score) || score <= 0 || score > 1)
                {
                    throw new LinguistException($"The native runtime returned invalid classifier score {score}.");
                }

                results[index] = new ClassificationResult(GetLanguage(languageId), score);
            }

            return new ClassificationResults(checked((int)NativeMethods.ClassificationConsideredBytes(handle)), results);
        }
    }

    public void Dispose() => _runtime.Dispose();

    private LinguistVersionInfo ReadVersion()
    {
        NativeVersionInfo version = new()
        {
            StructSize = (uint)Unsafe.SizeOf<NativeVersionInfo>(),
        };
        ThrowForStatus(NativeMethods.RuntimeVersion(_runtime, &version), 0);
        if (version.AbiMajor != AbiMajor)
        {
            throw new LinguistException($"Runtime reported incompatible ABI version {version.AbiMajor}.{version.AbiMinor}.");
        }

        return new LinguistVersionInfo(
            version.AbiMajor,
            version.AbiMinor,
            ReadRequiredString(version.WrapperVersion),
            ReadRequiredString(version.RubyVersion),
            ReadRequiredString(version.LinguistVersion),
            ReadRequiredString(version.LinguistRevision),
            ReadRequiredString(version.ClassifierSha256));
    }

    private void EnsureLanguages()
    {
        if (_languages is not null)
        {
            return;
        }

        int count = CheckedLength(NativeMethods.RuntimeLanguageCount(_runtime), "language count");
        var languages = new LinguistLanguage[count];
        var languagesById = new Dictionary<ulong, LinguistLanguage>(count);
        for (int index = 0; index < count; index++)
        {
            ulong languageId = 0;
            ThrowForStatus(NativeMethods.RuntimeLanguageIdAt(_runtime, (nuint)index, &languageId), 0);
            LinguistLanguage language = ReadLanguage(languageId);
            if (!languagesById.TryAdd(languageId, language))
            {
                throw new LinguistException($"The native runtime returned duplicate language ID {languageId}.");
            }

            languages[index] = language;
        }

        _languages = Array.AsReadOnly(languages);
        _languagesById = languagesById;
        foreach (LinguistLanguage language in languages)
        {
            if (language.GroupLanguageId is ulong groupId && !languagesById.ContainsKey(groupId))
            {
                throw new LinguistException($"Language {language.Id} references unknown group language ID {groupId}.");
            }
        }
    }

    private LinguistLanguage ReadLanguage(ulong languageId)
    {
        NativeLanguageInfo info = new()
        {
            StructSize = (uint)Unsafe.SizeOf<NativeLanguageInfo>(),
        };
        ThrowForStatus(NativeMethods.RuntimeLanguageInfo(_runtime, languageId, &info), 0);
        if (info.LanguageId != languageId || info.LanguageId == 0)
        {
            throw new LinguistException("The native runtime returned inconsistent language metadata.");
        }

        return new LinguistLanguage(
            info.LanguageId,
            info.GroupLanguageId == 0 ? null : info.GroupLanguageId,
            ReadRequiredString(info.Name),
            ReadOptionalString(info.FileSystemName),
            ReadLanguageType(info.Type),
            (info.Flags & 1) != 0,
            (info.Flags & 2) != 0,
            ReadOptionalString(info.Color),
            ReadRequiredString(info.TextMateScope),
            ReadOptionalString(info.AceMode),
            ReadOptionalString(info.CodeMirrorMode),
            ReadOptionalString(info.CodeMirrorMimeType),
            ReadLanguageCollection(languageId, NativeLanguageCollection.Aliases, info.AliasCount),
            ReadLanguageCollection(languageId, NativeLanguageCollection.Extensions, info.ExtensionCount),
            ReadLanguageCollection(languageId, NativeLanguageCollection.Interpreters, info.InterpreterCount),
            ReadLanguageCollection(languageId, NativeLanguageCollection.Filenames, info.FilenameCount));
    }

    private string[] ReadLanguageCollection(ulong languageId, NativeLanguageCollection collection, uint count)
    {
        string[] values = new string[checked((int)count)];
        for (int index = 0; index < values.Length; index++)
        {
            NativeStringView value = default;
            ThrowForStatus(NativeMethods.RuntimeLanguageCollectionValue(_runtime, languageId, collection, (nuint)index, &value), 0);
            values[index] = ReadRequiredString(value);
        }

        return values;
    }

    private LinguistLanguage? FindOne(LanguageLookupKind kind, string value)
    {
        IReadOnlyList<LinguistLanguage> matches = FindMany(kind, value);
        return matches.Count switch
        {
            0 => null,
            1 => matches[0],
            _ => throw new LinguistException($"The native runtime returned multiple languages for singular {kind} lookup."),
        };
    }

    private IReadOnlyList<LinguistLanguage> FindMany(LanguageLookupKind kind, string value)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(value);
        byte[] valueBytes = EncodeRequired(value, nameof(value));
        fixed (byte* valuePointer = valueBytes)
        {
            nint languages = 0;
            nint error = 0;
            NativeStatus status = NativeMethods.RuntimeLookupLanguages(
                _runtime,
                (NativeLookupKind)kind,
                new NativeStringView(valuePointer, (nuint)valueBytes.Length),
                &languages,
                &error);
            try
            {
                ThrowForStatus(status, error);
            }
            catch
            {
                if (languages != 0)
                {
                    NativeMethods.LanguageIdListRelease(languages);
                }

                throw;
            }

            if (languages == 0)
            {
                throw new LinguistException("The native runtime succeeded without returning a language list handle.");
            }

            using NativeLanguageIdListHandle handle = new(languages);
            int count = CheckedLength(NativeMethods.LanguageIdListCount(handle), "language lookup result count");
            var results = new LinguistLanguage[count];
            var seenLanguageIds = new HashSet<ulong>();
            for (int index = 0; index < count; index++)
            {
                ulong languageId = 0;
                ThrowForStatus(NativeMethods.LanguageIdListAt(handle, (nuint)index, &languageId), 0);
                if (!seenLanguageIds.Add(languageId))
                {
                    throw new LinguistException($"The native runtime returned duplicate lookup language ID {languageId}.");
                }

                results[index] = GetLanguage(languageId);
            }

            return Array.AsReadOnly(results);
        }
    }

    private BlobAnalysis CopyAnalysis(NativeAnalysisHandle analysis, bool isEmpty, BlobAnalysisOptions options)
    {
        ulong languageId = NativeMethods.AnalysisLanguageId(analysis);
        LinguistLanguage? language = languageId == 0 ? null : GetLanguage(languageId);
        var trace = new StrategyTraceEntry[options.IncludeStrategyTrace
            ? CheckedLength(NativeMethods.AnalysisTraceCount(analysis), "analysis trace count")
            : 0];

        for (int traceIndex = 0; traceIndex < trace.Length; traceIndex++)
        {
            NativeStrategyTraceEntry entry = new()
            {
                StructSize = (uint)Unsafe.SizeOf<NativeStrategyTraceEntry>(),
            };
            ThrowForStatus(NativeMethods.AnalysisTraceEntry(analysis, (nuint)traceIndex, &entry), 0);
            var candidates = new LinguistLanguage[checked((int)entry.CandidateCount)];
            for (int candidateIndex = 0; candidateIndex < candidates.Length; candidateIndex++)
            {
                ulong candidateId = 0;
                ThrowForStatus(NativeMethods.AnalysisTraceCandidate(analysis, (nuint)traceIndex, (nuint)candidateIndex, &candidateId), 0);
                candidates[candidateIndex] = GetLanguage(candidateId);
            }

            trace[traceIndex] = new StrategyTraceEntry(ReadDetectionStrategy(entry.Strategy), candidates);
        }

        BlobResultFlags flags = (BlobResultFlags)NativeMethods.AnalysisFlags(analysis);
        const BlobResultFlags knownFlags = BlobResultFlags.LikelyBinary |
            BlobResultFlags.Binary |
            BlobResultFlags.Text |
            BlobResultFlags.Image |
            BlobResultFlags.Solid |
            BlobResultFlags.Csv |
            BlobResultFlags.Pdf |
            BlobResultFlags.Large |
            BlobResultFlags.Viewable |
            BlobResultFlags.SafeToColorize |
            BlobResultFlags.HighLongLineRatio |
            BlobResultFlags.LfsPointer |
            BlobResultFlags.Vendored |
            BlobResultFlags.Documentation |
            BlobResultFlags.Generated |
            BlobResultFlags.Detectable |
            BlobResultFlags.IncludeInStatistics;
        if ((flags & ~knownFlags) != 0)
        {
            throw new LinguistException($"The native runtime returned unsupported blob result flags 0x{(ulong)flags:x}.");
        }

        return new BlobAnalysis(
            language,
            ReadDetectionStrategy(NativeMethods.AnalysisStrategy(analysis)),
            isEmpty,
            flags,
            ReadAnalysisText(analysis, NativeAnalysisTextField.MimeType),
            ReadAnalysisText(analysis, NativeAnalysisTextField.ContentType),
            ReadAnalysisText(analysis, NativeAnalysisTextField.Disposition),
            ReadOptionalAnalysisText(analysis, NativeAnalysisTextField.Encoding),
            ReadOptionalAnalysisText(analysis, NativeAnalysisTextField.RubyEncoding),
            ReadOptionalAnalysisText(analysis, NativeAnalysisTextField.TextMateScope),
            options.IncludeLineCounts ? NativeMethods.AnalysisLoc(analysis) : null,
            options.IncludeLineCounts ? NativeMethods.AnalysisSloc(analysis) : null,
            trace);
    }

    private string ReadAnalysisText(NativeAnalysisHandle analysis, NativeAnalysisTextField field)
    {
        NativeStringView value = default;
        ThrowForStatus(NativeMethods.AnalysisText(analysis, field, &value), 0);
        return ReadRequiredString(value);
    }

    private string? ReadOptionalAnalysisText(NativeAnalysisHandle analysis, NativeAnalysisTextField field)
    {
        NativeStringView value = default;
        ThrowForStatus(NativeMethods.AnalysisText(analysis, field, &value), 0);
        return ReadOptionalString(value);
    }

    private LinguistLanguage GetLanguage(ulong languageId)
    {
        if (languageId == 0)
        {
            throw new LinguistException("The native runtime returned language ID zero where a language was required.");
        }

        EnsureLanguages();
        return _languagesById!.TryGetValue(languageId, out LinguistLanguage? language)
            ? language
            : throw new LinguistException($"The native runtime returned unknown language ID {languageId}.");
    }

    private void ThrowIfDisposed()
    {
        if (_runtime.IsClosed || _runtime.IsInvalid)
        {
            throw new ObjectDisposedException(nameof(NativeLinguistRuntimeBackend));
        }
    }

    private static NativeStringView CreateStringView(byte* pointer, byte[]? bytes) => new(pointer, (nuint)(bytes?.Length ?? 0));

    private static byte[] EncodeRequired(string value, string parameterName)
    {
        try
        {
            return StrictUtf8.GetBytes(value);
        }
        catch (EncoderFallbackException exception)
        {
            throw new ArgumentException("The value contains invalid UTF-16 and cannot be encoded as strict UTF-8.", parameterName, exception);
        }
    }

    private static byte[]? EncodeOptional(string? value, string parameterName) => value is null ? null : EncodeRequired(value, parameterName);

    private static string ReadRequiredString(NativeStringView value) =>
        ReadOptionalString(value) ?? throw new LinguistException("The native runtime omitted a required string value.");

    private static string? ReadOptionalString(NativeStringView value)
    {
        if (value.Data == null)
        {
            if (value.Length != 0)
            {
                throw new LinguistException("The native runtime returned a null string pointer with a nonzero length.");
            }

            return null;
        }

        int length = CheckedLength(value.Length, "UTF-8 string length");
        try
        {
            return StrictUtf8.GetString(new ReadOnlySpan<byte>(value.Data, length));
        }
        catch (DecoderFallbackException exception)
        {
            throw new LinguistException("The native runtime returned invalid UTF-8.", exception);
        }
    }

    private static int CheckedLength(nuint length, string description) =>
        length <= int.MaxValue ? (int)length : throw new LinguistException($"The native runtime returned an unsupported {description} of {length}.");

    private static LanguageType ReadLanguageType(uint value) => Enum.IsDefined((LanguageType)value)
        ? (LanguageType)value
        : throw new LinguistException($"The native runtime returned unsupported language type {value}.");

    private static DetectionStrategy ReadDetectionStrategy(uint value) => Enum.IsDefined((DetectionStrategy)value)
        ? (DetectionStrategy)value
        : throw new LinguistException($"The native runtime returned unsupported detection strategy {value}.");

    private static void ThrowForStatus(NativeStatus status, nint error)
    {
        if (status == NativeStatus.Ok)
        {
            if (error != 0)
            {
                using NativeErrorHandle unexpectedError = new(error);
                throw new LinguistException("The native runtime returned an error handle for a successful operation.");
            }

            return;
        }

        using NativeErrorHandle errorHandle = new(error);
        NativeStatus effectiveStatus = error == 0 ? status : NativeMethods.ErrorStatus(errorHandle);
        string message = error == 0 ? $"Native Linguist operation failed with status {status}." : ReadRequiredString(NativeMethods.ErrorMessage(errorHandle));
        switch (effectiveStatus)
        {
            case NativeStatus.InvalidArgument:
            case NativeStatus.InvalidUtf8:
                throw new ArgumentException(message);
            case NativeStatus.Unsupported:
                throw new NotSupportedException(message);
            case NativeStatus.NotFound:
                throw new KeyNotFoundException(message);
            case NativeStatus.OutOfMemory:
                throw new OutOfMemoryException(message);
            case NativeStatus.RubyException:
                throw new LinguistRubyException(
                    message,
                    error == 0 ? null : ReadOptionalString(NativeMethods.ErrorRubyClass(errorHandle)),
                    error == 0 ? null : ReadOptionalString(NativeMethods.ErrorRubyBacktrace(errorHandle)));
            default:
                throw new LinguistException(message);
        }
    }
}

internal sealed class NativeRuntimeHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    internal NativeRuntimeHandle(nint handle)
        : base(true) => SetHandle(handle);

    protected override bool ReleaseHandle()
    {
        NativeMethods.RuntimeRelease(handle);
        return true;
    }
}

internal sealed class NativeAnalysisHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    internal NativeAnalysisHandle(nint handle)
        : base(true) => SetHandle(handle);

    protected override bool ReleaseHandle()
    {
        NativeMethods.AnalysisRelease(handle);
        return true;
    }
}

internal sealed class NativeClassificationHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    internal NativeClassificationHandle(nint handle)
        : base(true) => SetHandle(handle);

    protected override bool ReleaseHandle()
    {
        NativeMethods.ClassificationRelease(handle);
        return true;
    }
}

internal sealed class NativeLanguageIdListHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    internal NativeLanguageIdListHandle(nint handle)
        : base(true) => SetHandle(handle);

    protected override bool ReleaseHandle()
    {
        NativeMethods.LanguageIdListRelease(handle);
        return true;
    }
}

internal sealed class NativeErrorHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    internal NativeErrorHandle(nint handle)
        : base(true) => SetHandle(handle);

    protected override bool ReleaseHandle()
    {
        if (!IsInvalid)
        {
            NativeMethods.ErrorRelease(handle);
        }

        return true;
    }
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct NativeStringView
{
    internal NativeStringView(void* data, nuint length)
    {
        Data = (byte*)data;
        Length = length;
    }

    internal byte* Data;
    internal nuint Length;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct NativeBytesView
{
    internal NativeBytesView(byte* data, nuint length)
    {
        Data = data;
        Length = length;
    }

    internal byte* Data;
    internal nuint Length;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeRuntimeOptions
{
    internal uint StructSize;
    internal uint Flags;
    internal NativeStringView AssetRoot;
    internal ulong Reserved1;
    internal ulong Reserved2;
    internal ulong Reserved3;
    internal ulong Reserved4;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeBlobInput
{
    internal uint StructSize;
    internal NativeBlobInputFlags Flags;
    internal NativeStringView Path;
    internal NativeStringView Name;
    internal NativeBytesView Data;
    internal ulong Reserved1;
    internal ulong Reserved2;
    internal ulong Reserved3;
    internal ulong Reserved4;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeAnalysisOptions
{
    internal uint StructSize;
    internal NativeAnalysisFlags Flags;
    internal uint Strategies;
    internal uint Reserved32;
    internal ulong Reserved1;
    internal ulong Reserved2;
    internal ulong Reserved3;
    internal ulong Reserved4;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct NativeClassifyOptions
{
    internal uint StructSize;
    internal uint Flags;
    internal uint AllowedTypes;
    internal uint MaximumBytes;
    internal ulong* CandidateLanguageIds;
    internal nuint CandidateLanguageCount;
    internal ulong Reserved1;
    internal ulong Reserved2;
    internal ulong Reserved3;
    internal ulong Reserved4;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeVersionInfo
{
    internal uint StructSize;
    internal uint AbiMajor;
    internal uint AbiMinor;
    internal uint Reserved32;
    internal NativeStringView WrapperVersion;
    internal NativeStringView RubyVersion;
    internal NativeStringView LinguistVersion;
    internal NativeStringView LinguistRevision;
    internal NativeStringView ClassifierSha256;
    internal ulong Reserved1;
    internal ulong Reserved2;
    internal ulong Reserved3;
    internal ulong Reserved4;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeLanguageInfo
{
    internal uint StructSize;
    internal uint Type;
    internal ulong LanguageId;
    internal ulong GroupLanguageId;
    internal uint Flags;
    internal uint AliasCount;
    internal uint ExtensionCount;
    internal uint InterpreterCount;
    internal uint FilenameCount;
    internal NativeStringView Name;
    internal NativeStringView FileSystemName;
    internal NativeStringView Color;
    internal NativeStringView TextMateScope;
    internal NativeStringView AceMode;
    internal NativeStringView CodeMirrorMode;
    internal NativeStringView CodeMirrorMimeType;
    internal ulong Reserved1;
    internal ulong Reserved2;
    internal ulong Reserved3;
    internal ulong Reserved4;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeStrategyTraceEntry
{
    internal uint StructSize;
    internal uint Strategy;
    internal uint CandidateCount;
    internal uint Reserved32;
    internal ulong Reserved1;
    internal ulong Reserved2;
    internal ulong Reserved3;
    internal ulong Reserved4;
}

internal enum NativeStatus : int
{
    Ok = 0,
    InvalidArgument = 1,
    AbiMismatch = 2,
    Unsupported = 3,
    NotFound = 4,
    InvalidUtf8 = 5,
    RubyException = 6,
    NativeFailure = 7,
    OutOfMemory = 8,
    InternalError = 9,
}

[Flags]
internal enum NativeBlobInputFlags : uint
{
    Symlink = 1 << 0,
    LfsTracked = 1 << 1,
}

[Flags]
internal enum NativeAnalysisFlags : uint
{
    AllowEmpty = 1 << 0,
    IncludeTrace = 1 << 1,
    IncludeLineCounts = 1 << 2,
}

internal enum NativeAnalysisTextField : uint
{
    MimeType = 1,
    ContentType = 2,
    Disposition = 3,
    Encoding = 4,
    RubyEncoding = 5,
    TextMateScope = 6,
}

internal enum NativeLanguageCollection : uint
{
    Aliases = 1,
    Extensions = 2,
    Interpreters = 3,
    Filenames = 4,
}

internal enum NativeLookupKind : uint
{
    Name = 1,
    Alias = 2,
    Filename = 3,
    Extension = 4,
    Interpreter = 5,
}

internal static unsafe partial class NativeMethods
{
    private const string LibraryName = "originary_linguist";

    [LibraryImport(LibraryName, EntryPoint = "ol_abi_version_major")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial uint AbiVersionMajor();

    [LibraryImport(LibraryName, EntryPoint = "ol_abi_version_minor")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial uint AbiVersionMinor();

    [LibraryImport(LibraryName, EntryPoint = "ol_runtime_create")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial NativeStatus RuntimeCreate(NativeRuntimeOptions* options, nint* runtime, nint* error);

    [LibraryImport(LibraryName, EntryPoint = "ol_runtime_release")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void RuntimeRelease(nint runtime);

    [LibraryImport(LibraryName, EntryPoint = "ol_runtime_capabilities")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ulong RuntimeCapabilities(NativeRuntimeHandle runtime);

    [LibraryImport(LibraryName, EntryPoint = "ol_runtime_version")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial NativeStatus RuntimeVersion(NativeRuntimeHandle runtime, NativeVersionInfo* version);

    [LibraryImport(LibraryName, EntryPoint = "ol_runtime_language_count")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial nuint RuntimeLanguageCount(NativeRuntimeHandle runtime);

    [LibraryImport(LibraryName, EntryPoint = "ol_runtime_language_id_at")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial NativeStatus RuntimeLanguageIdAt(NativeRuntimeHandle runtime, nuint index, ulong* languageId);

    [LibraryImport(LibraryName, EntryPoint = "ol_runtime_language_info")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial NativeStatus RuntimeLanguageInfo(NativeRuntimeHandle runtime, ulong languageId, NativeLanguageInfo* info);

    [LibraryImport(LibraryName, EntryPoint = "ol_runtime_language_collection_value")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial NativeStatus RuntimeLanguageCollectionValue(NativeRuntimeHandle runtime, ulong languageId, NativeLanguageCollection collection, nuint index, NativeStringView* value);

    [LibraryImport(LibraryName, EntryPoint = "ol_runtime_lookup_languages")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial NativeStatus RuntimeLookupLanguages(NativeRuntimeHandle runtime, NativeLookupKind kind, NativeStringView value, nint* languages, nint* error);

    [LibraryImport(LibraryName, EntryPoint = "ol_runtime_analyze")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial NativeStatus RuntimeAnalyze(NativeRuntimeHandle runtime, NativeBlobInput* blob, NativeAnalysisOptions* options, nint* analysis, nint* error);

    [LibraryImport(LibraryName, EntryPoint = "ol_runtime_classify")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial NativeStatus RuntimeClassify(NativeRuntimeHandle runtime, NativeBytesView data, NativeClassifyOptions* options, nint* classification, nint* error);

    [LibraryImport(LibraryName, EntryPoint = "ol_analysis_release")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void AnalysisRelease(nint analysis);

    [LibraryImport(LibraryName, EntryPoint = "ol_analysis_language_id")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ulong AnalysisLanguageId(NativeAnalysisHandle analysis);

    [LibraryImport(LibraryName, EntryPoint = "ol_analysis_strategy")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial uint AnalysisStrategy(NativeAnalysisHandle analysis);

    [LibraryImport(LibraryName, EntryPoint = "ol_analysis_flags")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ulong AnalysisFlags(NativeAnalysisHandle analysis);

    [LibraryImport(LibraryName, EntryPoint = "ol_analysis_loc")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ulong AnalysisLoc(NativeAnalysisHandle analysis);

    [LibraryImport(LibraryName, EntryPoint = "ol_analysis_sloc")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ulong AnalysisSloc(NativeAnalysisHandle analysis);

    [LibraryImport(LibraryName, EntryPoint = "ol_analysis_text")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial NativeStatus AnalysisText(NativeAnalysisHandle analysis, NativeAnalysisTextField field, NativeStringView* value);

    [LibraryImport(LibraryName, EntryPoint = "ol_analysis_trace_count")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial nuint AnalysisTraceCount(NativeAnalysisHandle analysis);

    [LibraryImport(LibraryName, EntryPoint = "ol_analysis_trace_entry")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial NativeStatus AnalysisTraceEntry(NativeAnalysisHandle analysis, nuint index, NativeStrategyTraceEntry* entry);

    [LibraryImport(LibraryName, EntryPoint = "ol_analysis_trace_candidate")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial NativeStatus AnalysisTraceCandidate(NativeAnalysisHandle analysis, nuint traceIndex, nuint candidateIndex, ulong* languageId);

    [LibraryImport(LibraryName, EntryPoint = "ol_classification_release")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ClassificationRelease(nint classification);

    [LibraryImport(LibraryName, EntryPoint = "ol_classification_count")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial nuint ClassificationCount(NativeClassificationHandle classification);

    [LibraryImport(LibraryName, EntryPoint = "ol_classification_considered_bytes")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial uint ClassificationConsideredBytes(NativeClassificationHandle classification);

    [LibraryImport(LibraryName, EntryPoint = "ol_classification_result")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial NativeStatus ClassificationResult(NativeClassificationHandle classification, nuint index, ulong* languageId, double* score);

    [LibraryImport(LibraryName, EntryPoint = "ol_language_id_list_release")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void LanguageIdListRelease(nint languages);

    [LibraryImport(LibraryName, EntryPoint = "ol_language_id_list_count")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial nuint LanguageIdListCount(NativeLanguageIdListHandle languages);

    [LibraryImport(LibraryName, EntryPoint = "ol_language_id_list_at")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial NativeStatus LanguageIdListAt(NativeLanguageIdListHandle languages, nuint index, ulong* languageId);

    [LibraryImport(LibraryName, EntryPoint = "ol_error_status")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial NativeStatus ErrorStatus(NativeErrorHandle error);

    [LibraryImport(LibraryName, EntryPoint = "ol_error_message")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial NativeStringView ErrorMessage(NativeErrorHandle error);

    [LibraryImport(LibraryName, EntryPoint = "ol_error_ruby_class")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial NativeStringView ErrorRubyClass(NativeErrorHandle error);

    [LibraryImport(LibraryName, EntryPoint = "ol_error_ruby_backtrace")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial NativeStringView ErrorRubyBacktrace(NativeErrorHandle error);

    [LibraryImport(LibraryName, EntryPoint = "ol_error_release")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ErrorRelease(nint error);
}
