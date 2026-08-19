namespace Winfred;

/// <summary>
/// Subsequence matching in the spirit of Alfred/fzf: every query character must appear
/// in order, and matches score higher when they land on word boundaries, run
/// consecutively, or start the text.
/// </summary>
public static class FuzzyMatcher
{
    public const double NoMatch = 0;

    /// <summary>Scores <paramref name="query"/> against <paramref name="text"/>; 0 means no match.</summary>
    public static double Score(string text, string query)
    {
        if (query.Length == 0) return 1;
        if (text.Length == 0 || query.Length > text.Length) return NoMatch;

        // Fast exact paths — these dominate everything the fuzzy walk can produce.
        int exact = text.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (exact == 0)
            return text.Length == query.Length ? 200 : 140 - LengthPenalty(text);
        if (exact > 0)
        {
            double bonus = IsBoundary(text, exact) ? 25 : 0;
            return 95 + bonus - LengthPenalty(text) - Math.Min(10, exact * 0.3);
        }

        return Subsequence(text, query);
    }

    /// <summary>Best score across several fields (title, path, url…), each with a weight.</summary>
    public static double ScoreAny(string query, params (string Text, double Weight)[] fields)
    {
        double best = NoMatch;
        foreach (var (text, weight) in fields)
        {
            if (string.IsNullOrEmpty(text)) continue;
            double score = Score(text, query) * weight;
            if (score > best) best = score;
        }
        return best;
    }

    /// <summary>Every whitespace-separated term must match somewhere; scores are averaged.</summary>
    public static double ScoreTerms(string text, string[] terms)
    {
        if (terms.Length == 0) return 1;
        double total = 0;
        foreach (var term in terms)
        {
            double score = Score(text, term);
            if (score <= NoMatch) return NoMatch;
            total += score;
        }
        return total / terms.Length;
    }

    private static double Subsequence(string text, string query)
    {
        double score = 0;
        int textIndex = 0;
        int consecutive = 0;
        int firstMatch = -1;
        bool allOnBoundaries = true;

        foreach (char rawQueryChar in query)
        {
            char queryChar = char.ToLowerInvariant(rawQueryChar);
            int found = -1;
            for (int i = textIndex; i < text.Length; i++)
            {
                if (char.ToLowerInvariant(text[i]) != queryChar) continue;
                found = i;
                break;
            }
            if (found < 0) return NoMatch;

            if (firstMatch < 0) firstMatch = found;
            bool boundary = IsBoundary(text, found);
            if (!boundary) allOnBoundaries = false;

            if (found == textIndex && textIndex > 0)
            {
                consecutive++;
                score += 8 + Math.Min(consecutive * 3, 15);
            }
            else
            {
                consecutive = 0;
                score += boundary ? 14 : 3;
            }
            textIndex = found + 1;
        }

        // Reject matches smeared across the string — "winfred" should not match a 78-character
        // song title just because those letters appear in order somewhere inside it. An
        // initials match ("wf" → "Wire Frames.pdf") is exempt: every hit starts a word.
        int span = textIndex - firstMatch;
        if (!allOnBoundaries && span > 0 && (double)query.Length / span < MinDensity)
            return NoMatch;

        if (firstMatch == 0) score += 12;
        score += 10.0 * query.Length / text.Length; // prefer tight matches
        return Math.Max(1, score - LengthPenalty(text));
    }

    /// <summary>How much of the span a non-acronym match must actually cover.</summary>
    private const double MinDensity = 0.4;

    /// <summary>True when the character starts a word: first char, after a separator, or a camelCase hump.</summary>
    private static bool IsBoundary(string text, int index)
    {
        if (index <= 0) return true;
        char previous = text[index - 1];
        if (previous is ' ' or '-' or '_' or '.' or '/' or '\\' or '(' or '[' or ':' or ',' or '@') return true;
        return char.IsLower(previous) && char.IsUpper(text[index]);
    }

    private static double LengthPenalty(string text) => Math.Min(12, text.Length * 0.06);
}
