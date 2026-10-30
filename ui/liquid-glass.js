/* Offline design preview. Device data here is synthetic; the WPF app owns hardware access. */
'use strict';
const devices = [
  {id:'s23',name:'晨钟暮鼓 的 S23 Ultra',kind:'mouse',transport:'bluetooth',percent:100,state:'已断开 · 缓存电量',meta:'已断开',source:'Windows 电量属性（缓存）',trayState:'已断开 · 缓存'},
  {id:'btmouse',name:'BT5.0 Mouse',kind:'mouse',transport:'bluetooth',percent:70,state:'已断开 · 缓存电量',meta:'已断开',source:'Windows 电量属性（缓存）',trayState:'已断开 · 缓存'},
  {id:'g502',name:'G502 X LIGHTSPEED',kind:'mouse',transport:'usb',percent:28,state:'电量正常',meta:'电量正常',source:'Logitech HID++ · 1004',trayState:'缓存'},
  {id:'sony',name:'WH-1000XM4',kind:'headphones',transport:'bluetooth',percent:100,state:'已断开 · 缓存电量',meta:'已断开',source:'Windows 电量属性（缓存）',trayState:'已断开 · 缓存'},
  {id:'nuphy',name:'NuPhy Air75 V3',kind:'keyboard',transport:'usb',percent:null,state:'等待电量信息',meta:'等待电量信息',source:'NuPhy Air75 V3 · 电量暂不支持',trayState:'连接待确认'},
  {id:'pelta',name:'ROG PELTA (2.4GHz)',kind:'headphones',transport:'usb',percent:null,state:'等待电量信息',meta:'等待电量信息',source:'HID 设备发现',trayState:'连接待确认'}
];
const paths = {
  home:'<path d="m3 10 9-7 9 7M5 9v12h14V9M9 21v-8h6v8"/>',
  bell:'<path d="M18 8a6 6 0 0 0-12 0c0 7-3 7-3 9h18c0-2-3-2-3-9M10 21h4"/>',
  layers:'<path d="m3 7 9-5 9 5-9 5-9-5Zm0 5 9 5 9-5M3 17l9 5 9-5"/>',
  search:'<circle cx="10.5" cy="10.5" r="6.5"/><path d="m16 16 4 4"/>',
  bluetooth:'<path d="m7 6 10 12-5 4V2l5 4L7 18"/>',
  usb:'<path d="M9 3v6m6-6v6M7 9h10v4a5 5 0 0 1-10 0V9Zm5 9v4"/>',
  mouse:'<rect x="6" y="2" width="12" height="20" rx="6"/><path d="M12 6v4"/>',
  keyboard:'<rect x="1.5" y="5" width="21" height="14" rx="3"/><path d="M5 9h.1M9 9h.1M13 9h.1M17 9h.1M5 12h.1M9 12h.1M13 12h.1M17 12h.1M7 16h10"/>',
  headphones:'<path d="M3 14v-3a9 9 0 0 1 18 0v3"/><rect x="1" y="12" width="5" height="9" rx="2.5"/><rect x="18" y="12" width="5" height="9" rx="2.5"/>',
  refresh:'<path d="M20 7v5h-5M4 17v-5h5M5.2 7.5a8 8 0 0 1 13-2L20 8M4 16l1.8 2.5a8 8 0 0 0 13-2"/>',
  moon:'<path d="M21 14a9 9 0 0 1-11-11 9 9 0 1 0 11 11Z"/>'
};
const icon = name => `<svg viewBox="0 0 24 24" aria-hidden="true">${paths[name] || paths.usb}</svg>`;
document.querySelectorAll('[data-icon]').forEach(el => el.innerHTML = icon(el.dataset.icon));

// Convex perimeter displacement, inspired by Backdrop's blur + lens pipeline.
// The map samples only the backdrop; labels and device artwork stay sharp.
const svgNS='http://www.w3.org/2000/svg';
const lensDefs=document.createElementNS(svgNS,'svg');lensDefs.style.cssText='position:fixed;left:0;top:0;width:0;height:0;overflow:hidden;pointer-events:none';lensDefs.setAttribute('aria-hidden','true');document.body.append(lensDefs);
let lensIndex=0;
const lensObserver=new ResizeObserver(entries=>entries.forEach(({target})=>{
  const width=Math.round(target.offsetWidth),height=Math.round(target.offsetHeight);if(width<20||height<20)return;
  if(target.dataset.lensSize===`${width},${height}`)return;target.dataset.lensSize=`${width},${height}`;
  const id=target.dataset.lensId||`glass-lens-${++lensIndex}`;target.dataset.lensId=id;
  document.getElementById(id)?.remove();
  const canvas=document.createElement('canvas');canvas.width=width;canvas.height=height;const ctx=canvas.getContext('2d'),map=ctx.createImageData(width,height);
  const radius=Math.min(parseFloat(getComputedStyle(target).borderTopLeftRadius)||23,width/2,height/2),rim=Math.min(15,radius*.72);
  for(let y=0;y<height;y++)for(let x=0;x<width;x++){
    const qx=Math.abs(x-width/2)-(width/2-radius),qy=Math.abs(y-height/2)-(height/2-radius),ox=Math.max(qx,0),oy=Math.max(qy,0),length=Math.hypot(ox,oy);
    const depth=radius-Math.min(Math.max(qx,qy),0)-length;
    let nx=0,ny=0;if(length>0){nx=ox/length;ny=oy/length}else if(qx>qy)nx=1;else ny=1;
    const bend=depth>0&&depth<rim?Math.sin(depth/rim*Math.PI)*.43:0,index=(y*width+x)*4;
    map.data[index]=Math.round(128+127*nx*Math.sign(x-width/2)*bend);map.data[index+1]=Math.round(128+127*ny*Math.sign(y-height/2)*bend);map.data[index+2]=128;map.data[index+3]=255;
  }
  ctx.putImageData(map,0,0);
  const filter=document.createElementNS(svgNS,'filter');filter.id=id;filter.setAttribute('x','0%');filter.setAttribute('y','0%');filter.setAttribute('width','100%');filter.setAttribute('height','100%');filter.setAttribute('color-interpolation-filters','sRGB');
  const image=document.createElementNS(svgNS,'feImage');image.setAttribute('href',canvas.toDataURL());image.setAttribute('x','0');image.setAttribute('y','0');image.setAttribute('width','100%');image.setAttribute('height','100%');image.setAttribute('result','normalMap');filter.append(image);
  const displacement=document.createElementNS(svgNS,'feDisplacementMap');displacement.setAttribute('in','SourceGraphic');displacement.setAttribute('in2','normalMap');displacement.setAttribute('scale','18');displacement.setAttribute('xChannelSelector','R');displacement.setAttribute('yChannelSelector','G');filter.append(displacement);lensDefs.append(filter);
  if(CSS.supports('backdrop-filter',`blur(24px) saturate(190%) url(#${id})`))target.style.backdropFilter=`blur(24px) saturate(190%) url(#${id})`;
}));
function observeGlass(){document.querySelectorAll('.glass').forEach(el=>lensObserver.observe(el))}

function illustration(kind, id) {
  const defs = `<defs><linearGradient id="body-${id}" x1="15%" y1="5%" x2="90%" y2="90%"><stop stop-color="#f9fffd"/><stop offset=".36" stop-color="#dbefea"/><stop offset="1" stop-color="#85b6aa"/></linearGradient><linearGradient id="edge-${id}" x1="0" y1="0" x2="1" y2="1"><stop stop-color="#e9fffa"/><stop offset=".5" stop-color="#8dbdb1"/><stop offset="1" stop-color="#387e6c"/></linearGradient><radialGradient id="shadow-${id}"><stop stop-color="#367f6b" stop-opacity=".32"/><stop offset="1" stop-color="#367f6b" stop-opacity="0"/></radialGradient><linearGradient id="shine-${id}" x1="0" y1="0" x2="1" y2="1"><stop stop-color="white" stop-opacity=".9"/><stop offset="1" stop-color="white" stop-opacity="0"/></linearGradient></defs>`;
  let art;
  if (kind === 'mouse') art = `<ellipse cx="110" cy="144" rx="37" ry="8" fill="url(#shadow-${id})"/><g transform="rotate(${id==='g502'?-12:id==='btmouse'?-6:5} 110 80)"><path d="M75 47C78 6 137 5 146 45L153 100C158 151 81 158 75 112C70 91 73 69 75 47Z" fill="url(#body-${id})" stroke="url(#edge-${id})" stroke-width="2"/><path d="M81 47C84 15 105 18 112 20L111 65C94 78 77 65 81 47Z" fill="url(#shine-${id})"/><path d="m110 21 1 38" stroke="#c1dcd2" stroke-width="2"/><rect x="106" y="40" width="9" height="23" rx="4.5" fill="#286e5a"/><path d="M110 44v12" stroke="#d7f8e8" stroke-width="1.5"/><path d="M100 133h20" stroke="#12d8b7" stroke-width="4" stroke-linecap="round"/><path d="M100 132h20" stroke="#66ffe1" stroke-width="1" stroke-linecap="round"/></g>`;
  else if (kind === 'keyboard') {
    let keys=''; for(let r=0;r<5;r++)for(let c=0;c<12;c++)keys+=`<rect x="${22+c*15}" y="${51+r*11}" width="11" height="8" rx="1.2" fill="#cee7dc" fill-opacity=".7" stroke="#a4d1c0" stroke-width=".5"/>`;
    art=`<ellipse cx="110" cy="139" rx="86" ry="10" fill="url(#shadow-${id})"/><rect x="8" y="38" width="204" height="99" rx="15" fill="url(#body-${id})" stroke="url(#edge-${id})" stroke-width="2.2"/><rect x="13" y="42" width="194" height="89" rx="11" fill="none" stroke="white" stroke-opacity=".75"/>${keys}<rect x="76" y="106" width="69" height="9" rx="2" fill="#c5e2d5" stroke="#a4d1c0"/><path d="M32 126h155" stroke="#12c2a8" stroke-opacity=".5" stroke-width="2"/>`;
  } else art=`<ellipse cx="110" cy="145" rx="49" ry="9" fill="url(#shadow-${id})"/><path d="M55 83C47-9 175-9 167 83" fill="none" stroke="#bfddd3" stroke-width="14"/><path d="M55 83C47-9 175-9 167 83" fill="none" stroke="#e8f9f3" stroke-width="9"/><path d="M55 83C47-9 175-9 167 83" fill="none" stroke="#a8d0c1" stroke-width="1"/><rect x="37" y="76" width="38" height="63" rx="18" fill="url(#body-${id})" stroke="url(#edge-${id})" stroke-width="2"/><rect x="147" y="76" width="38" height="63" rx="18" fill="url(#body-${id})" stroke="url(#edge-${id})" stroke-width="2"/><rect x="41" y="80" width="30" height="54" rx="14" fill="none" stroke="white" stroke-opacity=".8"/><rect x="151" y="80" width="30" height="54" rx="14" fill="none" stroke="white" stroke-opacity=".8"/><rect x="47" y="89" width="18" height="37" rx="9" fill="#95c3b2" fill-opacity=".75"/><rect x="157" y="89" width="18" height="37" rx="9" fill="#95c3b2" fill-opacity=".75"/><circle cx="56" cy="107" r="5" fill="#12bda0"/><circle cx="166" cy="107" r="5" fill="#12bda0"/>`;
  return `<svg viewBox="0 0 220 160" aria-hidden="true">${defs}${art}</svg>`;
}
let toastTimer;
function toast(message){const el=document.querySelector('#toast');el.textContent=message;el.hidden=false;clearTimeout(toastTimer);toastTimer=setTimeout(()=>el.hidden=true,3200)}
let filter='all',search='',page=0,previewThreshold=30;
function renderCards(){
  const grid=document.querySelector('#device-grid');if(!grid)return;
  const filtered=devices.filter(d=>(filter==='all'||(filter==='low'?d.percent!==null&&d.percent<=previewThreshold:d.transport===filter))&&d.name.toLocaleLowerCase().includes(search.toLocaleLowerCase()));
  const pages=Math.max(1,Math.ceil(filtered.length/6));page=Math.min(page,pages-1);
  grid.querySelectorAll('.device-card').forEach(card=>{lensObserver.unobserve(card);document.getElementById(card.dataset.lensId)?.remove()});
  grid.innerHTML=filtered.slice(page*6,page*6+6).map((d,i)=>`<article class="device-card glass ${d.percent!==null&&d.percent<=previewThreshold?'low':''}" data-device="${d.id}" aria-label="${d.name}，${d.percent===null?'电量未知':d.percent+'%'}，${d.state}"><div class="card-meta">${icon(d.transport)}<span>${d.transport==='bluetooth'?'蓝牙':'USB 接收器'}</span><span class="connection-separator">·</span><span>${d.meta}</span>${d.percent!==null&&d.percent<=previewThreshold?'<span class="low-badge">低</span>':''}</div><div class="device-art">${illustration(d.kind,d.id)}</div><h2 title="${d.name}">${d.name}</h2><p class="card-state">${d.state}</p><div class="battery-value ${d.percent===null?'unknown':''}"><strong>${d.percent===null?'—':d.percent}</strong><span>${d.percent===null?'电量未知':'%'}</span></div><div class="battery-track ${d.percent===null?'unknown':''}">${d.percent===null?'':`<div class="battery-fill" style="width:${d.percent}%;--delay:${i*65}ms"></div>`}</div><p class="card-source" title="${d.source}">${d.source}</p></article>`).join('');
  grid.hidden=filtered.length===0;document.querySelector('#empty-state').hidden=filtered.length!==0;
  document.querySelector('#page-controls').hidden=pages<=1;document.querySelector('#page-count').textContent=`${page+1} / ${pages}`;
  document.querySelector('#previous-page').disabled=page===0;document.querySelector('#next-page').disabled=page+1>=pages;
  document.querySelector('#status-text').textContent=`${filtered.length} 台设备 · ${filtered.filter(d=>d.percent!==null).length} 台电量可读`;
  grid.querySelectorAll('.device-card').forEach(card=>{
    card.addEventListener('pointermove',e=>{if(matchMedia('(prefers-reduced-motion: reduce)').matches||document.body.classList.contains('reduce-motion'))return;const r=card.getBoundingClientRect(),x=e.clientX-r.left,y=e.clientY-r.top;card.style.setProperty('--mx',`${x}px`);card.style.setProperty('--my',`${y}px`);card.style.setProperty('--rx',`${(x/r.width-.5)*3}deg`);card.style.setProperty('--ry',`${-(y/r.height-.5)*3}deg`)});
    card.addEventListener('pointerleave',()=>{card.style.setProperty('--rx','0deg');card.style.setProperty('--ry','0deg')});
  });
  observeGlass();
}
document.querySelectorAll('[data-filter]').forEach(button=>button.addEventListener('click',()=>{filter=button.dataset.filter;page=0;document.querySelectorAll('[data-filter]').forEach(b=>{b.classList.toggle('selected',b===button);b.setAttribute('aria-selected',String(b===button))});renderCards()}));
const tabs=[...document.querySelectorAll('[role=tab]')];tabs.forEach((tab,i)=>{tab.tabIndex=i===0?0:-1;tab.addEventListener('click',()=>tabs.forEach(t=>t.tabIndex=t===tab?0:-1));tab.addEventListener('keydown',e=>{let next;if(e.key==='ArrowRight')next=(i+1)%tabs.length;else if(e.key==='ArrowLeft')next=(i+tabs.length-1)%tabs.length;else if(e.key==='Home')next=0;else if(e.key==='End')next=tabs.length-1;else return;e.preventDefault();tabs[next].focus();tabs[next].click()})});
document.querySelector('#device-search')?.addEventListener('input',e=>{search=e.target.value;page=0;renderCards()});
document.querySelector('#clear-search')?.addEventListener('click',()=>{search='';document.querySelector('#device-search').value='';document.querySelector('[data-filter=all]').click()});
document.querySelector('#previous-page')?.addEventListener('click',()=>{page--;renderCards()});document.querySelector('#next-page')?.addEventListener('click',()=>{page++;renderCards()});
document.querySelectorAll('[data-page]').forEach(button=>button.addEventListener('click',()=>{const settings=button.dataset.page==='settings';document.querySelector('#overview-page').hidden=settings;document.querySelector('#settings-page').hidden=!settings;document.querySelector('#page-title').textContent=settings?'提醒与设置':'设备总览';document.querySelectorAll('[data-page]').forEach(b=>b.classList.toggle('active',b===button))}));
document.querySelector('#refresh')?.addEventListener('click',()=>toast('已显示示例缓存 · 桌面端主动读取间隔为 30 分钟'));
document.querySelectorAll('[data-window]').forEach(button=>button.addEventListener('click',()=>{if(button.dataset.window==='maximize'){if(document.fullscreenElement)document.exitFullscreen();else document.documentElement.requestFullscreen().catch(()=>toast('请使用浏览器全屏查看'));}else if(button.dataset.window==='close')location.href='usbhub-popup.html';else toast('网页预览 · 最小化操作请在桌面程序中使用')}));
const form=document.querySelector('#settings-form');
if(form){
  let saved;try{saved=JSON.parse(localStorage.getItem('usbhub-design-settings')||'null')}catch{}
  if(saved){for(const key of ['notifications','close-to-tray','motion'])if(typeof saved[key]==='boolean')document.getElementById(key).checked=saved[key];if(Number.isFinite(saved.threshold))document.querySelector('#threshold').value=Math.min(50,Math.max(5,saved.threshold));}
  const threshold=document.querySelector('#threshold');previewThreshold=Number(threshold.value);document.querySelector('#threshold-value').value=threshold.value;threshold.addEventListener('input',()=>document.querySelector('#threshold-value').value=threshold.value);
  document.body.classList.toggle('reduce-motion',!document.querySelector('#motion').checked);
  form.addEventListener('submit',e=>{e.preventDefault();const data={threshold:Number(threshold.value)};for(const key of ['notifications','close-to-tray','motion'])data[key]=document.getElementById(key).checked;try{localStorage.setItem('usbhub-design-settings',JSON.stringify(data));previewThreshold=data.threshold;renderCards();document.body.classList.toggle('reduce-motion',!data.motion);toast('预览设置已保存')}catch{toast('当前浏览器无法保存预览设置')}});
}
renderCards();
const list=document.querySelector('#tray-list');
if(list){
  const order=['s23','nuphy','btmouse','g502','pelta','sony'];
  list.innerHTML=order.map(id=>devices.find(d=>d.id===id)).map(d=>`<div class="tray-row ${d.percent!==null&&d.percent<=previewThreshold?'low':''}"><div class="type-icon">${icon(d.kind)}</div><div><div class="tray-name" title="${d.name}">${d.name}</div><div class="tray-transport">${icon(d.transport)}<span>${d.transport==='bluetooth'?'蓝牙':'USB 接收器'}</span></div></div><div class="tray-reading ${d.percent===null?'unknown':''}"><strong>${d.percent===null?'电量未知':d.percent+'%'}</strong><small>${d.trayState}</small></div></div>`).join('');
  document.querySelector('#popup-refresh').addEventListener('click',()=>toast('已显示示例缓存 · 未执行硬件查询'));
  document.querySelector('#popup-exit').addEventListener('click',()=>{document.querySelector('.tray-panel').hidden=true;document.querySelector('#closed-panel').hidden=false});
  document.querySelector('#reopen-popup').addEventListener('click',()=>{document.querySelector('.tray-panel').hidden=false;document.querySelector('#closed-panel').hidden=true});
}
observeGlass();
