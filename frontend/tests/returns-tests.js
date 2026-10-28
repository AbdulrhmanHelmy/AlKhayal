"use strict";
var assert = require("assert"), fs = require("fs"), path = require("path"), vm = require("vm");
var Element = require("./inventory-tests.js").Element;
var source = fs.readFileSync(path.join(__dirname, "../js/modules/returns.js"), "utf8"), app = fs.readFileSync(path.join(__dirname, "../js/app.js"), "utf8");
var registry = JSON.parse(fs.readFileSync(path.join(__dirname, "../data/screens.json"), "utf8"));
function settle() { return new Promise(function (resolve) { setImmediate(resolve); }); }
function harness() {
  var calls = [], posts = [], navigation = [], active = true, seq = 0, deferredLookup = null;
  var invoice = { id: 4, type: "sales", number: "S-4", partyName: "عميل", warehouseId: 1, paymentMode: "cash", lines: [{ id: 9, productName: "صنف", unitName: "قطعة", quantity: "2", available: "1", units: [] }] };
  var record = { id: 2, type: "sales", number: "SR-2", invoiceNumber: "S-4", partyName: "عميل", total: "10.10", cash: "10.10", settlement: "cash", createdUtc: "2026-10-28T12:00:00Z", lines: [{ productName: "صنف", unitName: "قطعة", quantity: "1", amount: "10.10", serials: [] }], movements: [{ id: 5, accountName: "مرتجعات البيع", type: "withdraw", amount: "10.10" }] };
  var win = { AlKhayalRenderers: { "under-construction": function () { throw new Error("returns not enabled in shell"); } }, crypto: { getRandomValues: function (b) { b.fill(++seq); } }, AlKhayalUtils: { escape: function (v) { return String(v == null ? "" : v).replace(/</g, "&lt;"); }, debounce: function (fn) { return fn; } }, AlKhayalUI: { initialize: function () {}, pageHeader: function (s, actions) { return actions || ""; }, toast: function () {} }, AlKhayalApi: { request: function (url, config) {
    calls.push({ url: url, config: config }); if (config) return new Promise(function (resolve, reject) { posts.push({ resolve: resolve, reject: reject }); });
    if (url.indexOf("/api/returns/invoice?") === 0) return deferredLookup ? new Promise(function (resolve, reject) { deferredLookup.push({ resolve: resolve, reject: reject }); }) : Promise.resolve(invoice);
    return Promise.resolve(url.indexOf("/api/returns?") === 0 ? [record] : record);
  } } };
  vm.runInNewContext(source, { window: win, Uint8Array: Uint8Array, Date: Date }, { filename: "returns.js" });
  vm.runInNewContext(app.slice(0, app.indexOf("  function roleLabel(")) + "window.__dispatch=implementationRenderer;}());", { window: win, document: { getElementById: function () { return null; } } });
  var context = { state: {}, isActive: function () { return active; }, navigate: function (route, state) { navigation.push({ route: route, state: state }); } };
  return { invoice: invoice, record: record, context: context, calls: calls, posts: posts, navigation: navigation, defer: function () { deferredLookup = []; return deferredLookup; }, leave: function () { active = false; },
    render: function (route) { var node = new Element("section"), screen = registry.screens.find(function (s) { return s.route === route; }); win.__dispatch(screen)(screen, node, context); return node; }, writes: function () { return calls.filter(function (x) { return x.config; }); } };
}
function submit(node, selector) { node.querySelector(selector).onsubmit({ preventDefault: function () {} }); }
function input(node, selector, value) { var field = node.querySelector(selector); field.value = value; field.oninput(); }
async function management() {
  var h = harness(), node = h.render("returns-overview"); await settle();
  var search = node.querySelector("#returnSearch"); input(node, "#returnSearch", "S-4"); await settle(); assert.strictEqual(search, node.querySelector("#returnSearch"));
  input(node, "#returnSearch", ""); await settle(); assert.strictEqual(search, node.querySelector("#returnSearch")); assert.ok(node.querySelector("[data-return-id]"), "cleared search reloads the unfiltered list");
  var row = node.querySelector("[data-return-id]"); node.querySelector("[data-return-results]").onclick({ target: row }); assert.equal(h.navigation[0].route, "return-details"); assert.equal(h.navigation[0].state.returnId, 2);
  node.querySelector("[data-add-return]").onclick(); input(node, "#returnNumber", "S-4"); submit(node, "#returnLookup"); await settle();
  assert.ok(node.querySelector("[data-return-lines]").innerHTML.includes("من خزنة مرتجعات البيع فقط"));
  assert.ok(!node.querySelector("[data-return-lines]").innerHTML.includes("إيراد الوردية"), "sales refunds must not describe any shift-revenue funding");
  input(node, '[data-return-quantity="9"]', "2"); input(node, "#returnReason", "مرتجع"); submit(node, "#managedReturnForm"); assert.equal(h.writes().length, 0, "excess quantity rejected before submission");
  input(node, '[data-return-quantity="9"]', "1"); submit(node, "#managedReturnForm"); submit(node, "#managedReturnForm"); assert.equal(h.writes().length, 1);
  var original = JSON.parse(h.writes()[0].config.body); assert.equal(original.type, "sales"); assert.equal(original.invoiceId, 4); assert.equal(original.lines[0].quantity, "1");
  h.posts[0].reject({ status: 503, message: "فقد الرد" }); await settle(); assert.ok(node.querySelector("#returnNumber").disabled); assert.ok(node.querySelector('[data-return-quantity="9"]').disabled);
  node = h.render("returns-overview"); await settle(); submit(node, "#managedReturnForm"); assert.deepStrictEqual(JSON.parse(h.writes()[1].config.body), original);
  h.posts[1].resolve({ type: "sales", id: 8 }); await settle(); assert.equal(h.navigation[1].state.returnId, 8); assert.equal(h.context.state.returnDraft.invoice, null);
}
async function purchaseAndStale() {
  var h = harness(); h.invoice.type = "purchase"; h.invoice.paymentMode = "credit"; var node = h.render("returns-overview"); await settle();
  var type = node.querySelector("#returnType"); type.value = "purchase"; type.onchange(); input(node, "#returnNumber", "P-4"); submit(node, "#returnLookup"); await settle();
  assert.equal(node.querySelector("#returnSettlement").value, "supplier_credit"); input(node, '[data-return-quantity="9"]', "1"); submit(node, "#managedReturnForm"); assert.equal(JSON.parse(h.writes()[0].config.body).returnMode, "supplier_credit");
  h.posts[0].reject({ status: 409, message: "رصيد غير كاف" }); await settle(); assert.equal(node.querySelector("#returnNumber").disabled, false); assert.equal(node.querySelector('[data-return-quantity="9"]').value, "1");
  var stale = harness(), pending = stale.defer(); node = stale.render("returns-overview"); await settle(); input(node, "#returnNumber", "old"); submit(node, "#returnLookup"); input(node, "#returnNumber", "new"); submit(node, "#returnLookup"); pending[1].resolve(stale.invoice); await settle(); pending[0].reject({ message: "old error" }); await settle(); assert.equal(node.querySelector("[data-return-error]").hidden, true); assert.ok(node.querySelector("#managedReturnForm"));
}
async function details() {
  var h = harness(); h.context.state.returnType = "sales"; h.context.state.returnId = 2; var node = h.render("return-details"); await settle(); assert.ok(node.querySelector("[data-return-detail]").innerHTML.includes("مرتجعات البيع")); assert.ok(node.querySelector("[data-return-detail]").innerHTML.includes("S-4"));
  node.querySelector("[data-returns-back]").onclick(); assert.equal(h.navigation[0].route, "returns-overview");
  var direct = harness(), directNode = direct.render("return-details"); await settle();
  assert.ok(directNode.querySelector("[data-return-detail]").innerHTML.includes("اختر مرتجعًا من القائمة"));
  var picker = directNode.querySelector("#returnDetailChoice"); assert.equal(picker.disabled, false);
  picker.value = "sales:2"; picker.onchange(); await settle();
  assert.equal(direct.context.state.returnId, 2); assert.ok(directNode.querySelector("[data-return-detail]").innerHTML.includes("S-4"));
  var search = directNode.querySelector("#returnDetailSearch"); input(directNode, "#returnDetailSearch", "S-4"); await settle();
  assert.strictEqual(search, directNode.querySelector("#returnDetailSearch")); assert.equal(picker.value, "sales:2");
  input(directNode, "#returnDetailSearch", ""); await settle(); assert.equal(picker.disabled, false);
}
module.exports = async function () {
  assert.deepStrictEqual(registry.modules.find(function (m) { return m.id === "returns"; }).items, ["returns-overview", "return-details"]);
  assert.notEqual(registry.screens.find(function (x) { return x.route === "return-details"; }).navigationHidden, true);
  ["sales-returns", "purchase-returns"].forEach(function (route) { var s = registry.screens.find(function (x) { return x.route === route; }); assert.equal(s.redirectTo, "returns-overview"); assert.equal(s.navigationHidden, true); });
  assert.equal(registry.screens.find(function (x) { return x.route === "return-details"; }).permissionRoute, "returns-overview");
  var navigateLine=app.split(/\r?\n/).find(function(line){return line.indexOf("  function navigate(")===0;});
  var shell={screensByRoute:{"return-details":{title:"تفاصيل المرتجع"}},tabs:[],resolveRoute:function(route){return route;},routeEnabled:function(){return true;},UI:{toast:function(){}},activateTab:function(){},localStorage:{setItem:function(){}}};
  vm.runInNewContext(navigateLine+'\nnavigate("return-details",{returnType:"sales",returnId:1});navigate("return-details",{returnType:"purchase",returnId:2});',shell);
  assert.equal(shell.tabs.length,1);assert.equal(shell.tabs[0].state.returnId,2);assert.equal(shell.tabs[0].state.returnType,"purchase","existing detail tab receives the newly selected return identity");
  await management(); await purchaseAndStale(); await details(); console.log("Unified returns shell dispatch, lookup, quantity validation, treasury details, retry identity and navigation tests passed.");
};
