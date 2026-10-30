import {spawn} from 'node:child_process';
import {mkdir,readFile,writeFile} from 'node:fs/promises';
import {resolve,extname} from 'node:path';
import {createServer} from 'node:http';
import assert from 'node:assert/strict';

const root=process.cwd(),out=resolve(root,'artifacts/liquid-glass-web');
await mkdir(out,{recursive:true});
const server=createServer(async(req,res)=>{try{const path=resolve(root,'.'+decodeURIComponent(new URL(req.url,'http://localhost').pathname));if(!path.startsWith(root+'\\'))throw Error('outside root');res.setHeader('Content-Type',({'.html':'text/html; charset=utf-8','.css':'text/css; charset=utf-8','.js':'text/javascript; charset=utf-8'})[extname(path)]||'application/octet-stream');res.end(await readFile(path))}catch{res.writeHead(404);res.end()}});
await new Promise(r=>server.listen(0,'127.0.0.1',r));
const base=`http://127.0.0.1:${server.address().port}`;
const browser=spawn('C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe',['--headless=new','--disable-gpu','--no-first-run','--no-default-browser-check','--remote-debugging-port=0',`--user-data-dir=${resolve(root,'.local/headless-glass-profile')}`,'about:blank'],{windowsHide:true,stdio:['ignore','ignore','pipe']});
let ws;
try{
  const endpoint=await new Promise((resolve,reject)=>{let log='';browser.stderr.on('data',d=>{log+=d;const m=log.match(/DevTools listening on (ws:\/\/[^\s]+)/);if(m)resolve(m[1]);});browser.on('error',reject);browser.on('exit',c=>reject(Error(`Edge exited ${c}: ${log}`)));setTimeout(()=>reject(Error('DevTools startup timeout')),20000).unref()});
  ws=new WebSocket(endpoint);await new Promise((r,j)=>{ws.onopen=r;ws.onerror=j});
  let id=0;const pending=new Map(),events=[];
  ws.onmessage=e=>{const msg=JSON.parse(e.data);if(msg.id){const item=pending.get(msg.id);pending.delete(msg.id);msg.error?item.reject(Error(JSON.stringify(msg.error))):item.resolve(msg.result)}else{events.push(msg)}};
  const call=(method,params={},sessionId)=>new Promise((resolve,reject)=>{const n=++id;pending.set(n,{resolve,reject});ws.send(JSON.stringify({id:n,method,params,...(sessionId?{sessionId}:{})}))});
  const {targetId}=await call('Target.createTarget',{url:'about:blank'});
  const {sessionId}=await call('Target.attachToTarget',{targetId,flatten:true});
  const c=(method,params)=>call(method,params,sessionId);
  await c('Page.enable');await c('Runtime.enable');await c('Emulation.setDeviceMetricsOverride',{width:1440,height:900,deviceScaleFactor:1,mobile:false});
  async function evaluate(expression){const result=await c('Runtime.evaluate',{expression,returnByValue:true,awaitPromise:true});if(result.exceptionDetails)throw Error(JSON.stringify(result.exceptionDetails));return result.result.value}
  async function navigate(page){await c('Page.navigate',{url:base+'/'+page});for(let i=0;i<100;i++){if(await evaluate(`document.readyState==='complete' && !!document.querySelector('${page.includes('popup')?'#tray-list .tray-row':'#device-grid .device-card'}')`))return;await new Promise(r=>setTimeout(r,50))}throw Error('Page load timeout')}
  async function screenshot(name){await evaluate(`(()=>{let s=document.createElement('style');s.textContent='*{transition:none!important;animation:none!important}';document.head.append(s);return document.fonts.ready})()`);await c('Page.createIsolatedWorld',{frameId:(await c('Page.getFrameTree')).frameTree.frame.id,worldName:'capture'});const {data}=await c('Page.captureScreenshot',{format:'png',captureBeyondViewport:false});await writeFile(resolve(out,name),Buffer.from(data,'base64'))}
  await navigate('usbhub.html');
  await evaluate(`new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve)))`);
  const layout=await evaluate(`(()=>{const r=e=>{let x=e.getBoundingClientRect();return {x:x.x,y:x.y,width:x.width,height:x.height,bottom:x.bottom}};return {viewport:[innerWidth,innerHeight],scroll:[document.documentElement.scrollWidth,document.documentElement.scrollHeight],sidebar:r(document.querySelector('.sidebar')),cards:[...document.querySelectorAll('.device-card')].map(r),blur:getComputedStyle(document.querySelector('.glass')).backdropFilter}})()`);
  assert.equal(layout.sidebar.width,236);assert.deepEqual(layout.scroll,[1440,900]);assert.equal(layout.cards.length,6);assert(layout.cards.every(r=>r.bottom<865));assert(layout.blur.includes('blur(24px)'));
  assert.equal(await evaluate(`document.querySelectorAll('feDisplacementMap').length>=8`),true);
  assert.equal(await evaluate(`[...document.querySelectorAll('.device-card')].every(card=>card.querySelector('.card-source').getBoundingClientRect().bottom<card.getBoundingClientRect().bottom)`),true);
  await screenshot('main-1440x900.png');
  const counts={};for(const filter of ['bluetooth','usb','low','all']){counts[filter]=await evaluate(`document.querySelector('[data-filter=${filter}]').click();document.querySelectorAll('.device-card').length`)}assert.deepEqual(counts,{bluetooth:3,usb:3,low:1,all:6});
  await evaluate(`document.querySelector('[data-filter=all]').focus()`);await c('Input.dispatchKeyEvent',{type:'keyDown',key:'ArrowRight',code:'ArrowRight',windowsVirtualKeyCode:39});await c('Input.dispatchKeyEvent',{type:'keyUp',key:'ArrowRight',code:'ArrowRight',windowsVirtualKeyCode:39});
  assert.equal(await evaluate(`document.activeElement.dataset.filter`),'bluetooth');assert.equal(await evaluate(`document.querySelectorAll('.device-card').length`),3);await evaluate(`document.querySelector('[data-filter=all]').click()`);
  assert.equal(await evaluate(`(()=>{const s=document.querySelector('#device-search');s.value='NuPhy';s.dispatchEvent(new Event('input'));return document.querySelectorAll('.device-card').length})()`),1);
  assert.equal(await evaluate(`(()=>{const s=document.querySelector('#device-search');s.value='no-match';s.dispatchEvent(new Event('input'));return !document.querySelector('#empty-state').hidden})()`),true);await screenshot('empty-search.png');
  await evaluate(`document.querySelector('#clear-search').click()`);
  await evaluate(`(()=>{let c=document.querySelector('.device-card'),r=c.getBoundingClientRect();c.dispatchEvent(new PointerEvent('pointermove',{clientX:r.x+r.width*.8,clientY:r.y+r.height*.2}));return c.style.cssText})()`);
  assert.equal(await evaluate(`document.querySelector('.device-card').style.getPropertyValue('--rx')!==''`),true);
  await evaluate(`document.querySelector('[data-page=settings]').click()`);await screenshot('settings.png');
  assert.equal(await evaluate(`(()=>{let x=document.querySelector('#threshold');x.value='30';x.dispatchEvent(new Event('input'));document.querySelector('#settings-form').requestSubmit();return JSON.parse(localStorage.getItem('usbhub-design-settings')).threshold})()`),30);
  assert.equal(await evaluate(`(()=>{let x=document.querySelector('#threshold');x.value='20';x.dispatchEvent(new Event('input'));document.querySelector('#settings-form').requestSubmit();document.querySelector('[data-filter=low]').click();return document.querySelectorAll('.device-card').length})()`),0);
  await evaluate(`localStorage.removeItem('usbhub-design-settings')`);
  await navigate('usbhub-popup.html');
  assert.equal(await evaluate(`document.querySelectorAll('.tray-row').length`),6);assert.equal(await evaluate(`document.querySelector('.tray-panel').getBoundingClientRect().width`),300);
  await c('Emulation.setDeviceMetricsOverride',{width:900,height:900,deviceScaleFactor:1,mobile:false});await screenshot('popup-900x900.png');
  await evaluate(`document.querySelector('#popup-refresh').click()`);assert.equal(await evaluate(`!document.querySelector('#toast').hidden`),true);
  await evaluate(`document.querySelector('#popup-exit').click()`);assert.equal(await evaluate(`!document.querySelector('#closed-panel').hidden`),true);await evaluate(`document.querySelector('#reopen-popup').click()`);assert.equal(await evaluate(`!document.querySelector('.tray-panel').hidden`),true);
  await c('Emulation.setDeviceMetricsOverride',{width:390,height:740,deviceScaleFactor:1,mobile:false});await screenshot('popup-390x740.png');
  assert.equal(await evaluate(`document.documentElement.scrollWidth<=innerWidth && document.documentElement.scrollHeight<=innerHeight`),true);
  await c('Emulation.setDeviceMetricsOverride',{width:390,height:260,deviceScaleFactor:1,mobile:false});
  await evaluate(`new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve)))`);
  const constrained=await evaluate(`(()=>{const panel=document.querySelector('.tray-panel').getBoundingClientRect(),list=document.querySelector('.tray-list'),actions=document.querySelector('.tray-actions').getBoundingClientRect();list.scrollTop=list.scrollHeight;return {fits:panel.top>=0&&panel.bottom<=innerHeight,scrolls:list.scrollHeight>list.clientHeight,lastDeviceReachable:Math.abs(list.scrollHeight-list.clientHeight-list.scrollTop)<=1,actionsVisible:actions.bottom<=panel.bottom}})()`);
  assert.deepEqual(constrained,{fits:true,scrolls:true,lastDeviceReachable:true,actionsVisible:true});await screenshot('popup-constrained-390x260.png');
  const errors=events.filter(e=>e.method==='Runtime.exceptionThrown');assert.equal(errors.length,0,JSON.stringify(errors));
  const result={success:true,layout,filterCounts:counts,keyboardTabs:true,liveSearch:true,emptyState:true,pointerHighlightAndTilt:true,perimeterRefraction:true,settingsPersistence:true,configuredThresholdFilter:true,popupRows:6,popupWidth:300,popupActions:true,mobilePopupFits:true,constrainedPopup:constrained,consoleExceptions:0,screenshotStyle:'*{transition:none!important;animation:none!important}'};
  await writeFile(resolve(out,'verification.json'),JSON.stringify(result,null,2));console.log(JSON.stringify(result,null,2));
}finally{if(ws?.readyState===1){ws.send(JSON.stringify({id:999999,method:'Browser.close'}));ws.close()}browser.kill();server.close()}
