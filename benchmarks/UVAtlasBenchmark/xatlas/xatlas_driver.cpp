// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
//
// Black-box driver of xatlas (https://github.com/jpcy/xatlas, MIT) for the UV atlas comparison (benchmarks/UVAtlasBenchmark,
// workflow uvatlas-compare). It calls the public API only (Create, AddMesh, ComputeCharts, PackCharts and the output
// structs) with default options, and writes the atlas in the layout of benchmarks/Shared/UvEvaluation.cs: one vertex per
// atlas vertex (v and vt with the same index), one face per input triangle in input order. The xatlas source is never
// read for its algorithms and nothing of it is copied here.
//
// Usage: xatlas_driver in.obj out.obj   (in.obj from UVAtlasBenchmark to-obj: v and triangle f lines only)
// Prints one JSON line: status (ok, invalid_input, failed, multi_atlas, unmapped), ms (ComputeCharts + PackCharts), charts.
// Output triangles are mapped back to input faces by their input vertex ids (sorted triples, multiset), not by the order
// xatlas emits them. An input face that xatlas does not emit (eg a degenerate one) gets no face: the evaluation reports
// the atlas as incomplete.
#include "xatlas.h"

#include <algorithm>
#include <array>
#include <chrono>
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <fstream>
#include <map>
#include <sstream>
#include <string>
#include <vector>

static std::string Name(const char* path) {
    std::string p(path);
    size_t s = p.find_last_of("/\\");
    return s == std::string::npos ? p : p.substr(s + 1);
}

static void Json(const std::string& file, const char* status, double ms, size_t charts, const std::string& error) {
    std::printf("{\"file\":\"%s\",\"tool\":\"xatlas\",\"status\":\"%s\",\"ms\":%.3f,\"charts\":%zu,\"error\":\"%s\"}\n",
                file.c_str(), status, ms, charts, error.c_str());
}

// v x y z and triangles (fan triangulation of larger faces, index before the first '/')
static bool ReadObj(const char* path, std::vector<float>& pos, std::vector<uint32_t>& idx, std::string& error) {
    std::ifstream in(path);
    if (!in) { error = "cannot open input"; return false; }
    std::string line;
    while (std::getline(in, line)) {
        if (line.compare(0, 2, "v ") == 0) {
            std::istringstream s(line.substr(2));
            double x, y, z;
            if (!(s >> x >> y >> z)) { error = "bad vertex"; return false; }
            pos.push_back((float)x); pos.push_back((float)y); pos.push_back((float)z);
        } else if (line.compare(0, 2, "f ") == 0) {
            std::istringstream s(line.substr(2));
            std::vector<uint32_t> corners;
            std::string tok;
            while (s >> tok) {
                long v = std::strtol(tok.c_str(), nullptr, 10);
                if (v < 1) { error = "bad face index"; return false; }
                corners.push_back((uint32_t)(v - 1));
            }
            if (corners.size() < 3) { error = "face with fewer than 3 corners"; return false; }
            for (size_t k = 1; k + 1 < corners.size(); ++k) {
                idx.push_back(corners[0]); idx.push_back(corners[k]); idx.push_back(corners[k + 1]);
            }
        }
    }
    if (pos.empty() || idx.empty()) { error = "no triangles"; return false; }
    for (uint32_t v : idx)
        if (v >= pos.size() / 3) { error = "face index out of range"; return false; }
    return true;
}

static std::array<uint32_t, 3> Key(uint32_t a, uint32_t b, uint32_t c) {
    std::array<uint32_t, 3> k = {a, b, c};
    std::sort(k.begin(), k.end());
    return k;
}

int main(int argc, char** argv) {
    if (argc != 3) {
        std::fprintf(stderr, "usage: xatlas_driver in.obj out.obj\n");
        return 2;
    }
    const std::string file = Name(argv[1]);
    std::vector<float> pos;
    std::vector<uint32_t> idx;
    std::string error;
    if (!ReadObj(argv[1], pos, idx, error)) {
        Json(file, "invalid_input", 0, 0, error);
        return 0;
    }
    const size_t nf = idx.size() / 3;

    xatlas::Atlas* atlas = xatlas::Create();
    xatlas::MeshDecl decl;
    decl.vertexPositionData = pos.data();
    decl.vertexPositionStride = sizeof(float) * 3;
    decl.vertexCount = (uint32_t)(pos.size() / 3);
    decl.indexData = idx.data();
    decl.indexFormat = xatlas::IndexFormat::UInt32;
    decl.indexCount = (uint32_t)idx.size();
    if (xatlas::AddMesh(atlas, decl, 1) != xatlas::AddMeshError::Success) {
        Json(file, "invalid_input", 0, 0, "AddMesh rejected the mesh");
        xatlas::Destroy(atlas);
        return 0;
    }

    auto t0 = std::chrono::steady_clock::now();
    xatlas::ComputeCharts(atlas);
    xatlas::PackCharts(atlas);
    double ms = std::chrono::duration<double, std::milli>(std::chrono::steady_clock::now() - t0).count();

    if (atlas->meshCount != 1 || atlas->meshes == nullptr) {
        Json(file, "failed", ms, 0, "no output mesh");
        xatlas::Destroy(atlas);
        return 0;
    }
    if (atlas->atlasCount != 1) {
        Json(file, "multi_atlas", ms, atlas->chartCount, "the charts fill several atlases");
        xatlas::Destroy(atlas);
        return 0;
    }
    const xatlas::Mesh& m = atlas->meshes[0];
    const double w = atlas->width, h = atlas->height;

    // input faces by their sorted vertex triples; each output triangle takes one unused input face with its triple
    std::map<std::array<uint32_t, 3>, std::vector<size_t>> faces_of;
    for (size_t f = 0; f < nf; ++f)
        faces_of[Key(idx[3 * f], idx[3 * f + 1], idx[3 * f + 2])].push_back(f);
    std::vector<long> out_of_face(nf, -1);
    size_t unmapped = 0;
    for (uint32_t i = 0; i < m.indexCount / 3; ++i) {
        uint32_t a = m.vertexArray[m.indexArray[3 * i]].xref;
        uint32_t b = m.vertexArray[m.indexArray[3 * i + 1]].xref;
        uint32_t c = m.vertexArray[m.indexArray[3 * i + 2]].xref;
        auto it = faces_of.find(Key(a, b, c));
        if (it == faces_of.end() || it->second.empty()) { unmapped++; continue; }
        size_t f = it->second.back();
        it->second.pop_back();
        out_of_face[f] = (long)i;
    }
    if (unmapped > 0) {
        Json(file, "unmapped", ms, atlas->chartCount, std::to_string(unmapped) + " output triangles match no input face");
        xatlas::Destroy(atlas);
        return 0;
    }

    std::ofstream out(argv[2]);
    out.precision(9);
    for (uint32_t v = 0; v < m.vertexCount; ++v) {
        uint32_t x = m.vertexArray[v].xref;
        out << "v " << pos[3 * x] << ' ' << pos[3 * x + 1] << ' ' << pos[3 * x + 2] << '\n';
    }
    for (uint32_t v = 0; v < m.vertexCount; ++v)
        out << "vt " << m.vertexArray[v].uv[0] / w << ' ' << m.vertexArray[v].uv[1] / h << '\n';
    size_t written = 0;
    for (size_t f = 0; f < nf; ++f) {
        if (out_of_face[f] < 0) continue;   // not emitted by xatlas: the evaluation reports the atlas as incomplete
        const uint32_t* t = &m.indexArray[3 * (size_t)out_of_face[f]];
        out << "f " << t[0] + 1 << '/' << t[0] + 1 << ' ' << t[1] + 1 << '/' << t[1] + 1 << ' ' << t[2] + 1 << '/' << t[2] + 1 << '\n';
        written++;
    }
    out.close();
    Json(file, "ok", ms, atlas->chartCount, written == nf ? "" : "some input faces are not in the atlas");
    xatlas::Destroy(atlas);
    return 0;
}
