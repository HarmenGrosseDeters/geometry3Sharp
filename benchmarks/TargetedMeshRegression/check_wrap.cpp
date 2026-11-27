#include <CGAL/Exact_predicates_inexact_constructions_kernel.h>
#include <CGAL/IO/polygon_soup_io.h>
#include <CGAL/Side_of_triangle_mesh.h>
#include <CGAL/Surface_mesh.h>
#include <CGAL/boost/graph/IO/OBJ.h>
#include <iostream>
#include <vector>

using Kernel = CGAL::Exact_predicates_inexact_constructions_kernel;
using Mesh = CGAL::Surface_mesh<Kernel::Point_3>;

int main(int argc, char** argv) {
    if (argc != 3) return 1;
    std::vector<Kernel::Point_3> points;
    std::vector<std::vector<std::size_t>> faces;
    Mesh wrap;
    if (!CGAL::IO::read_polygon_soup(argv[1], points, faces) ||
        !CGAL::IO::read_OBJ(argv[2], wrap) || !CGAL::is_closed(wrap)) return 2;
    using Side = CGAL::Side_of_triangle_mesh<Mesh, Kernel>;
    Side::AABB_tree tree(wrap.faces().begin(), wrap.faces().end(), wrap);
    tree.build();
    Side side(tree);
    for (const auto& face : faces) {
        if (face.size() != 3) return 3;
        const auto& a = points[face[0]];
        const auto& b = points[face[1]];
        const auto& c = points[face[2]];
        if (side(a) != CGAL::ON_BOUNDED_SIDE) {
            std::cout << "outside_vertex\n";
            return 0;
        }
        Kernel::Triangle_3 triangle(a,b,c);
        if (triangle.is_degenerate()) {
            for (const auto& segment : {Kernel::Segment_3(a,b), Kernel::Segment_3(b,c), Kernel::Segment_3(c,a)}) {
                if (segment.source() != segment.target() && tree.do_intersect(segment)) {
                    std::cout << "boundary_contact\n";
                    return 0;
                }
            }
        } else if (tree.do_intersect(triangle)) {
            std::cout << "boundary_contact\n";
            return 0;
        }
    }
    std::cout << "strictly_enclosed\n";
}
