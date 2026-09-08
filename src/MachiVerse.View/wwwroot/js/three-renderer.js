import * as THREE from "https://cdn.jsdelivr.net/npm/three@0.185.1/build/three.webgpu.js";

let renderer = null;
let scene = null;
let camera = null;
let projectionRoot = null;
let canvas = null;
let resizeObserver = null;
let contextLostHandler = null;
let currentProjection = null;
let currentOptions = null;
let rendererState = "uninitialized";

function requireCanvas(canvasId) {
    const element = document.getElementById(canvasId);
    if (!(element instanceof HTMLCanvasElement)) {
        throw new Error(`Renderer canvas not found: ${canvasId}`);
    }
    return element;
}

function isUint64Decimal(value) {
    if (typeof value !== "string" || !/^(0|[1-9][0-9]*)$/.test(value)) return false;
    try {
        const parsed = BigInt(value);
        return parsed >= 0n && parsed <= 18446744073709551615n;
    } catch {
        return false;
    }
}

function isFiniteVector(value, positive = false) {
    if (!value || !Number.isFinite(value.x) || !Number.isFinite(value.y) || !Number.isFinite(value.z)) return false;
    return !positive || (value.x > 0 && value.y > 0 && value.z > 0);
}

function isKnownPrimitiveKind(value) {
    return value === "terrain" || value === "built" || value === "presence";
}

function isKnownMaterialProfile(value) {
    return value === "terrain" || value === "built" || value === "presence";
}

function validatePrimitive(primitive) {
    if (!primitive || typeof primitive.primitiveId !== "string" || primitive.primitiveId.length === 0) return false;
    if (!isKnownPrimitiveKind(primitive.kind) || !isKnownMaterialProfile(primitive.materialProfile)) return false;
    if (!isFiniteVector(primitive.position) || !isFiniteVector(primitive.scale, true)) return false;
    if (!Number.isFinite(primitive.lodMinDistance) || primitive.lodMinDistance < 0) return false;
    if (!Number.isFinite(primitive.lodMaxDistance) || primitive.lodMaxDistance <= primitive.lodMinDistance) return false;
    return true;
}

function resizeRenderer() {
    if (!renderer || !camera || !canvas) return;
    const width = Math.max(1, canvas.clientWidth || canvas.parentElement?.clientWidth || 1);
    const height = Math.max(1, canvas.clientHeight || 1);
    const pixelRatio = Math.min(window.devicePixelRatio || 1, currentOptions.maxPixelRatio);
    renderer.setPixelRatio(pixelRatio);
    renderer.setSize(width, height, false);
    camera.aspect = width / height;
    camera.updateProjectionMatrix();
}

function backendMode(forceWebGl) {
    if (forceWebGl) return "webgl2-forced";
    return navigator.gpu ? "webgpu-preferred" : "webgl2-fallback";
}

function status() {
    return {
        initialized: renderer !== null,
        backendMode: currentOptions?.backendMode ?? "uninitialized",
        threeRevision: THREE.REVISION,
        basisStep: currentProjection?.basisStep ?? null,
        projectionRecordCount: currentProjection?.records?.length ?? 0,
        sceneObjectCount: projectionRoot?.children?.length ?? 0,
        rendererState
    };
}

function createPresentationMaterial(profile) {
    // WebGPURenderer standard materials are node-material based. Custom material work in
    // VIEW-03 starts from MeshStandardNodeMaterial rather than raw GLSL/WebGLRenderer paths.
    const color = profile === "terrain"
        ? 0x6f8062
        : profile === "built"
            ? 0x9aa2ad
            : 0x66b5ff;
    return new THREE.MeshStandardNodeMaterial({ color });
}

function createPresentationObject(primitive) {
    let geometry;
    if (primitive.kind === "presence") {
        geometry = new THREE.SphereGeometry(0.5, 16, 8);
    } else {
        // Terrain and built projections both start from full-3D volume primitives. Canonical
        // schema adapters may replace these fixture-level geometries with mesh/asset projections
        // without changing the renderer contract.
        geometry = new THREE.BoxGeometry(1, 1, 1);
    }

    const material = createPresentationMaterial(primitive.materialProfile);
    const object = new THREE.Mesh(geometry, material);
    object.name = primitive.primitiveId;
    object.position.set(primitive.position.x, primitive.position.y, primitive.position.z);
    object.scale.set(primitive.scale.x, primitive.scale.y, primitive.scale.z);
    object.frustumCulled = true;
    object.userData.primitiveKind = primitive.kind;
    object.userData.lodMinDistance = primitive.lodMinDistance;
    object.userData.lodMaxDistance = primitive.lodMaxDistance;
    object.userData.presentationOnly = true;
    return object;
}

function disposeProjectionObjects() {
    if (!projectionRoot) return;
    projectionRoot.traverse(object => {
        if (object === projectionRoot) return;
        object.geometry?.dispose?.();
        if (Array.isArray(object.material)) object.material.forEach(material => material.dispose?.());
        else object.material?.dispose?.();
    });
    projectionRoot.clear();
}

function updatePresentationLod() {
    if (!projectionRoot || !camera) return;
    for (const object of projectionRoot.children) {
        const distance = camera.position.distanceTo(object.position);
        object.visible = distance >= object.userData.lodMinDistance && distance < object.userData.lodMaxDistance;
    }
}

function enterDegradedRendering(reason) {
    rendererState = `degraded:${reason}`;
    if (canvas) canvas.dataset.rendererState = rendererState;
    renderer?.setAnimationLoop(null);
}

function observeWebGpuDeviceLoss(activeRenderer) {
    const deviceLost = activeRenderer?.backend?.device?.lost;
    if (!deviceLost || typeof deviceLost.then !== "function") return;

    deviceLost.then(() => {
        if (renderer === activeRenderer) enterDegradedRendering("webgpu-device-lost");
    }).catch(() => {
        if (renderer === activeRenderer) enterDegradedRendering("webgpu-device-lost");
    });
}

export async function initializeRendererBoundary(canvasId, maxPixelRatio, forceWebGl = false) {
    if (!Number.isFinite(maxPixelRatio) || maxPixelRatio <= 0) {
        throw new Error("maxPixelRatio must be a positive finite number.");
    }

    await disposeRendererBoundary();
    canvas = requireCanvas(canvasId);
    currentOptions = {
        canvasId,
        maxPixelRatio,
        forceWebGl,
        backendMode: backendMode(forceWebGl)
    };

    scene = new THREE.Scene();
    scene.background = new THREE.Color(0x101216);

    camera = new THREE.PerspectiveCamera(60, 1, 0.05, 1000000);
    camera.position.set(0, 4, 8);
    camera.lookAt(0, 0, 0);

    projectionRoot = new THREE.Group();
    projectionRoot.name = "machiverse-scene-projection";
    scene.add(projectionRoot);

    scene.add(new THREE.HemisphereLight(0xffffff, 0x222233, 1.5));

    renderer = new THREE.WebGPURenderer({
        canvas,
        antialias: true,
        forceWebGL: forceWebGl
    });
    const activeRenderer = renderer;
    await renderer.init();
    observeWebGpuDeviceLoss(activeRenderer);

    resizeObserver = new ResizeObserver(resizeRenderer);
    resizeObserver.observe(canvas);
    window.addEventListener("resize", resizeRenderer, { passive: true });
    resizeRenderer();

    contextLostHandler = event => {
        event.preventDefault();
        enterDegradedRendering("webgl-context-lost");
    };
    canvas.addEventListener("webglcontextlost", contextLostHandler, false);

    rendererState = "ready";
    renderer.setAnimationLoop(() => {
        if (!renderer || !scene || !camera || rendererState !== "ready") return;
        updatePresentationLod();
        renderer.render(scene, camera);
    });

    canvas.dataset.rendererBoundary = "ready";
    canvas.dataset.rendererState = rendererState;
    canvas.dataset.rendererBackend = currentOptions.backendMode;
    canvas.dataset.threeRevision = THREE.REVISION;
    return status();
}

export function applySceneProjection(projection) {
    if (!renderer || !projectionRoot || !canvas) {
        throw new Error("Renderer has not been initialized.");
    }
    if (!projection || !isUint64Decimal(projection.basisStep)) {
        throw new Error("Invalid SceneProjectionModel basis step.");
    }
    if (!Array.isArray(projection.records) || !Array.isArray(projection.primitives)) {
        throw new Error("Invalid SceneProjectionModel collection fields.");
    }
    if (projection.records.some(record => !isUint64Decimal(record.recordRevision))) {
        throw new Error("Invalid SceneProjectionModel record revision.");
    }
    if (projection.primitives.some(primitive => !validatePrimitive(primitive))) {
        throw new Error("Invalid SceneProjectionModel primitive.");
    }

    const primitiveIds = new Set();
    for (const primitive of projection.primitives) {
        if (primitiveIds.has(primitive.primitiveId)) {
            throw new Error(`Duplicate SceneProjection primitive: ${primitive.primitiveId}`);
        }
        primitiveIds.add(primitive.primitiveId);
    }

    disposeProjectionObjects();
    for (const primitive of projection.primitives) {
        projectionRoot.add(createPresentationObject(primitive));
    }

    // Confirmed identity/revision metadata remains separate from Three.js presentation objects.
    currentProjection = projection;
    projectionRoot.userData.basisStep = projection.basisStep;
    projectionRoot.userData.continuityTokenHex = projection.continuityTokenHex;
    projectionRoot.userData.projectionSchemaDigestHex = projection.projectionSchemaDigestHex;
    projectionRoot.userData.records = projection.records.map(record => ({
        recordSchemaId: record.recordSchemaId,
        recordIdHex: record.recordIdHex,
        recordRevision: record.recordRevision
    }));

    canvas.dataset.confirmedBasisStep = projection.basisStep;
    canvas.dataset.projectionRecordCount = String(projection.records.length);
    canvas.dataset.sceneObjectCount = String(projection.primitives.length);
}

export function clearSceneProjection() {
    disposeProjectionObjects();
    currentProjection = null;
    if (projectionRoot) projectionRoot.userData = {};
    if (canvas) {
        delete canvas.dataset.confirmedBasisStep;
        delete canvas.dataset.projectionRecordCount;
        delete canvas.dataset.sceneObjectCount;
    }
}

export function simulateRendererLossForTest(reason = "test-loss") {
    enterDegradedRendering(reason);
    return status();
}

export async function reinitializeRendererBoundary(canvasId, maxPixelRatio, forceWebGl = false) {
    return initializeRendererBoundary(canvasId, maxPixelRatio, forceWebGl);
}

export function getRendererStatus() {
    return status();
}

export async function disposeRendererBoundary() {
    if (renderer) renderer.setAnimationLoop(null);
    if (resizeObserver) {
        resizeObserver.disconnect();
        resizeObserver = null;
    }
    window.removeEventListener("resize", resizeRenderer);
    if (canvas && contextLostHandler) {
        canvas.removeEventListener("webglcontextlost", contextLostHandler, false);
    }
    contextLostHandler = null;

    clearSceneProjection();
    renderer?.dispose();
    renderer = null;
    scene = null;
    camera = null;
    projectionRoot = null;
    currentProjection = null;
    currentOptions = null;
    rendererState = "disposed";
    if (canvas) {
        canvas.dataset.rendererState = rendererState;
        delete canvas.dataset.rendererBackend;
    }
    canvas = null;
}
