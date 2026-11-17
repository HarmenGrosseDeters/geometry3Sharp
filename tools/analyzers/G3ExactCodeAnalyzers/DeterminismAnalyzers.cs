using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;

namespace G3ExactCodeAnalyzers
{
    /// <summary>
    /// The allow list and the registrations of a determinism proof scope (docs/design/DETERMINISM_PROOF.md, stage 0 of
    /// option A), read from the additional file DeterministicApis.txt. One entry per line, "//" starts a comment:
    /// <code>
    /// &lt;doc id&gt; ; [tags] reason                      an external member the scope may use (tags: NET, sites, hash)
    /// site &lt;api doc id&gt; @ &lt;member doc id&gt; ; why     a member of the scope that may use a [sites] entry
    /// if &lt;member doc id&gt; ; n ; test ; why          the n conditional-compilation blocks of a member, and the test that checks their branches
    /// cast &lt;member doc id&gt; ; n ; why               the n conversions of a double to an integer type in a member, and their guard
    /// skiplocalsinit &lt;member doc id&gt; ; argument    a [SkipLocalsInit] member, and why every buffer element is written before it is read
    /// construct &lt;member doc id&gt; ; &lt;delegate-invocation | string-concatenation&gt; ; n ; why   the n delegate calls or string concatenations of a member, and why their result cannot depend on the environment
    /// type &lt;file name&gt; ; &lt;type name&gt;                 only the named type (and what it contains) of this scope file is in the scope; a file with no such line is in it as a whole
    /// </code>
    /// Tags: [NET] a member of one side of an #if NET block (not used when NET is not defined); [sites] every member
    /// that uses it needs a site line; [assumed] a member of this assembly in a file outside the scope that the scope calls, taken
    /// as an obligation of a later stage: its signature is on the list, its body is not pinned and not analyzed (no [body:]); [hash] it may be used only inside a GetHashCode implementation; [form] the reason of
    /// each site line starts with the argument form and its tests, "P1 [Class.Test, ...] why" (P1 disjoint writes, P2 exact
    /// commutative fold, P3 collect then canonicalize, P4 unique answer, P5 renaming invariance:
    /// docs/design/DETERMINISM_PROOF.md section 4.3; P0: an ordered source, a list or an array in index order, no argument needed).
    /// The delegate-invocation lines of 'construct' need the same form.
    /// </summary>
    public sealed class DeterministicApiList
    {
        public const string FileName = "DeterministicApis.txt";

        public sealed class Entry
        {
            public string Id;
            public string Reason;
            public bool NetOnly, Sites, Hash, Form, Assumed;
            /// <summary> [body:h]: the hash of the reviewed declaration of a geometry3Sharp member outside the scope (BodyHash) </summary>
            public string Body;
            public int Line;
        }

        public sealed class Registration
        {
            public string Kind;        // site, if, cast, skiplocalsinit, type (Member: the file name, Test: the type name)
            public string Member;
            public string Api;         // site only
            public int Count;          // if, cast
            public string Test;        // if
            public string Reason;
            public int Line;
        }

        public readonly Dictionary<string, Entry> Entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        public readonly List<Registration> Registrations = new List<Registration>();
        public readonly List<string> Errors = new List<string>();

        public static DeterministicApiList Parse(string text)
        {
            var list = new DeterministicApiList();
            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            for (int i = 0; i < lines.Length; ++i) {
                string l = lines[i].Trim();
                if (l.Length == 0 || l.StartsWith("//", StringComparison.Ordinal))
                    continue;
                int line = i + 1;
                int sp = l.IndexOf(' ');
                string kind = (sp > 0) ? l.Substring(0, sp) : "";
                // the last field (the reason) may contain ';'
                int fields = kind == "if" ? 4 : kind == "cast" ? 3 : 2;
                string[] f = l.Split(new[] { ';' }, fields, StringSplitOptions.None).Select(x => x.Trim()).ToArray();
                sp = f[0].IndexOf(' ');
                string rest = (sp > 0) ? f[0].Substring(sp + 1).Trim() : "";
                switch (kind) {
                    case "site": {
                        int at = rest.IndexOf(" @ ", StringComparison.Ordinal);
                        if (at < 0 || f.Length != 2 || f[1].Length == 0) { list.Errors.Add(line + ": site <api> @ <member> ; why"); continue; }
                        AddRegistration(list, new Registration { Kind = kind, Api = rest.Substring(0, at).Trim(), Member = rest.Substring(at + 3).Trim(), Reason = f[1], Line = line });
                        continue;
                    }
                    case "if": {
                        int n;
                        if (f.Length != 4 || !int.TryParse(f[1], out n) || n <= 0 || f[2].Length == 0 || f[3].Length == 0) { list.Errors.Add(line + ": if <member> ; n ; test ; why"); continue; }
                        AddRegistration(list, new Registration { Kind = kind, Member = rest, Count = n, Test = f[2], Reason = f[3], Line = line });
                        continue;
                    }
                    case "cast": {
                        int n;
                        if (f.Length != 3 || !int.TryParse(f[1], out n) || n <= 0 || f[2].Length == 0) { list.Errors.Add(line + ": cast <member> ; n ; why"); continue; }
                        AddRegistration(list, new Registration { Kind = kind, Member = rest, Count = n, Reason = f[2], Line = line });
                        continue;
                    }
                    case "type": {
                        if (f.Length != 2 || rest.Length == 0 || f[1].Length == 0 || rest.IndexOf(' ') >= 0 || f[1].IndexOf(' ') >= 0) { list.Errors.Add(line + ": type <file name> ; <type name>"); continue; }
                        list.Registrations.Add(new Registration { Kind = kind, Member = rest, Test = f[1], Reason = f[1], Line = line });
                        continue;
                    }
                    case "construct": {
                        // construct <member> ; <pseudo id> ; n ; why
                        string[] g = l.Split(new[] { ';' }, 4, StringSplitOptions.None).Select(x => x.Trim()).ToArray();
                        int n;
                        string who = g[0].Substring(g[0].IndexOf(' ') + 1).Trim();
                        if (g.Length != 4 || !int.TryParse(g[2], out n) || n <= 0 || g[1].Length == 0 || g[3].Length == 0 || g[1].IndexOf(' ') >= 0) { list.Errors.Add(line + ": construct <member> ; <pseudo id> ; n ; why"); continue; }
                        AddRegistration(list, new Registration { Kind = kind, Member = who, Api = "construct:" + g[1], Count = n, Reason = g[3], Line = line });
                        continue;
                    }
                    case "skiplocalsinit": {
                        if (f.Length != 2 || f[1].Length == 0) { list.Errors.Add(line + ": skiplocalsinit <member> ; argument"); continue; }
                        AddRegistration(list, new Registration { Kind = kind, Member = rest, Reason = f[1], Line = line });
                        continue;
                    }
                }
                if (f.Length != 2 || f[0].Length < 3 || f[0][1] != ':' || f[0].IndexOf(' ') >= 0) { list.Errors.Add(line + ": <doc id> ; reason"); continue; }
                var e = new Entry { Id = f[0], Line = line };
                string r = f[1];
                while (r.StartsWith("[", StringComparison.Ordinal)) {
                    int close = r.IndexOf(']');
                    if (close < 0) break;
                    string tag = r.Substring(1, close - 1);
                    if (tag == "NET") e.NetOnly = true;
                    else if (tag == "sites") e.Sites = true;
                    else if (tag == "hash") e.Hash = true;
                    else if (tag == "form") e.Form = true;
                    else if (tag == "assumed") e.Assumed = true;
                    else if (tag.StartsWith("body:", StringComparison.Ordinal) && tag.Length == 21 && tag.Substring(5).All(ch => "0123456789abcdef".IndexOf(ch) >= 0)) {
                        if (e.Body != null) list.Errors.Add(line + ": two [body:] tags");
                        e.Body = tag.Substring(5);
                    }
                    else list.Errors.Add(line + ": unknown tag [" + tag + "]");
                    r = r.Substring(close + 1).TrimStart();
                }
                e.Reason = r;
                if (r.Length == 0) { list.Errors.Add(line + ": no reason"); continue; }
                if (list.Entries.ContainsKey(e.Id)) { list.Errors.Add(line + ": duplicate " + e.Id); continue; }
                list.Entries.Add(e.Id, e);
            }
            return list;
        }

        /// <summary> the pseudo ids that a member may register by count ('construct' lines): a call of a delegate (the callee is
        /// chosen by the caller of the member: the line names the argument why its result cannot depend on the environment) and
        /// a string concatenation (the text of exception messages and diagnostics) </summary>
        public static bool Countable(string pseudo)
        {
            return pseudo == "construct:delegate-invocation" || pseudo == "construct:string-concatenation" || pseudo == "construct:interface-invocation";
        }

        static bool IsDocId(string id)
        {
            return id != null && id.Length > 2 && id[1] == ':' && "TMFPE".IndexOf(id[0]) >= 0 && id.IndexOf(' ') < 0;
        }

        static void AddRegistration(DeterministicApiList list, Registration r)
        {
            if (!IsDocId(r.Member) || (r.Kind == "site" && !IsDocId(r.Api)) || (r.Kind == "construct" && !Countable(r.Api)))
                list.Errors.Add(r.Line + ": " + r.Kind + ": not a documentation id: '" + (IsDocId(r.Member) ? r.Api : r.Member) + "'");
            else
                list.Registrations.Add(r);
        }

        public Registration Find(string kind, string member, string api = null)
        {
            foreach (Registration r in Registrations)
                if (r.Kind == kind && r.Member == member && (api == null || r.Api == api))
                    return r;
            return null;
        }
    }

    /// <summary>
    /// The closed-world rules of a determinism proof scope (docs/design/DETERMINISM_PROOF.md, stage 0 of option A), scoped
    /// by .editorconfig like G3E0004:
    /// G3D0001 every member of another assembly that the scope calls, reads, constructs or applies (also operators,
    /// conversions, attributes, lock) is on the allow list DeterministicApis.txt;
    /// G3D0002 a restricted entry is used only where its restriction allows ([sites]: registered members; [hash], and
    /// every GetHashCode call: inside a GetHashCode implementation, so that a hash value reaches a hash container only);
    /// G3D0005 every conditional-compilation block is registered with the test that checks its branches;
    /// G3D0006 every conversion of a double to an integer type is registered with its guard;
    /// G3D0008 every [SkipLocalsInit] member is registered with the argument that its buffers are written before read;
    /// G3D0009 (at the end of the compilation) every entry and registration is used: the list is exact.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class DeterminismScopeAnalyzer : DiagnosticAnalyzer
    {
        public const string ApiId = "G3D0001", RestrictedId = "G3D0002", ConditionalId = "G3D0005", CastId = "G3D0006",
            DirectiveId = "G3D0007", SkipLocalsInitId = "G3D0008", UnusedId = "G3D0009";

        /// <summary> the diagnostic property with the doc id that a diagnostic is about (the member used, or the member that holds the construct) </summary>
        public const string IdProperty = "id", MemberProperty = "member", CountProperty = "count";

        static DiagnosticDescriptor Rule(string id, string title, string message, params string[] tags)
        {
            return new DiagnosticDescriptor(id, title, message, "Reliability", DiagnosticSeverity.Warning, isEnabledByDefault: true,
                description: "Determinism proof scope (docs/design/DETERMINISM_PROOF.md); enabled for the scope's files in .editorconfig.",
                customTags: tags);
        }

        static readonly DiagnosticDescriptor ApiRule = Rule(ApiId, "External member not on the determinism allow list",
            "Proof scope: '{0}' is not on the allow list " + DeterministicApiList.FileName + " (add it with the reason why its result is specified on every runtime, and the regenerated ledger line in docs/design/DETERMINISM_PROOF.md 5.4)");
        static readonly DiagnosticDescriptor RestrictedRule = Rule(RestrictedId, "Restricted member used where its restriction does not allow it",
            "Proof scope: '{0}' {1}");
        static readonly DiagnosticDescriptor ConditionalRule = Rule(ConditionalId, "Unregistered conditional compilation",
            "Proof scope: {0} conditional-compilation block(s) in '{1}', registered: {2} (an 'if' line in " + DeterministicApiList.FileName + " with the test that checks both branches)");
        static readonly DiagnosticDescriptor CastRule = Rule(CastId, "Unregistered conversion of a double to an integer type",
            "Proof scope: {0} conversion(s) of a double to an integer type in '{1}', registered: {2} (out of range and NaN differ between runtimes; a 'cast' line in " + DeterministicApiList.FileName + " with the guard)");
        static readonly DiagnosticDescriptor DirectiveRule = Rule(DirectiveId, "Directive that can switch off or bypass the scope's rules",
            "Proof scope: '{0}' is not allowed in the scope (it can switch off a rule, change what is compiled or move a location)");
        static readonly DiagnosticDescriptor SkipLocalsInitRule = Rule(SkipLocalsInitId, "Unregistered [SkipLocalsInit]",
            "Proof scope: '{0}' skips the zeroing of its locals; register it ('skiplocalsinit' line in " + DeterministicApiList.FileName + ") with the argument that every element is written before it is read");
        static readonly DiagnosticDescriptor UnusedRule = Rule(UnusedId, "Unused or malformed allow-list line",
            "Proof scope: " + DeterministicApiList.FileName + " line {0}: {1}", WellKnownDiagnosticTags.CompilationEnd);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(ApiRule, RestrictedRule, ConditionalRule, CastRule, DirectiveRule, SkipLocalsInitRule, UnusedRule);

        static readonly string[] ScopeIds = { ApiId, RestrictedId, ConditionalId, CastId, DirectiveId, SkipLocalsInitId, UnusedId };

        public override void Initialize(AnalysisContext context)
        {
            // (generated code too: a kernel file marked as generated, by a header, its name or generated_code, must not
            // drop out of the scope silently)
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);
            context.EnableConcurrentExecution();
            context.RegisterCompilationStartAction(Start);
        }

        sealed class State
        {
            public DeterministicApiList List;
            public Compilation Compilation;
            public readonly ConcurrentDictionary<SyntaxTree, bool> InScope = new ConcurrentDictionary<SyntaxTree, bool>();
            public readonly ConcurrentDictionary<string, bool> UsedEntries = new ConcurrentDictionary<string, bool>();
            public readonly ConcurrentDictionary<int, bool> UsedLines = new ConcurrentDictionary<int, bool>();
            public SyntaxTree FirstScopeTree;
            /// <summary> file name -> the types that alone are in the scope there (the 'type' lines) </summary>
            public Dictionary<string, List<DeterministicApiList.Registration>> TypeScopes;
            public readonly ConcurrentDictionary<SyntaxTree, TextSpan[]> TypeSpans = new ConcurrentDictionary<SyntaxTree, TextSpan[]>();
            public readonly object Lock = new object();
            public bool NetDefined;
            public readonly ConcurrentDictionary<string, bool> Directives = new ConcurrentDictionary<string, bool>();
        }

        static void Start(CompilationStartAnalysisContext ctx)
        {
            var s = new State { Compilation = ctx.Compilation };
            AdditionalText file = ctx.Options.AdditionalFiles.FirstOrDefault(f =>
                string.Equals(System.IO.Path.GetFileName(f.Path), DeterministicApiList.FileName, StringComparison.OrdinalIgnoreCase));
            s.List = DeterministicApiList.Parse(file?.GetText(ctx.CancellationToken)?.ToString() ?? "");
            s.TypeScopes = new Dictionary<string, List<DeterministicApiList.Registration>>(StringComparer.Ordinal);
            foreach (DeterministicApiList.Registration r in s.List.Registrations)
                if (r.Kind == "type") {
                    List<DeterministicApiList.Registration> l;
                    if (!s.TypeScopes.TryGetValue(r.Member, out l)) s.TypeScopes[r.Member] = l = new List<DeterministicApiList.Registration>();
                    l.Add(r);
                }
            foreach (SyntaxTree t in ctx.Compilation.SyntaxTrees) {
                if (t.Options.PreprocessorSymbolNames.Contains("NET"))
                    s.NetDefined = true;
                break;
            }

            ctx.RegisterOperationAction(c => OnOperation(c, s), OperationKind.Invocation, OperationKind.ObjectCreation,
                OperationKind.PropertyReference, OperationKind.FieldReference, OperationKind.MethodReference, OperationKind.EventReference,
                OperationKind.Binary, OperationKind.Unary, OperationKind.Conversion, OperationKind.CompoundAssignment,
                OperationKind.Increment, OperationKind.Decrement, OperationKind.Lock, OperationKind.Loop, OperationKind.Using,
                OperationKind.InterpolatedString, OperationKind.Tuple, OperationKind.Await, OperationKind.AnonymousObjectCreation,
                OperationKind.DynamicInvocation, OperationKind.DynamicMemberReference, OperationKind.DynamicObjectCreation,
                OperationKind.DynamicIndexerAccess, OperationKind.Range, OperationKind.UsingDeclaration, OperationKind.RecursivePattern,
                OperationKind.ListPattern, OperationKind.SlicePattern, OperationKind.TypeParameterObjectCreation,
                OperationKind.FunctionPointerInvocation, OperationKind.TupleBinary);
            ctx.RegisterOperationAction(c => OnFunction(c, s), OperationKind.LocalFunction, OperationKind.AnonymousFunction);
            ctx.RegisterOperationBlockStartAction(c => OnBlockStart(c, s));
            ctx.RegisterSemanticModelAction(c => OnModel(c, s));
            ctx.RegisterSymbolAction(c => OnSymbol(c, s), SymbolKind.Method);
            ctx.RegisterCompilationEndAction(c => OnEnd(c, s));
        }

        static bool InScope(State s, SyntaxTree tree)
        {
            return s.InScope.GetOrAdd(tree, t => {
                bool any = false;
                SyntaxTreeOptionsProvider p = s.Compilation.Options.SyntaxTreeOptionsProvider;
                foreach (string id in ScopeIds) {
                    ReportDiagnostic r;
                    if (p == null || !p.TryGetDiagnosticValue(t, id, default, out r) || r != ReportDiagnostic.Suppress) { any = true; break; }
                }
                if (any) lock (s.Lock) { if (s.FirstScopeTree == null || string.CompareOrdinal(t.FilePath, s.FirstScopeTree.FilePath) < 0) s.FirstScopeTree = t; }
                return any;
            });
        }

        /// <summary> a position of a scope file: in the scope as a whole, or, if the list restricts the file to named types
        /// ('type' lines), inside one of them (spans by the syntax of the type declarations, found anywhere in the file) </summary>
        static bool InScopeAt(State s, SyntaxTree tree, TextSpan span)
        {
            if (!InScope(s, tree))
                return false;
            List<DeterministicApiList.Registration> types;
            if (!s.TypeScopes.TryGetValue(System.IO.Path.GetFileName(tree.FilePath), out types))
                return true;
            foreach (TextSpan t in TypeSpansOf(s, tree, types))
                if (t.Contains(span.Start))
                    return true;
            return false;
        }

        /// <summary> a span of a scope file that touches the scope: inside it, or (for a directive block) overlapping one of the named types </summary>
        static bool TouchesScope(State s, SyntaxTree tree, TextSpan span)
        {
            if (!InScope(s, tree))
                return false;
            List<DeterministicApiList.Registration> types;
            if (!s.TypeScopes.TryGetValue(System.IO.Path.GetFileName(tree.FilePath), out types))
                return true;
            foreach (TextSpan t in TypeSpansOf(s, tree, types))
                if (t.OverlapsWith(span) || t.Contains(span.Start))
                    return true;
            return false;
        }

        static TextSpan[] TypeSpansOf(State s, SyntaxTree tree, List<DeterministicApiList.Registration> types)
        {
            return s.TypeSpans.GetOrAdd(tree, t => {
                var spans = new List<TextSpan>();
                foreach (BaseTypeDeclarationSyntax d in t.GetRoot().DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
                    foreach (DeterministicApiList.Registration r in types)
                        if (d.Identifier.Text == r.Test) {
                            spans.Add(d.Span);
                            lock (s.Lock) s.UsedLines[r.Line] = true;
                        }
                return spans.ToArray();
            });
        }

        /// <summary> the member that holds an operation: lambdas and local functions belong to their enclosing member </summary>
        public static ISymbol Holder(ISymbol s)
        {
            while (s is IMethodSymbol m && (m.MethodKind == MethodKind.AnonymousFunction || m.MethodKind == MethodKind.LocalFunction))
                s = m.ContainingSymbol;
            return s;
        }

        static string DocId(ISymbol s)
        {
            if (s is IMethodSymbol m && m.ReducedFrom != null)
                s = m.ReducedFrom;
            // (the element of a tuple, also a named one, is the field ItemN of its System.ValueTuple type)
            if (s is IFieldSymbol f && f.CorrespondingTupleField != null)
                s = f.CorrespondingTupleField;
            return s.OriginalDefinition.GetDocumentationCommentId();
        }

        /// <summary> outside the scope: a member of another assembly, or of this one declared only in files outside the
        /// scope (Vector3d, MathUtil, ...: the kernel inherits whatever they compute, so they are listed like the BCL) </summary>
        static bool External(State s, ISymbol member)
        {
            if (member == null || member.ContainingAssembly == null)
                return false;
            if (!SymbolEqualityComparer.Default.Equals(member.ContainingAssembly, s.Compilation.Assembly))
                return true;
            ISymbol d = member.OriginalDefinition;
            if (d is IMethodSymbol m && m.ReducedFrom != null)
                d = m.ReducedFrom;
            ImmutableArray<SyntaxReference> refs = d.DeclaringSyntaxReferences;
            // (implicitly declared members, eg the default constructor of a struct: where their type is declared)
            for (ISymbol t = d.ContainingSymbol; refs.Length == 0 && t != null; t = t.ContainingSymbol)
                refs = t.DeclaringSyntaxReferences;
            if (refs.Length == 0)
                return false;
            foreach (SyntaxReference r in refs)
                if (InScopeAt(s, r.SyntaxTree, r.Span))
                    return false;
            return true;
        }

        /// <summary> a GetHashCode implementation: an override of object.GetHashCode or the implementation of an
        /// IEqualityComparer&lt;T&gt;.GetHashCode(T) </summary>
        public static bool IsHashImplementation(ISymbol holder)
        {
            if (!(holder is IMethodSymbol m) || m.Name != "GetHashCode" || m.ReturnType.SpecialType != SpecialType.System_Int32)
                return false;
            if (m.Parameters.Length == 0)
                return m.IsOverride;
            if (m.Parameters.Length != 1 || m.ContainingType == null)
                return false;
            foreach (INamedTypeSymbol i in m.ContainingType.AllInterfaces) {
                if (i.OriginalDefinition.ToDisplayString() != "System.Collections.Generic.IEqualityComparer<T>")
                    continue;
                foreach (ISymbol im in i.GetMembers("GetHashCode"))
                    if (SymbolEqualityComparer.Default.Equals(m.ContainingType.FindImplementationForInterfaceMember(im), m))
                        return true;
            }
            return false;
        }

        /// <summary> constructs that the compiler lowers to framework calls the operation tree does not show (string
        /// concatenation and formatting, tuples, await, dynamic, ranges, anonymous types): reported under a pseudo id,
        /// which no list entry can allow </summary>
        static string HiddenCall(IOperation op)
        {
            switch (op) {
                case IBinaryOperation b when b.OperatorMethod == null && b.OperatorKind == BinaryOperatorKind.Add
                    && (b.LeftOperand.Type?.SpecialType == SpecialType.System_String || b.RightOperand.Type?.SpecialType == SpecialType.System_String):
                    return "construct:string-concatenation";
                case ICompoundAssignmentOperation ca when ca.OperatorMethod == null && ca.Target.Type?.SpecialType == SpecialType.System_String:
                    return "construct:string-concatenation";
                case IInterpolatedStringOperation _: return "construct:interpolated-string";
                // (a tuple literal of up to 7 elements is a call of the constructor of System.ValueTuple`N, a member like any other: UsedMembers;
                // a longer one nests a ValueTuple`8 and stays a pseudo id)
                case ITupleOperation t when TupleConstructor(t) == null: return "construct:tuple";
                case IAwaitOperation _: return "construct:await";
                case IAnonymousObjectCreationOperation _: return "construct:anonymous-type";
                case IRangeOperation _: return "construct:range";
                case IDynamicInvocationOperation _: case IDynamicMemberReferenceOperation _: case IDynamicObjectCreationOperation _:
                case IDynamicIndexerAccessOperation _:
                    return "construct:dynamic";
                case ITypeParameterObjectCreationOperation _: return "construct:new-type-parameter";
                // (the target is chosen at run time: what it computes is not visible here)
                case IFunctionPointerInvocationOperation _: return "construct:function-pointer-call";
                case IInvocationOperation di when di.TargetMethod.MethodKind == MethodKind.DelegateInvoke: return "construct:delegate-invocation";
                // a call of a member of an interface declared in this assembly (a struct visitor of a generic method, say): the
                // implementation is chosen by the caller and may lie outside the scope, so it is registered by count, like a delegate
                case IInvocationOperation ii when ii.TargetMethod.ContainingType?.TypeKind == TypeKind.Interface
                    && SymbolEqualityComparer.Default.Equals(ii.TargetMethod.ContainingAssembly, ii.SemanticModel?.Compilation.Assembly):
                    return "construct:interface-invocation";
                case ITupleBinaryOperation _: return "construct:tuple-equality";
                case ISlicePatternOperation _: return "construct:slice-pattern";
                case IBinaryOperation b when b.OperatorMethod == null && (b.OperatorKind == BinaryOperatorKind.Equals || b.OperatorKind == BinaryOperatorKind.NotEquals)
                    && b.LeftOperand.Type?.SpecialType == SpecialType.System_String && b.RightOperand.Type?.SpecialType == SpecialType.System_String:
                    return "construct:string-equality";
                case IBinaryOperation b when b.OperatorMethod == null && (b.LeftOperand.Type?.TypeKind == TypeKind.Delegate || b.RightOperand.Type?.TypeKind == TypeKind.Delegate):
                    return "construct:delegate-combination";
                case ICompoundAssignmentOperation ca when ca.OperatorMethod == null && ca.Target.Type?.TypeKind == TypeKind.Delegate:
                    return "construct:delegate-combination";
                case ILockOperation l when l.LockedValue?.Type?.ToDisplayString() == "System.Threading.Lock":
                    return "construct:lock-object";
            }
            // predefined decimal operators and conversions are calls of System.Decimal
            if ((op is IBinaryOperation || op is IUnaryOperation || op is IConversionOperation || op is ICompoundAssignmentOperation || op is IIncrementOrDecrementOperation)
                && (IsDecimal(op.Type) || op.ChildOperations.Any(x => IsDecimal(x.Type))))
                return "construct:decimal";
            return null;
        }

        static bool IsDecimal(ITypeSymbol t)
        {
            return t != null && (t.SpecialType == SpecialType.System_Decimal || (t is INamedTypeSymbol n && n.IsGenericType
                && n.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T && n.TypeArguments[0].SpecialType == SpecialType.System_Decimal));
        }

        /// <summary> the constructor of the System.ValueTuple type that a tuple literal builds (null: more than 7 elements) </summary>
        static IMethodSymbol TupleConstructor(ITupleOperation t)
        {
            var type = t.Type as INamedTypeSymbol;
            if (type == null || !type.IsTupleType || type.TupleElements.Length > 7)
                return null;
            INamedTypeSymbol under = type.TupleUnderlyingType ?? type;
            return under.InstanceConstructors.FirstOrDefault(c => c.Parameters.Length == type.TupleElements.Length);
        }

        /// <summary> the members an operation calls or reads (its own; not those of its children) </summary>
        static void UsedMembers(IOperation op, Compilation compilation, List<ISymbol> used)
        {
            switch (op) {
                case IInvocationOperation i: used.Add(i.TargetMethod); break;
                case IObjectCreationOperation o: if (o.Constructor != null) used.Add(o.Constructor); break;
                case ITupleOperation tu: { IMethodSymbol tc = TupleConstructor(tu); if (tc != null) used.Add(tc); break; }
                case IPropertyReferenceOperation p: used.Add(p.Property); break;
                case IFieldReferenceOperation f: used.Add(f.Field); break;
                case IMethodReferenceOperation r: used.Add(r.Method); break;
                case IEventReferenceOperation e: used.Add(e.Event); break;
                case IBinaryOperation b: if (b.OperatorMethod != null) used.Add(b.OperatorMethod); break;
                case IUnaryOperation u: if (u.OperatorMethod != null) used.Add(u.OperatorMethod); break;
                case IConversionOperation v: if (v.OperatorMethod != null) used.Add(v.OperatorMethod); break;
                case ICompoundAssignmentOperation ca: if (ca.OperatorMethod != null) used.Add(ca.OperatorMethod); break;
                case IIncrementOrDecrementOperation id: if (id.OperatorMethod != null) used.Add(id.OperatorMethod); break;
                case ILockOperation lk when lk.LockedValue?.Type?.ToDisplayString() == "System.Threading.Lock":
                    break;      // (a pseudo id, HiddenCall)
                case ILockOperation _:
                    // lowered to Monitor.Enter(object, ref bool) and Monitor.Exit(object)
                    INamedTypeSymbol monitor = compilation.GetTypeByMetadataName("System.Threading.Monitor");
                    if (monitor != null)
                        foreach (ISymbol m in monitor.GetMembers())
                            if (m is IMethodSymbol mm && ((mm.Name == "Enter" && mm.Parameters.Length == 2 && mm.Parameters[1].RefKind == RefKind.Ref) || (mm.Name == "Exit" && mm.Parameters.Length == 1)))
                                used.Add(mm);
                    break;
                case IForEachLoopOperation fe:
                    if (fe.Syntax is CommonForEachStatementSyntax fs && fe.SemanticModel != null) {
                        ForEachStatementInfo info = fe.SemanticModel.GetForEachStatementInfo(fs);
                        // a user-defined conversion of the elements to the loop variable's type
                        if (info.ElementConversion.MethodSymbol != null)
                            used.Add(info.ElementConversion.MethodSymbol);
                        ITypeSymbol ct = fe.Collection is IConversionOperation conv ? conv.Operand.Type : fe.Collection.Type;
                        if (ct is IArrayTypeSymbol)
                            break;      // lowered to an index loop
                        foreach (ISymbol m in new ISymbol[] { info.GetEnumeratorMethod, info.MoveNextMethod, info.CurrentProperty, info.DisposeMethod })
                            if (m != null) used.Add(m);
                    }
                    break;
                case IRecursivePatternOperation rp: if (rp.DeconstructSymbol != null) used.Add(rp.DeconstructSymbol); break;
                case IListPatternOperation lp:
                    if (lp.LengthSymbol != null) used.Add(lp.LengthSymbol);
                    if (lp.IndexerSymbol != null) used.Add(lp.IndexerSymbol);
                    break;
                case IUsingDeclarationOperation _:
                case IUsingOperation _:
                    INamedTypeSymbol disp = compilation.GetSpecialType(SpecialType.System_IDisposable);
                    foreach (ISymbol m in disp.GetMembers("Dispose")) used.Add(m);
                    break;
            }
        }

        static void OnOperation(OperationAnalysisContext c, State s)
        {
            IOperation op = c.Operation;
            if (!InScopeAt(s, op.Syntax.SyntaxTree, op.Syntax.Span))
                return;
            var used = new List<ISymbol>();
            UsedMembers(op, s.Compilation, used);
            ISymbol holder = Holder(c.ContainingSymbol);
            string holderId = holder?.GetDocumentationCommentId() ?? "";
            string hidden = HiddenCall(op);
            if (hidden != null && !DeterministicApiList.Countable(hidden))
                c.ReportDiagnostic(Diagnostic.Create(ApiRule, op.Syntax.GetLocation(), Props(hidden, holderId), hidden));
            foreach (ISymbol m0 in used) {
                ISymbol m = m0 is IFieldSymbol tf && tf.CorrespondingTupleField != null ? tf.CorrespondingTupleField : m0;
                string id = DocId(m);
                DeterministicApiList.Entry e = null;
                bool listed = id != null && External(s, m) && s.List.Entries.TryGetValue(id, out e);
                // the hash rule: every GetHashCode, also of the scope's own types (their dispatch is not followed), and every [hash] entry
                bool hash = (m is IMethodSymbol hm && hm.Name == "GetHashCode" && hm.Parameters.Length <= 1) || (listed && e.Hash);
                if (hash && !IsHashImplementation(holder))
                    c.ReportDiagnostic(Diagnostic.Create(RestrictedRule, op.Syntax.GetLocation(), Props(id, holderId),
                        id, "is a hash value (seeded per process on .NET for some types): only inside a GetHashCode implementation, so that it reaches a hash container only"));
                if (id == null || !External(s, m))
                    continue;
                if (!listed) {
                    c.ReportDiagnostic(Diagnostic.Create(ApiRule, op.Syntax.GetLocation(), Props(id, holderId), id));
                    continue;
                }
                s.UsedEntries[id] = true;
                if (e.Sites) {
                    DeterministicApiList.Registration r = s.List.Find("site", holderId, id);
                    if (r == null)
                        c.ReportDiagnostic(Diagnostic.Create(RestrictedRule, op.Syntax.GetLocation(), Props(id, holderId),
                            id, "is a restricted entry: '" + holderId + "' needs a 'site' line with the justification"));
                    else {
                        s.UsedLines[r.Line] = true;
                        if (e.Form && !FormReason.IsMatch(r.Reason))
                            c.ReportDiagnostic(Diagnostic.Create(RestrictedRule, op.Syntax.GetLocation(), Props(id, holderId),
                                id, "needs an argument form and its tests in the site line of '" + holderId + "': 'P1' to 'P5', then '[Class.Test, ...]', then the reason"));
                    }
                }
            }
        }

        /// <summary> the reason of a site of a [form] entry: "P3 [Class.Test, Class.Other] why" </summary>
        public static readonly System.Text.RegularExpressions.Regex FormReason = new System.Text.RegularExpressions.Regex(
            @"^P[0-5] \[[A-Za-z0-9_.]+(, [A-Za-z0-9_.]+)*\] \S", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        static ImmutableDictionary<string, string> Props(string id, string member, int count = -1)
        {
            var b = ImmutableDictionary.CreateBuilder<string, string>();
            b[IdProperty] = id ?? "";
            b[MemberProperty] = member ?? "";
            if (count >= 0) b[CountProperty] = count.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return b.ToImmutable();
        }

        /// <summary> a conversion of a double (or float) to an integer type, checked or not, also of their nullable forms </summary>
        public static bool IsFloatToIntegerCast(IConversionOperation v)
        {
            // (a constant conversion is evaluated by the compiler: its value is fixed, or it does not compile)
            return v.OperatorMethod == null && !v.ConstantValue.HasValue && IsFloatToInteger(v.Operand?.Type, v.Type);
        }

        static ITypeSymbol Underlying(ITypeSymbol t)
        {
            return (t is INamedTypeSymbol n && n.IsGenericType && n.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T) ? n.TypeArguments[0] : t;
        }

        public static bool IsFloatToInteger(ITypeSymbol from, ITypeSymbol to)
        {
            from = Underlying(from);
            to = Underlying(to);
            if (from == null || to == null)
                return false;
            if (from.SpecialType != SpecialType.System_Double && from.SpecialType != SpecialType.System_Single)
                return false;
            if (to.TypeKind == TypeKind.Enum)
                return true;
            switch (to.SpecialType) {
                case SpecialType.System_SByte: case SpecialType.System_Byte: case SpecialType.System_Int16: case SpecialType.System_UInt16:
                case SpecialType.System_Int32: case SpecialType.System_UInt32: case SpecialType.System_Int64: case SpecialType.System_UInt64:
                case SpecialType.System_Char: case SpecialType.System_IntPtr: case SpecialType.System_UIntPtr:
                    return true;
            }
            return false;
        }

        static void OnBlockStart(OperationBlockStartAnalysisContext c, State s)
        {
            if (c.OperationBlocks.Length == 0 || !InScopeAt(s, c.OperationBlocks[0].Syntax.SyntaxTree, c.OperationBlocks[0].Syntax.Span))
                return;
            ISymbol holder = Holder(c.OwningSymbol);
            var casts = new List<IOperation>();
            var hiddenOps = new List<KeyValuePair<string, IOperation>>();
            c.RegisterOperationAction(oc => {
                string h = HiddenCall(oc.Operation);
                if (h != null && DeterministicApiList.Countable(h))
                    lock (hiddenOps) hiddenOps.Add(new KeyValuePair<string, IOperation>(h, oc.Operation));
            }, OperationKind.Binary, OperationKind.CompoundAssignment, OperationKind.Invocation);
            c.RegisterOperationAction(oc => {
                bool cast = oc.Operation is IConversionOperation v && IsFloatToIntegerCast(v);
                // (foreach (int i in doubles) converts each element explicitly)
                if (oc.Operation is IForEachLoopOperation fe && fe.Syntax is CommonForEachStatementSyntax fs && fe.SemanticModel != null) {
                    ForEachStatementInfo info = fe.SemanticModel.GetForEachStatementInfo(fs);
                    ILocalSymbol loopVar = fe.Locals.FirstOrDefault();
                    cast = info.ElementConversion.MethodSymbol == null && loopVar != null && IsFloatToInteger(info.ElementType, loopVar.Type);
                }
                if (cast)
                    lock (casts) casts.Add(oc.Operation);
            }, OperationKind.Conversion, OperationKind.Loop);
            c.RegisterOperationBlockEndAction(ec => {
                // operations the public operation tree does not model (kind None, eg collection expressions in Roslyn 4.8):
                // what they call is not visible, so they are not allowed
                foreach (IOperation block in ec.OperationBlocks)
                    foreach (IOperation o in block.DescendantsAndSelf())
                        if (o.Kind == OperationKind.None && !Modelled(o)) {
                            string pseudo = "construct:unmodelled-" + o.Syntax.Kind();
                            ec.ReportDiagnostic(Diagnostic.Create(ApiRule, o.Syntax.GetLocation(), Props(pseudo, holder.GetDocumentationCommentId()), pseudo));
                        }
                foreach (IGrouping<string, KeyValuePair<string, IOperation>> g in hiddenOps.GroupBy(x => x.Key)) {
                    string hid = holder.GetDocumentationCommentId() ?? "";
                    DeterministicApiList.Registration cr = s.List.Find("construct", hid, g.Key);
                    if (cr != null)
                        s.UsedLines[cr.Line] = true;
                    if (cr != null && (g.Key == "construct:delegate-invocation" || g.Key == "construct:interface-invocation") && !DeterminismScopeAnalyzer.FormReason.IsMatch(cr.Reason))
                        ec.ReportDiagnostic(Diagnostic.Create(ApiRule, g.First().Value.Syntax.GetLocation(), Props(g.Key, hid, g.Count()),
                            g.Key + " in '" + hid + "': the line needs an argument form and its tests: 'P1' to 'P5', then '[Class.Test, ...]', then the reason"));
                    if (cr != null && cr.Count == g.Count())
                        continue;
                    foreach (var kv in g)
                        ec.ReportDiagnostic(Diagnostic.Create(ApiRule, kv.Value.Syntax.GetLocation(), Props(g.Key, hid, g.Count()), g.Key + " (" + g.Count() + " in '" + hid + "', registered: " + (cr?.Count ?? 0) + ")"));
                }
                if (casts.Count == 0)
                    return;
                // (one context per member: its blocks, with its lambdas and local functions; each accessor is a member)
                string id = holder.GetDocumentationCommentId() ?? "";
                DeterministicApiList.Registration r = s.List.Find("cast", id);
                if (r != null)
                    s.UsedLines[r.Line] = true;
                if (r != null && r.Count == casts.Count)
                    return;
                foreach (IOperation v in casts)
                    ec.ReportDiagnostic(Diagnostic.Create(CastRule, v.Syntax.GetLocation(), Props(id, id, casts.Count), casts.Count, id, r?.Count ?? 0));
            });
        }

        /// <summary> operations of kind None (Roslyn 4.8) whose meaning is known and that call nothing: stackalloc, the
        /// element access of a pointer, a fixed-size buffer, and a fixed statement that pins an array, a fixed-size buffer
        /// or an address (not a span or a string: GetPinnableReference, RuntimeHelpers) </summary>
        static bool Modelled(IOperation o)
        {
            SemanticModel m = o.SemanticModel;
            switch (o.Syntax) {
                case StackAllocArrayCreationExpressionSyntax _:
                case ImplicitStackAllocArrayCreationExpressionSyntax _:
                    return true;
                case ElementAccessExpressionSyntax e:
                    return m?.GetTypeInfo(e.Expression).Type is IPointerTypeSymbol || FixedBuffer(m, e.Expression);
                case PrefixUnaryExpressionSyntax pu when pu.IsKind(SyntaxKind.PointerIndirectionExpression):
                    return true;
                case IdentifierNameSyntax _:
                case MemberAccessExpressionSyntax _:
                    // (a variable inside a pointer or fixed-buffer construct; a member it names is reported by its own operation)
                    ISymbol named = m?.GetSymbolInfo((ExpressionSyntax)o.Syntax).Symbol;
                    return named is ILocalSymbol || named is IParameterSymbol || FixedBuffer(m, (ExpressionSyntax)o.Syntax);
                case FixedStatementSyntax f:
                    foreach (VariableDeclaratorSyntax v in f.Declaration.Variables) {
                        ExpressionSyntax x = v.Initializer?.Value;
                        if (x == null) return false;
                        if (x is PrefixUnaryExpressionSyntax pu && pu.IsKind(SyntaxKind.AddressOfExpression)) continue;
                        if (m?.GetTypeInfo(x).Type is IArrayTypeSymbol || FixedBuffer(m, x)) continue;
                        return false;
                    }
                    return true;
            }
            return false;
        }

        static bool FixedBuffer(SemanticModel m, ExpressionSyntax x)
        {
            // (of a struct in this compilation: a fixed buffer has no members of its own)
            return m?.GetSymbolInfo(x).Symbol is IFieldSymbol f && f.IsFixedSizeBuffer
                && SymbolEqualityComparer.Default.Equals(f.ContainingAssembly, m.Compilation.Assembly);
        }

        // #if / #elif blocks per member (a directive belongs to the member whose declaration holds its token)
        static void OnModel(SemanticModelAnalysisContext c, State s)
        {
            SyntaxTree tree = c.SemanticModel.SyntaxTree;
            if (!InScope(s, tree))
                return;
            var byMember = new Dictionary<string, List<Location>>();
            // the members that can hold an #if: everything but namespaces and types (an #if between members belongs to the next one)
            List<SyntaxNode> members = tree.GetRoot(c.CancellationToken).DescendantNodes()
                .Where(m => (m is MemberDeclarationSyntax && !(m is BaseNamespaceDeclarationSyntax) && !(m is BaseTypeDeclarationSyntax) && !(m is DelegateDeclarationSyntax))
                            || m is AccessorDeclarationSyntax).ToList();
            // the #if ... #endif ranges (inactive ones included: Roslyn parses directives in disabled text too); a member
            // that lies wholly inside one may not exist on another target, so it never holds a block (a directive inside such
            // a member goes to the first member after the range: no other member lies wholly inside it)
            var ranges = new List<TextSpan>();
            foreach (SyntaxTrivia tr in tree.GetRoot(c.CancellationToken).DescendantTrivia(descendIntoTrivia: true))
                if (tr.IsKind(SyntaxKind.IfDirectiveTrivia) && tr.GetStructure() is DirectiveTriviaSyntax ifd) {
                    DirectiveTriviaSyntax endif = ifd.GetRelatedDirectives().LastOrDefault();
                    if (endif != null && endif.Span.End > tr.SpanStart)
                        ranges.Add(TextSpan.FromBounds(tr.SpanStart, endif.Span.End));
                }
            members = members.Where(m => !ranges.Any(r => r.Contains(m.Span))).ToList();
            foreach (SyntaxTrivia tr in tree.GetRoot(c.CancellationToken).DescendantTrivia(descendIntoTrivia: true)) {
                string bad = Forbidden(tr);
                if (bad != null) {
                    // (also reported at the end, at the start of the first file of the scope: a pragma cannot reach there)
                    s.Directives[System.IO.Path.GetFileName(tree.FilePath) + ": " + bad] = true;
                    c.ReportDiagnostic(Diagnostic.Create(DirectiveRule, tr.GetLocation(), Props(bad, ""), bad));
                    continue;
                }
                if (!tr.IsKind(SyntaxKind.IfDirectiveTrivia) && !tr.IsKind(SyntaxKind.ElifDirectiveTrivia))
                    continue;
                // (the forbidden directives above are checked in the whole file, also outside the scope's types: a pragma
                // or #define there reaches into them)
                // (an #if ... #endif block that surrounds the type, its attributes or one of two versions of it is in the scope
                // although its #if line lies before the type's span)
                TextSpan block = new TextSpan(tr.SpanStart, 0);
                if (tr.GetStructure() is DirectiveTriviaSyntax bd) {
                    DirectiveTriviaSyntax lastd = bd.GetRelatedDirectives().LastOrDefault();
                    if (lastd != null && lastd.Span.End > tr.SpanStart) block = TextSpan.FromBounds(tr.SpanStart, lastd.Span.End);
                }
                if (!TouchesScope(s, tree, block))
                    continue;
                // by position, the same whatever the #if disables (the token that carries the trivia is not: it depends
                // on the target framework): the innermost member whose span holds the directive; else, for an #if around
                // an attribute or around whole members, the first member whose span ends after the block's #endif (the
                // attributed member; or the member after the block, which exists on every target; after the last member
                // of a type that is the first member of the next type, or 'file:' at the end of the file)
                int pos = tr.SpanStart;
                int end = pos;
                if (tr.GetStructure() is DirectiveTriviaSyntax dir) {
                    DirectiveTriviaSyntax last = dir.GetRelatedDirectives().LastOrDefault();
                    if (last != null) end = last.SpanStart;
                }
                SyntaxNode n = members.Where(m => m.Span.Contains(pos)).OrderBy(m => m.Span.Length).FirstOrDefault()
                    ?? members.Where(m => m.Span.End > end).OrderBy(m => m.SpanStart).FirstOrDefault();
                ISymbol holder = null;
                if (n != null) {
                    SyntaxNode decl = n is FieldDeclarationSyntax fd ? (SyntaxNode)fd.Declaration.Variables.First()
                        : n is EventFieldDeclarationSyntax ed ? ed.Declaration.Variables.First() : n;
                    holder = c.SemanticModel.GetDeclaredSymbol(decl, c.CancellationToken);
                }
                string id = holder?.GetDocumentationCommentId() ?? ("file:" + System.IO.Path.GetFileName(tree.FilePath));
                List<Location> l;
                if (!byMember.TryGetValue(id, out l)) byMember[id] = l = new List<Location>();
                l.Add(tr.GetLocation());
            }
            foreach (var kv in byMember) {
                DeterministicApiList.Registration r = s.List.Find("if", kv.Key);
                if (r != null) s.UsedLines[r.Line] = true;
                if (r != null && r.Count == kv.Value.Count)
                    continue;
                foreach (Location loc in kv.Value)
                    c.ReportDiagnostic(Diagnostic.Create(ConditionalRule, loc, Props(kv.Key, kv.Key, kv.Value.Count), kv.Value.Count, kv.Key, r?.Count ?? 0));
            }
        }

        /// <summary> a directive that switches off diagnostics, changes what is compiled or moves locations </summary>
        static string Forbidden(SyntaxTrivia tr)
        {
            switch (tr.GetStructure()) {
                case PragmaWarningDirectiveTriviaSyntax p:
                    if (p.DisableOrRestoreKeyword.IsKind(SyntaxKind.RestoreKeyword))
                        return null;
                    if (p.ErrorCodes.Count == 0 || p.ErrorCodes.Any(e => e.ToString().Trim().StartsWith("G3", StringComparison.OrdinalIgnoreCase) || e.ToString().Trim() == "RS0030"))
                        return p.ToString().Trim();
                    return null;
                case DefineDirectiveTriviaSyntax d: return d.ToString().Trim();
                case UndefDirectiveTriviaSyntax u: return u.ToString().Trim();
                case LineDirectiveTriviaSyntax l: return l.ToString().Trim();
                case LineSpanDirectiveTriviaSyntax ls: return ls.ToString().Trim();
            }
            return null;
        }

        static bool HasSkipLocalsInit(ISymbol x)
        {
            foreach (AttributeData a in x.GetAttributes())
                if (a.AttributeClass?.ToDisplayString() == "System.Runtime.CompilerServices.SkipLocalsInitAttribute")
                    return true;
            return false;
        }

        /// <summary> the symbols whose [SkipLocalsInit] reaches a method: itself, its property or event, its containing
        /// types, the module (C#: the attribute on any of them applies to the method's locals) </summary>
        static IEnumerable<ISymbol> SkipScopes(IMethodSymbol m)
        {
            yield return m;
            if (m.AssociatedSymbol != null)
                yield return m.AssociatedSymbol;
            for (ISymbol x = m.ContainingSymbol; x != null; x = x.ContainingSymbol)
                if (!(x is INamespaceSymbol))
                    yield return x;
        }

        // [SkipLocalsInit] on a local function or lambda: registered under the member that holds it
        static void OnFunction(OperationAnalysisContext c, State s)
        {
            IMethodSymbol f = (c.Operation as ILocalFunctionOperation)?.Symbol ?? (c.Operation as IAnonymousFunctionOperation)?.Symbol;
            if (f == null || !HasSkipLocalsInit(f) || !InScopeAt(s, c.Operation.Syntax.SyntaxTree, c.Operation.Syntax.Span))
                return;
            string id = Holder(c.ContainingSymbol)?.GetDocumentationCommentId() ?? "";
            DeterministicApiList.Registration r = s.List.Find("skiplocalsinit", id);
            if (r != null) { s.UsedLines[r.Line] = true; return; }
            c.ReportDiagnostic(Diagnostic.Create(SkipLocalsInitRule, c.Operation.Syntax.GetLocation(), Props(id, id), id));
        }

        static void OnSymbol(SymbolAnalysisContext c, State s)
        {
            var m = (IMethodSymbol)c.Symbol;
            ISymbol source = SkipScopes(m).FirstOrDefault(HasSkipLocalsInit);
            if (source == null)
                return;
            string id = m.GetDocumentationCommentId() ?? "";
            // (registered on the symbol that carries the attribute: a registration of a type covers the type's attribute,
            // not a method's own one)
            DeterministicApiList.Registration r = s.List.Find("skiplocalsinit", source.GetDocumentationCommentId() ?? "\0");
            foreach (Location loc in m.Locations) {
                if (!loc.IsInSource || !InScopeAt(s, loc.SourceTree, loc.SourceSpan))
                    continue;
                if (r != null) { s.UsedLines[r.Line] = true; continue; }
                c.ReportDiagnostic(Diagnostic.Create(SkipLocalsInitRule, loc, Props(id, id), id));
            }
        }

        /// <summary>
        /// The hash of a member's declaration as reviewed: FNV-1a 64 over the UTF-8 text of its declaration syntax (the
        /// attributes, signature and body; for a field its whole field declaration), token by token: token text verbatim
        /// (string literals keep their spaces), comments and directives verbatim (inside a /* */ comment its indentation
        /// counts), the text of a disabled #if branch tokenized as if it were active, a line break as one "\n" and other
        /// white space as one space where it separates two tokens, nothing at the start or end of a line (so a re-indent
        /// between tokens or a CRLF / LF checkout keeps the hash, a joined line does not). Not covered: the leading trivia
        /// of the declaration before its first directive (its doc comment), the containing type, what the names bind to.
        /// The parts of a member with several declarations are sorted by their text. The same on every runtime and target
        /// framework, whichever #if branch the target compiles.
        /// </summary>
        public static string BodyHash(ISymbol member)
        {
            var parts = new List<string>();
            foreach (SyntaxReference r in member.DeclaringSyntaxReferences) {
                SyntaxNode n = r.GetSyntax();
                if (n is VariableDeclaratorSyntax && n.Parent?.Parent is BaseFieldDeclarationSyntax fd)
                    n = fd;
                parts.Add(Canonical(n));
            }
            parts.Sort(StringComparer.Ordinal);
            unchecked {
                ulong h = 14695981039346656037UL;
                foreach (byte b in System.Text.Encoding.UTF8.GetBytes(string.Join("\n\n", parts))) { h ^= b; h *= 1099511628211UL; }
                return h.ToString("x16", System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        /// <summary> the canonical text of a declaration (see BodyHash) </summary>
        public static string Canonical(SyntaxNode n)
        {
            var sb = new System.Text.StringBuilder();
            bool pendingSpace = false;
            void Text(string t)
            {
                t = t.Replace("\r\n", "\n").Replace('\r', '\n');
                if (t.Length == 0) return;
                if (pendingSpace && sb.Length > 0 && sb[sb.Length - 1] != '\n') sb.Append(' ');
                pendingSpace = false;
                sb.Append(t);
            }
            void Break()
            {
                pendingSpace = false;
                if (sb.Length > 0 && sb[sb.Length - 1] != '\n') sb.Append('\n');
            }
            void Trivia(SyntaxTriviaList list)
            {
                foreach (SyntaxTrivia t in list) {
                    if (t.IsKind(SyntaxKind.WhitespaceTrivia)) pendingSpace = true;
                    else if (t.IsKind(SyntaxKind.EndOfLineTrivia)) Break();
                    else if (t.HasStructure) { Text(t.ToFullString().TrimEnd()); Break(); }     // a directive (its own line)
                    else if (t.IsKind(SyntaxKind.DisabledTextTrivia))
                        // the branch #if switches off on this target: tokenized as if it were active, so that every
                        // target gives the same text
                        foreach (SyntaxToken d in SyntaxFactory.ParseTokens(t.ToFullString())) {
                            Trivia(d.LeadingTrivia);
                            Text(d.Text);
                            Trivia(d.TrailingTrivia);
                        }
                    else { Text(t.ToFullString()); if (t.IsKind(SyntaxKind.SingleLineCommentTrivia)) Break(); }
                }
            }
            SyntaxToken first = n.GetFirstToken(), last = n.GetLastToken();
            // before the first token: from its first directive on (an #if around a leading attribute: on a target that
            // switches the attribute off, the whole block is trivia of the first token)
            SyntaxTriviaList lead = first.LeadingTrivia;
            int fromDirective = -1;
            for (int i = 0; i < lead.Count && fromDirective < 0; ++i)
                if (lead[i].IsDirective) fromDirective = i;
            if (fromDirective >= 0)
                Trivia(SyntaxFactory.TriviaList(lead.Skip(fromDirective)));
            foreach (SyntaxToken tok in n.DescendantTokens()) {
                if (tok != first) Trivia(tok.LeadingTrivia);
                Text(tok.Text);
                if (tok != last) Trivia(tok.TrailingTrivia);
            }
            return sb.ToString().TrimEnd('\n');
        }

        static void OnEnd(CompilationAnalysisContext c, State s)
        {
            SyntaxTree first = s.FirstScopeTree;
            if (first == null)
                return;     // no file of the scope in this compilation
            Location at = Location.Create(first, new TextSpan(0, 0));
            foreach (string d in s.Directives.Keys.OrderBy(x => x, StringComparer.Ordinal))
                c.ReportDiagnostic(Diagnostic.Create(UnusedRule, at, Props(d, ""), "-", "directive in the scope: " + d));
            foreach (string e in s.List.Errors)
                c.ReportDiagnostic(Diagnostic.Create(UnusedRule, at, Props("", ""), e.Substring(0, e.IndexOf(':')), "malformed: " + e.Substring(e.IndexOf(':') + 1).Trim()));
            // the members of this assembly outside the scope: their declarations as reviewed (a changed body needs a new review)
            foreach (DeterministicApiList.Entry e in s.List.Entries.Values.OrderBy(x => x.Line)) {
                ISymbol m = DocumentationCommentId.GetFirstSymbolForDeclarationId(e.Id, s.Compilation);
                bool own = m != null && SymbolEqualityComparer.Default.Equals(m.ContainingAssembly, s.Compilation.Assembly) && m.DeclaringSyntaxReferences.Length > 0;
                if (e.Assumed) {
                    // (an obligation of a later stage: the member exists in this assembly, outside the scope, and is not pinned)
                    if (!own || e.Body != null)
                        c.ReportDiagnostic(Diagnostic.Create(UnusedRule, at, Props(e.Id, ""), e.Line, "[assumed] is for a member of this assembly without a [body:] tag; '" + e.Id + "' is " + (own ? "pinned" : "not one")));
                    continue;
                }
                if (!own) {
                    if (e.Body != null && m != null)
                        c.ReportDiagnostic(Diagnostic.Create(UnusedRule, at, Props(e.Id, ""), e.Line, "[body:] pins declarations of this assembly only; '" + e.Id + "' has none here"));
                    continue;
                }
                // closure: every geometry3Sharp member that the pinned declaration uses and that is declared outside the
                // scope is itself on the list (and so pinned), so that a change of a callee cannot escape the review
                // (the operations, with the members OnOperation counts, implicit conversions, foreach and patterns
                // included; and the symbols of the expressions, for what has no operation, eg attribute arguments)
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (SyntaxReference r in m.DeclaringSyntaxReferences) {
                    SemanticModel model = s.Compilation.GetSemanticModel(r.SyntaxTree);
                    var uses = new List<ISymbol>();
                    foreach (SyntaxNode x in r.GetSyntax().DescendantNodesAndSelf()) {
                        IOperation root = model.GetOperation(x);
                        if (root != null && root.Parent == null)
                            foreach (IOperation o in root.DescendantsAndSelf())
                                UsedMembers(o, s.Compilation, uses);
                        if (x is ExpressionSyntax || x is ConstructorInitializerSyntax) {
                            ISymbol sy = model.GetSymbolInfo(x).Symbol;
                            if (sy != null) uses.Add(sy);
                        }
                    }
                    foreach (ISymbol u in uses) {
                        ISymbol used = u;
                        if (used is IMethodSymbol um && um.ReducedFrom != null) used = um.ReducedFrom;
                        if (used == null || !(used is IMethodSymbol || used is IPropertySymbol || used is IFieldSymbol || used is IEventSymbol))
                            continue;
                        if (SymbolEqualityComparer.Default.Equals(used.OriginalDefinition, m.OriginalDefinition) || !External(s, used)
                            || !SymbolEqualityComparer.Default.Equals(used.ContainingAssembly, s.Compilation.Assembly))
                            continue;
                        string uid = used.OriginalDefinition.GetDocumentationCommentId();
                        if (uid == null || !seen.Add(uid))
                            continue;
                        if (s.List.Entries.ContainsKey(uid))
                            s.UsedEntries[uid] = true;      // (used through a pinned declaration)
                        else
                            c.ReportDiagnostic(Diagnostic.Create(UnusedRule, at, Props(e.Id, uid), e.Line, "the pinned declaration of '" + e.Id + "' uses '" + uid + "', which is not on the list (add and pin it, so that its changes are reviewed too)"));
                    }
                }
                string h = BodyHash(m);
                if (e.Body == null)
                    c.ReportDiagnostic(Diagnostic.Create(UnusedRule, at, Props(e.Id, ""), e.Line, "'" + e.Id + "' is a member of this assembly: review its declaration and pin it with [body:" + h + "]"));
                else if (e.Body != h)
                    c.ReportDiagnostic(Diagnostic.Create(UnusedRule, at, Props(e.Id, ""), e.Line, "the declaration of '" + e.Id + "' changed since its review ([body:" + e.Body + "], now " + h + "): review it again, update the reason and the tag"));
            }
            foreach (DeterministicApiList.Entry e in s.List.Entries.Values)
                if (!s.UsedEntries.ContainsKey(e.Id) && (s.NetDefined || !e.NetOnly))
                    c.ReportDiagnostic(Diagnostic.Create(UnusedRule, at, Props(e.Id, ""), e.Line, "'" + e.Id + "' is not used by the scope (remove it, and regenerate the ledger line in docs/design/DETERMINISM_PROOF.md 5.4)"));
            foreach (DeterministicApiList.Registration r in s.List.Registrations)
                if (!s.UsedLines.ContainsKey(r.Line) && (s.NetDefined || !NetOnlyRegistration(s, r)))
                    c.ReportDiagnostic(Diagnostic.Create(UnusedRule, at, Props(r.Api ?? r.Member, r.Member), r.Line, r.Kind + " '" + r.Member + "' is not used by the scope (remove it, and regenerate the ledger line in docs/design/DETERMINISM_PROOF.md 5.4)"));
        }

        // a registration whose construct exists only when NET is defined: a site of a [NET] entry, a [SkipLocalsInit]
        // (an attribute that the scope applies under #if NET)
        static bool NetOnlyRegistration(State s, DeterministicApiList.Registration r)
        {
            DeterministicApiList.Entry e;
            if (r.Kind == "site")
                return s.List.Entries.TryGetValue(r.Api, out e) && e.NetOnly;
            return r.Kind == "skiplocalsinit";
        }
    }
}
