using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DotLiquid;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace TabTabGo.Templating.Liquid.Filters
{
    public static class JObjectFilters
    {
        public static JToken JSelectTokenByProperty(JToken jObject, string property , string rootPath = "$.")
        {
            return jObject.SelectToken(rootPath + property);
        }
        public static JToken JSelectToken(JToken jObject, string path)
        {
            return jObject.SelectToken(path);
        }

        public static IEnumerable<object> JSelectTokenValuesByProperty(JToken jObject, string rootPath, string property)
        {
            //var jObject = ConvertToJObject(json);
            var result = jObject.SelectTokens(rootPath + property).Where(j => j.Type != JTokenType.Array).Select(j => j.Value<object>()).ToList();

            return result;
        }
        public static IEnumerable<object> JSelectTokenValues(JToken jObject, string path)
        {
            //var jObject = ConvertToJObject(json);
            var result = jObject.SelectTokens(path).Where(j => j.Type != JTokenType.Array).Select(j => j.Value<object>()).ToList();

            return result;
        }

        public static object JSelectTokenValue(JToken jObject, string path)
        {
            var token = jObject.SelectToken(path);
            if (token != null && token.HasValues)
            {
                return token.Value<object>();
            }
            return null;
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
