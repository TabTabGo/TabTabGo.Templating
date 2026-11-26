using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using DotLiquid;
using System;
using System.Globalization;
using Newtonsoft.Json.Linq;
using TabTabGo.Templating.Liquid.Filters;
using TabTabGo.Templating.Liquid.Internal;
using TabTabGo.Templating.Liquid.Tags;
using System.Runtime.CompilerServices;

namespace TabTabGo.Templating.Liquid
{
    public class TemplatingEngine : TabTabGo.Templating.TemplatingEngine
    {
        private static object _lock = new object();
        private static bool _isIinitialized = false;
        private readonly Dictionary<int, Dictionary<object, object>> _hashCache = new Dictionary<int, Dictionary<object, object>>();
        private static readonly ConcurrentDictionary<(string Path, DateTime LastWriteUtc, int MaxNestingDepth), Lazy<Template>> _templateCache =
            new ConcurrentDictionary<(string Path, DateTime LastWriteUtc, int MaxNestingDepth), Lazy<Template>>();
        private static long _parseCount;
        public override string EngineName => "Liquid";

        private readonly TemplatingOptions _options;

        /// <summary>Creates an engine with default <see cref="TemplatingOptions"/>.</summary>
        public TemplatingEngine() : this(new TemplatingOptions())
        {
        }

        /// <summary>Creates an engine that applies <paramref name="options"/> to every render.</summary>
        /// <param name="options">The options (cache switch, allowed settings, regex time-out, XML size cap).</param>
        public TemplatingEngine(TemplatingOptions options)
        {
            _options = TemplatingOptionsValidator.Validate(options, validateRegexTimeout: true);
        }

        /// <summary>Creates an engine from the options registered in dependency injection.</summary>
        /// <param name="options">The registered options.</param>
        public TemplatingEngine(Microsoft.Extensions.Options.IOptions<TemplatingOptions> options)
            : this((options ?? throw new ArgumentNullException(nameof(options))).Value)
        {
        }
        public void Initialize()
        {
            lock (_lock)
            {
                // DotLiquid's built-in Replace/ReplaceFirst/RemoveFirst... treat their pattern as a regex with a 10 s time-out held in
                // a process-wide static. Lower it to the shortest RegexTimeout of any engine initialised so far ("the strictest
                // engine wins"); it is never raised again.
                if (_options.RegexTimeout != Timeout.InfiniteTimeSpan && _options.RegexTimeout < Template.RegexTimeOut)
                {
                    Template.RegexTimeOut = _options.RegexTimeout;
                }

                if (!_isIinitialized)
                {
                    Template.NamingConvention = new DotLiquid.NamingConventions.CSharpNamingConvention();
                    DotLiquid.Liquid.UseRubyDateFormat = false;

                    Template.RegisterFilter(typeof(CollectionFilters));
                    Template.RegisterFilter(typeof(ValueFilters));
                    Template.RegisterFilter(typeof(CustomFilters));
                    Template.RegisterFilter(typeof(JObjectFilters));
                    Template.RegisterFilter(typeof(OpenXmlFilters));
                    Template.RegisterSafeType(typeof(JToken), new string[] { });
                    Template.RegisterTag<Increment>("increment");

                    _isIinitialized = true;
                }
            }
        }

        /// <summary>Number of template files parsed since the process started (used by tests to prove single parsing).</summary>
        internal static long ParseCount => Interlocked.Read(ref _parseCount);

        public override bool IsCached(string templatePath)
        {
            if (!_options.CacheTemplates || string.IsNullOrEmpty(templatePath))
            {
                return false;
            }

            var fullPath = Path.GetFullPath(templatePath);
            return File.Exists(fullPath)
                && _templateCache.TryGetValue((fullPath, File.GetLastWriteTimeUtc(fullPath), _options.MaxNestingDepth), out var cached)
                && cached.IsValueCreated;
        }

        public override void ParseTemplate(string templatePath)
        {
            GetTemplate(templatePath);
        }

        public override object Render(string templatePath, object sourceData, string culture ="en-Us")
        {
            Initialize();

            var (template, fullPath) = GetTemplate(templatePath);

            var hash = HashIt(sourceData);

            // The file system is resolved from the full path (a bare "main.liquid" has an empty directory name) and
            // travels with the render: DotLiquid reads Registers["file_system"] before the process-wide static (B-1).
            var fileSystem = new EnhancedLocalFileSystem(Path.GetDirectoryName(fullPath));
            var cultureInfo = new CultureInfo(culture);

            // The parsed template is cached and shared by every render, so each render gets its own Context. Rendering
            // through the template's own Registers/InstanceAssigns would let {% assign %} values and registers of one
            // render appear in the next one (or in a parallel one).
            var context = new Context(
                new List<Hash> { Hash.FromDictionary(hash) },
                new Hash(),
                Hash.FromDictionary(new Dictionary<string, object> { ["file_system"] = fileSystem }),
                ErrorsOutputMode.Display,
                0,
                0,
                cultureInfo);

            using (RenderScope.Begin(_options, fileSystem))
            {
                // A regex time-out in DotLiquid's built-in Replace/ReplaceFirst propagates as RegexMatchTimeoutException: the
                // render failed, and a failure must not look like a successful (e-mail body, report) result.
                return template.Render(RenderParameters.FromContext(context, cultureInfo));
            }
        }

        public override string RenderString(string templateString, object sourceData, string culture ="en-Us")
        {
            // Parsed per call, so nothing is shared between renders.
            var template = TemplateNesting.Parse(templateString, _options.MaxNestingDepth);
            var hash = HashIt(sourceData);
            var parameters = new RenderParameters(new CultureInfo(culture))
            {
                LocalVariables = Hash.FromDictionary(hash)
            };

            // With TemplatesPath set, includes resolve (confined) against it. Without it DotLiquid falls back to a file
            // system the host assigned to Template.FileSystem, as before 8.0.4.
            EnhancedLocalFileSystem fileSystem = null;
            if (!string.IsNullOrEmpty(_options.TemplatesPath))
            {
                fileSystem = new EnhancedLocalFileSystem(Path.GetFullPath(_options.TemplatesPath));
                parameters.Registers = Hash.FromDictionary(new Dictionary<string, object> { ["file_system"] = fileSystem });
            }

            using (RenderScope.Begin(_options, fileSystem))
            {
                return template.Render(parameters);
            }
        }

        private (Template Template, string FullPath) GetTemplate(string templatePath)
        {
            var fullPath = string.IsNullOrEmpty(templatePath) ? templatePath : Path.GetFullPath(templatePath);
            if (string.IsNullOrEmpty(fullPath) || !File.Exists(fullPath))
            {
                throw new Exception($"File {templatePath} doesn't exist!");
            }

            if (!_options.CacheTemplates)
            {
                return (ParseFile(fullPath, _options.MaxNestingDepth), fullPath);
            }

            // Keyed by full path and last write time: an edited file is parsed again, and relative and absolute spellings
            // of one path share an entry (B-2).
            var lastWrite = File.GetLastWriteTimeUtc(fullPath);
            var key = (fullPath, lastWrite, _options.MaxNestingDepth);
            var entry = _templateCache.GetOrAdd(key, k => new Lazy<Template>(() => ParseFile(k.Path, k.MaxNestingDepth), LazyThreadSafetyMode.ExecutionAndPublication));

            foreach (var stale in _templateCache.Keys.Where(k => k.Path == fullPath && k.LastWriteUtc != lastWrite).ToList())
            {
                _templateCache.TryRemove(stale, out _);
            }

            try
            {
                return (entry.Value, fullPath);
            }
            catch
            {
                _templateCache.TryRemove(key, out _); // do not cache a failed parse
                throw;
            }
        }

        private static Template ParseFile(string fullPath, int maxNestingDepth)
        {
            Interlocked.Increment(ref _parseCount);
            var template = TemplateNesting.Parse(File.ReadAllText(fullPath), maxNestingDepth);
            template.MakeThreadSafe(); // the instance is shared; renders use their own Context (see Render)
            return template;
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
                || type == typeof(JObject)
                || type == typeof(Decimal);

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
