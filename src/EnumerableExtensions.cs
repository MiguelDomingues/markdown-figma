using System;
using System.Linq;
using System.Collections.Generic;

namespace MarkdownFigma
{
    public static class EnumerableExtensions
    {

        // Based on https://stackoverflow.com/a/41608973
        public static IEnumerable<T> SelectRecursive<T>(this IEnumerable<T> source, Func<T, IEnumerable<T>> selector)
        {
            foreach (var parent in source)
            {
                yield return parent;

                var children = selector(parent);
                if (children != null && children.Any())
                    foreach (var child in SelectRecursive(children, selector))
                        yield return child;
            }
        }

    }
}