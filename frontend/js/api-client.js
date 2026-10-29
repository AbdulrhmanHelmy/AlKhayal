(function (global) {
  "use strict";
  var csrfKey = "alkhayal_csrf";
  var errorMessages = { unauthorized: "انتهت جلسة الدخول.", forbidden: "لا تملك صلاحية تنفيذ هذا الإجراء.", invalid_csrf: "انتهت صلاحية الطلب. أعد المحاولة.", not_found: "تعذر العثور على البيانات المطلوبة.", conflict: "تعارضت العملية مع تغيير أحدث. حدّث الشاشة وأعد المحاولة.", invalid_response: "استجابة غير صالحة من الخادم.", database_busy: "قاعدة البيانات مشغولة حاليًا. أعد المحاولة بعد لحظات." };
  function safeErrorMessage(value, code, status) { if (value && /[\u0600-\u06ff]/.test(value)) return value; if (errorMessages[code]) return errorMessages[code]; if (status === 401) return errorMessages.unauthorized; if (status === 403) return errorMessages.forbidden; if (status === 404) return errorMessages.not_found; if (status === 409) return errorMessages.conflict; return "تعذر تنفيذ الطلب. أعد المحاولة."; }
  function rememberCsrf(data) { if (data && data.csrfToken) sessionStorage.setItem(csrfKey, data.csrfToken); return data; }
  async function request(path, options) {
    var config = Object.assign({ credentials: "same-origin" }, options || {});
    config.headers = Object.assign({ "Content-Type": "application/json", "Accept": "application/json" }, config.headers || {});
    var method = (config.method || "GET").toUpperCase(), csrf = sessionStorage.getItem(csrfKey);
    if (method !== "GET" && method !== "HEAD" && csrf) config.headers["X-CSRF-Token"] = csrf;
    var response, timeoutId = null, timedOut = false;
    if (global.AbortController && !config.signal) {
      var controller = new global.AbortController();
      config.signal = controller.signal;
      timeoutId = global.setTimeout(function () { timedOut = true; controller.abort(); }, 20000);
    }
    try { response = await fetch(path, config); }
    catch (networkError) { var unavailable = new Error(timedOut ? "انتهت مهلة الاتصال بالخادم. تحقق من اتصال التطبيق وحاول مجددًا." : "الخادم غير متاح حاليًا."); unavailable.code = timedOut ? "request_timeout" : "network_error"; unavailable.status = 0; throw unavailable; }
    finally { if (timeoutId !== null) global.clearTimeout(timeoutId); }
    var body = await response.json().catch(function () { return { success: false, error: { code: "invalid_response", message: "استجابة غير صالحة من الخادم." } }; });
    if (!response.ok || !body.success) { var code = body.error && body.error.code, error = new Error(safeErrorMessage(body.error && body.error.message, code, response.status)); error.code = code; error.status = response.status; if (response.status === 401) sessionStorage.removeItem(csrfKey); throw error; }
    return rememberCsrf(body.data);
  }
  global.AlKhayalApi = { request: request, clearSession: function () { sessionStorage.removeItem(csrfKey); } };
}(window));
