// Thin rendering layer for the geometry3Sharp demos: all geometry is computed in C#; this file only
// turns vertex / index buffers into three.js objects and draws them. three.js comes from the import map.
import * as THREE from 'three';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';

let renderer, scene, camera, controls, group, canvasEl;
let wireframe = false, flat = true;
let lastFitKey = null;

export function init(canvas) {
    canvasEl = canvas;
    renderer = new THREE.WebGLRenderer({ canvas, antialias: true, preserveDrawingBuffer: true });
    renderer.setPixelRatio(window.devicePixelRatio || 1);
    scene = new THREE.Scene();
    scene.background = new THREE.Color(0xf4f5f7);
    camera = new THREE.PerspectiveCamera(40, 1, 0.01, 1000);
    camera.position.set(3, 2.2, 3.4);
    controls = new OrbitControls(camera, canvas);
    controls.enableDamping = true;
    scene.add(new THREE.HemisphereLight(0xffffff, 0x8890a0, 1.6));
    const sun = new THREE.DirectionalLight(0xffffff, 1.8);
    sun.position.set(3, 5, 4);
    scene.add(sun);
    const back = new THREE.DirectionalLight(0xffffff, 0.6);
    back.position.set(-4, -2, -3);
    scene.add(back);
    group = new THREE.Group();
    scene.add(group);
    const resize = () => {
        const w = canvas.clientWidth || 600, h = canvas.clientHeight || 400;
        renderer.setSize(w, h, false);
        camera.aspect = w / h;
        camera.updateProjectionMatrix();
    };
    new ResizeObserver(resize).observe(canvas);
    resize();
    const loop = () => { controls.update(); renderer.render(scene, camera); requestAnimationFrame(loop); };
    loop();
    return true;
}

function disposeGroup() {
    for (const o of [...group.children]) {
        group.remove(o);
        o.geometry?.dispose();
        o.material?.dispose();
    }
}

// meshes: [{ positions: number[] (xyz), indices: number[], color: '#rrggbb', opacity: number, lines: bool }]
// The positions arrive in the demo's frame divided by `scale` (the C# side passes the 2^k scale back out),
// so the camera framing does not jump when only the scale slider moves.
export function setMeshes(meshes, fitKey) {
    disposeGroup();
    const box = new THREE.Box3();
    for (const m of meshes) {
        const g = new THREE.BufferGeometry();
        g.setAttribute('position', new THREE.Float32BufferAttribute(m.positions, 3));
        if (m.lines) {
            const mat = new THREE.LineBasicMaterial({ color: m.color });
            const seg = new THREE.LineSegments(g, mat);
            seg.renderOrder = 2;
            group.add(seg);
        } else {
            g.setIndex(m.indices);
            g.computeVertexNormals();
            const transparent = m.opacity < 1;
            const mat = new THREE.MeshStandardMaterial({
                color: m.color, flatShading: flat, wireframe, side: THREE.DoubleSide,
                transparent, opacity: m.opacity, depthWrite: !transparent,
                polygonOffset: true, polygonOffsetFactor: 1, polygonOffsetUnits: 1,
                metalness: 0.0, roughness: 0.75
            });
            const mesh = new THREE.Mesh(g, mat);
            group.add(mesh);
            if (!wireframe && m.edges) {
                const wf = new THREE.LineSegments(new THREE.WireframeGeometry(g),
                    new THREE.LineBasicMaterial({ color: 0x334155, transparent: true, opacity: 0.25 }));
                group.add(wf);
            }
        }
        g.computeBoundingBox();
        if (!g.boundingBox.isEmpty()) box.union(g.boundingBox);
    }
    if (fitKey !== lastFitKey && !box.isEmpty()) {
        lastFitKey = fitKey;
        const c = box.getCenter(new THREE.Vector3());
        const r = Math.max(box.getSize(new THREE.Vector3()).length() * 0.5, 1e-9);
        controls.target.copy(c);
        const dir = new THREE.Vector3(0.62, 0.48, 0.72).normalize();
        camera.position.copy(c).addScaledVector(dir, r * 3.0);
        camera.near = r / 100; camera.far = r * 100;
        camera.updateProjectionMatrix();
    }
    return group.children.length;
}

export function setStyle(isWireframe, isFlat) {
    wireframe = isWireframe; flat = isFlat;
    for (const o of group.children) {
        if (o.isMesh) {
            o.material.wireframe = wireframe;
            o.material.flatShading = flat;
            o.material.needsUpdate = true;
        }
    }
}
