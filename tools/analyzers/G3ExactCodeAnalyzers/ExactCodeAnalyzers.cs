using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace G3ExactCodeAnalyzers
{
    /// <summary>
    /// G3E0001: no single precision (System.Single, float) in the exact-arithmetic code: its error bounds are derived
    /// for double. Reports every expression, declaration and signature whose type is float or contains it: float[],
    /// List&lt;float&gt;, and structs with float fields (g3.Vector3f, AxisAlignedBox3f, ...), whose values went through
    /// float rounding even when they are converted to double. The banned-API analyzer (RS0030) only sees member
    /// accesses such as float.Epsilon, not declarations, casts or literals.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class NoSinglePrecisionAnalyzer : DiagnosticAnalyzer
    {
        public const string DiagnosticId = "G3E0001";

        static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
            DiagnosticId,
            "No single precision in exact code",
            "Exact code: {0} has type '{1}'; the error bounds are derived for double",
            "Reliability",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description: "The exact predicates and their floating-point filters assume double precision. "
                + "The rule is enabled for the exact-arithmetic files in .editorconfig.");

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        static readonly OperationKind[] AllKinds = Enum.GetValues(typeof(OperationKind)).Cast<OperationKind>()
            .Where(k => k != OperationKind.None).Distinct().ToArray();

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterOperationAction(AnalyzeOperation, AllKinds);
            context.RegisterSymbolAction(AnalyzeSymbol, SymbolKind.Field, SymbolKind.Property, SymbolKind.Method,
                SymbolKind.Event);
        }

        /// <summary> float, or an array, pointer, generic type or struct (by its instance fields) with float in it </summary>
        public static bool ContainsSingle(ITypeSymbol t)
        {
            return ContainsSingle(t, 0);
        }

        static bool ContainsSingle(ITypeSymbol t, int depth)
        {
            if (t == null || depth > 8)
                return false;
            switch (t) {
                case IArrayTypeSymbol a:
                    return ContainsSingle(a.ElementType, depth + 1);
                case IPointerTypeSymbol p:
                    return ContainsSingle(p.PointedAtType, depth + 1);
                case INamedTypeSymbol n:
                    if (n.SpecialType == SpecialType.System_Single)
                        return true;
                    if (n.SpecialType != SpecialType.None)
                        return false;
                    foreach (ITypeSymbol arg in n.TypeArguments)
                        if (ContainsSingle(arg, depth + 1))
                            return true;
                    // a struct holds its fields by value (Vector3f, AxisAlignedBox3f); a class is a reference, its
                    // float fields (DMesh3's normals) are not read by passing it around
                    if (n.TypeKind == TypeKind.Struct && n.TypeKind != TypeKind.Enum)
                        foreach (ISymbol m in n.GetMembers())
                            if (m is IFieldSymbol f && !f.IsStatic && !f.IsConst && ContainsSingle(f.Type, depth + 1))
                                return true;
                    return false;
                default:
                    return false;
            }
        }

        static void AnalyzeOperation(OperationAnalysisContext ctx)
        {
            IOperation op = ctx.Operation;
            if (op is IVariableDeclaratorOperation d) {
                if (ContainsSingle(d.Symbol.Type))
                    ctx.ReportDiagnostic(Diagnostic.Create(Rule, d.Syntax.GetLocation(), "local '" + d.Symbol.Name + "'", d.Symbol.Type.ToDisplayString()));
                return;
            }
            if (!ContainsSingle(op.Type))
                return;
            // report the outermost float expression only (not each of its operands)
            if (op.Parent != null && ContainsSingle(op.Parent.Type))
                return;
            ctx.ReportDiagnostic(Diagnostic.Create(Rule, op.Syntax.GetLocation(), "expression '" + op.Syntax + "'", op.Type.ToDisplayString()));
        }

        static void AnalyzeSymbol(SymbolAnalysisContext ctx)
        {
            ISymbol s = ctx.Symbol;
            if (s.IsImplicitlyDeclared)
                return;
            switch (s) {
                case IFieldSymbol f:
                    Report(ctx, f, f.Type, "field");
                    break;
                case IPropertySymbol p:
                    Report(ctx, p, p.Type, "property");
                    foreach (IParameterSymbol ps in p.Parameters)
                        Report(ctx, ps, ps.Type, "parameter");
                    break;
                case IEventSymbol e:
                    Report(ctx, e, e.Type, "event");
                    break;
                case IMethodSymbol m:
                    if (m.AssociatedSymbol != null)     // accessors: reported with the property or event
                        return;
                    Report(ctx, m, m.ReturnType, "return value of");
                    foreach (IParameterSymbol ps in m.Parameters)
                        Report(ctx, ps, ps.Type, "parameter");
                    foreach (ITypeParameterSymbol tp in m.TypeParameters)
                        foreach (ITypeSymbol c in tp.ConstraintTypes)
                            Report(ctx, tp, c, "constraint of");
                    break;
            }
        }

        static void Report(SymbolAnalysisContext ctx, ISymbol s, ITypeSymbol t, string what)
        {
            if (!ContainsSingle(t))
                return;
            foreach (Location loc in s.Locations)
                if (loc.IsInSource)
                    ctx.ReportDiagnostic(Diagnostic.Create(Rule, loc, what + " '" + s.Name + "'", t.ToDisplayString()));
        }
    }

    /// <summary>
    /// G3E0002: no parallel loops in the exact code without review. A floating-point reduction across threads depends
    /// on the schedule, and an analyzer cannot see what a loop body aggregates, so every use of Parallel, PLINQ
    /// (ParallelEnumerable) and g3.gParallel is reported; reviewed uses are allowed per member in
    /// core/ExactCodeAllowList.cs. A rule of its own (not RS0030), so that the allow-list switches off only this
    /// check, not the FMA and float bans.
    ///
    /// G3E0003: no fused multiply-add: it rounds once instead of twice, and the error-free transformations and the
    /// filter bounds assume separately rounded products and sums. Reports every call of a framework method whose name
    /// is a multiply-add or multiply-subtract (Math / double.FusedMultiplyAdd, MultiplyAddEstimate, the Vector* and
    /// intrinsic FusedMultiplyAdd / MultiplyAdd* / MultiplySubtract* methods), also those added in later frameworks.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class ExactCodeApiAnalyzer : DiagnosticAnalyzer
    {
        public const string ParallelId = "G3E0002";
        public const string FmaId = "G3E0003";
        public const string LibmId = "G3E0004";

        static readonly DiagnosticDescriptor ParallelRule = new DiagnosticDescriptor(
            ParallelId,
            "No unreviewed parallel loop in exact code",
            "Exact code: '{0}' runs in parallel; it needs a review (no floating-point reduction across threads) and an entry in core/ExactCodeAllowList.cs",
            "Reliability",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        static readonly DiagnosticDescriptor FmaRule = new DiagnosticDescriptor(
            FmaId,
            "No fused multiply-add in exact code",
            "Exact code: '{0}' is a fused multiply-add; the error bounds assume separately rounded products and sums",
            "Reliability",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        static readonly DiagnosticDescriptor LibmRule = new DiagnosticDescriptor(
            LibmId,
            "No platform libm in code that promises cross-platform bit identity",
            "'{0}' is a transcendental function of the platform's libm (its last bit differs between Windows, Linux, macOS and .NET Framework); use g3.CoreMathPort.CoreMath (correctly rounded)",
            "Reliability",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(ParallelRule, FmaRule, LibmRule);

        /// <summary> the transcendental functions whose last bit depends on the platform (docs/design/DETERMINISM.md, libm rule);
        /// + - * / Sqrt Floor Ceiling Round Truncate Abs Min Max CopySign ScaleB are exact everywhere and not listed </summary>
        public static readonly ImmutableHashSet<string> LibmNames = ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase,
            "Sin", "Cos", "Tan", "Asin", "Acos", "Atan", "Atan2", "Sinh", "Cosh", "Tanh", "Asinh", "Acosh", "Atanh",
            "Exp", "Log", "Log2", "Log10", "Pow", "Cbrt", "SinCos", "Exp2", "Exp10", "Hypot", "SinPi", "CosPi", "TanPi",
            "AsinPi", "AcosPi", "AtanPi", "Atan2Pi", "ExpM1", "LogP1", "Log2P1", "Log10P1", "Exp2M1", "Exp10M1", "RootN", "Compound",
            // estimates that differ between x64 and ARM
            "ReciprocalEstimate", "ReciprocalSqrtEstimate");

        /// <summary> a platform libm function of System.Math / MathF / double / float </summary>
        public static bool IsPlatformLibm(IMethodSymbol m)
        {
            string type = m.ContainingType?.ToDisplayString() ?? "";
            if (type != "System.Math" && type != "System.MathF" && type != "double" && type != "float"
                && type != "System.Double" && type != "System.Single")
                return false;
            return LibmNames.Contains(m.Name);
        }

        /// <summary> the types whose members run work in parallel </summary>
        public static readonly ImmutableHashSet<string> ParallelTypes = ImmutableHashSet.Create(
            "System.Threading.Tasks.Parallel", "System.Linq.ParallelEnumerable", "g3.gParallel");

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterOperationAction(Analyze, OperationKind.Invocation, OperationKind.MethodReference,
                OperationKind.PropertyReference, OperationKind.FieldReference);
        }

        /// <summary> a framework method that multiplies and adds in one rounding </summary>
        public static bool IsFusedMultiplyAdd(IMethodSymbol m)
        {
            string ns = m.ContainingNamespace?.ToDisplayString() ?? "";
            if (!(ns == "System" || ns.StartsWith("System.", StringComparison.Ordinal)))
                return false;
            string name = m.Name;
            return name.IndexOf("FusedMultiply", StringComparison.Ordinal) >= 0
                || name.StartsWith("MultiplyAdd", StringComparison.Ordinal)
                || name.StartsWith("MultiplySubtract", StringComparison.Ordinal)
                || name.StartsWith("MultiplyAddNegated", StringComparison.Ordinal)
                || name.StartsWith("MultiplySubtractNegated", StringComparison.Ordinal);
        }

        static void Analyze(OperationAnalysisContext ctx)
        {
            ISymbol member;
            switch (ctx.Operation) {
                case IInvocationOperation i: member = i.TargetMethod; break;
                case IMethodReferenceOperation r: member = r.Method; break;
                case IPropertyReferenceOperation p: member = p.Property; break;
                case IFieldReferenceOperation f: member = f.Field; break;
                default: return;
            }
            INamedTypeSymbol type = member.ContainingType;
            if (type != null && ParallelTypes.Contains(type.OriginalDefinition.ToDisplayString()))
                ctx.ReportDiagnostic(Diagnostic.Create(ParallelRule, ctx.Operation.Syntax.GetLocation(), type.Name + "." + member.Name));
            if (member is IMethodSymbol m && IsFusedMultiplyAdd(m))
                ctx.ReportDiagnostic(Diagnostic.Create(FmaRule, ctx.Operation.Syntax.GetLocation(), type?.Name + "." + m.Name));
            if (member is IMethodSymbol lm && IsPlatformLibm(lm))
                ctx.ReportDiagnostic(Diagnostic.Create(LibmRule, ctx.Operation.Syntax.GetLocation(), type?.Name + "." + lm.Name));
        }
    }
}
