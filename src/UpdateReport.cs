namespace MarkdownFigma
{
    internal enum UpdateAction
    {
        NONE,
        UPDATE_SIMILARITY,
        UPDATE,
        DELETE,
        FIGMA_MISSING,
        UNUSED,
    }

    internal class UpdateReport
    {
        internal string Name;
        internal string URL;
        internal double Similarity;
        internal UpdateAction Action;
    }
}