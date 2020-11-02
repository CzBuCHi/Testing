using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;

namespace DokanNetTesting
{
    internal static class Extensions
    {
        [NotNull]
        public static IEnumerable<TResult> FullOuterJoin<TA, TB, TKey, TResult>(
            [NotNull] this IEnumerable<TA> a,
            [NotNull] IEnumerable<TB> b,
            [NotNull] Func<TA, TKey> selectKeyA,
            [NotNull] Func<TB, TKey> selectKeyB,
            [NotNull] Func<TA, TB, TKey, TResult> projection,
            TA defaultA = default,
            TB defaultB = default,
            IEqualityComparer<TKey> cmp = null) {

            cmp = cmp ?? EqualityComparer<TKey>.Default;
            ILookup<TKey, TA> alookup = a.ToLookup(selectKeyA, cmp);
            ILookup<TKey, TB> blookup = b.ToLookup(selectKeyB, cmp);

            HashSet<TKey> keys = new HashSet<TKey>(alookup.Select(p => p.Key), cmp);
            keys.UnionWith(blookup.Select(p => p.Key));

            return
                from key in keys
                from xa in alookup[key].DefaultIfEmpty(defaultA)
                from xb in blookup[key].DefaultIfEmpty(defaultB)
                select projection(xa, xb, key);
        }
    }
}
