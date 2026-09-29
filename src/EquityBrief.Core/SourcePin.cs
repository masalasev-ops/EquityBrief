using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

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
//
// A line that is a comment and nothing else, and a line holding only white space,
// are left out too, and every other line is read without the white space at its
// ends. A comment, a citation of a decision among them, and the spacing between
// lines are read by a person and never by the code, so moving one leaves every
// answer the pinned code gives where it was, and a pin that moved with them would
// stop a night for a change no answer can show. A comment written after code on
// its line is still pinned, with the code it sits beside.
// see: A comment or a blank line moves no pin, and every other change to a pinned source does
public static class SourcePin
{
    public static string Of(IEnumerable<string> sources, string declaration)
    {
        var lines = sources
            .Select(source => source.Replace("\r\n", "\n", StringComparison.Ordinal).TrimStart('﻿').Split('\n'))
            .Select(source => source.Select(line => line.Trim()).Where(Read).ToArray())
            .ToArray();

        // The whole line and exactly one of it, so nothing written beside the version goes unpinned.
        var declares = new Regex("^\\s*" + Regex.Escape(declaration) + " \"[0-9a-f]{12}\";\\s*$");
        var declared = lines.Sum(source => source.Count(declares.IsMatch));

        if (declared != 1)
        {
            throw new InvalidOperationException(FormattableString.Invariant(
                $"The sources carry {declared} line(s) that declare the version and nothing else, as '{declaration} \"<twelve hex characters>\";', and a pin leaves out exactly one."));
        }

        var pinned = string.Join(
            "\n",
            lines.Select(source => string.Join("\n", source.Where(line => !declares.IsMatch(line)))));

        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(pinned));

        // Twelve hex characters: a pin, not a signature, and the check recomputes
        // it from the files rather than trusting it.
        return Convert.ToHexString(digest)[..12].ToLowerInvariant();
    }

    // Whether a line, its ends already trimmed, is one the pin reads: anything but white space alone
    // and a comment alone.
    static bool Read(string trimmed) => trimmed.Length > 0 && !trimmed.StartsWith("//", StringComparison.Ordinal);
}
