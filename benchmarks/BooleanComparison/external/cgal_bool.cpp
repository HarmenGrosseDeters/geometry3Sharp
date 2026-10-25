// CGAL corefinement booleans (Polygon Mesh Processing), for the comparison in benchmarks/BooleanComparison.
// Usage: cgal_bool <epick|epeck> <union|intersection|difference> A.off B.off out.off
//        cgal_bool <epick|epeck> seq ops.txt out.off
// Prints one JSON line: ms of the boolean (without I/O and checks), or the reason it did not run.
// seq runs a list of operations in memory (with exact constructions without rounding in between), one per
// line, the result of line i in slot i: "load <path>" or "<union|intersection|difference> <i> <j>"; it
// writes the result of the last line and prints the summed time of the booleans and the number done.
#include <CGAL/Exact_predicates_inexact_constructions_kernel.h>
#include <CGAL/Exact_predicates_exact_constructions_kernel.h>
#include <CGAL/Surface_mesh.h>
#include <CGAL/Polygon_mesh_processing/corefinement.h>
#include <CGAL/Polygon_mesh_processing/self_intersections.h>
#include <CGAL/Polygon_mesh_processing/orientation.h>
#include <CGAL/boost/graph/IO/polygon_mesh_io.h>
#include <chrono>
#include <cstdio>
#include <fstream>
#include <iomanip>
#include <sstream>
#include <string>
#include <vector>

namespace PMP = CGAL::Polygon_mesh_processing;

template <class Mesh>
void write_off(const Mesh& out, const char* fout)
{
    // full double precision (EPECK: rounded to the nearest double)
    std::ofstream f(fout);
    f << std::setprecision(17) << "OFF\n" << out.number_of_vertices() << " " << out.number_of_faces() << " 0\n";
    std::map<typename Mesh::Vertex_index, int> id;
    for (auto v : out.vertices()) {
        id[v] = (int)id.size();
        auto p = out.point(v);
        f << CGAL::to_double(p.x()) << " " << CGAL::to_double(p.y()) << " " << CGAL::to_double(p.z()) << "\n";
    }
    for (auto fc : out.faces()) {
        f << "3";
        for (auto v : CGAL::vertices_around_face(out.halfedge(fc), out)) f << " " << id[v];
        f << "\n";
    }
}

template <class K>
int run(const std::string& op, const char* fa, const char* fb, const char* fout)
{
    typedef CGAL::Surface_mesh<typename K::Point_3> Mesh;
    Mesh a, b, out;
    if (!CGAL::IO::read_polygon_mesh(fa, a) || !CGAL::IO::read_polygon_mesh(fb, b)) {
        printf("{\"ok\":false,\"error\":\"read failed (not a polygon mesh)\"}\n");
        return 0;
    }
    for (Mesh* m : { &a, &b }) {
        if (!CGAL::is_closed(*m)) { printf("{\"ok\":false,\"error\":\"input not closed\"}\n"); return 0; }
        if (PMP::does_self_intersect(*m)) { printf("{\"ok\":false,\"error\":\"input self-intersects\"}\n"); return 0; }
        if (!PMP::does_bound_a_volume(*m)) { printf("{\"ok\":false,\"error\":\"input does not bound a volume\"}\n"); return 0; }
    }
    bool ok = false;
    auto t0 = std::chrono::steady_clock::now();
    try {
        if (op == "union") ok = PMP::corefine_and_compute_union(a, b, out);
        else if (op == "intersection") ok = PMP::corefine_and_compute_intersection(a, b, out);
        else ok = PMP::corefine_and_compute_difference(a, b, out);
    } catch (std::exception& e) {
        printf("{\"ok\":false,\"error\":\"exception: %s\"}\n", e.what());
        return 0;
    }
    auto t1 = std::chrono::steady_clock::now();
    long long ms = std::chrono::duration_cast<std::chrono::milliseconds>(t1 - t0).count();
    if (!ok) { printf("{\"ok\":false,\"ms\":%lld,\"error\":\"output not manifold\"}\n", ms); return 0; }
    write_off(out, fout);
    printf("{\"ok\":true,\"ms\":%lld}\n", ms);
    return 0;
}

template <class K>
int run_seq(const char* fops, const char* fout)
{
    typedef CGAL::Surface_mesh<typename K::Point_3> Mesh;
    std::ifstream in(fops);
    std::vector<std::string> lines;
    for (std::string line; std::getline(in, line);)
        if (!line.empty()) lines.push_back(line);
    // free each slot after its last use
    std::vector<size_t> last(lines.size());
    std::vector<std::vector<int>> args(lines.size());
    for (size_t i = 0; i < lines.size(); ++i) {
        std::istringstream ss(lines[i]);
        std::string op; ss >> op;
        last[i] = i;
        if (op != "load")
            for (int x; ss >> x;) { args[i].push_back(x); last[x] = i; }
    }
    std::vector<Mesh> slots(lines.size());
    long long ms = 0;
    int booleans = 0;
    for (size_t i = 0; i < lines.size(); ++i) {
        std::istringstream ss(lines[i]);
        std::string op; ss >> op;
        if (op == "load") {
            std::string path; ss >> path;
            if (!CGAL::IO::read_polygon_mesh(path, slots[i])) { printf("{\"ok\":false,\"error\":\"read failed (not a polygon mesh)\"}\n"); return 0; }
            Mesh& m = slots[i];
            if (!CGAL::is_closed(m)) { printf("{\"ok\":false,\"error\":\"input not closed\"}\n"); return 0; }
            if (PMP::does_self_intersect(m)) { printf("{\"ok\":false,\"error\":\"input self-intersects\"}\n"); return 0; }
            continue;
        }
        Mesh a = slots[args[i][0]], b = slots[args[i][1]];
        bool ok = false;
        auto t0 = std::chrono::steady_clock::now();
        try {
            if (op == "union") ok = PMP::corefine_and_compute_union(a, b, slots[i]);
            else if (op == "intersection") ok = PMP::corefine_and_compute_intersection(a, b, slots[i]);
            else ok = PMP::corefine_and_compute_difference(a, b, slots[i]);
        } catch (std::exception& e) {
            printf("{\"ok\":false,\"ms\":%lld,\"done\":%d,\"error\":\"exception: %s\"}\n", ms, booleans, e.what());
            return 0;
        }
        ms += std::chrono::duration_cast<std::chrono::milliseconds>(std::chrono::steady_clock::now() - t0).count();
        if (!ok) { printf("{\"ok\":false,\"ms\":%lld,\"done\":%d,\"error\":\"output not manifold\"}\n", ms, booleans); return 0; }
        ++booleans;
        for (int x : args[i]) if (last[x] == i) slots[x] = Mesh();
    }
    write_off(slots.back(), fout);
    printf("{\"ok\":true,\"ms\":%lld,\"done\":%d}\n", ms, booleans);
    return 0;
}

int main(int argc, char** argv)
{
    if (argc < 5) { fprintf(stderr, "usage: cgal_bool <epick|epeck> <op> A.off B.off out.off | cgal_bool <epick|epeck> seq ops.txt out.off\n"); return 1; }
    std::string kernel = argv[1], op = argv[2];
    if (op == "seq")
        return kernel == "epeck" ? run_seq<CGAL::Exact_predicates_exact_constructions_kernel>(argv[3], argv[4])
                                 : run_seq<CGAL::Exact_predicates_inexact_constructions_kernel>(argv[3], argv[4]);
    if (kernel == "epeck")
        return run<CGAL::Exact_predicates_exact_constructions_kernel>(op, argv[3], argv[4], argv[5]);
    return run<CGAL::Exact_predicates_inexact_constructions_kernel>(op, argv[3], argv[4], argv[5]);
}
