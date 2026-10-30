'use strict';
// Test-only outbound network denial for the packaged owner backend.
const assertLoopback=value=>{const url=new URL(typeof value==='string'?value:value.url);if(!['127.0.0.1','[::1]','localhost'].includes(url.hostname))throw new Error('Offline acceptance forbids external network');};
const originalFetch=globalThis.fetch;globalThis.fetch=(input,...args)=>{assertLoopback(input);return originalFetch(input,...args);};
for(const module of ['node:http','node:https']) {
 const client=require(module);
 for(const name of ['request','get']){const original=client[name];client[name]=function(options,...args){if(typeof options==='string'||options instanceof URL)assertLoopback(String(options));else if(!['127.0.0.1','localhost','::1'].includes(options.hostname||options.host))throw new Error('Offline acceptance forbids external network');return original.call(this,options,...args);};}
}
