/* C ABI of the NativeAOT spike (docs/design/NATIVE_AOT_FEASIBILITY.md). Not a supported interface yet.
 * Ownership: every handle is created by the library and freed by its g3_*_free; strings and arrays returned by
 * a getter belong to the handle and stay valid until it is freed. Arrays the caller passes are copied. No call
 * throws or aborts across the boundary; all return a g3 error code (0 = ok). Thread safety: distinct handles may be
 * used from different threads; one handle must not be used by two threads at once. */
#ifndef G3NATIVE_H
#define G3NATIVE_H
#include <stdint.h>
#ifdef __cplusplus
extern "C" {
#endif
enum { G3_OK = 0, G3_NULL_ARGUMENT = 1, G3_INVALID_MESH = 2, G3_INVALID_INPUT = 3, G3_ROUNDING_NOT_CLEAN = 4,
       G3_BUDGET_EXCEEDED = 5, G3_CANCELLED = 6, G3_EXCEPTION = 7, G3_BAD_OPTIONS = 8 };
enum { G3_UNION = 0, G3_INTERSECTION = 1, G3_DIFFERENCE = 2 };
typedef struct g3_mesh g3_mesh;
typedef struct g3_result g3_result;
typedef struct { int32_t struct_size; int32_t safe_rounding; int32_t strict_rounding; int32_t allow_open_meshes;
                 int32_t seed; int32_t _pad; int64_t max_pieces; } g3_options;  /* g3_options_default fills struct_size */
int32_t g3_version(void);
void g3_options_default(g3_options* o);
const char* g3_last_error(void);               /* of the calling thread's last failing call */
int32_t g3_mesh_create(const double* xyz, int64_t nv, const int32_t* tri, int64_t nt, g3_mesh** out);
void g3_mesh_free(g3_mesh* m);
int32_t g3_mesh_counts(const g3_mesh* m, int64_t* nv, int64_t* nt);
/* a result handle is returned (and must be freed) also when the status is not G3_OK; it then carries g3_result_error */
int32_t g3_boolean(const g3_mesh* a, const g3_mesh* b, int32_t op, const g3_options* opt, g3_result** out);
int32_t g3_imprint(const g3_mesh* target, const g3_mesh* cutter, const g3_options* opt, g3_result** out);
void g3_result_free(g3_result* r);
int32_t g3_result_status(const g3_result* r);
int32_t g3_result_counts(const g3_result* r, int64_t* nv, int64_t* nt);
int32_t g3_result_copy(const g3_result* r, double* xyz /*3*nv*/, int32_t* tri /*3*nt*/);
const char* g3_result_error(const g3_result* r);
const char* g3_result_not_clean_reason(const g3_result* r);   /* "" if clean */
/* spike test helpers */
int32_t g3_test_make_sphere(int32_t edge_vertices, double radius, double cx, double cy, double cz, g3_mesh** out);
uint64_t g3_test_result_hash(const g3_result* r);
#ifdef __cplusplus
}
#endif
#endif
