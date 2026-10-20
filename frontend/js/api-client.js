(function (global) {
  "use strict";
  var csrfKey = "alkhayal_csrf";
  function rememberCsrf(data) { if (data && data.csrfToken) sessionStorage.setItem(csrfKey, data.csrfToken); return data; }
  async function request(path, options) {
    var config = Object.assign({ credentials: "same-origin" }, options || {});
    config.headers = Object.assign({ "Content-Type": "application/json", "Accept": "application/json" }, config.headers || {});
    var method = (config.method || "GET").toUpperCase(), csrf = sessionStorage.getItem(csrfKey);
    if (method !== "GET" && method !== "HEAD" && csrf) config.headers["X-CSRF-Token"] = csrf;
    var response;
    try { response = await fetch(path, config); }
    catch (networkError) { var unavailable = new Error("الخادم غير متاح حاليًا."); unavailable.code = "network_error"; unavailable.status = 0; throw unavailable; }
    var body = await response.json().catch(function () { return { success: false, error: { code: "invalid_response", message: "استجابة غير صالحة من الخادم." } }; });
    if (!response.ok || !body.success) { var error = new Error(body.error && body.error.message || "تعذر تنفيذ الطلب."); error.code = body.error && body.error.code; error.status = response.status; if (response.status === 401) sessionStorage.removeItem(csrfKey); throw error; }
    return rememberCsrf(body.data);
  }
  global.AlKhayalApi = { request: request, clearSession: function () { sessionStorage.removeItem(csrfKey); } };
}(window));
