using System.Buffers;

namespace MBW.GHLinguist.Classification;

/// <summary>Receives tokens from <see cref="LinguistTokenizer" /> in the order Linguist produces them.</summary>
internal interface ILinguistTokenSink
{
    void Add(scoped ReadOnlySpan<byte> token);
}

/// <summary>Managed port of Linguist's flex tokenizer (<c>ext/linguist/tokenizer.l</c>).</summary>
/// <remarks>
/// This is a line-by-line transliteration of the scanner flex generated (<c>lex.linguist_yy.c</c>), driven by the
/// same DFA tables, so it produces byte-identical tokens, including flex's end-of-buffer, NUL and back-up handling.
/// Keep the structure aligned with the generated C when editing; the control flow mirrors its labels and gotos.
/// </remarks>
/// <seealso href="https://github.com/github-linguist/linguist/blob/196b2a14418cab005065c72c9759370934c184bc/ext/linguist/tokenizer.l" />
internal static class LinguistTokenizer
{
    /// <summary>Linguist's <c>extract_tokens</c> ignores input beyond this many bytes.</summary>
    internal const int MaximumInputBytes = 100_000;

    private const int MaximumTokenLength = 16;
    private const int StateInitial = 0;
    private const int StateCComment = 1;
    private const int StateXmlComment = 2;
    private const int StateHaskellComment = 3;
    private const int StateOcamlComment = 4;
    private const int StatePythonDoubleComment = 5;
    private const int StatePythonSingleComment = 6;
    private const int StateRoffComment = 7;
    private const int StateLeanComment = 8;

    internal static void Tokenize<TSink>(ReadOnlySpan<byte> data, ref TSink sink)
        where TSink : ILinguistTokenSink, allows ref struct
    {
        int length = Math.Min(data.Length, MaximumInputBytes);

        // yy_scan_bytes copies the input and appends two end-of-buffer NULs; the scanner writes into the copy.
        byte[] buffer = ArrayPool<byte>.Shared.Rent(length + 2);
        try
        {
            data[..length].CopyTo(buffer);
            buffer[length] = 0;
            buffer[length + 1] = 0;
            Scanner scanner = new(buffer, length);
            while (scanner.Lex(ref sink))
            {
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private ref struct Scanner
    {
        private readonly byte[] _buffer;
        private readonly int _charCount;
        private int _cBufP;
        private byte _holdChar;
        private int _textPtr;
        private int _start;
        private int _atBol;
        private int _lastAcceptingState;
        private int _lastAcceptingCpos;

        internal Scanner(byte[] buffer, int charCount)
        {
            _buffer = buffer;
            _charCount = charCount;
            _cBufP = 0;
            _holdChar = buffer[0];
            _textPtr = 0;
            _start = 1;
            _atBol = 1;
        }

        /// <summary>One <c>linguist_yylex</c> call: returns <see langword="false" /> where flex returns 0.</summary>
        internal bool Lex<TSink>(ref TSink sink)
            where TSink : ILinguistTokenSink, allows ref struct
        {
            ReadOnlySpan<short> accept = LinguistTokenizerTables.Accept;
            byte[] buffer = _buffer;

            while (true)
            {
                int cp = _cBufP;
                buffer[cp] = _holdChar;
                int bp = cp;
                int state = _start + _atBol;
                int action;

            Match:
                do
                {
                    int c = LinguistTokenizerTables.EquivalenceClasses[buffer[cp]];
                    if (accept[state] != 0)
                    {
                        _lastAcceptingState = state;
                        _lastAcceptingCpos = cp;
                    }

                    state = Transition(state, c);
                    ++cp;
                }
                while (state != LinguistTokenizerTables.JamState);

                cp = _lastAcceptingCpos;
                state = _lastAcceptingState;

            FindAction:
                action = accept[state];

                // YY_DO_BEFORE_ACTION
                _textPtr = bp;
                int leng = cp - bp;
                _holdChar = buffer[cp];
                buffer[cp] = 0;
                _cBufP = cp;

            DoAction:
                if (action is > 0 and < LinguistTokenizerTables.EndOfBufferAction && leng > 0)
                {
                    // YY_RULE_SETUP
                    _atBol = buffer[_textPtr + leng - 1] == (byte)'\n' ? 1 : 0;
                }

                switch (action)
                {
                    case 0:
                        // Must back up: undo YY_DO_BEFORE_ACTION.
                        buffer[cp] = _holdChar;
                        cp = _lastAcceptingCpos;
                        state = _lastAcceptingState;
                        goto FindAction;
                    case 1:
                    {
                        int off = StrRChr(_textPtr, (byte)' ');
                        off = off < 0 ? _textPtr : off + 1;
                        FeedShebang(off, ref sink);
                        return EatUntilEndOfLine();
                    }
                    case 2:
                    {
                        int off = StrRChr(_textPtr, (byte)'/');
                        off = off < 0 ? _textPtr : off + 1;
                        if (IsCString(off, "env"u8))
                        {
                            if (!EatUntilEndOfLine())
                            {
                                return false;
                            }

                            break;
                        }

                        FeedShebang(off, ref sink);
                        return EatUntilEndOfLine();
                    }
                    case 3: sink.Add("COMMENT#"u8); return true;
                    case 4: sink.Add("COMMENT//!"u8); return true;
                    case 5: sink.Add("COMMENT//"u8); return true;
                    case 6: sink.Add("COMMENT--"u8); return true;
                    case 7: sink.Add("COMMENT%"u8); return true;
                    case 8: sink.Add("COMMENT\""u8); return true;
                    case 9: sink.Add("COMMENT;"u8); return true;
                    case 10: sink.Add("COMMENT.\\\""u8); return true;
                    case 11: sink.Add("COMMENT'\\\""u8); return true;
                    case 12: sink.Add("COMMENT$!"u8); return true;
                    case 13: sink.Add("COMMENT/*"u8); return true;
                    case 14: sink.Add("COMMENT/**"u8); Begin(StateCComment); return true;
                    case 15: sink.Add("COMMENT/*!"u8); Begin(StateCComment); return true;
                    case 16: sink.Add("COMMENT/*"u8); Begin(StateCComment); return true;
                    case 17: sink.Add("COMMENT<!--"u8); Begin(StateXmlComment); return true;
                    case 18: sink.Add("COMMENT{-"u8); Begin(StateHaskellComment); return true;
                    case 19: sink.Add("COMMENT(*"u8); Begin(StateOcamlComment); return true;
                    case 20: sink.Add("COMMENT\"\"\""u8); Begin(StatePythonDoubleComment); return true;
                    case 21: sink.Add("COMMENT'''"u8); Begin(StatePythonSingleComment); return true;
                    case 22: sink.Add("COMMENT.ig"u8); Begin(StateRoffComment); return true;
                    case 23: sink.Add("COMMENT/-"u8); Begin(StateLeanComment); return true;
                    case 24: sink.Add("COMMENT/-"u8); Begin(StateLeanComment); return true;
                    case 25 or 34 or 37 or 81: break;
                    case >= 26 and <= 33: Begin(StateInitial); break;
                    case 35:
                        if (!EatUntilUnescaped((byte)'"'))
                        {
                            return false;
                        }

                        break;
                    case 36:
                        if (!EatUntilUnescaped((byte)'\''))
                        {
                            return false;
                        }

                        break;
                    case >= 38 and <= 80:
                        sink.Add(buffer.AsSpan(_textPtr, Math.Min(leng, MaximumTokenLength)));
                        return true;
                    case 82: throw new InvalidOperationException("flex scanner jammed");
                    case LinguistTokenizerTables.EndOfBufferAction:
                    {
                        int matchedText = cp - _textPtr - 1;
                        buffer[cp] = _holdChar;
                        if (_cBufP <= _charCount)
                        {
                            // This was really a NUL.
                            _cBufP = _textPtr + matchedText;
                            state = GetPreviousState();
                            int nextState = TryNulTransition(state);
                            bp = _textPtr;
                            if (nextState != 0)
                            {
                                cp = ++_cBufP;
                                state = nextState;
                                goto Match;
                            }

                            cp = _lastAcceptingCpos;
                            state = _lastAcceptingState;
                            goto FindAction;
                        }

                        // yy_get_next_buffer for a scan_bytes buffer, which is never refilled.
                        if (_cBufP - _textPtr == 1)
                        {
                            // Only the end-of-buffer was matched: end of file. yywrap returns 1.
                            _cBufP = _textPtr;
                            action = LinguistTokenizerTables.EndOfBufferAction + ((_start - 1) / 2) + 1;
                            leng = 0;
                            goto DoAction;
                        }

                        // Text was matched before the end-of-buffer; process it first.
                        _cBufP = _charCount;
                        state = GetPreviousState();
                        cp = _cBufP;
                        bp = _textPtr;
                        goto FindAction;
                    }
                    case > LinguistTokenizerTables.EndOfBufferAction and <= LinguistTokenizerTables.EndOfBufferAction + 1 + StateLeanComment:
                        // YY_STATE_EOF for any start condition: yyterminate().
                        return false;
                    default:
                        throw new InvalidOperationException("fatal flex scanner internal error--no action found");
                }
            }
        }

        private void Begin(int startCondition) => _start = 1 + (2 * startCondition);

        private static int Transition(int state, int c)
        {
            ReadOnlySpan<short> check = LinguistTokenizerTables.Check;
            ReadOnlySpan<short> stateBase = LinguistTokenizerTables.Base;
            while (check[stateBase[state] + c] != state)
            {
                state = LinguistTokenizerTables.Default[state];
                if (state >= LinguistTokenizerTables.MetaThreshold)
                {
                    c = LinguistTokenizerTables.Meta[c];
                }
            }

            return LinguistTokenizerTables.Next[stateBase[state] + c];
        }

        private int GetPreviousState()
        {
            int state = _start + _atBol;
            for (int cp = _textPtr; cp < _cBufP; ++cp)
            {
                byte value = _buffer[cp];
                int c = value != 0 ? LinguistTokenizerTables.EquivalenceClasses[value] : 1;
                if (LinguistTokenizerTables.Accept[state] != 0)
                {
                    _lastAcceptingState = state;
                    _lastAcceptingCpos = cp;
                }

                state = Transition(state, c);
            }

            return state;
        }

        private int TryNulTransition(int state)
        {
            if (LinguistTokenizerTables.Accept[state] != 0)
            {
                _lastAcceptingState = state;
                _lastAcceptingCpos = _cBufP;
            }

            state = Transition(state, 1);
            return state == LinguistTokenizerTables.JamState ? 0 : state;
        }

        /// <summary>flex <c>input()</c>; returns 0 at end of input, as yywrap returns 1.</summary>
        private int Input()
        {
            byte[] buffer = _buffer;
            buffer[_cBufP] = _holdChar;
            if (buffer[_cBufP] == 0 && _cBufP >= _charCount)
            {
                // Need more input, but a scan_bytes buffer has none: every caller stops scanning on 0.
                return 0;
            }

            int c = buffer[_cBufP];
            buffer[_cBufP] = 0;
            _holdChar = buffer[++_cBufP];
            _atBol = c == '\n' ? 1 : 0;
            return c;
        }

        /// <summary><c>eat_until_eol()</c>; returns <see langword="false" /> where the macro returns 0 from yylex.</summary>
        private bool EatUntilEndOfLine()
        {
            int c;
            while ((c = Input()) != '\n' && c != 0)
            {
            }

            return c != 0;
        }

        /// <summary><c>eat_until_unescaped(q)</c>; returns <see langword="false" /> where the macro returns 0 from yylex.</summary>
        private bool EatUntilUnescaped(byte quote)
        {
            int c;
            while ((c = Input()) != 0)
            {
                if (c == '\n')
                {
                    break;
                }

                if (c == '\\')
                {
                    c = Input();
                    if (c == 0)
                    {
                        return false;
                    }
                }
                else if (c == quote)
                {
                    break;
                }
            }

            return c != 0;
        }

        private readonly void FeedShebang<TSink>(int offset, ref TSink sink)
            where TSink : ILinguistTokenSink, allows ref struct
        {
            ReadOnlySpan<byte> prefix = "SHEBANG#!"u8;
            int length = Math.Min(StrLen(offset), MaximumTokenLength);
            Span<byte> token = stackalloc byte[prefix.Length + MaximumTokenLength];
            prefix.CopyTo(token);
            _buffer.AsSpan(offset, length).CopyTo(token[prefix.Length..]);
            sink.Add(token[..(prefix.Length + length)]);
        }

        private readonly int StrLen(int offset) => _buffer.AsSpan(offset).IndexOf((byte)0);

        private readonly int StrRChr(int offset, byte value)
        {
            int index = _buffer.AsSpan(offset, StrLen(offset)).LastIndexOf(value);
            return index < 0 ? -1 : offset + index;
        }

        private readonly bool IsCString(int offset, ReadOnlySpan<byte> value) =>
            _buffer.AsSpan(offset, StrLen(offset)).SequenceEqual(value);
    }
}
