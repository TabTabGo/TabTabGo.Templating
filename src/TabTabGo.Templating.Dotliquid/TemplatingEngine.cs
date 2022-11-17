using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using DotLiquid;
using System;
using System.Globalization;
using DotLiquid.FileSystems;
using Newtonsoft.Json.Linq;
using TabTabGo.Templating.Liquid.Filters;
using TabTabGo.Templating.Liquid.Tags;
using System.Runtime.CompilerServices;
using TabTabGo.Core;

namespace TabTabGo.Templating.Liquid
{
    public class TemplatingEngine : TabTabGo.Templating.TemplatingEngine
    {
        private static object _lock = new object();
        private static bool _isIinitialized = false;
        private readonly Dictionary<int, Dictionary<object, object>> _hashCache = new Dictionary<int, Dictionary<object, object>>();
        private static readonly Dictionary<string, Template> _templateCache = new Dictionary<string, Template>();
        public override string EngineName => "Liquid";
        public void Initialize()
        {
            lock (_lock)
            {
                if (!_isIinitialized)
                {
                    Template.NamingConvention = new DotLiquid.NamingConventions.CSharpNamingConvention();
                    DotLiquid.Liquid.UseRubyDateFormat = false;

                    Template.RegisterFilter(typeof(CollectionFilters));
                    Template.RegisterFilter(typeof(ValueFilters));
                    Template.RegisterFilter(typeof(JObjectFilters));
                    Template.RegisterFilter(typeof(OpenXmlFilters));
                    Template.RegisterSafeType(typeof(JToken), new string[] { });
                    Template.RegisterTag<Increment>("increment");

                    _isIinitialized = true;
                }
            }
        }

        public override bool IsCached(string templatePath)
        {
            return _templateCache.ContainsKey(templatePath);
        }

        public override void ParseTemplate(string templatePath)
        {
            if (!_templateCache.ContainsKey(templatePath))
            {
                if (!File.Exists(templatePath))
                {
                    throw new Exception($"File {templatePath} doesn't exist!");
                }

                lock (_lock)
                {
                    var template = Template.Parse(File.ReadAllText(templatePath));
                    _templateCache[templatePath] = template;
                }
            }
        }

        public override object Render(string templatePath, object sourceData, string culture ="en-Us")
        {
            Initialize();

            ParseTemplate(templatePath);

            var hash = HashIt(sourceData);

            Template.FileSystem = new EnhancedLocalFileSystem(Path.GetDirectoryName(templatePath));
            var parameters = new RenderParameters(new CultureInfo(culture))
            {
                LocalVariables = Hash.FromDictionary(hash)
            };

            var result = _templateCache[templatePath].Render(parameters);

            return result;
        }

        public override void RegisterFilter(Type type)
        {
            Template.RegisterFilter(type);
        }

        private Hash HashIt(object obj)
        {
            if (!_hashCache.ContainsKey(Thread.CurrentThread.ManagedThreadId))
            {
                _hashCache[Thread.CurrentThread.ManagedThreadId] = new Dictionary<object, object>();
            }

            var result = HashItInternal(obj);

            _hashCache[Thread.CurrentThread.ManagedThreadId].Clear();
            _hashCache[Thread.CurrentThread.ManagedThreadId] = null;
            _hashCache.Remove(Thread.CurrentThread.ManagedThreadId);

            return (Hash)result;
        }

        private object HashItInternal(object obj, Hash parentHash = null, string propertyName = null)
        {
            if (obj == null) return null;

            if (_hashCache[Thread.CurrentThread.ManagedThreadId].ContainsKey(obj))
            {
                return _hashCache[Thread.CurrentThread.ManagedThreadId][obj];
            }

            if (obj.GetType() == typeof(JObject))
            {
                if (!string.IsNullOrEmpty(propertyName) && parentHash != null)
                {
                    parentHash[propertyName + "Json"] = obj;
                }

                return HashJObject(obj as JObject);
            }
            else if (obj.GetType() == typeof(JArray))
            {
                if (!string.IsNullOrEmpty(propertyName) && parentHash != null)
                {
                    parentHash[propertyName + "Json"] = obj;
                }

                return HashJArray(obj as JArray);
            }
            else if (IsEnumerableType(obj.GetType()) || IsCollectionType(obj.GetType()))
            {
                int i = 0;
                var collection = new List<object>();

                _hashCache[Thread.CurrentThread.ManagedThreadId][obj] = collection;

                foreach (var item in (IEnumerable)obj)
                {
                    var isHashable = IsHashable(item.GetType());
                    var hashedValue = isHashable ? item : HashItInternal(item);

                    collection.Add(hashedValue);
                    i++;
                }

                return collection;
            }
            else
            {
                var result = new Hash();

                _hashCache[Thread.CurrentThread.ManagedThreadId][obj] = result;

                foreach (PropertyInfo property in obj.GetType().GetProperties())
                {
                    var val = property.GetValue(obj, null);
                    var memberName = Template.NamingConvention.GetMemberName(property.Name);
                    var isHashable = IsHashable(property.PropertyType);
                    var hashedValue = isHashable ? val : HashItInternal(val, result, memberName);
                    result[memberName] = hashedValue;
                }

                return result;
            }
        }

        private Hash HashJObject(JObject obj)
        {
            var result = new Hash();

            _hashCache[Thread.CurrentThread.ManagedThreadId][obj] = result;

            if (obj.Type == JTokenType.Object)
            {
                foreach (var child in obj.Children())
                {
                    if (child.Type == JTokenType.Property)
                    {
                        var property = (JProperty)child;
                        string name = property.Name;
                        object value = null;

                        if (property.Value != null)
                        {
                            var tokenValue = ((JToken)property.Value);
                            var tokenType = tokenValue.Type;
                            if (tokenType == JTokenType.Object)
                            {
                                value = HashJObject((JObject)property.Value);
                            }
                            else if (tokenType == JTokenType.Array)
                            {
                                value = HashJArray((JArray)property.Value);
                            }
                            else
                            {
                                value = ((JValue)property.Value).Value;
                            }
                        }

                        result[name] = value;
                    }
                }
            }

            return result;
        }

        private ICollection<object> HashJArray(JArray obj)
        {
            var collection = new List<object>();

            _hashCache[Thread.CurrentThread.ManagedThreadId][obj] = collection;

            foreach (var item in obj)
            {
                if (item.Type == JTokenType.Object)
                {
                    collection.Add(HashJObject(item as JObject));
                }
                else if (item.Type == JTokenType.Array)
                {
                    collection.Add(HashJArray(item as JArray));
                }
                else
                {
                    var value = item as JValue;
                    collection.Add(value.Value);
                }
            }

            return collection;
        }

        private bool IsHashable(Type type)
        {
            if (type == typeof(object))
            {
                return false;
            }
            var typeInfo = type.GetTypeInfo();
            
            if (typeInfo.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>))
            {
                return IsHashable(Nullable.GetUnderlyingType(type));
            }

            return (typeInfo.IsPrimitive
                || type == typeof(string)
                || !type.GetProperties().Any())
                || type == typeof(DateTime)
                || type == typeof(DateTimeOffset)
                || type == typeof(JToken)
                || type == typeof(JObject);

        }

        private bool IsEnumerableType(Type type)
        {
            var typeInfo = type.GetTypeInfo();
            return typeInfo.GetInterface("IEnumerable", true) != null;
        }

        private bool IsCollectionType(Type type)
        {
            var typeInfo = type.GetTypeInfo();
            return typeInfo.GetInterface("ICollection", true) != null;
        }

        private bool IsAnonymousType(Type type)
        {
            if (type == null)
                throw new ArgumentNullException("type");

            var typeInfo = type.GetTypeInfo();
            // HACK: The only way to detect anonymous types right now.
            return typeInfo.IsDefined(typeof(CompilerGeneratedAttribute), false)
                && typeInfo.IsGenericType && type.Name.Contains("AnonymousType")
                && (type.Name.StartsWith("<>") || type.Name.StartsWith("VB$"))
                && (typeInfo.Attributes & TypeAttributes.NotPublic) == TypeAttributes.NotPublic;
        }
    }
}
