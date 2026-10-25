// CGAL corefinement booleans (Polygon Mesh Processing), for the comparison in benchmarks/BooleanComparison.
// Usage: cgal_bool <epick|epeck> <union|intersection|difference> A.off B.off out.off
// Prints one JSON line: ms of the boolean (without I/O and checks), or the reason it did not run.
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
#include <string>

namespace PMP = CGAL::Polygon_mesh_processing;

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
    // write with full double precision (EPECK: rounded to the nearest double)
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
    printf("{\"ok\":true,\"ms\":%lld}\n", ms);
    return 0;
}

int main(int argc, char** argv)
{
    if (argc < 6) { fprintf(stderr, "usage: cgal_bool <epick|epeck> <op> A.off B.off out.off\n"); return 1; }
    std::string kernel = argv[1], op = argv[2];
    if (kernel == "epeck")
        return run<CGAL::Exact_predicates_exact_constructions_kernel>(op, argv[3], argv[4], argv[5]);
    return run<CGAL::Exact_predicates_inexact_constructions_kernel>(op, argv[3], argv[4], argv[5]);
}
