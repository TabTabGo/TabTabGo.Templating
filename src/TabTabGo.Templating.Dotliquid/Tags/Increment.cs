using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using DotLiquid;
using DotLiquid.Exceptions;
using DotLiquid.Util;

namespace TabTabGo.Templating.Liquid.Tags
{
    public class Increment : Tag
    {
        private string _variable;
        private static readonly Regex Syntax = R.B(R.Q(@"({0}+)\s*"), DotLiquid.Liquid.VariableSignature);

        public override void Initialize(string tagName, string markup, List<string> tokens)
        {
            Match syntaxMatch = Syntax.Match(markup);
            if (syntaxMatch.Success)
            {
                _variable = syntaxMatch.Groups[1].Value;
            }
            else
            {
                throw new SyntaxException("Syntax error in Increment tag");
            }

            base.Initialize(tagName, markup, tokens);
        }

        public override void Render(Context context, TextWriter result)
        {
            var variable = context.Scopes.Last()[_variable];

            context.Scopes.Last()[_variable] = int.Parse(variable.ToString()) + 1;
        }
    }
}
