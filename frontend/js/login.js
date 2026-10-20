(function () {
  "use strict";
  var form = document.getElementById("loginForm"), username = document.getElementById("username"), password = document.getElementById("password"), remember = document.getElementById("remember"), error = document.getElementById("formError"), button = document.getElementById("loginButton"), spinner = document.getElementById("spinner"), status = document.getElementById("statusText"), dot = document.getElementById("statusDot");
  var saved = localStorage.getItem("alkhayal_username"); if (saved) { username.value = saved; remember.checked = true; }
  AlKhayalApi.request("/api/auth/me").then(function () { location.replace("/index.html"); }).catch(function () {});
  document.getElementById("togglePassword").addEventListener("click", function () { password.type = password.type === "password" ? "text" : "password"; this.textContent = password.type === "password" ? "إظهار" : "إخفاء"; this.setAttribute("aria-label", this.textContent + " كلمة المرور"); });
  fetch("/api/system/health", { credentials: "same-origin" }).then(function (response) { if (!response.ok) throw new Error(); status.textContent = "متصل بالخادم"; dot.style.background = "var(--success)"; }).catch(function () { status.textContent = "الخادم غير متاح"; dot.style.background = "var(--danger)"; });
  form.addEventListener("submit", async function (event) {
    event.preventDefault(); document.getElementById("usernameError").textContent = ""; document.getElementById("passwordError").textContent = ""; error.textContent = "";
    if (!username.value.trim()) { document.getElementById("usernameError").textContent = "أدخل اسم المستخدم."; username.focus(); return; } if (!password.value) { document.getElementById("passwordError").textContent = "أدخل كلمة المرور."; password.focus(); return; }
    button.disabled = true; spinner.hidden = false;
    try { await AlKhayalApi.request("/api/auth/login", { method: "POST", body: JSON.stringify({ username: username.value.trim(), password: password.value }) }); if (remember.checked) localStorage.setItem("alkhayal_username", username.value.trim()); else localStorage.removeItem("alkhayal_username"); location.replace("/index.html"); }
    catch (loginError) { if (loginError.code === "invalid_credentials") error.textContent = "اسم المستخدم أو كلمة المرور غير صحيحة."; else if (loginError.code === "disabled_account") error.textContent = "هذا الحساب موقوف. راجع مسؤول النظام."; else if (loginError.code === "network_error") error.textContent = "تعذر الوصول إلى الخادم المحلي."; else error.textContent = loginError.message || "تعذر تسجيل الدخول."; }
    finally { button.disabled = false; spinner.hidden = true; }
  });
}());
