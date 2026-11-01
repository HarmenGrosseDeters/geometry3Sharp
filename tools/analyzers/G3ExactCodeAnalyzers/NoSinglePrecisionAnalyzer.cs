using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace G3ExactCodeAnalyzers
{
    /// <summary>
    /// G3E0001: no single precision (System.Single, float) in the exact-arithmetic code: its error bounds are derived
    /// for double. Reports every expression, declaration and signature whose type is float or contains it (float[],
    /// List&lt;float&gt;). The banned-API analyzer (RS0030) only sees member accesses such as float.Epsilon, not
    /// declarations, casts or literals.
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
                + "The rule is enabled for math/Exact*.cs, math/AdaptivePredicates.cs and mesh_ops/ExactMesh*.cs in .editorconfig.");

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

        /// <summary> float, or an array, pointer or generic type with float in it </summary>
        public static bool ContainsSingle(ITypeSymbol t)
        {
            switch (t) {
                case null:
                    return false;
                case IArrayTypeSymbol a:
                    return ContainsSingle(a.ElementType);
                case IPointerTypeSymbol p:
                    return ContainsSingle(p.PointedAtType);
                case INamedTypeSymbol n:
                    if (n.SpecialType == SpecialType.System_Single)
                        return true;
                    foreach (ITypeSymbol arg in n.TypeArguments)
                        if (ContainsSingle(arg))
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
}
