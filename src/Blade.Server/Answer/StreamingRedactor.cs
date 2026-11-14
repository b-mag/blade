using System.Text;

namespace Blade.Server.Answer;

/// <summary>
/// Turns raw model chunks into text that is safe to show while the answer is still being written.
/// Only complete words are released, and each release is PII-redacted, so a licence or SSN number can
/// never reach the screen partially typed before the redactor recognises it. The PII patterns contain
/// no whitespace, so redacting a whitespace-terminated prefix gives the same result as redacting the
/// whole answer. The final, guard-checked answer replaces the streamed text when the stream ends.
/// </summary>
public sealed class StreamingRedactor
{
    private readonly StringBuilder _raw = new();
    private string _sent = "";

    /// <summary>Adds a raw chunk; returns the new safe text to send, or an empty string if nothing is ready.</summary>
    public string Push(string chunk)
    {
        _raw.Append(chunk);
        var raw = _raw.ToString();
        var cut = LastWhitespace(raw);
        return cut < 0 ? "" : Release(raw[..(cut + 1)]);
    }

    /// <summary>Releases whatever is left once the model has finished.</summary>
    public string Flush() => Release(_raw.ToString());

    /// <summary>Starts over (the server is regenerating the answer).</summary>
    public void Reset()
    {
        _raw.Clear();
        _sent = "";
    }

    private string Release(string stableRaw)
    {
        var (safe, _) = PiiRedactor.Redact(stableRaw);
        // Redacting a longer prefix only ever extends the previous result; if it ever didn't, hold back
        // rather than send something inconsistent. The final answer corrects the display either way.
        if (safe.Length <= _sent.Length || !safe.StartsWith(_sent, StringComparison.Ordinal)) return "";
        var delta = safe[_sent.Length..];
        _sent = safe;
        return delta;
    }

    private static int LastWhitespace(string s)
    {
        for (var i = s.Length - 1; i >= 0; i--)
            if (char.IsWhiteSpace(s[i])) return i;
        return -1;
    }
}
