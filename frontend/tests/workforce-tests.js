"use strict";
var assert = require('assert'), fs = require('fs'), path = require('path'), vm = require('vm');
var Element = require('./inventory-tests.js').Element;
var workforce = fs.readFileSync(path.join(__dirname, '../js/modules/workforce.js'), 'utf8');
var registry = JSON.parse(fs.readFileSync(path.join(__dirname, '../data/screens.json'), 'utf8'));
function settle() { return new Promise(function (resolve) { setImmediate(resolve); }); }
function harness(deferred) {
  var calls = [], pending = [], reads = [], options, modal, active = true, seq = 0;
  var rows = [{ id: 1, code: 'EMP-000001', name: 'موظف محفوظ', version: 1, isActive: true, attendanceEnabled: false }];
  var win = { crypto: { getRandomValues: function (bytes) { bytes.fill(++seq); } }, AlKhayalRenderers: {}, AlKhayalUtils: { escape: function (v) { return String(v == null ? '' : v); }, debounce: function (f) { return f; }, dateTime: String }, AlKhayalFeatures: { isEnabled: function () { return false; } },
    AlKhayalUI: { pageHeader: function (s, a) { return a || ''; }, toast: function () {}, closeModal: function () { modal.isConnected = false; },
      field: function (d, v) { if (d.type === 'select') return '<label>' + d.label + '<select name="' + d.id + '">' + d.options.map(function (o) { return '<option value="' + o.value + '"' + (String(v || '') === String(o.value) ? ' selected' : '') + '>' + o.label + '</option>'; }).join('') + '</select></label>'; return '<label>' + d.label + '<input name="' + d.id + '" value="' + (v == null ? '' : v) + '"></label>'; },
      modal: function (o) { options = o; modal = new Element('section'); modal.innerHTML = o.body + o.actions; var f = modal.querySelector('[data-workforce-form]'); if (f) { f.elements = f.querySelectorAll('input,select,textarea'); f.elements.forEach(function (n) { n.name = n.attrs.name; }); f.reportValidity = function () { return true; }; } o.onOpen(modal); }
    }, AlKhayalApi: { request: function (url, config) {
      calls.push({ url: url, config: config });
      if (config) return new Promise(function (resolve, reject) { pending.push({ resolve: resolve, reject: reject }); });
      if (url === '/api/users') return Promise.resolve([{ id: 3, displayName: 'مدير', username: 'owner', isActive: true }]);
      if (deferred) return new Promise(function (resolve, reject) { reads.push({ resolve: resolve, reject: reject }); });
      return Promise.resolve({ items: rows, total: rows.length, page: 1, pageSize: 25 });
    } }
  };
  vm.runInNewContext(workforce, { window: win, Uint8Array: Uint8Array });
  var context = { state: {}, user: { id: 3, role: 'administrator' }, isActive: function () { return active; }, navigate: function () {} };
  return { calls: calls, pending: pending, reads: reads, rows: rows, context: context, options: function () { return options; }, modal: function () { return modal; }, leave: function () { active = false; },
    render: function (route) { var node = new Element('section'), s = registry.screens.find(function (r) { return r.route === route; }); win.AlKhayalRenderers[s.renderer](s, node, context); var form=node.querySelector("[data-filters]"); if(form){form.elements=form.querySelectorAll("input,select,textarea");form.elements.forEach(function(n){n.name=n.attrs.name;});} return node; },
    writes: function () { return calls.filter(function (c) { return c.config; }); }
  };
}
async function employeeForms() {
  var h = harness(), node = h.render('employees-list'); await settle(); await settle();
  assert.ok(node.querySelector('[data-results]').innerHTML.includes('موظف محفوظ'));
  var input = node.querySelector('[name="search"]'); input.value = 'بحث'; input.oninput(); await settle();
  assert.strictEqual(node.querySelector('[name="search"]'), input, 'search stays mounted');
  assert.ok(h.calls.some(function (c) { return c.url.includes('search=%D8%A8%D8%AD%D8%AB'); }));
  node.querySelector('[data-new]').onclick(); var modal = h.modal(), name = modal.querySelector('[name="name"]'); name.value = 'الاسم فقط';
  var form = modal.querySelector('[data-workforce-form]'), button = modal.querySelector('[data-save]'); button.onclick(); button.onclick();
  assert.equal(h.writes().length, 1); assert.equal(h.options().beforeClose(), false);
  var body = JSON.parse(h.writes()[0].config.body); assert.equal(body.name, 'الاسم فقط'); assert.equal(body.salary, null); assert.equal(body.payday, null); assert.equal(body.linkedUserId, null); assert.equal(body.attendanceEnabled, false);
  h.pending[0].reject({ status: 0, message: 'فقد الرد' }); await settle(); assert.equal(button.disabled, false); assert.equal(name.disabled, true);
  button.onclick(); assert.deepStrictEqual(JSON.parse(h.writes()[1].config.body), body, 'lost response retry preserves key and payload');
  h.pending[1].reject({ status: 400, message: 'حقل غير صالح' }); await settle(); assert.equal(name.disabled, false); assert.equal(name.value, 'الاسم فقط'); assert.equal(h.options().beforeClose(), true);
  form.onsubmit({ preventDefault: function () {} }); h.pending[2].resolve({ id: 1 }); await settle();
  assert.strictEqual(node.querySelector('[name="search"]'), input);
}
async function staleReads() {
  var h = harness(true), node = h.render('employees-list'); await settle();
  var search = node.querySelector('[name="search"]'); search.value = 'الأحدث'; search.oninput();
  h.reads[1].resolve({ items: [{ id: 2, name: 'الأحدث', code: 'EMP-000002' }], total: 1, page: 1, pageSize: 25 }); await settle();
  h.reads[0].resolve({ items: [{ id: 1, name: 'قديم', code: 'EMP-000001' }], total: 1, page: 1, pageSize: 25 }); await settle();
  assert.ok(node.querySelector('[data-results]').innerHTML.includes('الأحدث')); assert.ok(!node.querySelector('[data-results]').innerHTML.includes('قديم'));
  search.value = 'غير مرئي'; search.oninput(); h.leave(); var before = node.querySelector('[data-results]').innerHTML;
  h.reads[2].resolve({ items: [], total: 0, page: 1, pageSize: 25 }); await settle(); assert.equal(node.querySelector('[data-results]').innerHTML, before);
}
async function shiftForm() {
  var h = harness(), node = h.render('shifts-list'); await settle();
  node.querySelector('[data-open-shift]').onclick(); await settle();
  var modal = h.modal(); modal.querySelector('[name="amount"]').value = '10.10'; modal.querySelector('[data-save]').onclick(); modal.querySelector('[data-save]').onclick();
  assert.equal(h.writes().length, 1); var body = JSON.parse(h.writes()[0].config.body); assert.equal(body.amount, '10.10'); assert.equal(body.cashierId, 3); assert.equal(h.writes()[0].url, '/api/workforce/shifts/open');
  h.pending[0].reject({ status: 409, message: 'وردية مفتوحة' }); await settle(); assert.equal(modal.querySelector('[data-save]').disabled, false);
}
module.exports = async function () {
  ['employees-list', 'attendance', 'attendance-history', 'shifts-list', 'shift-reports', 'shift-details'].forEach(function (route) { assert.ok(registry.screens.find(function (s) { return s.route === route; }).renderer.startsWith('workforce-')); });
  assert.ok(workforce.includes('discrepanciesOnly') && workforce.includes('shifts/current'));
  assert.ok(!workforce.includes('window.print') && !workforce.includes('mock-services'));
  await employeeForms(); await staleReads(); await shiftForm();
};
