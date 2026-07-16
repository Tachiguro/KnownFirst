namespace KnownFirst.Core.Text;

public sealed record TextSpan(int StartPosition, int Length, int Order)
{
    public int EndPosition => StartPosition + Length;
}
