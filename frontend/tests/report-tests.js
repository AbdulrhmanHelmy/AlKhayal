"use strict";
var assert = require('assert'), fs = require('fs'), path = require('path'), vm = require('vm'), Element = require('./inventory-tests.js').Element;
function settle() { return new Promise(function (resolve) { setImmediate(resolve); }); }
function harness(route, allowed) {
  var pending = [], calls = [], options = [], navigation = [], active = true, host = new Element('section');
  var html = Object.getOwnPropertyDescriptor(Element.prototype, 'innerHTML');
  Object.defineProperty(host, 'innerHTML', { get: function () { return html.get.call(host); }, set: function (value) {
    html.set.call(host, value);
    host.querySelectorAll('form').forEach(function (form) {
      form.elements = {};
      form.querySelectorAll('input,select').forEach(function (input) { form.elements[input.getAttribute('name')] = input; });
      var defaults = {}; Object.keys(form.elements).forEach(function (name) { defaults[name] = form.elements[name].value; });
      form.reset = function () { Object.keys(defaults).forEach(function (name) { form.elements[name].value = defaults[name]; }); };
    });
  } });
  var win = { AlKhayalRenderers: {}, AlKhayalUtils: { escape: function (x) { return String(x == null ? '' : x).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/"/g, '&quot;'); } }, AlKhayalUI: { pageHeader: function () { return ''; } }, AlKhayalApi: { request: function (url, config) { calls.push(url); options.push(config); return new Promise(function (resolve, reject) { pending.push({ resolve: resolve, reject: reject }); }); } } };
  var context = { window: win, URLSearchParams: URLSearchParams, Date: Date };
  ['report-ui', 'sales-report', 'transaction-reports', 'expense-report', 'net-profit-report', 'stock-reports', 'operational-reports'].forEach(function (name) { vm.runInNewContext(fs.readFileSync(path.join(__dirname, '../js/modules/' + name + '.js'), 'utf8'), context); });
  var renderContext = { state: {}, canNavigate: function (route) { return typeof allowed === 'function' ? allowed(route) : allowed !== false; }, isActive: function () { return active; }, navigate: function (to, state) { navigation.push({ route: to, state: state }); } };
  win.AlKhayalRenderers[['report-customers','report-suppliers','report-shifts','report-attendance','report-maintenance','report-installments','report-overview'].indexOf(route)>=0 ? 'operational-reports' : ['report-product-profit','report-inventory-valuation','report-top-products','report-low-stock','report-inventory-movements'].indexOf(route) >= 0 ? 'stock-reports' : route === 'report-net-profit' ? 'net-profit-report' : route === 'report-sales' ? 'sales-report' : route === 'report-expenses' ? 'expense-report' : 'transaction-reports']({ route: route }, host, renderContext);
  return { host: host, form: host.querySelector('form'), calls: calls, options:options, pending: pending, navigation: navigation, leave: function () { active = false; } };
}
function data(route, count) {
  return { count: count, total: '12.3400', returns: '1.0000', cancellations: '0.0000', netEvents: '11.3400', cash: '2.0000', wallet: '3.0000', card: '4.0000', credit: '5.0000', unclassified: '6.0000', salesReturnCount: 1, purchaseReturnCount: 1, salesReturns: '7.0000', purchaseReturns: '8.0000',
    items: count ? [{ id: 9, invoiceId: 8, invoiceNumber: 'INV-8', reference: 'REF-9', invoiceDate: '2026-03-01', postedUtc: '2026-03-01T12:00:00Z', eventUtc: '2026-03-01T12:00:00Z', customerName: 'عميل', cashierName: 'كاشير', party: 'مورد', method: 'cash', total: '12.3400', type: 'sales', status: 'posted', paymentMode: 'credit' }] : [],
    instruments: count ? [{ id: 7, type: 'wallet', name: 'محفظة', number: '00123', isActive: false, invoiceCount: 2, total: '3.0000' }] : [] };
}
async function exercise(route) {
  var h = harness(route), form = h.form, search = form.elements.search, result = h.host.querySelector('[data-results]');
  assert.ok(result.innerHTML.includes('جارٍ'));
  assert.equal(form.elements.period.value, 'month');
  assert.equal(new URL('http://local' + h.calls[0]).searchParams.get('from').slice(-2), '01');
  h.pending[0].resolve(data(route, 25)); await settle();
  assert.ok(result.innerHTML.includes('12.3400') || result.innerHTML.includes('3.0000'));
  assert.ok(result.innerHTML.includes('INV-8') || result.innerHTML.includes('REF-9') || result.innerHTML.includes('00123'));
  var link = result.querySelector('[data-report-route]'); link.onclick();
  assert.equal(h.navigation[0].route, route === 'report-sales' || route === 'report-returns' ? 'sales-management' : route === 'report-purchases' ? 'purchase-management' : 'payment-history');
  assert.equal(h.navigation[0].state.saleInvoiceId || h.navigation[0].state.invoiceId || h.navigation[0].state.instrumentId, route === 'report-sales' ? 9 : route === 'report-payment-methods' ? 7 : 8);
  result.querySelector('[data-next]').onclick(); assert.ok(h.calls[1].includes('page=2')); h.pending[1].resolve(data(route, 25)); await settle();
  result.querySelector('[data-prev]').onclick(); assert.ok(h.calls[2].includes('page=1')); h.pending[2].resolve(data(route, 0)); await settle(); assert.ok(result.innerHTML.includes('لا توجد'));
  ['today', 'yesterday', 'week', 'current-week', 'month', 'previous-month'].forEach(function (period) {
    form.elements.period.value = period; form.elements.period.onchange();
    var end = new Date(form.elements.to.value + 'T12:00:00'), start = new Date(form.elements.from.value + 'T12:00:00');
    if (period === 'today') assert.equal(form.elements.from.value, form.elements.to.value);
    if (period === 'yesterday') { var yesterday = new Date(); yesterday.setDate(yesterday.getDate() - 1); assert.equal(form.elements.from.value, form.elements.to.value); assert.equal(start.getFullYear(), yesterday.getFullYear()); assert.equal(start.getMonth(), yesterday.getMonth()); assert.equal(start.getDate(), yesterday.getDate()); }
    if (period === 'week') { end.setDate(end.getDate() - 6); assert.equal(start.getTime(), end.getTime()); }
    if (period === 'current-week') { assert.equal(start.getDay(), 6); assert.ok(end.getTime() - start.getTime() <= 7 * 86400000); }
    if (period === 'month') assert.equal(start.getDate(), 1);
    if (period === 'previous-month') { var now = new Date(), previous = new Date(now.getFullYear(), now.getMonth() - 1, 1), last = new Date(now.getFullYear(), now.getMonth(), 0); assert.equal(start.getFullYear(), previous.getFullYear()); assert.equal(start.getMonth(), previous.getMonth()); assert.equal(start.getDate(), 1); assert.equal(end.getDate(), last.getDate()); assert.equal(end.getMonth(), last.getMonth()); }
  });
  form.elements.from.value = '2026-02-01'; form.elements.from.onchange(); assert.equal(form.elements.period.value, 'custom'); form.elements.to.value = '2026-02-28'; search.value = 'needle';
  form.onsubmit({ preventDefault: function () {} }); assert.ok(h.calls[3].includes('from=2026-02-01') && h.calls[3].includes('search=needle'));
  assert.strictEqual(form.elements.search, search, 'search must remain mounted');
  h.pending[3].reject(new Error('تعذر الاتصال')); await settle(); assert.ok(result.innerHTML.includes('تعذر الاتصال'));
  result.querySelector('[data-retry]').onclick(); h.pending[4].resolve(data(route, 0)); await settle();
  h.host.querySelector('[data-reset]').onclick(); assert.equal(search.value, ''); assert.equal(form.elements.period.value, 'month'); assert.ok(h.calls[5].includes('page=1'));
  form.onsubmit({ preventDefault: function () {} }); h.pending[6].resolve(data(route, 25)); await settle(); var current = result.innerHTML; h.pending[5].resolve(data(route, 0)); await settle(); assert.equal(result.innerHTML, current, 'older response must be ignored');
  form.elements.to.value = '2020-01-01'; form.onsubmit({ preventDefault: function () {} }); assert.equal(h.calls.length, 7); assert.ok(result.innerHTML.includes('تحقق'));
  form.elements.to.value = '2026-12-31'; form.onsubmit({ preventDefault: function () {} }); h.leave(); h.pending[7].resolve(data(route, 25)); await settle(); assert.ok(result.innerHTML.includes('جارٍ'), 'leaving screen prevents repaint');
  var denied = harness(route, false); denied.pending[0].resolve(data(route, 1)); await settle(); assert.equal(denied.host.querySelector('[data-report-route]'), null, 'no dead drill-down when target permission is absent');
}
function profitData(complete, canClassify) {
  var figures = {complete:complete,revenue:'2400.0000',cogs:'1240.0000',grossProfit:'1160.0000',expenses:'160.0000',netProfit:'1000.0000',netMargin:'41.67',unclassifiedAmount:'30.0000',unclassifiedCount:complete?0:1,missingRevenueCount:0,missingCostCount:0,incompleteGrossAmount:'0.0000',excludedExpenses:'0.0000',eventCount:1};
  return {summary:figures,items:[Object.assign({period:'2026-03-01'},figures)],expenseCount:21,canClassify:canClassify,expenseItems:[{id:2,category:'إيجار',payee:'جهة',expenseDate:'2020-01-01',postedUtc:'2026-03-01T12:00:00Z',amount:'160.0000',classification:'unclassified',revision:0,reason:null,classifiedBy:null,classifiedUtc:null}]};
}
async function profitExercise() {
  var h=harness('report-net-profit'), form=h.form, output=h.host.querySelector('[data-results]');
  assert.ok(h.calls[0].includes('/api/reports/net-profit?') && h.calls[0].includes('breakdown=daily'));
  h.pending[0].resolve(profitData(false,true));await settle();
  assert.ok(output.innerHTML.includes('الحساب غير مكتمل') && output.innerHTML.includes('2020-01-01'));
  assert.ok(!output.innerHTML.includes('1000.0000'),'incomplete profit must be hidden even if response has a numeric subtotal');
  var panel=output.querySelector('[data-classify]'), save=panel.querySelector('[data-save]'), reason=panel.querySelector('[name="reason"]');
  save.onclick();assert.equal(h.calls.length,1);reason.value='مصروف تشغيلي موثق';panel.querySelector('[name="classification"]').value='included';save.onclick();save.onclick();
  assert.equal(h.calls.length,2,'duplicate click must not issue another classification');assert.equal(h.options[1].method,'POST');
  var command=JSON.parse(h.options[1].body);assert.equal(command.expectedRevision,0);assert.equal(command.classification,'included');
  h.pending[1].reject(new Error('انقطع الاتصال'));await settle();save.onclick();assert.equal(JSON.parse(h.options[2].body).operationKey,command.operationKey,'uncertain retry retains operation identity');
  h.pending[2].resolve({saved:true});await settle();h.pending[3].resolve(profitData(true,true));await settle();assert.ok(output.innerHTML.includes('1000.0000'));
  output.querySelector('[data-next]').onclick();assert.ok(h.calls[4].includes('page=2'));h.pending[4].resolve(profitData(true,false));await settle();assert.equal(output.querySelector('[data-classify]'),null);
  form.elements.breakdown.value='monthly';form.elements.from.value='2026-02-01';form.elements.to.value='2026-03-01';form.onsubmit({preventDefault:function(){}});
  assert.ok(h.calls[5].includes('breakdown=monthly') && h.calls[5].includes('page=1'));
  assert.strictEqual(form.elements.breakdown,form.querySelector('[name="breakdown"]'),'filters remain mounted');
  h.pending[5].reject(new Error('خطأ التقرير'));await settle();output.querySelector('[data-retry]').onclick();h.pending[6].resolve(profitData(true,false));await settle();
  form.onsubmit({preventDefault:function(){}});form.onsubmit({preventDefault:function(){}});h.pending[8].resolve(profitData(false,false));await settle();var latest=output.innerHTML;h.pending[7].resolve(profitData(true,false));await settle();assert.equal(output.innerHTML,latest);
  h.host.querySelector('[data-reset]').onclick();assert.equal(form.elements.period.value,'month');assert.equal(form.elements.breakdown.value,'daily');
  h.leave();h.pending[9].resolve(profitData(true,true));await settle();assert.ok(output.innerHTML.includes('جارٍ'));
  var denied=harness('report-net-profit',false);assert.equal(denied.calls.length,0);assert.ok(denied.host.innerHTML.includes('لا تملك صلاحية'));
  var empty=harness('report-net-profit');var data=profitData(true,false);data.summary.eventCount=0;data.expenseCount=0;data.expenseItems=[];empty.pending[0].resolve(data);await settle();assert.ok(empty.host.querySelector('[data-results]').innerHTML.includes('لا توجد أحداث'));
  console.log('Net profit renderer: exact server figures, incomplete coverage, administrator classification/retry, daily/monthly filters, paging, empty/error and stale responses passed.');
}
async function stockExercise(route) {
  var current = route === 'report-inventory-valuation' || route === 'report-low-stock', moves = route === 'report-inventory-movements';
  var h = harness(route), form = h.form, output = h.host.querySelector('[data-results]'), input = form.elements.search;
  assert.ok(h.calls[0].includes('/api/reports/' + route.substring(7) + '?'));
  assert.equal(!!form.elements.from,!current,'current snapshots must have no date filters');
  var record = {product:'منتج <محفوظ>',variant:'أحمر',sku:'SKU-01',unit:'قطعة',warehouse:'رئيسي',quantity:'8',sold:'12',returned:'3',cancelled:'1',revenue:'2400.0000',recordedAmount:'2400.0000',cost:'1240.0000',profit:'1160.0000',margin:'48.33',salePrice:'200.0000',sellingValue:'1600.0000',complete:true,status:'zero',quantityIn:'0',quantityOut:'2',type:'sale',documentType:'manual_issue',eventUtc:'2026-03-01T12:00:00Z',route:'sales-management',documentId:8,reference:'INV-8'};
  function result(count) {return {count:count,scope:'نطاق موثق',summary:{complete:true,incompleteCount:0,cost:'1240.0000',profit:'1160.0000',revenue:'2400.0000',sellingValue:'1600.0000',zeroCount:1},items:count?[record]:[]};}
  h.pending[0].resolve(result(21));await settle();assert.ok(output.innerHTML.includes('&lt;محفوظ>'),'saved labels must be escaped');
  if (moves) { output.querySelector('[data-report-route]').onclick();assert.equal(h.navigation[0].state.saleInvoiceId,8); }
  output.querySelector('[data-next]').onclick();assert.ok(h.calls[1].includes('page=2'));h.pending[1].resolve(result(21));await settle();
  input.value='needle';form.elements.categoryId.value='2';if(form.elements.warehouseId)form.elements.warehouseId.value='3';
  if (form.elements.type) form.elements.type.value='transfer';if(form.elements.status)form.elements.status.value='available';if(form.elements.sort)form.elements.sort.value='amount';
  form.onsubmit({preventDefault:function(){}});assert.ok(h.calls[2].includes('search=needle') && h.calls[2].includes('categoryId=2') && h.calls[2].includes('page=1'));assert.strictEqual(form.elements.search,input);
  h.pending[2].reject(new Error('خطأ محفوظ'));await settle();assert.ok(output.innerHTML.includes('خطأ محفوظ'));output.querySelector('[data-retry]').onclick();h.pending[3].resolve(result(0));await settle();assert.ok(output.innerHTML.includes('لا توجد'));
  form.onsubmit({preventDefault:function(){}});form.onsubmit({preventDefault:function(){}});var incomplete=result(21);incomplete.summary.complete=false;incomplete.summary.incompleteCount=1;incomplete.summary.cost=null;incomplete.summary.profit=null;incomplete.items=[Object.assign({},record,{complete:false,cost:null,profit:null})];h.pending[5].resolve(incomplete);await settle();var latest=output.innerHTML;h.pending[4].resolve(result(21));await settle();assert.equal(output.innerHTML,latest);
  if(route!=='report-low-stock' && route!=='report-top-products')assert.ok(output.innerHTML.includes('غير مكتمل'));
  h.host.querySelector('[data-reset]').onclick();assert.equal(input.value,'');assert.ok(h.calls[6].includes('page=1'));h.leave();h.pending[6].resolve(result(21));await settle();assert.ok(output.innerHTML.includes('جارٍ'));
  var denied=harness(route,false);assert.equal(denied.calls.length,0);
  if (moves) { var restricted=harness(route,function (target) { return target === route; });restricted.pending[0].resolve(result(1));await settle();assert.equal(restricted.host.querySelector('[data-report-route]'),null,'report access does not grant invoice access'); }
  if (moves) { var noLink=harness(route);var unsupported=result(1);unsupported.items=[Object.assign({},record,{route:null,reference:'حركة 9'})];noLink.pending[0].resolve(unsupported);await settle();assert.equal(noLink.host.querySelector('[data-report-route]'),null); }
}
async function operationalExercise(route) {
  var h=harness(route), form=h.form, output=h.host.querySelector('[data-results]'), report=route.substring(7), overview=report==='overview';
  function values(count) { return overview ? [{key:'NetProfit',complete:false,route:'report-net-profit'},{key:'customers',value:'123.4567',current:true,complete:true,route:'report-customers'}] : {count:count,summary:{count:String(count),balanceScaled:'123.4567',coverageCount:report==='installments'?'1':'0'},items:count?[{id:9,personId:8,party:'<script>عميل</script>',reference:'REF-9',eventUtc:'2026-03-01T12:00:00Z',balanceScaled:'123.4567',status:'open'}]:[]}; }
  if(report==='installments')assert.ok(h.host.innerHTML.includes('تنبيه تغطية'));
  assert.ok(h.calls[0].includes('/api/reports/'+report+'?'));h.pending[0].resolve(values(25));await settle();assert.ok(output.innerHTML.includes('123.4567'));
  if(overview)assert.ok(output.innerHTML.includes('غير مكتمل'));else {assert.ok(output.innerHTML.includes('&lt;script'));output.querySelector('[data-next]').onclick();assert.ok(h.calls[1].includes('page=2'));h.pending[1].resolve(values(0));await settle();assert.ok(output.innerHTML.includes('لا توجد'));}
  var n=h.pending.length;form.elements.from.value='2026-03-01';form.elements.to.value='2026-03-31';if(!overview){form.elements.search.value='needle';form.elements.personId.value='8';form.elements.status.value=report==='customers'||report==='suppliers'?'debt':report==='installments'?'overdue':report==='maintenance'?'تم التسليم':'closed';}
  var search=form.elements.search;form.onsubmit({preventDefault:function(){}});assert.strictEqual(form.elements.search,search);assert.ok(h.calls[n].includes('from=2026-03-01'));if(!overview)assert.ok(h.calls[n].includes('search=needle')&&h.calls[n].includes('personId=8'));
  h.pending[n].resolve(values(1));await settle();var link=output.querySelector('[data-report-route]');link.onclick();assert.ok(h.navigation.length);
  if(report==='customers')assert.equal(h.navigation[0].state.customerStatementId,9);if(report==='suppliers')assert.equal(h.navigation[0].state.supplierId,9);if(report==='shifts')assert.equal(h.navigation[0].state.shiftId,9);if(report==='attendance')assert.equal(h.navigation[0].state.attendanceQuery.employeeId,8);if(report==='maintenance')assert.equal(h.navigation[0].state.maintenanceTicketId,9);if(report==='installments')assert.equal(h.navigation[0].state.installmentContractId,9);
  n=h.pending.length;form.onsubmit({preventDefault:function(){}});h.pending[n].reject(new Error('تعذر الاتصال'));await settle();assert.ok(output.innerHTML.includes('تعذر الاتصال'));output.querySelector('[data-retry]').onclick();h.pending[n+1].resolve(values(0));await settle();
  n=h.pending.length;form.onsubmit({preventDefault:function(){}});form.onsubmit({preventDefault:function(){}});h.pending[n+1].resolve(values(1));await settle();var latest=output.innerHTML;h.pending[n].resolve(values(0));await settle();assert.equal(latest,output.innerHTML);
  n=h.pending.length;h.host.querySelector('[data-reset]').onclick();assert.equal(form.elements.period.value,'month');h.leave();h.pending[n].resolve(values(1));await settle();assert.ok(output.innerHTML.includes('جارٍ'));
  n=h.calls.length;form.elements.to.value='2000-01-01';form.onsubmit({preventDefault:function(){}});assert.equal(n,h.calls.length);
  var denied=harness(route,false);assert.equal(denied.calls.length,0);
  var restricted=harness(route,function(r){return r===route;});restricted.pending[0].resolve(values(1));await settle();if(!overview)assert.equal(restricted.host.querySelector('[data-report-route]'),null);
}
module.exports = async function () {
  for(var route of ["report-customers","report-suppliers","report-shifts","report-attendance","report-maintenance","report-installments","report-overview"]) await operationalExercise(route);
  console.log("Seven operational report renderers: server values, filters, pagination, links, permissions, incomplete overview, errors and stale responses passed.");
  for (var stock of ['report-product-profit','report-inventory-valuation','report-top-products','report-low-stock','report-inventory-movements']) await stockExercise(stock);
  console.log('Five persisted stock/product report renderers: filters, paging, mounted search, escaping, unknown evidence, navigation, errors and stale-response guards passed.');
  await profitExercise();
  for (var route of ['report-sales', 'report-purchases', 'report-returns', 'report-payment-methods']) await exercise(route);
  var expenses = harness('report-expenses'), output = expenses.host.querySelector('[data-results]');
  expenses.pending[0].resolve({count:21,total:'30.3000',categories:[{categoryId:1,category:'إيجار',count:1,total:'10.1000'}],items:[{id:2,expenseDate:'2020-01-01',postedUtc:'2026-03-01T12:00:00Z',category:'إيجار',payee:'مورد',notes:'وصف',userName:'admin',amount:'10.1000'}]}); await settle();
  assert.ok(output.innerHTML.includes('30.3000') && output.innerHTML.includes('2020-01-01') && output.innerHTML.includes('إيجار'));
  output.querySelector('[data-next]').onclick(); assert.ok(expenses.calls[1].includes('page=2'));
  expenses.pending[1].resolve({count:21,total:'30.3000',categories:[],items:[]}); await settle();
  expenses.form.elements.categoryId.value='1'; expenses.form.onsubmit({preventDefault:function(){}}); assert.ok(expenses.calls[2].includes('categoryId=1') && expenses.calls[2].includes('page=1'));
  expenses.pending[2].reject(new Error('خطأ')); await settle(); assert.ok(output.innerHTML.includes('خطأ')); output.querySelector('[data-retry]').onclick();
  expenses.pending[3].resolve({count:0,total:'0.0000',categories:[],items:[]}); await settle(); assert.ok(output.innerHTML.includes('لا توجد'));
  var source=fs.readFileSync(path.join(__dirname,'../js/modules/payment-methods.js'),'utf8');
  assert.ok(source.includes('context.state.instrumentId'), 'instrument report links must select existing history identity');
  console.log('All four reports: month/date presets, custom dates, exact summaries, records, pagination, reset, loading/empty/error/retry, document links, permissions and stale responses passed.');
};
if (require.main === module) module.exports().catch(function (e) { console.error(e); process.exitCode = 1; });
