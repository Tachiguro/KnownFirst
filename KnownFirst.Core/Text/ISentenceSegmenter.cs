namespace KnownFirst.Core.Text;

public interface ISentenceSegmenter
{
    IReadOnlyList<TextSpan> Segment(string content);
}
