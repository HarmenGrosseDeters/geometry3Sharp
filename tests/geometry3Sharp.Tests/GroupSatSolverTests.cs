using System;
using System.Collections.Generic;
using System.Linq;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// gs.GroupSatSolver (DPLL with watched literals, exactly-one groups, a decision budget): against brute force on random
    /// instances, the value order of the groups, the budget, and propagation.
    /// </summary>
    public class GroupSatSolverTests
    {
        // groups of three variables (exactly one), plus random clauses; returns the solver and the extra clauses
        static (GroupSatSolver, int[][], List<int[]>) Instance(Random rnd, int ng, int nExtra)
        {
            int nv = 3 * ng;
            var d = new GroupSatSolver(nv);
            var groups = new int[ng][];
            for (int k = 0; k < ng; ++k) {
                groups[k] = new[] { 3 * k + 1, 3 * k + 2, 3 * k + 3 };
                d.AddClause(3 * k + 1, 3 * k + 2, 3 * k + 3);
                d.AddClause(-(3 * k + 1), -(3 * k + 2)); d.AddClause(-(3 * k + 1), -(3 * k + 3)); d.AddClause(-(3 * k + 2), -(3 * k + 3));
            }
            var extra = new List<int[]>();
            for (int c = 0; c < nExtra; ++c) {
                int len = 1 + rnd.Next(3); var lits = new int[len];
                for (int i = 0; i < len; ++i) lits[i] = (rnd.Next(nv) + 1) * (rnd.Next(2) == 0 ? 1 : -1);
                extra.Add(lits); d.AddClause(lits);
            }
            return (d, groups, extra);
        }

        static bool Holds(int[] clause, bool[] a) => clause.Any(l => l > 0 ? a[l - 1] : !a[-l - 1]);

        [Fact]
        public void RandomInstances_AgreeWithBruteForce()
        {
            var rnd = new Random(5); int bad = 0, sat = 0, unsat = 0;
            for (int it = 0; it < 600; ++it) {
                int ng = 1 + rnd.Next(5);
                var (d, groups, extra) = Instance(rnd, ng, rnd.Next(1, 3 * ng + 1));
                bool expected = false;
                for (int code = 0; code < (int)Math.Pow(3, ng) && !expected; ++code) {
                    int cc = code; var asg = new bool[3 * ng];
                    for (int k = 0; k < ng; ++k) { asg[3 * k + cc % 3] = true; cc /= 3; }
                    if (extra.All(c => Holds(c, asg))) expected = true;
                }
                int r = d.Solve(groups, out bool[] model);
                if (expected) sat++; else unsat++;
                if ((r == GroupSatSolver.Satisfiable) != expected) bad++;
                if (r == GroupSatSolver.Satisfiable) {
                    foreach (var c in extra) Assert.True(Holds(c, model), "iteration " + it);
                    foreach (var g in groups) Assert.Equal(1, g.Count(l => model[l - 1]));
                } else Assert.Null(model);
            }
            Assert.Equal(0, bad);
            Assert.True(sat > 100 && unsat > 100, sat + " satisfiable, " + unsat + " unsatisfiable");
        }

        [Fact]
        public void FirstModel_FollowsTheValueOrderOfTheGroups()
        {
            var d = new GroupSatSolver(6);
            d.AddClause(1, 2, 3); d.AddClause(4, 5, 6);
            d.AddClause(-1, -2); d.AddClause(-1, -3); d.AddClause(-2, -3);
            d.AddClause(-4, -5); d.AddClause(-4, -6); d.AddClause(-5, -6);
            // the preferred values: variable 3 for the first group, 5 for the second
            Assert.Equal(GroupSatSolver.Satisfiable, d.Solve(new[] { new[] { 3, 1, 2 }, new[] { 5, 4, 6 } }, out bool[] model));
            Assert.Equal(new[] { false, false, true, false, true, false }, model);
            Assert.Equal(2, d.Decisions);
            // a clause that forbids the preferred value of the second group together with the first: the next value
            d = new GroupSatSolver(6);
            d.AddClause(1, 2, 3); d.AddClause(4, 5, 6);
            d.AddClause(-1, -2); d.AddClause(-1, -3); d.AddClause(-2, -3);
            d.AddClause(-4, -5); d.AddClause(-4, -6); d.AddClause(-5, -6);
            d.AddClause(-3, -5);
            Assert.Equal(GroupSatSolver.Satisfiable, d.Solve(new[] { new[] { 3, 1, 2 }, new[] { 5, 4, 6 } }, out model));
            Assert.Equal(new[] { false, false, true, true, false, false }, model);
        }

        [Fact]
        public void UnitClausesAndEmptyClause()
        {
            var d = new GroupSatSolver(3);
            d.AddClause(1); d.AddClause(-1, 2); d.AddClause(-2, 3);
            Assert.Equal(GroupSatSolver.Satisfiable, d.Solve(new int[0][], out bool[] m));
            Assert.Equal(new[] { true, true, true }, m);
            Assert.Equal(0, d.Decisions);                           // propagation alone
            d = new GroupSatSolver(2); d.AddClause(1); d.AddClause(-1);
            Assert.Equal(GroupSatSolver.Unsatisfiable, d.Solve(new int[0][], out m));
            d = new GroupSatSolver(2); d.AddClause(new int[0]);
            Assert.Equal(GroupSatSolver.Unsatisfiable, d.Solve(new int[0][], out m));
            Assert.Throws<ArgumentOutOfRangeException>(() => new GroupSatSolver(2).AddClause(3));
            Assert.Throws<ArgumentOutOfRangeException>(() => new GroupSatSolver(2).AddClause(0));
        }

        // n groups of three values, every pair of groups must differ in the value: unsatisfiable for n = 4 (a pigeonhole)
        static (GroupSatSolver, int[][]) Pigeons(int n)
        {
            var d = new GroupSatSolver(3 * n); var groups = new int[n][];
            for (int k = 0; k < n; ++k) {
                groups[k] = new[] { 3 * k + 1, 3 * k + 2, 3 * k + 3 };
                d.AddClause(groups[k]);
                for (int a = 0; a < 3; ++a) for (int b = a + 1; b < 3; ++b) d.AddClause(-groups[k][a], -groups[k][b]);
            }
            for (int k = 0; k < n; ++k) for (int l = k + 1; l < n; ++l) for (int v = 0; v < 3; ++v) d.AddClause(-(3 * k + v + 1), -(3 * l + v + 1));
            return (d, groups);
        }

        [Fact]
        public void Budget_StopsTheSearchAndIsCountedInDecisions()
        {
            var (d, g) = Pigeons(4);
            Assert.Equal(GroupSatSolver.Unsatisfiable, d.Solve(g, out _));
            long full = d.Decisions;
            Assert.True(full > 5);
            // a budget below the effort: Budget, after exactly budget + 1 counted decisions
            (d, g) = Pigeons(4);
            d.DecisionBudget = full - 3;
            Assert.Equal(GroupSatSolver.Budget, d.Solve(g, out bool[] model));
            Assert.Null(model);
            Assert.Equal(full - 2, d.Decisions);
            // a budget of exactly the effort is enough
            (d, g) = Pigeons(4);
            d.DecisionBudget = full;
            Assert.Equal(GroupSatSolver.Unsatisfiable, d.Solve(g, out _));
            Assert.Equal(full, d.Decisions);
            // three groups are satisfiable
            (d, g) = Pigeons(3);
            Assert.Equal(GroupSatSolver.Satisfiable, d.Solve(g, out model));
        }

        [Fact]
        public void Solve_IsRepeatable()
        {
            var rnd = new Random(9);
            var (d0, g0, _) = Instance(rnd, 5, 9);
            d0.Solve(g0, out bool[] first);
            for (int rep = 0; rep < 3; ++rep) {
                var (d, g, _) = Instance(new Random(9), 5, 9);
                d.Solve(g, out bool[] again);
                Assert.Equal(first, again); Assert.Equal(d0.Decisions, d.Decisions);
            }
        }
    }
}
