namespace MarkdownFigma
{
    internal enum UpdateAction
    {
        NONE,
        UPDATE_SIMILARITY,
        UPDATE,
        DELETE,
        FIGMA_MISSING,
    }

    internal class UpdateReport
    {
        internal string Name;
        internal string URL;
        internal double Similarity;
        internal UpdateAction Action;
    }
}