using System;
using System.Collections.Generic;

namespace SaveState.Generator
{
    /// <summary>Helpers shared by the generator's models for value equality over their issue lists.</summary>
    internal static class Models
    {
        public static bool IssuesEqual(List<IssueInfo> left, List<IssueInfo> right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }

            if (left.Count != right.Count)
            {
                return false;
            }

            for (int i = 0; i < left.Count; i++)
            {
                if (!left[i].Equals(right[i]))
                {
                    return false;
                }
            }

            return true;
        }

        public static int IssuesHash(List<IssueInfo> issues)
        {
            unchecked
            {
                int hash = 17;
                for (int i = 0; i < issues.Count; i++)
                {
                    hash = (hash * 31) + issues[i].GetHashCode();
                }

                return hash;
            }
        }

        public static bool StringEquals(string? left, string? right)
        {
            return string.Equals(left, right, StringComparison.Ordinal);
        }
    }
}
