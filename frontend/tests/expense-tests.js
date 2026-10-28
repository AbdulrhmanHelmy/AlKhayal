"use strict";
var assert = require("assert"), fs = require("fs"), path = require("path"), vm = require("vm");
var Element = require("./inventory-tests.js").Element;
var source = fs.readFileSync(path.join(__dirname, "../js/modules/expenses.js"), "utf8");
var shellSource = fs.readFileSync(path.join(__dirname, "../js/app.js"), "utf8");
var registry = JSON.parse(fs.readFileSync(path.join(__dirname, "../data/screens.json"), "utf8"));
function settle() { return new Promise(function (resolve) { setImmediate(resolve); }); }
function harness(defer) {
  var calls = [], pending = [], reads = [], active = true, sequence = 0;
  var types = [{ id: 1, name: "إيجار" }, { id: 2, name: "كهرباء" }], accounts = [{ id: 7, name: "خزنة الشركة", code: "C1", balance: "100.20" }];
  var win = { crypto: { getRandomValues: function (bytes) { bytes.fill(++sequence); } }, AlKhayalUtils: { escape: function (value) { return String(value == null ? "" : value).replace(/</g, "&lt;"); }, normalize: function (value) { return String(value || "").toLowerCase(); } }, AlKhayalRenderers: { "under-construction": function () { throw new Error("Expense route incorrectly dispatched to under-construction"); } }, AlKhayalUI: { initialize: function () {}, pageHeader: function () { return ""; }, toast: function () {} }, AlKhayalApi: { request: function (url, config) {
    calls.push({ url: url, config: config });
    if (config) return new Promise(function (resolve, reject) { pending.push({ resolve: resolve, reject: reject }); });
    if (defer) return new Promise(function (resolve, reject) { reads.push({ url: url, resolve: resolve, reject: reject }); });
    return Promise.resolve(url.endsWith("categories") ? types : url.endsWith("accounts") ? accounts : []);
  } } };
  vm.runInNewContext(source, { window: win, Uint8Array: Uint8Array, Date: Date }, { filename: "expenses.js" });
  var shellEnd = shellSource.indexOf("  function roleLabel(");
  assert.ok(shellEnd > 0, "test must execute the actual shell renderer dispatcher");
  vm.runInNewContext(shellSource.slice(0, shellEnd) + "window.__expenseDispatch = implementationRenderer;}());", { window: win, document: { getElementById: function () { return null; } } }, { filename: "app-dispatch.js" });
  var context = { state: {}, isActive: function () { return active; } };
  return { context: context, pending: pending, reads: reads, types: types, accounts: accounts, leave: function () { active = false; },
    render: function (renderer) { var node = new Element("section"), screen = registry.screens.find(function (s) { return s.renderer === renderer; }); assert.ok(screen, "expense renderer must have a registered screen"); win.__expenseDispatch(screen)(screen, node, context); return node; },
    writes: function () { return calls.filter(function (call) { return call.config; }); }
  };
}
function set(node, selector, value, handler) { var input = node.querySelector(selector); input.value = value; if (handler) input[handler](); }
function submit(node, selector) { node.querySelector(selector).onsubmit({ preventDefault: function () {} }); }
async function categories() {
  var h = harness(), node = h.render("expense-categories"); await settle();
  var search = node.querySelector("#expenseCategorySearch"); set(node, "#expenseCategorySearch", "إيجار", "oninput");
  assert.ok(node.querySelector("[data-expense-categories]").innerHTML.includes("إيجار")); assert.ok(!node.querySelector("[data-expense-categories]").innerHTML.includes("كهرباء"));
  assert.strictEqual(node.querySelector("#expenseCategorySearch"), search);
  set(node, "#expenseCategoryName", " مياه ", "oninput"); submit(node, "#expenseCategoryForm"); submit(node, "#expenseCategoryForm");
  assert.equal(h.writes().length, 1); assert.equal(JSON.parse(h.writes()[0].config.body).name, "مياه");
  h.pending[0].reject({ status: 0, message: "فقد الرد" }); await settle();
  assert.ok(node.querySelector("#expenseCategoryName").disabled); submit(node, "#expenseCategoryForm");
  assert.equal(h.writes()[0].config.body, h.writes()[1].config.body); h.pending[1].resolve({}); await settle();
  assert.equal(node.querySelector("#expenseCategoryName").value, ""); assert.strictEqual(node.querySelector("#expenseCategorySearch"), search);
}
async function voucher() {
  var h = harness(), node = h.render("expense-voucher"); await settle();
  assert.ok(node.querySelector("[data-expense-form]").innerHTML.includes("إيجار"));
  submit(node, "#expenseVoucherForm"); assert.equal(h.writes().length, 0);
  set(node, "#expenseType", "1", "onchange"); set(node, "#expenseAccount", "7", "onchange");
  assert.equal(node.querySelector("[data-expense-balance]").textContent, "100.20");
  set(node, "#expenseAmount", "0.001", "oninput"); submit(node, "#expenseVoucherForm"); assert.equal(h.writes().length, 0);
  set(node, "#expenseAmount", "10.10", "oninput"); set(node, "#expenseDate", "2026-10-28", "onchange"); set(node, "#expensePayee", "مالك", "oninput"); set(node, "#expenseNotes", "  إيجار  ", "oninput");
  submit(node, "#expenseVoucherForm"); submit(node, "#expenseVoucherForm"); assert.equal(h.writes().length, 1);
  var first = JSON.parse(h.writes()[0].config.body); assert.equal(first.categoryId, 1); assert.equal(first.treasuryAccountId, 7); assert.equal(first.amount, "10.10"); assert.equal(first.notes, "إيجار");
  assert.ok(node.querySelector("#expenseAccount").disabled);
  node = h.render("expense-voucher"); await settle(); submit(node, "#expenseVoucherForm");
  assert.equal(h.writes().length, 1, "switching tabs while saving cannot start a second request");
  h.pending[0].reject({ status: 503, message: "تعذر تأكيد الحفظ" }); await settle();
  assert.ok(node.querySelector("#expenseAmount").disabled); assert.equal(node.querySelector("[data-expense-save]").disabled, false);
  node = h.render("expense-voucher"); await settle(); submit(node, "#expenseVoucherForm"); assert.deepStrictEqual(JSON.parse(h.writes()[1].config.body), first);
  h.pending[1].resolve({ number: "EXP-000001", amount: "10.10", categoryName: "إيجار", replayed: true }); await settle();
  assert.equal(node.querySelector("#expenseAmount").value, ""); assert.equal(node.querySelector("#expenseAmount").disabled, false); assert.ok(node.querySelector("[data-expense-receipt]").innerHTML.includes("EXP-000001"));
  set(node, "#expenseType", "1", "onchange"); set(node, "#expenseAccount", "7", "onchange"); set(node, "#expenseAmount", "1000", "oninput");
  submit(node, "#expenseVoucherForm"); h.pending[2].reject({ status: 409, message: "الرصيد غير كافٍ" }); await settle();
  assert.equal(node.querySelector("#expenseAmount").disabled, false); assert.equal(node.querySelector("#expenseAmount").value, "1000"); assert.equal(h.context.state.expenseDraft.pending, null);
}
async function loading() {
  var empty = harness(); empty.types.length = 0; var node = empty.render("expense-voucher"); await settle(); assert.ok(node.querySelector("[data-expense-form]").innerHTML.includes("إدارة المصروفات")); assert.equal(node.querySelector("#expenseVoucherForm"), null);
  var h = harness(true); node = h.render("expense-voucher");
  h.reads[0].reject({ message: "تعذر التحميل" }); h.reads[1].resolve([]); h.reads[2].reject({ message: "تعذر قراءة السندات" }); await settle();
  assert.ok(node.querySelector("[data-expense-retry]")); assert.ok(node.querySelector("[data-vouchers-retry]"));
  node.querySelector("[data-expense-retry]").onclick(); h.reads[3].resolve(h.types); h.reads[4].resolve(h.accounts); await settle(); assert.ok(node.querySelector("#expenseVoucherForm"));
  node.querySelector("[data-vouchers-retry]").onclick(); var before = node.querySelector("[data-expense-vouchers]").innerHTML; h.leave(); h.reads[5].resolve([{ number: "late" }]); await settle(); assert.equal(node.querySelector("[data-expense-vouchers]").innerHTML, before);
}
module.exports = async function () {
  var registry = JSON.parse(fs.readFileSync(path.join(__dirname, "../data/screens.json"), "utf8"));
  assert.deepStrictEqual(registry.modules.find(function (m) { return m.id === "expenses"; }).items, ["expenses-list", "expense-add"]);
  ["expense-categories", "financial-movement"].forEach(function (route) { var screen = registry.screens.find(function (s) { return s.route === route; }); assert.equal(screen.renderer, "legacy-redirect"); assert.equal(screen.navigationHidden, true); });
  assert.equal(registry.screens.find(function (s) { return s.route === "expenses-list"; }).title, "إدارة المصروفات");
  assert.equal(registry.screens.find(function (s) { return s.route === "expense-add"; }).title, "سند صرف");
  await categories(); await voucher(); await loading(); console.log("Expense types, persisted voucher payload, exact amounts, retry identity, loading and navigation tests passed.");
};
