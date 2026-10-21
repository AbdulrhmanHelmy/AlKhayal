(function (global) {
  "use strict";
  var snapshot = null, loadPromise = null, U = global.AlKhayalUtils;
  function pad(value) { return value < 10 ? "0" + value : String(value); }
  function configure(value) {
    if (!value) return;
    U.money = function (amount) { return new Intl.NumberFormat("ar-EG", { useGrouping: value.useGroupSeparators, minimumFractionDigits: value.decimalPrecision, maximumFractionDigits: value.decimalPrecision }).format(Number(amount || 0)) + " " + value.currencySymbol; };
    U.number = function (number) { return new Intl.NumberFormat("ar-EG", { useGrouping: value.useGroupSeparators, maximumFractionDigits: value.decimalPrecision }).format(Number(number || 0)); };
    U.date = function (input) { var date=input instanceof Date?input:new Date(input);if(isNaN(date.getTime()))return "—";var d=pad(date.getDate()),m=pad(date.getMonth()+1),y=date.getFullYear();return value.dateFormat==="yyyy-MM-dd"?y+"-"+m+"-"+d:value.dateFormat==="dd-MM-yyyy"?d+"-"+m+"-"+y:d+"/"+m+"/"+y; };
    U.time = function (input) { var date=input instanceof Date?input:new Date(input);if(isNaN(date.getTime()))return "—";var hours=date.getHours(),minutes=pad(date.getMinutes());if(value.timeFormat==="hh:mm tt"){var marker=hours>=12?"م":"ص";hours=hours%12||12;return pad(hours)+":"+minutes+" "+marker;}return pad(hours)+":"+minutes; };
    U.dateTime = function (input) { var date=input instanceof Date?input:new Date(input);return isNaN(date.getTime())?"—":U.date(date)+" "+U.time(date); };
  }
  function setSnapshot(value,options){var previous=snapshot?JSON.stringify(snapshot):"";snapshot=value;configure(value);if((!options||options.notify!==false)&&previous&&previous!==JSON.stringify(value))global.dispatchEvent(new CustomEvent("alkhayal:settings-changed",{detail:value}));return value;}
  function load(options){options=options||{};if(snapshot&&!options.force)return Promise.resolve(snapshot);if(loadPromise)return loadPromise;loadPromise=global.AlKhayalApi.request("/api/settings/general").then(function(value){return setSnapshot(value,options);});loadPromise=loadPromise.then(function(value){loadPromise=null;return value;},function(reason){loadPromise=null;throw reason;});return loadPromise;}
  global.AlKhayalSettings={load:load,setSnapshot:setSnapshot,current:function(){return snapshot;}};
}(window));
