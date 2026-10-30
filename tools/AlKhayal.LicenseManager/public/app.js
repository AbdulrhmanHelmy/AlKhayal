'use strict';
(function(){
  const $=id=>document.getElementById(id);
  const initial=location.hash.slice(1);if(initial)sessionStorage.setItem('owner-session',initial);
  history.replaceState(null,'',location.pathname);
  const token=sessionStorage.getItem('owner-session')||'';
  let customers=[],issued=null;
  const notice=text=>{$('notice').textContent=text;};
  async function api(route,input) {
    const response=await fetch('/api/'+route,{method:input===undefined?'GET':'POST',credentials:'same-origin',headers:{Authorization:'Bearer '+token,...(input===undefined?{}:{'Content-Type':'application/json'})},...(input===undefined?{}:{body:JSON.stringify(input)})});
    const result=await response.json();if(!response.ok)throw new Error(result.error||'تعذر الاتصال بالأداة المحلية.');return result;
  }
  const data=form=>Object.fromEntries(new FormData(form));
  function button(text,action){const b=document.createElement('button');b.type='button';b.textContent=text;b.addEventListener('click',()=>run(b,action));return b;}
  async function run(control,action){if(control)control.disabled=true;try{await action();}catch(error){notice(error.message);}finally{if(control)control.disabled=false;}}
  function download(name,text,type='text/plain'){const a=document.createElement('a');const url=URL.createObjectURL(new Blob([text],{type}));a.href=url;a.download=name;a.click();setTimeout(()=>URL.revokeObjectURL(url),1000);}
  async function copy(text){try{await navigator.clipboard.writeText(text);}catch{const field=document.createElement('textarea');field.value=text;document.body.append(field);field.select();const copied=document.execCommand('copy');field.remove();if(!copied)throw new Error('تعذر النسخ. حدد النص وانسخه باستخدام Ctrl+C.');}notice('تم نسخ كود التفعيل كاملًا.');}
  function cell(row,value){const c=document.createElement('td');c.textContent=String(value==null?'':value);row.append(c);return c;}
  const localDate=value=>new Date(value).toLocaleString('ar-EG');
  async function refresh(){
    const status=await api('status');$('customer-count').textContent=status.customers;$('device-count').textContent=status.devices;$('license-count').textContent=status.licenses;
    const keyText=status.key.available?'المفتاح جاهز · '+status.key.signingKeyId:'لم تتم تهيئة مفتاح المالك بعد.';
    $('dashboard-key').textContent=keyText;$('key-status').textContent=keyText;$('data-directory').textContent=status.key.dataDirectory;
    $('key-setup').hidden=status.key.available;$('export-public').disabled=!status.key.available;$('backup-key').disabled=!status.key.available;
    customers=await api('customers');const select=$('existing-customer');const selected=select.value;select.replaceChildren(new Option('عميل جديد أو مطابقة بالاسم والهاتف',''));
    customers.forEach(c=>select.add(new Option(c.CustomerName,c.CustomerId)));select.value=selected;
  }
  async function loadCustomers(){
    const search=$('customer-search').value;const [rows,devices]=await Promise.all([api('customers?search='+encodeURIComponent(search)),api('devices?search='+encodeURIComponent(search))]);
    $('customer-rows').replaceChildren();for(const c of rows){const row=document.createElement('tr');cell(row,c.CustomerName);cell(row,c.Phone||'—');cell(row,c.DeviceCount);cell(row,c.LicenseCount);cell(row,'').append(button('عرض السجل',async()=>{$('history-search').value=c.CustomerId;await page('history');}));$('customer-rows').append(row);}
    if(!rows.length){const row=document.createElement('tr');cell(row,'لا توجد نتائج.').colSpan=5;$('customer-rows').append(row);}
    $('device-rows').replaceChildren();for(const d of devices){const row=document.createElement('tr');cell(row,d.CustomerName);cell(row,d.DeviceLabel||'—');const ref=cell(row,d.FingerprintReference);ref.className='code';ref.dir='ltr';cell(row,localDate(d.FirstSeenUtc));$('device-rows').append(row);}
  }
  async function loadHistory(){
    const rows=await api('history?search='+encodeURIComponent($('history-search').value));$('history-list').replaceChildren();
    for(const row of rows){const article=document.createElement('article');article.className='history-entry';const title=document.createElement('h3');title.textContent=row.CustomerName;article.append(title);
      for(const text of [localDate(row.IssuedUtc)+' · ترخيص دائم',row.FingerprintReference,row.LicenseId]){const p=document.createElement('p');p.textContent=text;if(text===row.FingerprintReference||text===row.LicenseId){p.className='code';p.dir='ltr';}article.append(p);}
      const code=document.createElement('textarea');code.readOnly=true;code.dir='ltr';code.rows=3;code.value=row.SignedLicenseText;code.setAttribute('aria-label','كود التفعيل '+row.LicenseId);article.append(code);
      const actions=document.createElement('div');actions.className='actions';actions.append(button('نسخ كود التفعيل',()=>copy(row.SignedLicenseText)),button('حفظ ملف الترخيص',()=>download(row.LicenseId+'.alklic',row.SignedLicenseText)));article.append(actions);$('history-list').append(article);}
    if(!rows.length)$('history-list').textContent='لا توجد تراخيص مطابقة.';
  }
  async function page(id){document.querySelectorAll('.page').forEach(p=>p.hidden=p.id!==id);document.querySelectorAll('nav button').forEach(b=>b.classList.toggle('active',b.dataset.page===id));const nav=document.querySelector('nav [data-page="'+id+'"]');$('page-title').textContent=nav.textContent;notice('');if(id==='customers')await loadCustomers();if(id==='history')await loadHistory();if(id==='dashboard'||id==='settings'||id==='issue')await refresh();}
  document.querySelectorAll('[data-page]').forEach(b=>b.addEventListener('click',()=>run(null,()=>page(b.dataset.page))));
  $('existing-customer').addEventListener('change',()=>{const c=customers.find(c=>c.CustomerId===$('existing-customer').value);if(c){const form=$('issue-form');form.elements.customerName.value=c.CustomerName;form.elements.phone.value=c.Phone;}});
  $('customer-form').addEventListener('submit',e=>{e.preventDefault();run(e.submitter,async()=>{await api('customers',data(e.target));e.target.reset();await refresh();await loadCustomers();notice('تم حفظ العميل.');});});
  $('validate-request').addEventListener('click',e=>run(e.target,async()=>{const result=await api('validate',{requestCode:$('request-code').value.trim()});$('request-result').textContent='✓ '+result.requestId;notice('طلب الجهاز صالح.');}));
  $('issue-form').addEventListener('submit',e=>{e.preventDefault();run(e.submitter,async()=>{try{const input=data(e.target);input.requestCode=input.requestCode.trim();issued=await api('issue',input);$('signed-license').value=issued.SignedLicenseText;$('issued-result').hidden=false;$('issued-title').textContent=issued.reused?'ترخيص محفوظ لهذا الجهاز — أُعيد عرضه':'صدر الترخيص وحُفظ في السجل';await refresh();notice(issued.reused?'لم يُنشأ ترخيص مكرر؛ يمكنك نسخ الترخيص المحفوظ.':'تم إصدار الترخيص الدائم وحفظه.');}finally{e.target.elements.password.value='';}});});
  $('copy-issued').addEventListener('click',e=>run(e.target,()=>copy(issued.SignedLicenseText)));$('export-issued').addEventListener('click',()=>download(issued.LicenseId+'.alklic',issued.SignedLicenseText));
  for(const [id,action] of [['customer-search',loadCustomers],['history-search',loadHistory]])$(id).addEventListener('input',()=>run(null,action));
  $('key-form').addEventListener('submit',e=>{e.preventDefault();run(e.submitter,async()=>{const input=data(e.target);try{if(input.password!==input.confirmPassword)throw new Error('كلمتا المرور غير متطابقتين.');await api('key/initialize',{password:input.password,confirmation:input.approved?'CREATE_OWNER_KEY':''});await refresh();notice('تم إنشاء المفتاح. صدّر المفتاح العام واحتفظ بنسخة مشفرة آمنة.');}finally{e.target.reset();}});});
  $('import-key-form').addEventListener('submit',e=>{e.preventDefault();run(e.submitter,async()=>{try{await api('key/import',data(e.target));await refresh();notice('تمت استعادة المفتاح الموجود.');}finally{e.target.elements.password.value='';}});});
  $('export-public').addEventListener('click',e=>run(e.target,async()=>{const result=await api('public-key');download(result.signingKeyId+'.xml',result.xml,'application/xml');}));
  $('backup-key').addEventListener('click',e=>run(e.target,async()=>{const result=await api('key/backup',{});notice('حُفظت نسخة مشفرة على الجهاز فقط: '+result.path+' — انقلها إلى وسيط آمن واحتفظ بكلمة المرور منفصلة.');}));
  $('backup-registry').addEventListener('click',e=>run(e.target,async()=>download('AlKhayal-registry-'+new Date().toISOString().slice(0,10)+'.json',JSON.stringify(await api('backup'),null,2),'application/json')));
  $('restore-registry').addEventListener('change',e=>run(e.target,async()=>{try{const file=e.target.files[0];if(!file)return;if(file.size>4*1024*1024)throw new Error('ملف النسخة كبير جدًا.');await api('restore',JSON.parse(await file.text()));await refresh();notice('تم دمج السجل دون حذف التاريخ.');}finally{e.target.value='';}}));
  run(null,refresh);
})();
