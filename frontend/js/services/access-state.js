(function (global) {
  "use strict";
  var snapshot = { role: "", screenIds: [] }, loadPromise = null;
  function signature(value) { return (value.screenIds || []).slice().sort().join("|") + ":" + (value.role || ""); }
  function setSnapshot(value, options) { var next = value || { role: "", screenIds: [] }, changed = signature(snapshot) !== signature(next); snapshot = next; if (changed && (!options || options.notify !== false)) global.dispatchEvent(new CustomEvent("alkhayal:access-changed", { detail: snapshot })); return snapshot; }
  function load(options) { if (loadPromise) return loadPromise; loadPromise = global.AlKhayalApi.request("/api/auth/me/screen-access").then(function (value) { return setSnapshot(value, options); }); loadPromise = loadPromise.then(function (value) { loadPromise = null; return value; }, function (reason) { loadPromise = null; throw reason; }); return loadPromise; }
  function routeEnabled(screen) { return !!screen && snapshot.screenIds.indexOf(screen.permissionRoute || screen.route) >= 0; }
  global.AlKhayalAccess = { load: load, current: function () { return snapshot; }, routeEnabled: routeEnabled, setSnapshot: setSnapshot };
}(window));
