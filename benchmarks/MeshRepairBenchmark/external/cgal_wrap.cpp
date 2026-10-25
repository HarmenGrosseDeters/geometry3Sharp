// CGAL 3D Alpha Wrapping (CGAL::alpha_wrap_3), for the comparison with MeshAlphaWrap (compare_wrap.py).
// Written against CGAL's documented API only; links against CGAL (GPL), so a compiled cgal_wrap is not distributed.
// Usage: cgal_wrap <mesh file> <relative alpha> <relative offset> [out.off]
//   alpha = bounding box diagonal / relative alpha, offset = diagonal / relative offset (as MeshAlphaWrap)
// Prints one JSON line: ms of the wrap (without I/O), output vertices and faces.
#include <CGAL/Exact_predicates_inexact_constructions_kernel.h>
#include <CGAL/Surface_mesh.h>
#include <CGAL/alpha_wrap_3.h>
#include <CGAL/IO/polygon_soup_io.h>
#include <CGAL/IO/OFF.h>
#include <array>
#include <chrono>
#include <cmath>
#include <cstdio>
#include <cstdlib>
#include <fstream>
#include <vector>

typedef CGAL::Exact_predicates_inexact_constructions_kernel K;
typedef K::Point_3 Point_3;
typedef CGAL::Surface_mesh<Point_3> Mesh;

int main(int argc, char** argv)
{
    if (argc < 4) { printf("{\"ok\":false,\"error\":\"usage\"}\n"); return 1; }
    std::vector<Point_3> points;
    std::vector<std::array<std::size_t, 3>> faces;
    if (!CGAL::IO::read_polygon_soup(argv[1], points, faces) || points.empty()) {
        printf("{\"ok\":false,\"error\":\"read failed\"}\n");
        return 0;
    }
    double xmin = 1e300, ymin = 1e300, zmin = 1e300, xmax = -1e300, ymax = -1e300, zmax = -1e300;
    for (const Point_3& p : points) {
        xmin = std::min(xmin, p.x()); xmax = std::max(xmax, p.x());
        ymin = std::min(ymin, p.y()); ymax = std::max(ymax, p.y());
        zmin = std::min(zmin, p.z()); zmax = std::max(zmax, p.z());
    }
    double diag = std::sqrt((xmax - xmin) * (xmax - xmin) + (ymax - ymin) * (ymax - ymin) + (zmax - zmin) * (zmax - zmin));
    double alpha = diag / std::atof(argv[2]), offset = diag / std::atof(argv[3]);
    Mesh wrap;
    auto t0 = std::chrono::steady_clock::now();
    CGAL::alpha_wrap_3(points, faces, alpha, offset, wrap);
    auto t1 = std::chrono::steady_clock::now();
    long ms = (long)std::chrono::duration_cast<std::chrono::milliseconds>(t1 - t0).count();
    if (argc > 4) {
        std::ofstream out(argv[4]);
        out.precision(17);
        CGAL::IO::write_OFF(out, wrap);
    }
    printf("{\"ok\":true,\"ms\":%ld,\"vertices\":%zu,\"faces\":%zu,\"alpha\":%.17g,\"offset\":%.17g}\n",
           ms, (size_t)num_vertices(wrap), (size_t)num_faces(wrap), alpha, offset);
    return 0;
}
