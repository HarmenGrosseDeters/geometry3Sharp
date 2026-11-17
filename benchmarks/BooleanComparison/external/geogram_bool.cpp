// Geogram exact mesh booleans (MeshSurfaceIntersection, Levy 2024, "Exact predicates, exact constructions and combinatorics
// for mesh CSG"), for the comparison in benchmarks/BooleanComparison (README.md, "Geogram"). Geogram is BSD-3 (Inria); this
// driver is the repo's own code and only calls its public API. It is built on a GitHub-hosted runner only
// (external/build_geogram.sh); the binary is not distributed.
// Usage: geogram_bool <union|intersection|difference> A.off B.off out.off
//        geogram_bool seq ops.txt out.off
// Prints one JSON line: ms of the boolean (without I/O), or the reason it did not run. seq runs a list of operations in
// memory, one per line, the result of line i in slot i: "load <path>" or "<union|intersection|difference> <i> <j>"; it
// writes the result of the last line and prints the summed time of the booleans and the number done (the format of
// cgal_bool seq). Between the steps of a sequence the result is the double mesh geogram returns.
// The boolean is mesh_boolean_operation(result, A, B, "A+B" | "A*B" | "A-B", MESH_BOOL_OPS_DEFAULT), the entry point geogram
// documents for two closed surfaces: exact intersections (expansion_nt homogeneous points), radial sort, classification, and
// the merge and Delaunay retriangulation of coplanar facets. A new vertex gets the double PCK::approximate of its exact point
// (MeshSurfaceIntersection::find_or_create_exact_vertex); the result mesh holds these doubles, and they are written with 17
// digits, so the file has exactly the doubles geogram returns.
// Threads: GEOGRAM_THREADS (environment) caps Process::set_max_threads; unset: all cores (geogram's default). parallel_for sizes
// its thread groups by Process::maximum_concurrent_threads(), which returns that cap; the JSON line reports it ("threads").
// Warm-up: ours times its boolean after an untimed warm-up run (the JIT). With GEOGRAM_WARMUP=1 a single boolean is run twice on
// the same inputs: "cold_ms" is the first run, "ms" the second (the result of the second is written); unset, "ms" is the only run.
// "ms" has microsecond resolution (a fraction), so no fast operation is reported as 0.
// Geogram does not check its inputs (closed, without self-intersections); neither does this driver, so it never refuses.
#include <geogram/basic/common.h>
#include <geogram/basic/assert.h>
#include <geogram/basic/logger.h>
#include <geogram/basic/process.h>
#include <geogram/mesh/mesh.h>
#include <geogram/mesh/mesh_surface_intersection.h>

#include <chrono>
#include <cstdio>
#include <cstdlib>
#include <fstream>
#include <memory>
#include <sstream>
#include <string>
#include <vector>

using GEO::index_t;
using GEO::Mesh;

// the reason as a JSON string body: no quotes, backslashes or control characters
static std::string clean(const std::string& s)
{
    std::string r;
    for (char c : s) r += (c == '"' || c == '\\' || (unsigned char)c < 32) ? ' ' : c;
    return r.substr(0, 200);
}

// ASCII OFF (as BooleanComparison and cgal_bool write it): "OFF", counts, vertices, faces "n i0 .. in-1" (a polygon is
// split into a fan, as the eval of BooleanComparison reads it). Comments (#) and blank lines are skipped.
static bool read_off(const std::string& path, Mesh& m)
{
    std::ifstream f(path);
    if (!f) return false;
    std::vector<std::string> lines;
    for (std::string l; std::getline(f, l);) {
        size_t h = l.find('#');
        if (h != std::string::npos) l = l.substr(0, h);
        if (l.find_first_not_of(" \t\r") != std::string::npos) lines.push_back(l);
    }
    size_t k = 0;
    if (k < lines.size() && lines[k].compare(0, 3, "OFF") == 0) {
        std::string rest = lines[k].substr(3);
        if (rest.find_first_not_of(" \t\r") == std::string::npos) ++k; else lines[k] = rest;
    }
    if (k >= lines.size()) return false;
    long nv = -1, nf = -1;
    { std::istringstream ss(lines[k++]); ss >> nv >> nf; }
    if (nv < 0 || nf < 0 || k + (size_t)nv + (size_t)nf > lines.size()) return false;
    m.clear();
    m.vertices.set_dimension(3);
    if (nv > 0) m.vertices.create_vertices((index_t)nv);
    for (long i = 0; i < nv; ++i) {
        std::istringstream ss(lines[k++]);
        double* p = m.vertices.point_ptr((index_t)i);
        if (!(ss >> p[0] >> p[1] >> p[2])) return false;
    }
    for (long i = 0; i < nf; ++i) {
        std::istringstream ss(lines[k++]);
        long n = 0;
        if (!(ss >> n) || n < 3) return false;
        std::vector<long> v((size_t)n);
        for (long j = 0; j < n; ++j)
            if (!(ss >> v[(size_t)j]) || v[(size_t)j] < 0 || v[(size_t)j] >= nv) return false;
        for (long j = 1; j + 1 < n; ++j)
            m.facets.create_triangle((index_t)v[0], (index_t)v[(size_t)j], (index_t)v[(size_t)j + 1]);
    }
    m.facets.connect();
    return true;
}

// the vertices that a facet uses (in order of first use), with full double precision; facets as they are (geogram's result
// is triangulated; a polygon would be written as a polygon, and the eval splits it into a fan)
static bool write_off(const Mesh& m, const char* path)
{
    std::vector<long> id(m.vertices.nb(), -1);
    std::vector<index_t> order;
    for (index_t f = 0; f < m.facets.nb(); ++f)
        for (index_t j = 0; j < m.facets.nb_vertices(f); ++j) {
            index_t v = m.facets.vertex(f, j);
            if (id[v] < 0) { id[v] = (long)order.size(); order.push_back(v); }
        }
    FILE* out = std::fopen(path, "w");
    if (!out) return false;
    std::fprintf(out, "OFF\n%zu %u 0\n", order.size(), (unsigned)m.facets.nb());
    for (index_t v : order) {
        const double* p = m.vertices.point_ptr(v);
        std::fprintf(out, "%.17g %.17g %.17g\n", p[0], p[1], p[2]);
    }
    for (index_t f = 0; f < m.facets.nb(); ++f) {
        std::fprintf(out, "%u", (unsigned)m.facets.nb_vertices(f));
        for (index_t j = 0; j < m.facets.nb_vertices(f); ++j) std::fprintf(out, " %ld", id[m.facets.vertex(f, j)]);
        std::fprintf(out, "\n");
    }
    return std::fclose(out) == 0;
}

static const char* expr_of(const std::string& op)
{
    if (op == "union") return "A+B";
    if (op == "intersection") return "A*B";
    if (op == "difference") return "A-B";
    return nullptr;
}

// one boolean; false and the reason on an exception (geogram assertions throw, set_assert_mode(ASSERT_THROW))
static bool boolean_op(Mesh& result, const Mesh& a, const Mesh& b, const char* expr, double& ms, std::string& error)
{
    auto t0 = std::chrono::steady_clock::now();
    try {
        GEO::mesh_boolean_operation(result, a, b, expr, GEO::MESH_BOOL_OPS_DEFAULT);
    } catch (std::exception& e) {
        error = std::string("exception: ") + e.what();
    } catch (...) {
        error = "exception";
    }
    ms = std::chrono::duration_cast<std::chrono::microseconds>(std::chrono::steady_clock::now() - t0).count() / 1000.0;
    return error.empty();
}

static int run(const std::string& op, const char* fa, const char* fb, const char* fout)
{
    const char* expr = expr_of(op);
    if (!expr) { std::printf("{\"ok\":false,\"error\":\"unknown operation\"}\n"); return 1; }
    Mesh a, b, out;
    if (!read_off(fa, a) || !read_off(fb, b)) { std::printf("{\"ok\":false,\"error\":\"read failed (not an OFF mesh)\"}\n"); return 0; }
    double ms = 0, cold = -1;
    std::string error;
    const char* w = std::getenv("GEOGRAM_WARMUP");
    if (w && std::string(w) == "1") {
        Mesh first;
        if (!boolean_op(first, a, b, expr, cold, error)) {
            std::printf("{\"ok\":false,\"ms\":%.3f,\"error\":\"%s\"}\n", cold, clean(error).c_str());
            return 0;
        }
    }
    if (!boolean_op(out, a, b, expr, ms, error)) {
        std::printf("{\"ok\":false,\"ms\":%.3f,\"error\":\"%s\"}\n", ms, clean(error).c_str());
        return 0;
    }
    if (!write_off(out, fout)) { std::printf("{\"ok\":false,\"ms\":%.3f,\"error\":\"driver: cannot write the result\"}\n", ms); return 0; }
    std::printf("{\"ok\":true,\"ms\":%.3f,", ms);
    if (cold >= 0) std::printf("\"cold_ms\":%.3f,", cold);
    std::printf("\"threads\":%u}\n", (unsigned)GEO::Process::maximum_concurrent_threads());
    return 0;
}

static int run_seq(const char* fops, const char* fout)
{
    std::ifstream in(fops);
    std::vector<std::string> lines;
    for (std::string line; std::getline(in, line);)
        if (!line.empty()) lines.push_back(line);
    if (lines.empty()) { std::printf("{\"ok\":false,\"error\":\"driver: bad ops file (no operations)\"}\n"); return 0; }
    // free each slot after its last use
    std::vector<size_t> last(lines.size());
    std::vector<std::vector<int>> args(lines.size());
    for (size_t i = 0; i < lines.size(); ++i) {
        std::istringstream ss(lines[i]);
        std::string op; ss >> op;
        last[i] = i;
        if (op != "load")
            for (int x; ss >> x;) {
                if (x < 0 || (size_t)x >= i) { std::printf("{\"ok\":false,\"error\":\"driver: bad ops file (bad operand)\"}\n"); return 0; }
                args[i].push_back(x); last[(size_t)x] = i;
            }
    }
    std::vector<std::unique_ptr<Mesh>> slots(lines.size());
    double ms = 0;
    int booleans = 0;
    for (size_t i = 0; i < lines.size(); ++i) {
        std::istringstream ss(lines[i]);
        std::string op; ss >> op;
        slots[i].reset(new Mesh());
        if (op == "load") {
            std::string path; ss >> path;
            if (!read_off(path, *slots[i])) { std::printf("{\"ok\":false,\"error\":\"read failed (not an OFF mesh)\"}\n"); return 0; }
            continue;
        }
        const char* expr = expr_of(op);
        if (!expr || args[i].size() != 2 || !slots[(size_t)args[i][0]] || !slots[(size_t)args[i][1]]) {
            std::printf("{\"ok\":false,\"error\":\"driver: bad ops file (bad operation)\"}\n");
            return 0;
        }
        double t = 0;
        std::string error;
        bool ok = boolean_op(*slots[i], *slots[(size_t)args[i][0]], *slots[(size_t)args[i][1]], expr, t, error);
        ms += t;
        if (!ok) { std::printf("{\"ok\":false,\"ms\":%.3f,\"done\":%d,\"error\":\"%s\"}\n", ms, booleans, clean(error).c_str()); return 0; }
        ++booleans;
        for (int x : args[i]) if (last[(size_t)x] == i) slots[(size_t)x].reset();
    }
    if (!write_off(*slots.back(), fout)) { std::printf("{\"ok\":false,\"ms\":%.3f,\"done\":%d,\"error\":\"driver: cannot write the result\"}\n", ms, booleans); return 0; }
    std::printf("{\"ok\":true,\"ms\":%.3f,\"done\":%d,\"threads\":%u}\n", ms, booleans, (unsigned)GEO::Process::maximum_concurrent_threads());
    return 0;
}

int main(int argc, char** argv)
{
    if (argc < 4) {
        std::fprintf(stderr, "usage: geogram_bool <union|intersection|difference> A.off B.off out.off | geogram_bool seq ops.txt out.off\n");
        return 1;
    }
    GEO::initialize(GEO::GEOGRAM_INSTALL_NONE);
    GEO::Logger::instance()->set_quiet(true);         // geogram's log goes to stdout; the driver's JSON line is the only output
    GEO::set_assert_mode(GEO::ASSERT_THROW);
    if (const char* t = std::getenv("GEOGRAM_THREADS"))
        GEO::Process::set_max_threads((index_t)std::atoi(t));
    std::string op = argv[1];
    int rc;
    if (op == "seq") rc = run_seq(argv[2], argv[3]);
    else if (argc < 5) { std::fprintf(stderr, "usage: geogram_bool <op> A.off B.off out.off\n"); rc = 1; }
    else rc = run(op, argv[2], argv[3], argv[4]);
    std::fflush(stdout);
    // no GEO::terminate(): the process ends here, and its statistics output is not wanted
    return rc;
}
