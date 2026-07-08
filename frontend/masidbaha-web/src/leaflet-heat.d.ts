// leaflet.heat has no official @types package — this ambient declaration
// just satisfies the compiler for `import 'leaflet.heat'` (side-effect
// import that patches L.heatLayer onto the Leaflet namespace). The actual
// heatLayer() call site casts to `any` since the plugin's options aren't
// typed either.
declare module 'leaflet.heat';
