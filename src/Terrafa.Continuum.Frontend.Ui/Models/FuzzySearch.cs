// Copyright (c) 2026 Terrafa Limited. All rights reserved.

namespace Terrafa.Continuum.Frontend.Models;

/// <summary>Subsequence matcher — every query character must appear in order, runs score higher.</summary>
public static class FuzzySearch
{
    public static bool TryMatch(string candidate, string query, out int score)
    {
        score = 0;
        if (query.Length == 0) return true;

        var searchFrom = 0;
        var lastIndex = -2;
        var streak = 0;

        foreach (var character in query)
        {
            if (character == ' ') continue;
            var index = candidate.IndexOf(character.ToString(), searchFrom, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                score = 0;
                return false;
            }

            streak = index == lastIndex + 1 ? streak + 1 : 0;
            score += 10 + streak * 6 - Math.Min(index - lastIndex - 1, 6);
            if (index == 0) score += 12;
            else if (!char.IsLetterOrDigit(candidate[index - 1])) score += 8;

            lastIndex = index;
            searchFrom = index + 1;
        }

        score += Math.Max(0, 24 - candidate.Length);
        return true;
    }
}
