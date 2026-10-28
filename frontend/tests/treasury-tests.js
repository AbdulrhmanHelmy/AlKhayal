"use strict";
var assert = require("assert"), fs = require("fs"), path = require("path"), vm = require("vm");
var Element = require("./inventory-tests.js").Element;
var source = fs.readFileSync(path.join(__dirname, "../js/modules/treasury.js"), "utf8");
var registry = JSON.parse(fs.readFileSync(path.join(__dirname, "../data/screens.json"), "utf8"));
function settle() { return new Promise(function (resolve) { setImmediate(resolve); }); }
function harness(deferReads) {
  var calls = [], pending = [], reads = [], modal, options, active = true, sequence = 0;
  var rows = [{ id: 1, code: "C1", name: "الخزنة الرئيسية", type: "cash", balance: "100.10", version: 1 }, { id: 2, code: "B1", name: "بنك الشركة", type: "bank", balance: "0.00", version: 1 }];
  var win = { crypto: { getRandomValues: function (bytes) { bytes.fill(++sequence); } }, AlKhayalUtils: { escape: function (value) { return String(value == null ? "" : value); }, normalize: function (value) { return String(value || "").toLowerCase(); } }, AlKhayalRenderers: {}, AlKhayalUI: {
    pageHeader: function (screen, actions) { return actions || ""; }, toast: function () {}, closeModal: function () {}, confirm: function () { return Promise.resolve(true); },
    modal: function (value) { options = value; modal = new Element("section"); modal.innerHTML = value.body + value.actions; value.onOpen(modal); }
  }, AlKhayalApi: { request: function (url, config) {
    calls.push({ url: url, config: config });
    if (config && config.method !== "GET") return new Promise(function (resolve, reject) { pending.push({ resolve: resolve, reject: reject }); });
    if (deferReads) return new Promise(function (resolve, reject) { reads.push({ resolve: resolve, reject: reject }); });
    return Promise.resolve(rows);
  } } };
  vm.runInNewContext(source, { window: win, Uint8Array: Uint8Array }, { filename: "treasury.js" });
  var context = { state: {}, isActive: function () { return active; } };
  return { context: context, calls: calls, pending: pending, reads: reads, rows: rows, modal: function () { return modal; }, options: function () { return options; }, leave: function () { active = false; },
    render: function (route) { var container = new Element("section"); win.AlKhayalRenderers[route === "accounts-list" ? "treasury-accounts" : "treasury-movement"]({ route: route }, container, context); return container; },
    writes: function () { return calls.filter(function (call) { return call.config; }); }
  };
}
function set(host, selector, value, handler) { var node = host.querySelector(selector); node.value = value; if (handler) node[handler](); }
function submit(host, selector) { host.querySelector(selector).onsubmit({ preventDefault: function () {} }); }
async function directory() {
  var h = harness(), node = h.render("accounts-list"); await settle();
  var search = node.querySelector("#treasurySearch"); search.value = "B1"; search.oninput();
  assert.strictEqual(node.querySelector("#treasurySearch"), search, "search stays mounted");
  assert.ok(node.querySelector("[data-treasury-results]").innerHTML.includes("بنك الشركة"));
  assert.ok(!node.querySelector("[data-treasury-results]").innerHTML.includes("الخزنة الرئيسية"));
  node.querySelector("[data-account-add]").onclick(); var modal = h.modal();
  set(modal, "#treasuryCode", "NEW"); set(modal, "#treasuryName", "حساب جديد"); set(modal, "#treasuryType", "bank"); set(modal, "#treasuryNotes", "بيان");
  submit(modal, "#treasuryAccountForm"); submit(modal, "#treasuryAccountForm");
  assert.equal(h.writes().length, 1); assert.equal(h.options().beforeClose(), false);
  var command = JSON.parse(h.writes()[0].config.body); assert.equal(command.type, "bank"); assert.equal(command.name, "حساب جديد");
  h.pending[0].reject({ status: 409, message: "كود مكرر" }); await settle();
  assert.equal(modal.querySelector("#treasuryName").value, "حساب جديد"); assert.equal(modal.querySelector("[data-account-save]").disabled, false);
  submit(modal, "#treasuryAccountForm"); h.pending[1].resolve({}); await settle();
  assert.strictEqual(node.querySelector("#treasurySearch"), search, "save refresh keeps search mounted");
  node.querySelector("[data-account-edit]").onclick(); modal = h.modal(); set(modal, "#treasuryNotes", "");
  submit(modal, "#treasuryAccountForm"); assert.equal(h.writes()[2].config.method, "PUT"); assert.equal(JSON.parse(h.writes()[2].config.body).version, 1);
  h.pending[2].resolve({}); await settle();
  node.querySelector("[data-account-delete]").onclick(); await settle();
  assert.equal(h.writes()[3].config.method, "DELETE"); assert.ok(h.writes()[3].url.endsWith("?version=1"));
}
async function movement() {
  var h = harness(), node = h.render("account-deposit"); await settle();
  set(node, "#treasuryAccount", "1", "onchange"); set(node, "#treasuryAmount", "0.001", "oninput");
  submit(node, "#treasuryMovementForm"); assert.equal(h.writes().length, 0);
  set(node, "#treasuryAmount", "0.10", "oninput"); set(node, "#treasuryMovementNotes", "إيداع", "oninput");
  submit(node, "#treasuryMovementForm"); submit(node, "#treasuryMovementForm"); assert.equal(h.writes().length, 1);
  var first = JSON.parse(h.writes()[0].config.body); assert.equal(first.amount, "0.10"); assert.ok(node.querySelector("[data-movement-save]").disabled);
  h.pending[0].reject({ status: 0, message: "فقد الرد" }); await settle();
  assert.ok(node.querySelector("#treasuryAmount").disabled, "uncertain operation cannot become a new financial command");
  node = h.render("account-deposit"); await settle(); submit(node, "#treasuryMovementForm");
  var retry = JSON.parse(h.writes()[1].config.body); assert.deepStrictEqual(retry, first, "retry survives tab rendering with the same operation identity");
  h.pending[1].resolve({ id: 7, amount: "0.10", balanceAfter: "100.20", createdUtc: "2026-10-28T12:00:00Z", replayed: true }); await settle();
  assert.equal(node.querySelector("#treasuryAmount").value, ""); assert.ok(!node.querySelector("#treasuryAmount").disabled); assert.ok(node.querySelector("[data-treasury-receipt]").innerHTML.includes("100.20"));
  assert.equal(h.context.state.treasuryDraft.pending, null);
  var withdraw = harness(), target = withdraw.render("account-withdraw"); await settle();
  set(target, "#treasuryAccount", "1", "onchange"); set(target, "#treasuryAmount", "1000", "oninput"); submit(target, "#treasuryMovementForm");
  assert.equal(withdraw.writes()[0].url, "/api/treasury/withdraw");
  withdraw.pending[0].reject({ status: 409, message: "الرصيد غير كاف" }); await settle();
  assert.equal(target.querySelector("#treasuryAmount").value, "1000"); assert.ok(!target.querySelector("#treasuryAmount").disabled);
}
async function stale() {
  var h = harness(true), node = h.render("accounts-list"), previous = node.innerHTML;
  h.leave(); h.reads[0].resolve(h.rows); await settle(); assert.equal(node.innerHTML, previous);
  var fail = harness(true), target = fail.render("account-withdraw"); fail.reads[0].reject({ message: "انقطع الاتصال" }); await settle();
  assert.ok(target.querySelector("[data-retry]")); target.querySelector("[data-retry]").onclick(); fail.reads[1].resolve([]); await settle();
  assert.ok(target.querySelector("[data-treasury-form]").innerHTML.includes("لا توجد خزن"));
}
module.exports = async function () {
  assert.deepStrictEqual(registry.modules.find(function (item) { return item.id === "treasury"; }).items, ["accounts-list", "account-deposit", "account-withdraw"]);
  ["account-add", "accounts-balance", "account-movements"].forEach(function (route) { var screen = registry.screens.find(function (item) { return item.route === route; }); assert.ok(screen.navigationHidden); assert.equal(screen.redirectTo, "accounts-list"); });
  await directory(); await movement(); await stale(); console.log("Treasury directory, CRUD, exact amount payload, retry identity, pending state and stale response tests passed.");
};
if (require.main === module) module.exports().catch(function (error) { console.error(error); process.exitCode = 1; });
