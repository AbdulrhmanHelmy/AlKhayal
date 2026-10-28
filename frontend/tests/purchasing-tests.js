"use strict";
var assert = require("assert"), fs = require("fs"), path = require("path"), vm = require("vm");
var Element = require("./inventory-tests.js").Element;
Object.defineProperty(Element.prototype, "id", { get: function () { return this.attrs.id || ""; }, configurable: true });
var source = fs.readFileSync(path.join(__dirname, "../js/modules/purchasing.js"), "utf8");
function settle() { return new Promise(function (resolve) { setImmediate(resolve); }); }
function product(id, name) { return { id: id, name: name, productType: "stock", isActive: true, isArchived: false }; }
function harness() {
  var calls = [], searches = [], writes = [], pending = [], active = true;
  var container = new Element("section");
  container.contains = function (node) { for (; node; node = node.parent) if (node === container) return true; return false; };
  var api = { request: function (url, config) {
    calls.push({ url: url, config: config });
    if (config) { writes.push(JSON.parse(config.body)); return new Promise(function (resolve, reject) { pending.push({resolve:resolve,reject:reject}); }); }
    if (url === "/api/suppliers") return Promise.resolve([{ id: 2, name: "مورد", payable: 0 }]);
    if (url === "/api/inventory/warehouses") return Promise.resolve([{ id: 1, name: "مخزن", isActive: true }]);
    if (url === "/api/treasury/accounts") return Promise.resolve([{id:9,name:"الخزنة",type:"cash",balance:"100.00"},{id:10,name:"البنك",type:"bank",balance:"200.00"}]);
    if (url.indexOf("/api/inventory/products?") === 0) return new Promise(function (resolve, reject) { searches.push({ url: url, resolve: resolve, reject: reject }); });
    if (url === "/api/inventory/products/7") return Promise.resolve({ id: 7, name: "صنف فعلي", purchasePrice: "10.10", salePrice: "12.20", capabilities: [], variants: [{ id: 71, name: "افتراضي", isActive: true }], units: [{ id: 72, name: "قطعة", isActive: true, isBase: true, isPurchaseUnit: true }] });
    throw new Error("Unexpected URL: " + url);
  } };
  var win = { AlKhayalUtils: { escape: function (x) { return String(x == null ? "" : x); }, money: String, debounce: function (fn) { return fn; } }, AlKhayalUI: { pageHeader: function () { return ""; }, toast: function () {} }, AlKhayalRenderers: {} };
  vm.runInNewContext(source, { window: win, AlKhayalApi: api, BigInt: BigInt }, { filename: "purchasing.js" });
  win.AlKhayalRenderers["purchase-management"]({}, container, { state: {}, isActive: function () { return active; } });
  return { node: container, calls: calls, searches: searches, writes: writes, pending: pending, leave: function () { active = false; }, click: function (selector) { container.onclick({ target: container.querySelector(selector) }); } };
}
async function browseAndSelect() {
  var h = harness(); await settle();
  assert.equal(h.searches.length, 1, "opening the page must load products without typing");
  assert.ok(h.searches[0].url.includes("search=&page=1&pageSize=20"));
  h.searches[0].resolve({ items: [product(7, "صنف فعلي"), { id: 8, name: "مؤرشف", productType: "stock", isActive: true, isArchived: true }], totalCount: 21 }); await settle();
  var search = h.node.querySelector("#purchaseSearch"), results = h.node.querySelector("#purchaseResults");
  assert.equal(results.hidden, false); assert.ok(results.innerHTML.includes("صنف فعلي")); assert.ok(!results.innerHTML.includes("مؤرشف"));
  h.click('[data-purchase-products-page="2"]'); assert.ok(h.searches[1].url.includes("page=2"));
  h.searches[1].resolve({ items: [product(7, "صنف فعلي")], totalCount: 21 }); await settle();
  assert.strictEqual(h.node.querySelector("#purchaseSearch"), search, "pagination must preserve the mounted search");
  h.click('[data-add-product="7"]'); await settle();
  assert.ok(h.node.querySelector("#purchaseLines").querySelector('[data-line="0"]'), "selection must create an invoice line");
  assert.equal(h.node.querySelector('[data-line-field="purchaseUnitPrice"]').value, "10.10");
  assert.equal(h.node.querySelector('[data-line-field="saleUnitPrice"]').value, "12.20");
  h.click("[data-purchase-save]");
  assert.equal(h.writes[0].supplierId, 2); assert.equal(h.writes[0].warehouseId, 1);
  assert.equal(h.writes[0].lines[0].variantId, 71); assert.equal(h.writes[0].lines[0].productUnitId, 72);
  assert.equal(h.writes[0].lines[0].purchaseUnitPrice, "10.10");
}
async function searchAndRetry() {
  var h = harness(); await settle();
  var input = h.node.querySelector("#purchaseSearch");
  input.value = "ق"; input.oninput(); assert.equal(h.searches.length, 2, "single-character search must run");
  h.searches[0].resolve({ items: [product(1, "نتيجة قديمة")], totalCount: 1 }); await settle();
  assert.ok(!h.node.querySelector("#purchaseResults").innerHTML.includes("نتيجة قديمة"));
  input.value = "قميص"; input.oninput();
  h.searches[2].resolve({ items: [product(2, "قميص حديث")], totalCount: 1 }); await settle();
  h.searches[1].resolve({ items: [product(3, "رد متأخر")], totalCount: 1 }); await settle();
  assert.ok(h.node.querySelector("#purchaseResults").innerHTML.includes("قميص حديث"));
  assert.ok(!h.node.querySelector("#purchaseResults").innerHTML.includes("رد متأخر"));
  assert.strictEqual(h.node.querySelector("#purchaseSearch"), input);
  input.value = ""; input.oninput(); assert.ok(h.searches[3].url.includes("search=&page=1"));
  h.searches[3].reject({ message: "تعذر الاتصال" }); await settle();
  h.click("[data-purchase-products-retry]"); assert.ok(h.searches[4].url.includes("search=&page=1"));
  h.searches[4].resolve({ items: [], totalCount: 0 }); await settle();
  assert.ok(h.node.querySelector("#purchaseResults").innerHTML.includes("لا توجد أصناف نشطة"));
  input.value = "جديد"; input.oninput(); h.leave();
  var html = h.node.querySelector("#purchaseResults").innerHTML;
  h.searches[5].resolve({ items: [product(4, "رد بعد المغادرة")], totalCount: 1 }); await settle();
  assert.equal(h.node.querySelector("#purchaseResults").innerHTML, html, "navigation must discard late product responses");
}
async function cashSelection() {
  var h=harness();await settle();
  h.searches[0].resolve({items:[product(7,"صنف فعلي")],totalCount:1});await settle();
  h.click('[data-add-product="7"]');await settle();
  assert.ok(h.node.querySelector("#purchaseTreasuryField").hidden);
  var mode=h.node.querySelector("#purchaseMode");mode.value="cash";h.node.onchange({target:mode});
  assert.ok(!h.node.querySelector("#purchaseTreasuryField").hidden);
  assert.ok(h.node.querySelector("#purchaseTreasury").required);
  h.click("[data-purchase-save]");assert.equal(h.writes.length,0,"cash requires explicit account selection");
  var account=h.node.querySelector("#purchaseTreasury");account.value="9";h.node.onchange({target:account});
  h.click("[data-purchase-save]");h.click("[data-purchase-save]");assert.equal(h.writes.length,1);
  assert.equal(h.writes[0].treasuryAccountId,9);assert.equal(h.writes[0].paymentMode,"cash");
  h.pending[0].resolve({id:5,status:"draft",paymentMode:"cash",treasuryAccountId:9,lines:h.writes[0].lines});await settle();
  assert.equal(h.node.querySelector("#purchaseTreasury").value,"9","saved account must survive redraw");
  assert.ok(!h.node.querySelector("[data-purchase-save]").disabled,"successful save must release busy state");
  account=h.node.querySelector("#purchaseTreasury");account.value="10";h.node.onchange({target:account});
  h.click("[data-purchase-post]");assert.equal(h.writes.length,1,"changed account must be saved before posting");
  mode=h.node.querySelector("#purchaseMode");mode.value="credit";h.node.onchange({target:mode});
  assert.ok(h.node.querySelector("#purchaseTreasuryField").hidden);assert.equal(h.node.querySelector("#purchaseTreasury").value,"");
  h.click("[data-purchase-save]");assert.equal(h.writes[1].treasuryAccountId,null);assert.equal(h.writes[1].paymentMode,"credit");
}
module.exports = async function () { await browseAndSelect(); await searchAndRetry(); await cashSelection(); console.log("Purchasing browse, selection, cash-account draft payload, retry and stale response tests passed."); };
if (require.main === module) module.exports().catch(function (error) { console.error(error); process.exitCode = 1; });
