#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using ChatGuard.Core.Text;

namespace ChatGuard.Core.Filtering
{
    /// <summary>An indexed term and its payload, as reported by <see cref="TermIndex{T}.Match"/>.</summary>
    internal sealed class TermHit<T>
    {
        private string? _key;

        public TermHit(string term, T payload)
        {
            Term = term;
            Payload = payload;
        }

        /// <summary>The term as passed to <see cref="TermIndex{T}.TryAdd"/>, before normalization.</summary>
        public string Term { get; }

        public T Payload { get; }

        /// <summary>
        /// The normalized, folded term that category-less allow rules compare against, computed on first use. Hits are
        /// shared between threads, so Volatile.Read and Interlocked.CompareExchange guard the cache: on ARM64 (IL2CPP
        /// or Mono) a plain store could expose a partly built string. A race at worst computes it twice.
        /// </summary>
        public string Key
        {
            get
            {
                string? key = Volatile.Read(ref _key);
                if (key == null)
                {
                    string computed = TextNormalizer.Fold(TextNormalizer.Normalize(Term));
                    key = Interlocked.CompareExchange(ref _key, computed, null) ?? computed;
                }

                return key;
            }
        }
    }

    /// <summary>
    /// Index of whole words, single-word prefixes and phrases. Terms are normalized, tokenized and folded (accents
    /// removed) when added, then matched against the message token forms from <see cref="TokenForms.Candidates"/>.
    /// Build it on one thread; after that, <see cref="Match"/> is safe from any thread.
    /// </summary>
    internal sealed class TermIndex<T>
    {
        public const int MinPrefixLength = 4;
        public const int MaxPhraseTokens = 6;

        private readonly Dictionary<string, List<TermHit<T>>> _exact = new Dictionary<string, List<TermHit<T>>>(StringComparer.Ordinal);
        private readonly Dictionary<char, List<PrefixEntry>> _prefixes = new Dictionary<char, List<PrefixEntry>>();
        private readonly Dictionary<string, List<PhraseEntry>> _phrases = new Dictionary<string, List<PhraseEntry>>(StringComparer.Ordinal);

        public int Count { get; private set; }

        /// <summary>
        /// Adds a word, a phrase of up to <see cref="MaxPhraseTokens"/> words, or, with <paramref name="isPrefix"/>, a
        /// single-word prefix of at least <see cref="MinPrefixLength"/> characters. Returns false with the reason in
        /// <paramref name="error"/> if the term breaks a limit or normalizes to nothing.
        /// </summary>
        public bool TryAdd(string rawTerm, bool isPrefix, T payload, out string? error)
        {
            error = null;
            string[] tokens = TextNormalizer.Tokenize(TextNormalizer.Normalize(rawTerm));
            if (tokens.Length == 0)
            {
                error = "term is empty after normalization";
                return false;
            }

            if (tokens.Length > MaxPhraseTokens)
            {
                error = "phrases may have at most " + MaxPhraseTokens + " words";
                return false;
            }

            for (int i = 0; i < tokens.Length; i++)
            {
                tokens[i] = TextNormalizer.Fold(tokens[i]);
            }

            var hit = new TermHit<T>(rawTerm, payload);
            if (tokens.Length == 1)
            {
                string token = tokens[0];
                if (isPrefix)
                {
                    if (token.Length < MinPrefixLength)
                    {
                        error = "prefix terms must be at least " + MinPrefixLength + " characters";
                        return false;
                    }

                    if (!_prefixes.TryGetValue(token[0], out List<PrefixEntry> list))
                    {
                        list = new List<PrefixEntry>();
                        _prefixes[token[0]] = list;
                    }

                    list.Add(new PrefixEntry(token, hit));
                }
                else
                {
                    if (!_exact.TryGetValue(token, out List<TermHit<T>> list))
                    {
                        list = new List<TermHit<T>>();
                        _exact[token] = list;
                    }

                    list.Add(hit);
                }
            }
            else
            {
                if (isPrefix)
                {
                    error = "prefix terms must be a single word";
                    return false;
                }

                if (!_phrases.TryGetValue(tokens[0], out List<PhraseEntry> list))
                {
                    list = new List<PhraseEntry>();
                    _phrases[tokens[0]] = list;
                }

                list.Add(new PhraseEntry(tokens, hit));
            }

            Count++;
            return true;
        }

        /// <summary>
        /// Appends each term found in <paramref name="candidates"/> to <paramref name="output"/>, in message order.
        /// Duplicates are kept: one entry per match, and a prefix can match both forms of a token.
        /// </summary>
        public void Match(string[][] candidates, List<TermHit<T>> output)
        {
            for (int i = 0; i < candidates.Length; i++)
            {
                string[] forms = candidates[i];
                for (int f = 0; f < forms.Length; f++)
                {
                    string form = forms[f];
                    if (_exact.TryGetValue(form, out List<TermHit<T>> exactHits))
                    {
                        output.AddRange(exactHits);
                    }

                    if (form.Length >= MinPrefixLength && _prefixes.TryGetValue(form[0], out List<PrefixEntry> prefixes))
                    {
                        foreach (PrefixEntry entry in prefixes)
                        {
                            if (form.StartsWith(entry.Prefix, StringComparison.Ordinal))
                            {
                                output.Add(entry.Hit);
                            }
                        }
                    }

                    if (_phrases.TryGetValue(form, out List<PhraseEntry> phrases))
                    {
                        foreach (PhraseEntry phrase in phrases)
                        {
                            if (PhraseMatches(phrase.Tokens, candidates, i))
                            {
                                output.Add(phrase.Hit);
                            }
                        }
                    }
                }
            }
        }

        private static bool PhraseMatches(string[] phrase, string[][] candidates, int start)
        {
            if (start + phrase.Length > candidates.Length)
            {
                return false;
            }

            for (int k = 1; k < phrase.Length; k++)
            {
                if (Array.IndexOf(candidates[start + k], phrase[k]) < 0)
                {
                    return false;
                }
            }

            return true;
        }

        private sealed class PrefixEntry
        {
            public PrefixEntry(string prefix, TermHit<T> hit)
            {
                Prefix = prefix;
                Hit = hit;
            }

            public string Prefix { get; }

            public TermHit<T> Hit { get; }
        }

        private sealed class PhraseEntry
        {
            public PhraseEntry(string[] tokens, TermHit<T> hit)
            {
                Tokens = tokens;
                Hit = hit;
            }

            public string[] Tokens { get; }

            public TermHit<T> Hit { get; }
        }
    }

    /// <summary>Builds the comparison forms of each message token.</summary>
    internal static class TokenForms
    {
        /// <summary>
        /// For each token: its folded form and, when different, that form with every run of a repeated character
        /// collapsed to one ("idiiot" → "idiot").
        /// </summary>
        public static string[][] Candidates(string[] tokens)
        {
            var result = new string[tokens.Length][];
            for (int i = 0; i < tokens.Length; i++)
            {
                string folded = TextNormalizer.Fold(tokens[i]);
                string collapsed = TextNormalizer.CollapseRuns(folded, 1);
                result[i] = collapsed == folded ? new[] { folded } : new[] { folded, collapsed };
            }

            return result;
        }
    }
}
