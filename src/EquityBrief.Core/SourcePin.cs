using System.Security.Cryptography;
using System.Text;

namespace EquityBrief.Core;

// A pin of source text: the hash a version constant is held to, so a change to
// the code a stored row names is a thing a check can see rather than a thing
// somebody remembers to record.
//
// Normalised before hashing in the two ways a checkout can differ from the bytes
// that were committed without anything in the file having changed: a carriage
// return before every newline, which is what a Windows checkout of an LF
// repository can produce, and a leading byte order mark, which an editor can add
// on a save. Without both, the pin of untouched code would depend on which
// machine cloned the repository.
//
// The line that declares the version is left out, because a hash of a file
// including its own hash could never be written down: putting the computed value
// into the file changes the file and so changes the value.
public static class SourcePin
{
    public static string Of(IEnumerable<string> sources, string declaration)
    {
        var pinned = string.Join(
            "\n",
            sources.Select(source => string.Join(
                "\n",
                source.Replace("\r\n", "\n", StringComparison.Ordinal)
                    .TrimStart('﻿')
                    .Split('\n')
                    .Where(line => !line.TrimStart().StartsWith(declaration, StringComparison.Ordinal)))));

        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(pinned));

        // Twelve hex characters: a pin, not a signature, and the check recomputes
        // it from the files rather than trusting it.
        return Convert.ToHexString(digest)[..12].ToLowerInvariant();
    }
}
