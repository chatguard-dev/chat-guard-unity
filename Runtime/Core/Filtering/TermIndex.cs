#nullable enable
using System;
using System.Collections.Generic;
using ChatGuard.Core.Text;

namespace ChatGuard.Core.Filtering
{
    /// <summary>A term that matched, with the payload attached to it when it was indexed.</summary>
    internal sealed class TermHit<T>
    {
        public TermHit(string term, T payload)
        {
            Term = term;
            Payload = payload;
        }

        /// <summary>The term as written in the source list (not normalized).</summary>
        public string Term { get; }

        public T Payload { get; }
    }

    /// <summary>
    /// Word/phrase/prefix index over folded tokens. Terms are normalized with
    /// <see cref="TextNormalizer"/> and folded with <see cref="TextNormalizer.Fold"/> at insert time;
    /// message tokens are matched through <see cref="TokenForms.Candidates"/> (folded and run-collapsed).
    /// </summary>
    internal sealed class TermIndex<T>
    {
        public const int MinPrefixLength = 4;
        public const int MaxPhraseTokens = 6;

        private readonly Dictionary<string, List<TermHit<T>>> _exact = new Dictionary<string, List<TermHit<T>>>(StringComparer.Ordinal);
        private readonly Dictionary<char, List<PrefixEntry>> _prefixes = new Dictionary<char, List<PrefixEntry>>();
        private readonly Dictionary<string, List<PhraseEntry>> _phrases = new Dictionary<string, List<PhraseEntry>>(StringComparer.Ordinal);

        public int Count { get; private set; }

        /// <summary>Adds a term. Returns false with an error when the term is unusable.</summary>
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

        /// <summary>Appends every hit found in the candidate token forms to <paramref name="output"/>.</summary>
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
        /// <summary>For each token: its folded form and, when different, the folded form with runs collapsed.</summary>
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
