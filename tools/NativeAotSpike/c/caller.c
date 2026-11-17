/* A tiny C caller of the NativeAOT library (g3native.so), written for the spike. Two things:
 *   caller selftest            : a cube and a shifted cube from plain arrays, union / intersection / difference, error paths
 *   caller bench <op> <n> <runs>: the offset-sphere boolean of the benchmark, same output line as the managed driver */
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <time.h>
#include "../g3native.h"

static double now_ms(void) { struct timespec t; clock_gettime(CLOCK_MONOTONIC, &t); return t.tv_sec * 1e3 + t.tv_nsec / 1e6; }
static int cmp(const void* a, const void* b) { double x = *(const double*)a, y = *(const double*)b; return (x > y) - (x < y); }

static void cube(double ox, double oy, double oz, double* v, int32_t* t)
{
    for (int i = 0; i < 8; i++) { v[3*i] = ox + (i & 1); v[3*i+1] = oy + ((i >> 1) & 1); v[3*i+2] = oz + ((i >> 2) & 1); }
    static const int32_t q[12][3] = { {0,2,1},{1,2,3}, {4,5,6},{5,7,6}, {0,1,4},{1,5,4}, {2,6,3},{3,6,7}, {0,4,2},{2,4,6}, {1,3,5},{3,7,5} };
    memcpy(t, q, sizeof q);
}

static int selftest(void)
{
    double va[24], vb[24]; int32_t ta[36], tb[36];
    cube(0, 0, 0, va, ta); cube(0.5, 0.5, 0.5, vb, tb);
    g3_mesh *a = 0, *b = 0;
    if (g3_mesh_create(va, 8, ta, 12, &a) || g3_mesh_create(vb, 8, tb, 12, &b)) { printf("create failed: %s\n", g3_last_error()); return 1; }
    const char* names[3] = { "union", "intersection", "difference" };
    for (int op = 0; op < 3; op++) {
        g3_result* r = 0;
        int rc = g3_boolean(a, b, op, NULL, &r);
        int64_t nv = 0, nt = 0; g3_result_counts(r, &nv, &nt);
        printf("%-12s rc=%d status=%d vertices=%lld triangles=%lld not_clean_reason='%s'\n", names[op], rc, g3_result_status(r), (long long)nv, (long long)nt, g3_result_not_clean_reason(r));
        if (rc == 0) {   /* copy the result out, caller-owned arrays */
            double* xyz = malloc(sizeof(double) * 3 * nv); int32_t* tri = malloc(sizeof(int32_t) * 3 * nt);
            g3_result_copy(r, xyz, tri);
            double vol = 0;   /* signed volume from the triangles: the C side can use the arrays without any .NET type */
            for (int64_t i = 0; i < nt; i++) {
                const double *p = xyz + 3 * tri[3*i], *q = xyz + 3 * tri[3*i+1], *s = xyz + 3 * tri[3*i+2];
                vol += (p[0]*(q[1]*s[2]-q[2]*s[1]) - p[1]*(q[0]*s[2]-q[2]*s[0]) + p[2]*(q[0]*s[1]-q[1]*s[0])) / 6.0;
            }
            printf("             volume=%.6f\n", vol);
            free(xyz); free(tri);
        }
        g3_result_free(r);
    }
    /* error paths: bad index, null, bad operation */
    int32_t bad[3] = { 0, 1, 99 }; g3_mesh* m = 0;
    int rc0 = g3_mesh_create(va, 8, bad, 1, &m);
    printf("bad index    rc=%d (%s)\n", rc0, g3_last_error());
    printf("null mesh    rc=%d\n", g3_boolean(NULL, b, 0, NULL, &(g3_result*){0}));
    g3_result* r = 0;
    printf("bad op       rc=%d\n", g3_boolean(a, b, 7, NULL, &r));
    g3_options o; g3_options_default(&o); o.max_pieces = 3;   /* a budget the pair exceeds */
    int rc = g3_boolean(a, b, 0, &o, &r);
    printf("max_pieces=3 rc=%d error='%s'\n", rc, r ? g3_result_error(r) : "");
    g3_result_free(r);
    g3_mesh_free(a); g3_mesh_free(b);
    return 0;
}

static int bench(int op, int n, int runs)
{
    g3_mesh *a = 0, *b = 0; g3_result* r = 0; double ms[64];
    if (runs > 64) runs = 64;
    g3_test_make_sphere(n, 1.0, 0, 0, 0, &a); g3_test_make_sphere(n, 0.9, 0.7, 0.3, 0.2, &b);
    for (int i = 0; i < runs; i++) {
        double t0 = now_ms();
        int rc = op == 3 ? g3_imprint(a, b, NULL, &r) : g3_boolean(a, b, op, NULL, &r);
        ms[i] = now_ms() - t0;
        if (rc) { printf("failed rc=%d %s\n", rc, g3_last_error()); return 1; }
        if (i + 1 < runs) g3_result_free(r);
    }
    int64_t nv, nt; g3_result_counts(r, &nv, &nt);
    double first = ms[0]; qsort(ms, runs, sizeof(double), cmp);
    printf("native  op=%s n=%d first_ms=%.1f median_ms=%.1f tris=%lld hash=%016llx reason='%s'\n", op == 3 ? "imprint" : (const char*[]){"0","1","2"}[op], n, first, ms[runs / 2], (long long)nt, (unsigned long long)g3_test_result_hash(r), g3_result_not_clean_reason(r));
    g3_result_free(r); g3_mesh_free(a); g3_mesh_free(b);
    return 0;
}

int main(int argc, char** argv)
{
    if (argc >= 2 && !strcmp(argv[1], "selftest")) return selftest();
    if (argc >= 5 && !strcmp(argv[1], "bench")) return bench(!strcmp(argv[2], "imprint") ? 3 : atoi(argv[2]), atoi(argv[3]), atoi(argv[4]));
    fprintf(stderr, "usage: caller selftest | caller bench <0|1|2|imprint> <edgeVertices> <runs>\n");
    return 2;
}
