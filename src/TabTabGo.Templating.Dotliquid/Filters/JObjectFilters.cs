using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using DotLiquid;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace TabTabGo.Templating.Liquid.Filters
{
    public static class JObjectFilters
    {
        // JSONPath filter expressions can contain regular expressions (@.n =~ /.../); Json.NET runs them without a time-out
        // unless told otherwise, so every path evaluation uses the configured RegexTimeout (S-8). A timed-out path matches nothing.
        private static JsonSelectSettings SelectSettings() => new JsonSelectSettings
        {
            RegexMatchTimeout = RenderScope.CurrentOptions.RegexTimeout
        };

        public static JToken JSelectTokenByProperty(JToken jObject, string property , string rootPath = "$.")
        {
            return SelectTokenSafe(jObject, rootPath + property);
        }

        public static JToken JSelectToken(JToken jObject, string path)
        {
            return SelectTokenSafe(jObject, path);
        }

        public static IEnumerable<object> JSelectTokenValuesByProperty(JToken jObject, string rootPath, string property)
        {
            return SelectValuesSafe(jObject, rootPath + property);
        }

        public static IEnumerable<object> JSelectTokenValues(JToken jObject, string path)
        {
            return SelectValuesSafe(jObject, path);
        }

        public static object JSelectTokenValue(JToken jObject, string path)
        {
            var token = SelectTokenSafe(jObject, path);
            if (token != null && token.HasValues)
            {
                return token.Value<object>();
            }
            return null;
        }

        private static JToken SelectTokenSafe(JToken jObject, string path)
        {
            try
            {
                return jObject.SelectToken(path, SelectSettings());
            }
            catch (RegexMatchTimeoutException)
            {
                return null;
            }
        }

        private static List<object> SelectValuesSafe(JToken jObject, string path)
        {
            try
            {
                return jObject.SelectTokens(path, SelectSettings()).Where(j => j.Type != JTokenType.Array).Select(j => j.Value<object>()).ToList();
            }
            catch (RegexMatchTimeoutException)
            {
                return new List<object>();
            }
        }

        public static string EscapeJsonString(string input)
        {
            return JsonConvert.ToString(input);
        }
        public  static JObject ConvertToJObject(string json)
        {
            return  JObject.Parse(json);
        }

        
    }
}
