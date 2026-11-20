using System.Text;

namespace TabTabGo.Templating.Liquid.Internal;

/// <summary>A <see cref="TextWriter"/> that keeps at most <c>limit</c> characters (0 = unlimited) and drops the rest (S-10).</summary>
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
        if (_limit == 0 || _buffer.Length < _limit)
        {
            _buffer.Append(value);
        }
    }

    public override void Write(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        if (_limit == 0)
        {
            _buffer.Append(value);
            return;
        }

        var room = (int)Math.Min(value.Length, _limit - _buffer.Length);
        if (room > 0)
        {
            _buffer.Append(value, 0, room);
        }
    }

    public override void Write(char[] buffer, int index, int count) => Write(new string(buffer, index, count));

    public override string ToString() => _buffer.ToString();
}
