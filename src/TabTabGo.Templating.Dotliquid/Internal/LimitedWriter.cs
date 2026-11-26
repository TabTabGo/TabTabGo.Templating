using System.Text;

namespace TabTabGo.Templating.Liquid.Internal;

/// <summary>
/// A <see cref="TextWriter"/> that collects at most <c>limit</c> characters (0 = unlimited) and throws
/// <see cref="RenderLimitExceededException"/> on the first character beyond it (S-10). It backs the render output and the <c>capture</c> tag.
/// </summary>
internal sealed class LimitedWriter : TextWriter
{
    private readonly StringBuilder _buffer = new();
    private readonly long _limit;

    public LimitedWriter(long limit, IFormatProvider formatProvider)
        : base(formatProvider)
    {
        _limit = limit;
    }

    public override Encoding Encoding => Encoding.Unicode;

    public override void Write(char value)
    {
        Reserve(1);
        _buffer.Append(value);
    }

    public override void Write(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        Reserve(value.Length);
        _buffer.Append(value);
    }

    public override void Write(char[] buffer, int index, int count)
    {
        Reserve(count);
        _buffer.Append(buffer, index, count);
    }

    public override void Write(ReadOnlySpan<char> buffer)
    {
        Reserve(buffer.Length);
        _buffer.Append(buffer);
    }

    public override string ToString() => _buffer.ToString();

    private void Reserve(int count)
    {
        if (_limit > 0 && _buffer.Length + (long)count > _limit)
        {
            throw RenderBudget.Exceeded(_limit);
        }
    }
}
