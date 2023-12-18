using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TabTabGo.Templating.Liquid.Extensions;

namespace TabTabGo.Templating.Liquid.Filters
{
    public class CustomFilters
    {
        public static string CountryName(string countryCode)
        {
            if (string.IsNullOrEmpty(countryCode))
            {
                return countryCode;
            }

            RegionInfo region = new RegionInfo(countryCode);
            string countryName = region.EnglishName;
            return countryName;
        }
    }
}
