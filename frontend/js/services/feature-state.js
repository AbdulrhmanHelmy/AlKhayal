(function (global) {
  "use strict";
  var snapshot = { modules: [], capabilities: [] }, loadPromise = null;
  function all() { return snapshot.modules.concat(snapshot.capabilities); }
  function find(key) { return all().find(function (item) { return item.key === key; }); }
  function isEnabled(key) { var item = find(key); return !key || !!(item && item.isEnabled); }
  function routeEnabled(screen) { return !!screen && isEnabled(screen.featureKey); }
  function navigationSignature(value) { return (value.modules || []).concat(value.capabilities || []).map(function (item) { return item.key + ":" + (item.isEnabled ? "1" : "0"); }).sort().join("|"); }
  function setSnapshot(value, options) {
    var next = value || { modules: [], capabilities: [] }, changed = navigationSignature(snapshot) !== navigationSignature(next);
    snapshot = next;
    if (changed && (!options || options.notify !== false)) global.dispatchEvent(new CustomEvent("alkhayal:features-changed", { detail: snapshot }));
    return snapshot;
  }
  function load(options) {
    if (loadPromise) return loadPromise;
    loadPromise = global.AlKhayalApi.request("/api/features/effective").then(function (value) { return setSnapshot(value, options); });
    loadPromise = loadPromise.then(function (value) { loadPromise = null; return value; }, function (reason) { loadPromise = null; throw reason; });
    return loadPromise;
  }
  global.AlKhayalFeatures = { load: load, current: function () { return snapshot; }, find: find, isEnabled: isEnabled, routeEnabled: routeEnabled, setSnapshot: setSnapshot };
}(window));

