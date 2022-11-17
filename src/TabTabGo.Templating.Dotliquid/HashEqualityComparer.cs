using DotLiquid;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TabTabGo.Templating.Liquid
{
    public class HashEqualityComparer : IEqualityComparer<Hash>
    {
        public bool Equals(Hash x, Hash y)
        {
            foreach (var key in x.Keys)
            {
                if (!y.ContainsKey(key))
                {
                    return false;
                }

                if (x[key] != y[key])
                {
                    return false;
                }
            }

            foreach (var key in y.Keys)
            {
                if (!x.ContainsKey(key))
                {
                    return false;
                }
            }

            return true;
        }

        public int GetHashCode(Hash obj)
        {
            return obj.GetHashCode();
        }
    }
}
