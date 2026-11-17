| class | method | pairs | operations | good | failed / exception / crash / timeout | opened / not closed | unclean | gaps | curve found | nothing inserted | rounding not clean | median ms | max ms |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| ulp-shift-few | exact_imprint | 100 | 200 | 8 | 0 / 0 / 0 / 0 | 0 | degenerate triangles 192 | – | 200 | – | 192 | 2173 | 10441 |
| ulp-shift-few | float_cut | 100 | 200 | 199 | 0 / 0 / 0 / 0 | 0 | degenerate triangles 1 | 0 | 200 | 198 | – | 193 | 629 |
| ulp-shift-few | exact_union | 100 | 100 | 100 | 0 / 0 / 0 / 0 | 0 | – | – | – | – | – | 956 | 21312 |
| ulp-shift-few | float_union | 100 | 100 | 0 | 0 / 0 / 0 / 0 | 100 | – | – | – | – | – | 1089 | 9929 |

Time ratio float / exact (geometric mean over the operations both methods ran ok; ms of the operation alone, at least 1):

| class | cut: float_cut / exact_imprint | operations | union: float_union / exact_union | operations |
|---|---|---|---|---|
| ulp-shift-few | 0.08 | 200 | 0.83 | 100 |

Disagreements (exact against float, per operation): 299 (ulp-shift-few: float inserted nothing 198, ulp-shift-few: only exact good 100, ulp-shift-few: only float good 1)

| class | case | operation | kind | exact | float |
|---|---|---|---|---|---|
| ulp-shift-few | ulp-shift-few_p0000_280283+280283 | a_by_b | float inserted nothing (exact: 332 vertices) | unclean: degenerate triangles | good |
| ulp-shift-few | ulp-shift-few_p0000_280283+280283 | b_by_a | float inserted nothing (exact: 365 vertices) | unclean: degenerate triangles | good |
| ulp-shift-few | ulp-shift-few_p0000_280283+280283 | union | only exact good | good | not closed |
| ulp-shift-few | ulp-shift-few_p0001_78738+78738 | a_by_b | float inserted nothing (exact: 39713 vertices) | unclean: degenerate triangles | good |
| ulp-shift-few | ulp-shift-few_p0001_78738+78738 | b_by_a | float inserted nothing (exact: 39206 vertices) | unclean: degenerate triangles | good |
| ulp-shift-few | ulp-shift-few_p0001_78738+78738 | union | only exact good | good | not closed |
| ulp-shift-few | ulp-shift-few_p0002_51012+51012 | a_by_b | float inserted nothing (exact: 310 vertices) | unclean: degenerate triangles | good |
| ulp-shift-few | ulp-shift-few_p0002_51012+51012 | b_by_a | float inserted nothing (exact: 639 vertices) | unclean: degenerate triangles | good |
| ulp-shift-few | ulp-shift-few_p0002_51012+51012 | union | only exact good | good | not closed |
| ulp-shift-few | ulp-shift-few_p0003_106557+106557 | a_by_b | float inserted nothing (exact: 461 vertices) | unclean: degenerate triangles | good |
| ulp-shift-few | ulp-shift-few_p0003_106557+106557 | b_by_a | float inserted nothing (exact: 812 vertices) | unclean: degenerate triangles | good |
| ulp-shift-few | ulp-shift-few_p0003_106557+106557 | union | only exact good | good | not closed |
| ulp-shift-few | ulp-shift-few_p0004_59706+59706 | a_by_b | float inserted nothing (exact: 1307 vertices) | unclean: degenerate triangles | good |
| ulp-shift-few | ulp-shift-few_p0004_59706+59706 | b_by_a | float inserted nothing (exact: 1184 vertices) | unclean: degenerate triangles | good |
| ulp-shift-few | ulp-shift-few_p0004_59706+59706 | union | only exact good | good | not closed |
| ulp-shift-few | ulp-shift-few_p0005_567031+567031 | a_by_b | float inserted nothing (exact: 738 vertices) | unclean: degenerate triangles | good |
| ulp-shift-few | ulp-shift-few_p0005_567031+567031 | b_by_a | float inserted nothing (exact: 712 vertices) | unclean: degenerate triangles | good |
| ulp-shift-few | ulp-shift-few_p0005_567031+567031 | union | only exact good | good | not closed |
| ulp-shift-few | ulp-shift-few_p0006_1454016+1454016 | a_by_b | float inserted nothing (exact: 13320 vertices) | unclean: degenerate triangles | good |
| ulp-shift-few | ulp-shift-few_p0006_1454016+1454016 | b_by_a | float inserted nothing (exact: 13185 vertices) | unclean: degenerate triangles | good |
| ulp-shift-few | ulp-shift-few_p0006_1454016+1454016 | union | only exact good | good | not closed |
| ulp-shift-few | ulp-shift-few_p0007_1228190+1228190 | a_by_b | float inserted nothing (exact: 13809 vertices) | unclean: degenerate triangles | good |
| ulp-shift-few | ulp-shift-few_p0007_1228190+1228190 | b_by_a | float inserted nothing (exact: 13865 vertices) | unclean: degenerate triangles | good |
| ulp-shift-few | ulp-shift-few_p0007_1228190+1228190 | union | only exact good | good | not closed |
| ulp-shift-few | ulp-shift-few_p0008_400238+400238 | a_by_b | float inserted nothing (exact: 3347 vertices) | unclean: degenerate triangles | good |
| ulp-shift-few | ulp-shift-few_p0008_400238+400238 | b_by_a | float inserted nothing (exact: 2875 vertices) | unclean: degenerate triangles | good |
| ulp-shift-few | ulp-shift-few_p0008_400238+400238 | union | only exact good | good | not closed |
| ulp-shift-few | ulp-shift-few_p0009_130969+130969 | a_by_b | float inserted nothing (exact: 556 vertices) | unclean: degenerate triangles | good |
| ulp-shift-few | ulp-shift-few_p0009_130969+130969 | b_by_a | float inserted nothing (exact: 676 vertices) | unclean: degenerate triangles | good |
| ulp-shift-few | ulp-shift-few_p0009_130969+130969 | union | only exact good | good | not closed |
| ulp-shift-few | ulp-shift-few_p0010_116293+116293 | a_by_b | float inserted nothing (exact: 2797 vertices) | unclean: degenerate triangles | good |
| ulp-shift-few | ulp-shift-few_p0010_116293+116293 | b_by_a | float inserted nothing (exact: 2866 vertices) | unclean: degenerate triangles | good |
| ulp-shift-few | ulp-shift-few_p0010_116293+116293 | union | only exact good | good | not closed |
| ulp-shift-few | ulp-shift-few_p0011_1146180+1146180 | a_by_b | float inserted nothing (exact: 3237 vertices) | unclean: degenerate triangles | good |
| ulp-shift-few | ulp-shift-few_p0011_1146180+1146180 | b_by_a | float inserted nothing (exact: 3195 vertices) | unclean: degenerate triangles | good |
| ulp-shift-few | ulp-shift-few_p0011_1146180+1146180 | union | only exact good | good | not closed |
| ulp-shift-few | ulp-shift-few_p0012_224829+224829 | a_by_b | float inserted nothing (exact: 2 vertices) | good | good |
| ulp-shift-few | ulp-shift-few_p0012_224829+224829 | b_by_a | float inserted nothing (exact: 3 vertices) | good | good |
| ulp-shift-few | ulp-shift-few_p0012_224829+224829 | union | only exact good | good | not closed |
| ulp-shift-few | ulp-shift-few_p0013_1315831+1315831 | a_by_b | float inserted nothing (exact: 666 vertices) | unclean: degenerate triangles | good |
| ulp-shift-few | ulp-shift-few_p0013_1315831+1315831 | b_by_a | float inserted nothing (exact: 659 vertices) | unclean: degenerate triangles | good |
| ulp-shift-few | ulp-shift-few_p0013_1315831+1315831 | union | only exact good | good | not closed |
| ulp-shift-few | ulp-shift-few_p0014_200968+200968 | a_by_b | float inserted nothing (exact: 511 vertices) | unclean: degenerate triangles | good |
| ulp-shift-few | ulp-shift-few_p0014_200968+200968 | b_by_a | float inserted nothing (exact: 513 vertices) | unclean: degenerate triangles | good |
| ulp-shift-few | ulp-shift-few_p0014_200968+200968 | union | only exact good | good | not closed |
| ulp-shift-few | ulp-shift-few_p0015_591218+591218 | a_by_b | float inserted nothing (exact: 731 vertices) | unclean: degenerate triangles | good |
| ulp-shift-few | ulp-shift-few_p0015_591218+591218 | b_by_a | float inserted nothing (exact: 689 vertices) | unclean: degenerate triangles | good |
| ulp-shift-few | ulp-shift-few_p0015_591218+591218 | union | only exact good | good | not closed |
| ulp-shift-few | ulp-shift-few_p0016_1494102+1494102 | a_by_b | float inserted nothing (exact: 1301 vertices) | unclean: degenerate triangles | good |
| ulp-shift-few | ulp-shift-few_p0016_1494102+1494102 | b_by_a | float inserted nothing (exact: 1220 vertices) | unclean: degenerate triangles | good |
| ulp-shift-few | ulp-shift-few_p0016_1494102+1494102 | union | only exact good | good | not closed |
| ulp-shift-few | ulp-shift-few_p0017_44396+44396 | a_by_b | float inserted nothing (exact: 1981 vertices) | unclean: degenerate triangles | good |
| ulp-shift-few | ulp-shift-few_p0017_44396+44396 | b_by_a | float inserted nothing (exact: 2000 vertices) | unclean: degenerate triangles | good |
| ulp-shift-few | ulp-shift-few_p0017_44396+44396 | union | only exact good | good | not closed |
| ulp-shift-few | ulp-shift-few_p0018_96076+96076 | a_by_b | float inserted nothing (exact: 179 vertices) | unclean: degenerate triangles | good |
| ulp-shift-few | ulp-shift-few_p0018_96076+96076 | b_by_a | float inserted nothing (exact: 193 vertices) | unclean: degenerate triangles | good |
| ulp-shift-few | ulp-shift-few_p0018_96076+96076 | union | only exact good | good | not closed |
| ulp-shift-few | ulp-shift-few_p0019_1146195+1146195 | a_by_b | float inserted nothing (exact: 3530 vertices) | unclean: degenerate triangles | good |
| ulp-shift-few | ulp-shift-few_p0019_1146195+1146195 | b_by_a | float inserted nothing (exact: 3662 vertices) | unclean: degenerate triangles | good |
| ulp-shift-few | ulp-shift-few_p0019_1146195+1146195 | union | only exact good | good | not closed |

(239 more in the raw rows)
